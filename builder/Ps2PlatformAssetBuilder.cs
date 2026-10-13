using helengine.baseplatform.Builders;
using helengine.baseplatform.Descriptors;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Results;
using helengine.baseplatform.Targets;
using helengine.baseplatform.Definitions;
using helengine.files;

namespace helengine.ps2.builder;

public sealed class Ps2PlatformAssetBuilder : IPlatformAssetBuilder {
    /// <summary>
    /// Environment-variable name used to override the PS2 repository root during build execution.
    /// </summary>
    const string RepositoryRootEnvironmentVariableName = "HELENGINE_PS2_REPOSITORY_ROOT";

    readonly IPs2NativeBuildExecutor NativeBuildExecutor;
    readonly Ps2MaterialCooker MaterialCooker;
    readonly Ps2PackedMeshCooker PackedMeshCooker;
    readonly Ps2PlatformCookWorkItemExecutor PlatformCookWorkItemExecutor;
    readonly Ps2DiscLayoutWriter DiscLayoutWriter;
    readonly Ps2RuntimeAssetPathManifestWriter RuntimeAssetPathManifestWriter;

    public Ps2PlatformAssetBuilder() {
        NativeBuildExecutor = new Ps2NativeBuildExecutor();
        MaterialCooker = new Ps2MaterialCooker();
        PackedMeshCooker = new Ps2PackedMeshCooker();
        PlatformCookWorkItemExecutor = new Ps2PlatformCookWorkItemExecutor();
        DiscLayoutWriter = new Ps2DiscLayoutWriter();
        RuntimeAssetPathManifestWriter = new Ps2RuntimeAssetPathManifestWriter();
        Descriptor = new PlatformBuilderDescriptor(
            "helengine.ps2.builder",
            "1.0.0",
            "ps2",
            new EngineCompatibilityRange("1.0.0", "999.0.0"),
            new ManifestCompatibilityRange(1, 3),
            ["ps2"],
            ["ps2"]);
        Definition = Ps2PlatformDefinitionFactory.Create();
    }

    public Ps2PlatformAssetBuilder(IPs2NativeBuildExecutor nativeBuildExecutor) {
        NativeBuildExecutor = nativeBuildExecutor ?? throw new ArgumentNullException(nameof(nativeBuildExecutor));
        MaterialCooker = new Ps2MaterialCooker();
        PackedMeshCooker = new Ps2PackedMeshCooker();
        PlatformCookWorkItemExecutor = new Ps2PlatformCookWorkItemExecutor();
        DiscLayoutWriter = new Ps2DiscLayoutWriter();
        RuntimeAssetPathManifestWriter = new Ps2RuntimeAssetPathManifestWriter();
        Descriptor = new PlatformBuilderDescriptor(
            "helengine.ps2.builder",
            "1.0.0",
            "ps2",
            new EngineCompatibilityRange("1.0.0", "999.0.0"),
            new ManifestCompatibilityRange(1, 3),
            ["ps2"],
            ["ps2"]);
        Definition = Ps2PlatformDefinitionFactory.Create();
    }

    public PlatformBuilderDescriptor Descriptor { get; }

    public PlatformDefinition Definition { get; }

    /// <summary>
    /// Rejects material cooking until the PS2 builder exposes concrete material schemas and a serialized runtime payload contract.
    /// </summary>
    /// <param name="request">Material translation request to validate.</param>
    /// <returns>Serialized PS2 runtime material payload.</returns>
    public PlatformMaterialCookResult CookMaterial(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        return MaterialCooker.Cook(request);
    }

    public Task<PlatformBuildReport> BuildAsync(
        PlatformBuildRequest request,
        IPlatformBuildProgressReporter progressReporter,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        CancellationToken cancellationToken) {
        ValidateRequest(request);
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (progressReporter == null) {
            throw new ArgumentNullException(nameof(progressReporter));
        }
        if (diagnosticReporter == null) {
            throw new ArgumentNullException(nameof(diagnosticReporter));
        }

        Directory.CreateDirectory(request.WorkingRoot);
        string phaseMarkerPath = Path.Combine(request.WorkingRoot, "ps2-build-phase.txt");

        List<PlatformBuildDiagnostic> diagnostics = [];
        List<PlatformBuildItemOutcome> sceneOutcomes = BuildSceneOutcomes(request.Manifest.Scenes);
        List<PlatformBuildItemOutcome> looseAssetOutcomes = BuildLooseAssetOutcomes(request.Manifest.LooseAssets);
        try {
            Ps2CookFingerprintStore fingerprintStore = new(Path.Combine(request.WorkingRoot, "ps2-staging"));
            IReadOnlyList<Ps2TextureCookWorkPlan> texturePlans = PlatformCookWorkItemExecutor.Prepare(request, fingerprintStore, cancellationToken);
            StageCookedArtifacts(request, fingerprintStore, diagnostics, diagnosticReporter, progressReporter, cancellationToken);
            if (diagnostics.Count == 0) {
                PlatformCookWorkItemExecutor.Publish(texturePlans, fingerprintStore, progressReporter, cancellationToken);
                fingerprintStore.Save();
            }
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception exception) {
            AddDiagnostic(diagnostics, diagnosticReporter, PlatformBuildDiagnosticSeverity.Error, "PS2BUILD003",
                "Failed PS2 cook preparation or staging: " + exception.Message, string.Empty, string.Empty, request.WorkingRoot);
        }

        if (request.ExecutionScope == PlatformBuildExecutionScope.AssetsOnly) {
            return Task.FromResult(CreateReport(diagnostics, sceneOutcomes, looseAssetOutcomes));
        }

        if (diagnostics.Count == 0) {
            try {
                Ps2BuildWorkspace workspace = CreateWorkspace(request);
                WritePhaseMarker(phaseMarkerPath, "workspace created");
                IReadOnlyDictionary<string, string> logicalToPhysicalPaths = DiscLayoutWriter.BuildLogicalToPhysicalPathMap(workspace);
                WritePhaseMarker(phaseMarkerPath, "logical path map built");
                RuntimeAssetPathManifestWriter.Write(workspace.GeneratedCoreRootPath, request.Manifest, logicalToPhysicalPaths);
                WritePhaseMarker(phaseMarkerPath, "runtime asset path manifest written");
                NativeBuildExecutor.Build(workspace, cancellationToken);
                WritePhaseMarker(phaseMarkerPath, "native build completed");
                DiscLayoutWriter.Write(workspace);
                WritePhaseMarker(phaseMarkerPath, "disc layout written");
                NativeBuildExecutor.PackageIso(workspace, cancellationToken);
                WritePhaseMarker(phaseMarkerPath, "iso packaged");
                VerifyPackagedOutputs(workspace);
                WritePhaseMarker(phaseMarkerPath, "packaged outputs verified");
                PublishPackagedOutputs(workspace);
                WritePhaseMarker(phaseMarkerPath, "packaged outputs published");
            } catch (Exception exception) {
                WritePhaseMarker(phaseMarkerPath, "exception: " + exception);
                throw;
            }
        }

        return Task.FromResult(CreateReport(diagnostics, sceneOutcomes, looseAssetOutcomes));
    }

    /// <summary>
    /// Builds the final build report; the build succeeded when no diagnostic was raised.
    /// </summary>
    /// <param name="diagnostics">Diagnostics raised during the build.</param>
    /// <param name="sceneOutcomes">Per-scene outcomes.</param>
    /// <param name="looseAssetOutcomes">Per-loose-asset outcomes.</param>
    /// <returns>Build report handed back to the editor.</returns>
    static PlatformBuildReport CreateReport(
        List<PlatformBuildDiagnostic> diagnostics,
        List<PlatformBuildItemOutcome> sceneOutcomes,
        List<PlatformBuildItemOutcome> looseAssetOutcomes) {
        return new PlatformBuildReport(
            diagnostics.Count == 0,
            [.. diagnostics],
            [.. sceneOutcomes],
            [.. looseAssetOutcomes]);
    }

    /// <summary>
    /// Validates manifest sources and destinations before staging the existing non-texture artifact flows.
    /// Every output path is still reserved for collision detection, but outputs whose fingerprint (source bytes,
    /// artifact kind, logical id) matches the store and whose staged file exists are not rewritten.
    /// </summary>
    void StageCookedArtifacts(
        PlatformBuildRequest request,
        Ps2CookFingerprintStore fingerprintStore,
        List<PlatformBuildDiagnostic> diagnostics,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        IPlatformBuildProgressReporter progressReporter,
        CancellationToken cancellationToken) {
        PlatformBuildArtifact[] cookedArtifacts = request.Manifest.CookedArtifacts ?? [];
        string root = Path.GetFullPath(Path.Combine(request.WorkingRoot, "ps2-staging"));
        string[] sources = new string[cookedArtifacts.Length];
        string[] destinations = new string[cookedArtifacts.Length];
        string[] outputRelatives = new string[cookedArtifacts.Length];
        HashSet<string> reserved = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < cookedArtifacts.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            PlatformBuildArtifact artifact = cookedArtifacts[index];
            string relative = Ps2TextureCookPath.Normalize(artifact.RelativePath);
            string outputRelative = IsModelArtifact(artifact) ? Path.ChangeExtension(relative, ".phm").Replace('\\', '/') : relative;
            Ps2TextureCookPath.Reserve(reserved, outputRelative);
            outputRelatives[index] = outputRelative;
            sources[index] = Ps2TextureCookPath.ResolveDestination(Directory.GetCurrentDirectory(), relative);
            destinations[index] = Ps2TextureCookPath.ResolveDestination(root, outputRelative);
            if (!File.Exists(sources[index])) {
                AddDiagnostic(diagnostics, diagnosticReporter, PlatformBuildDiagnosticSeverity.Error, "PS2BUILD002",
                    $"Cooked artifact '{artifact.RelativePath}' was not found in the staged package root.",
                    string.Empty, artifact.LogicalArtifactId, artifact.RelativePath);
            }
        }
        if (diagnostics.Count != 0) return;
        for (int index = 0; index < cookedArtifacts.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            PlatformBuildArtifact artifact = cookedArtifacts[index];
            string fingerprint = Ps2CookFingerprintStore.ComputeSourceFileFingerprint(sources[index], artifact.ArtifactKind, artifact.LogicalArtifactId);
            if (fingerprintStore.IsUpToDate(outputRelatives[index], fingerprint, destinations[index])) {
                progressReporter.Report(new PlatformBuildProgressUpdate("Stage Cooked Artifacts", artifact.LogicalArtifactId,
                    index + 1, cookedArtifacts.Length, $"Reused up-to-date cooked artifact '{artifact.RelativePath}'."));
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destinations[index]));
            if (IsModelArtifact(artifact)) {
                WritePs2ModelAsset(artifact, sources[index], destinations[index]);
            } else {
                File.Copy(sources[index], destinations[index], true);
            }
            fingerprintStore.Record(outputRelatives[index], fingerprint);
            progressReporter.Report(new PlatformBuildProgressUpdate("Stage Cooked Artifacts", artifact.LogicalArtifactId,
                index + 1, cookedArtifacts.Length, $"Staged cooked artifact '{artifact.RelativePath}'."));
        }
    }

    static List<PlatformBuildItemOutcome> BuildSceneOutcomes(PlatformBuildScene[] scenes) {
        List<PlatformBuildItemOutcome> outcomes = [];
        if (scenes == null) {
            return outcomes;
        }

        for (int index = 0; index < scenes.Length; index++) {
            outcomes.Add(new PlatformBuildItemOutcome(scenes[index].SceneId, PlatformBuildItemOutcomeKind.Succeeded));
        }

        return outcomes;
    }

    static List<PlatformBuildItemOutcome> BuildLooseAssetOutcomes(PlatformBuildAsset[] looseAssets) {
        List<PlatformBuildItemOutcome> outcomes = [];
        if (looseAssets == null) {
            return outcomes;
        }

        for (int index = 0; index < looseAssets.Length; index++) {
            outcomes.Add(new PlatformBuildItemOutcome(looseAssets[index].AssetId, PlatformBuildItemOutcomeKind.Succeeded));
        }

        return outcomes;
    }

    /// <summary>
    /// Returns whether one staged cooked artifact should be rewritten into the PS2-owned cooked model payload.
    /// </summary>
    /// <param name="artifact">Artifact metadata to inspect.</param>
    /// <returns>True when the artifact is a model payload; otherwise false.</returns>
    static bool IsModelArtifact(PlatformBuildArtifact artifact) {
        if (artifact == null) {
            throw new ArgumentNullException(nameof(artifact));
        }

        return string.Equals(artifact.ArtifactKind, "model", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes one PS2-owned cooked model asset instead of staging the generic cooked model payload.
    /// </summary>
    /// <param name="artifact">Artifact being staged.</param>
    /// <param name="sourcePath">Source path for the generic cooked model payload.</param>
    /// <param name="destinationPath">Destination path for the staged PS2-owned cooked model payload.</param>
    void WritePs2ModelAsset(PlatformBuildArtifact artifact, string sourcePath, string destinationPath) {
        if (artifact == null) {
            throw new ArgumentNullException(nameof(artifact));
        } else if (string.IsNullOrWhiteSpace(sourcePath)) {
            throw new ArgumentException("Source path must be provided for PS2 cooked model generation.", nameof(sourcePath));
        } else if (string.IsNullOrWhiteSpace(destinationPath)) {
            throw new ArgumentException("Destination path must be provided for PS2 cooked model generation.", nameof(destinationPath));
        }

        ModelAsset modelAsset;
        using (FileStream sourceStream = File.OpenRead(sourcePath)) {
            modelAsset = AssetSerializer.Deserialize(sourceStream) as ModelAsset;
        }
        if (modelAsset == null) {
            throw new InvalidOperationException($"Cooked model artifact '{artifact.RelativePath}' did not deserialize as a model asset.");
        }

        byte[] packedMeshBytes = PackedMeshCooker.Cook(modelAsset);
        Ps2ModelAsset ps2ModelAsset = new() {
            Id = string.IsNullOrWhiteSpace(modelAsset.Id) ? artifact.LogicalArtifactId : modelAsset.Id,
            Positions = modelAsset.Positions,
            Normals = modelAsset.Normals,
            TexCoords = modelAsset.TexCoords,
            Indices16 = ResolvePs2Indices16(modelAsset),
            PackedMeshBytes = packedMeshBytes
        };

        Console.WriteLine($"[helengine-ps2] write ps2 model asset begin path={artifact.RelativePath} bytes={packedMeshBytes.Length}");
        File.WriteAllBytes(destinationPath, Ps2AssetSerializer.SerializeToBytes(ps2ModelAsset));
        using FileStream verifyStream = File.OpenRead(destinationPath);
        Ps2ModelAsset verifiedPs2ModelAsset = Ps2AssetSerializer.Deserialize(verifyStream) as Ps2ModelAsset;
        int verifiedLength = verifiedPs2ModelAsset?.PackedMeshBytes?.Length ?? -1;
        Console.WriteLine($"[helengine-ps2] write ps2 model asset verify path={artifact.RelativePath} bytes={verifiedLength}");
    }

    /// <summary>
    /// Builds the staged PS2-owned cooked model artifact path from the generic cooked model artifact path.
    /// </summary>
    /// <param name="modelArtifactPath">Absolute path to the generic cooked model artifact destination.</param>
    /// <returns>Absolute path to the staged PS2-owned cooked model payload.</returns>
    static string BuildPs2ModelArtifactPath(string modelArtifactPath) {
        if (string.IsNullOrWhiteSpace(modelArtifactPath)) {
            throw new ArgumentException("Model artifact path must be provided.", nameof(modelArtifactPath));
        }

        string directoryPath = Path.GetDirectoryName(modelArtifactPath)
            ?? throw new InvalidOperationException($"Could not resolve the directory for model artifact '{modelArtifactPath}'.");
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(modelArtifactPath);
        return Path.Combine(directoryPath, fileNameWithoutExtension + ".phm");
    }

    /// <summary>
    /// Resolves the 16-bit index buffer stored by one PS2-owned cooked model asset.
    /// </summary>
    /// <param name="modelAsset">Generic cooked model asset being converted.</param>
    /// <returns>16-bit PS2 index buffer.</returns>
    static ushort[] ResolvePs2Indices16(ModelAsset modelAsset) {
        if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        } else if (modelAsset.Indices16 != null && modelAsset.Indices16.Length > 0) {
            return modelAsset.Indices16;
        } else if (modelAsset.Indices32 == null || modelAsset.Indices32.Length == 0) {
            return Array.Empty<ushort>();
        }

        ushort[] indices16 = new ushort[modelAsset.Indices32.Length];
        for (int index = 0; index < modelAsset.Indices32.Length; index++) {
            uint rawIndex = modelAsset.Indices32[index];
            if (rawIndex > ushort.MaxValue) {
                throw new InvalidOperationException("PS2 cooked models require 16-bit indices.");
            }

            indices16[index] = (ushort)rawIndex;
        }

        return indices16;
    }

    static void AddDiagnostic(
        List<PlatformBuildDiagnostic> diagnostics,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        PlatformBuildDiagnosticSeverity severity,
        string code,
        string message,
        string sceneId,
        string assetId,
        string sourceIdentity) {
        PlatformBuildDiagnostic diagnostic = new(severity, code, message, sceneId, assetId, sourceIdentity);
        diagnostics.Add(diagnostic);
        diagnosticReporter.Report(diagnostic);
    }

    static string ResolveStagedArtifactSourcePath(string relativePath) {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalizedRelativePath));
    }

    /// <summary>
    /// Appends one diagnostic phase marker into the current PS2 output root so export failures can be recovered after the host process exits.
    /// </summary>
    /// <param name="phaseMarkerPath">Absolute phase-marker file path inside the active output root.</param>
    /// <param name="message">Human-readable phase message to append.</param>
    static void WritePhaseMarker(string phaseMarkerPath, string message) {
        if (string.IsNullOrWhiteSpace(phaseMarkerPath)) {
            throw new ArgumentException("Phase marker path must be provided.", nameof(phaseMarkerPath));
        }
        if (string.IsNullOrWhiteSpace(message)) {
            throw new ArgumentException("Phase marker message must be provided.", nameof(message));
        }

        File.AppendAllText(phaseMarkerPath, message + Environment.NewLine);
        Console.WriteLine("[helengine-ps2] " + message);
    }

    static void VerifyPackagedOutputs(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (!File.Exists(workspace.DiscBootConfigPath)) {
            throw new FileNotFoundException("PS2 disc boot config was not produced.", workspace.DiscBootConfigPath);
        }
        if (!File.Exists(workspace.DiscExecutablePath)) {
            throw new FileNotFoundException("PS2 disc boot executable was not produced.", workspace.DiscExecutablePath);
        }
        if (!File.Exists(workspace.IsoOutputPath)) {
            throw new FileNotFoundException("PS2 ISO output was not produced.", workspace.IsoOutputPath);
        }
    }

    static string NormalizeRelativePath(string path) {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    static string ResolveRepositoryRootPath() {
        string configuredRepositoryRootPath = Environment.GetEnvironmentVariable(RepositoryRootEnvironmentVariableName) ?? string.Empty;
        if (IsRepositoryRootPath(configuredRepositoryRootPath)) {
            return Path.GetFullPath(configuredRepositoryRootPath);
        }

        string currentWorkingDirectoryPath = Directory.GetCurrentDirectory();
        string resolvedCurrentWorkingDirectoryPath = ResolveRepositoryRootPathFromStartPath(currentWorkingDirectoryPath);
        if (!string.IsNullOrWhiteSpace(resolvedCurrentWorkingDirectoryPath)) {
            return resolvedCurrentWorkingDirectoryPath;
        }

        string builderAssemblyPath = typeof(Ps2PlatformAssetBuilder).Assembly.Location;
        string builderAssemblyDirectoryPath = Path.GetDirectoryName(builderAssemblyPath) ?? string.Empty;
        string resolvedBuilderAssemblyDirectoryPath = ResolveRepositoryRootPathFromStartPath(builderAssemblyDirectoryPath);
        if (!string.IsNullOrWhiteSpace(resolvedBuilderAssemblyDirectoryPath)) {
            return resolvedBuilderAssemblyDirectoryPath;
        }

        string resolvedAppContextDirectoryPath = ResolveRepositoryRootPathFromStartPath(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(resolvedAppContextDirectoryPath)) {
            return resolvedAppContextDirectoryPath;
        }

        throw new InvalidOperationException("Could not resolve the helengine-ps2 repository root from the configured root, current working directory, builder assembly location, or host app base directory.");
    }

    /// <summary>
    /// Publishes completed PS2 packaging artifacts from the invocation-private workspace into the caller-selected final output root.
    /// </summary>
    /// <param name="workspace">Prepared PS2 build workspace containing verified private artifacts.</param>
    static void PublishPackagedOutputs(Ps2BuildWorkspace workspace) {
        if (workspace == null) {
            throw new ArgumentNullException(nameof(workspace));
        }

        Directory.CreateDirectory(workspace.OutputRootPath);
        if (Directory.Exists(workspace.PublishedDiscRootPath)) {
            Directory.Delete(workspace.PublishedDiscRootPath, true);
        }

        CopyDirectoryTree(workspace.DiscRootPath, workspace.PublishedDiscRootPath);
        File.Copy(workspace.IsoOutputPath, workspace.PublishedIsoOutputPath, true);
    }

    /// <summary>
    /// Copies every directory and file from one completed artifact tree into its final destination.
    /// </summary>
    /// <param name="sourceRootPath">Existing source artifact root.</param>
    /// <param name="destinationRootPath">Destination root that receives the copied artifact tree.</param>
    static void CopyDirectoryTree(string sourceRootPath, string destinationRootPath) {
        if (string.IsNullOrWhiteSpace(sourceRootPath)) {
            throw new ArgumentException("Source root path must be provided.", nameof(sourceRootPath));
        } else if (string.IsNullOrWhiteSpace(destinationRootPath)) {
            throw new ArgumentException("Destination root path must be provided.", nameof(destinationRootPath));
        } else if (!Directory.Exists(sourceRootPath)) {
            throw new DirectoryNotFoundException($"Source artifact root '{sourceRootPath}' was not found.");
        }

        string[] sourceFilePaths = Directory.GetFiles(sourceRootPath, "*", SearchOption.AllDirectories);
        for (int fileIndex = 0; fileIndex < sourceFilePaths.Length; fileIndex++) {
            string sourceFilePath = sourceFilePaths[fileIndex];
            string relativeFilePath = Path.GetRelativePath(sourceRootPath, sourceFilePath);
            string destinationFilePath = Path.Combine(destinationRootPath, relativeFilePath);
            string destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (string.IsNullOrWhiteSpace(destinationDirectoryPath)) {
                throw new InvalidOperationException($"Artifact destination '{destinationFilePath}' must have a parent directory.");
            }

            Directory.CreateDirectory(destinationDirectoryPath);
            File.Copy(sourceFilePath, destinationFilePath, true);
        }
    }

    /// <summary>
    /// Walks upward from one starting directory until the checked-in PS2 repository root markers are found.
    /// </summary>
    /// <param name="startPath">Candidate directory path used as the initial repository-root search location.</param>
    /// <returns>Resolved repository root path when found; otherwise an empty string.</returns>
    static string ResolveRepositoryRootPathFromStartPath(string startPath) {
        string currentPath = startPath;
        while (!string.IsNullOrWhiteSpace(currentPath)) {
            if (IsRepositoryRootPath(currentPath)) {
                return currentPath;
            }

            DirectoryInfo parentDirectory = Directory.GetParent(currentPath);
            if (parentDirectory == null) {
                break;
            }

            currentPath = parentDirectory.FullName;
        }

        return string.Empty;
    }

    static bool IsRepositoryRootPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        string makefilePath = Path.Combine(path, "Makefile");
        string bootHostPath = Path.Combine(path, "src", "platform", "ps2", "Ps2BootHost.cpp");
        return File.Exists(makefilePath) && File.Exists(bootHostPath);
    }

    static Ps2BuildWorkspace CreateWorkspace(PlatformBuildRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        string repositoryRootPath = ResolveRepositoryRootPath();
        string nativeBuildRootPath = string.IsNullOrWhiteSpace(request.NativeObjectCacheRoot)
            ? Path.Combine(request.WorkingRoot, "ps2-native")
            : request.NativeObjectCacheRoot;
        string nativeExecutablePath = Path.Combine(nativeBuildRootPath, "helengine_ps2.elf");
        string stagingRootPath = Path.Combine(request.WorkingRoot, "ps2-staging");
        string artifactRootPath = Path.Combine(request.WorkingRoot, "ps2-artifacts");
        return new Ps2BuildWorkspace(
            repositoryRootPath,
            stagingRootPath,
            request.GeneratedCoreCppRootPath,
            request.OutputRoot,
            artifactRootPath,
            nativeExecutablePath);
    }

    /// <summary>
    /// Validates the build request; assets-only builds (editor Play) need no generated core, so its checks only apply to full builds.
    /// </summary>
    /// <param name="request">Build request to validate.</param>
    static void ValidateRequest(PlatformBuildRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (request.ExecutionScope == PlatformBuildExecutionScope.AssetsOnly) {
            return;
        }
        if (string.IsNullOrWhiteSpace(request.GeneratedCoreCppRootPath)) {
            throw new ArgumentException("Generated core root path must be provided for PS2 builds.", nameof(request));
        }
        if (!Directory.Exists(request.GeneratedCoreCppRootPath)) {
            throw new DirectoryNotFoundException($"Generated core root '{request.GeneratedCoreCppRootPath}' was not found.");
        }
    }
}





