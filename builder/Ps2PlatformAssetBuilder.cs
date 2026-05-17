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
    const string RepositoryRootEnvironmentVariableName = "HELENGINE_PS2_REPOSITORY_ROOT";

    readonly IPs2NativeBuildExecutor NativeBuildExecutor;
    readonly Ps2MaterialCooker MaterialCooker;
    readonly Ps2PackedMeshCooker PackedMeshCooker;
    readonly Ps2PlatformCookWorkItemExecutor PlatformCookWorkItemExecutor;
    readonly Ps2DiscLayoutWriter DiscLayoutWriter;
    readonly Ps2RuntimeAssetPathManifestWriter RuntimeAssetPathManifestWriter;
    readonly Ps2CookedAssetPathRewriter CookedAssetPathRewriter;

    public Ps2PlatformAssetBuilder() {
        NativeBuildExecutor = new Ps2NativeBuildExecutor();
        MaterialCooker = new Ps2MaterialCooker();
        PackedMeshCooker = new Ps2PackedMeshCooker();
        PlatformCookWorkItemExecutor = new Ps2PlatformCookWorkItemExecutor();
        DiscLayoutWriter = new Ps2DiscLayoutWriter();
        RuntimeAssetPathManifestWriter = new Ps2RuntimeAssetPathManifestWriter();
        CookedAssetPathRewriter = new Ps2CookedAssetPathRewriter();
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
        CookedAssetPathRewriter = new Ps2CookedAssetPathRewriter();
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

        Directory.CreateDirectory(request.OutputRoot);
        Directory.CreateDirectory(request.WorkingRoot);
        string phaseMarkerPath = Path.Combine(request.OutputRoot, "ps2-build-phase.txt");

        List<PlatformBuildDiagnostic> diagnostics = [];
        List<PlatformBuildItemOutcome> sceneOutcomes = BuildSceneOutcomes(request.Manifest.Scenes);
        List<PlatformBuildItemOutcome> looseAssetOutcomes = BuildLooseAssetOutcomes(request.Manifest.LooseAssets);
        StageCookedArtifacts(request, diagnostics, diagnosticReporter, progressReporter, cancellationToken);
        PlatformCookWorkItemExecutor.Execute(request, diagnostics, diagnosticReporter, progressReporter, cancellationToken);

        if (diagnostics.Count == 0) {
            try {
                Ps2BuildWorkspace workspace = CreateWorkspace(request);
                WritePhaseMarker(phaseMarkerPath, "workspace created");
                IReadOnlyDictionary<string, string> logicalToPhysicalPaths = DiscLayoutWriter.BuildLogicalToPhysicalPathMap(workspace);
                WritePhaseMarker(phaseMarkerPath, "logical path map built");
                CookedAssetPathRewriter.Rewrite(workspace.StagingRootPath, logicalToPhysicalPaths);
                WritePhaseMarker(phaseMarkerPath, "cooked asset paths rewritten");
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
            } catch (Exception exception) {
                WritePhaseMarker(phaseMarkerPath, "exception: " + exception);
                throw;
            }
        }

        bool succeeded = diagnostics.Count == 0;

        return Task.FromResult(new PlatformBuildReport(
            succeeded,
            [.. diagnostics],
            [.. sceneOutcomes],
            [.. looseAssetOutcomes]));
    }

    void StageCookedArtifacts(
        PlatformBuildRequest request,
        List<PlatformBuildDiagnostic> diagnostics,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        IPlatformBuildProgressReporter progressReporter,
        CancellationToken cancellationToken) {
        PlatformBuildArtifact[] cookedArtifacts = request.Manifest.CookedArtifacts ?? [];
        for (int artifactIndex = 0; artifactIndex < cookedArtifacts.Length; artifactIndex++) {
            cancellationToken.ThrowIfCancellationRequested();

            PlatformBuildArtifact artifact = cookedArtifacts[artifactIndex];
            if (string.IsNullOrWhiteSpace(artifact.RelativePath)) {
                AddDiagnostic(
                    diagnostics,
                    diagnosticReporter,
                    PlatformBuildDiagnosticSeverity.Error,
                    "PS2BUILD001",
                    "Cooked artifact relative path is required.",
                    string.Empty,
                    string.Empty,
                    string.Empty);
                continue;
            }

            string sourcePath = ResolveStagedArtifactSourcePath(artifact.RelativePath);
            if (!File.Exists(sourcePath)) {
                AddDiagnostic(
                    diagnostics,
                    diagnosticReporter,
                    PlatformBuildDiagnosticSeverity.Error,
                    "PS2BUILD002",
                    $"Cooked artifact '{artifact.RelativePath}' was not found in the staged package root.",
                    string.Empty,
                    artifact.LogicalArtifactId,
                    artifact.RelativePath);
                continue;
            }

            string destinationPath = Path.Combine(request.WorkingRoot, "ps2-staging", NormalizeRelativePath(artifact.RelativePath));
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourcePath, destinationPath, true);
            EmbedPackedMeshBytes(artifact, destinationPath);
            progressReporter.Report(new PlatformBuildProgressUpdate(
                "Stage Cooked Artifacts",
                artifact.LogicalArtifactId,
                artifactIndex + 1,
                cookedArtifacts.Length,
                $"Staged cooked artifact '{artifact.RelativePath}'."));
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
    /// Embeds the first packed PS2 mesh payload inside staged cooked model artifacts.
    /// </summary>
    /// <param name="artifact">Artifact being staged.</param>
    /// <param name="destinationPath">Destination path for the staged artifact.</param>
    void EmbedPackedMeshBytes(PlatformBuildArtifact artifact, string destinationPath) {
        if (artifact == null) {
            throw new ArgumentNullException(nameof(artifact));
        } else if (!string.Equals(artifact.ArtifactKind, "model", StringComparison.OrdinalIgnoreCase)) {
            return;
        } else if (string.IsNullOrWhiteSpace(destinationPath)) {
            throw new ArgumentException("Destination path must be provided for packed mesh embedding.", nameof(destinationPath));
        }

        ModelAsset modelAsset;
        using (FileStream destinationStream = File.OpenRead(destinationPath)) {
            modelAsset = AssetSerializer.Deserialize(destinationStream) as ModelAsset;
        }
        if (modelAsset == null) {
            throw new InvalidOperationException($"Cooked model artifact '{artifact.RelativePath}' did not deserialize as a model asset.");
        }

        byte[] packedMeshBytes = PackedMeshCooker.Cook(modelAsset);
        Console.WriteLine($"[helengine-ps2] embed packed mesh begin path={artifact.RelativePath} bytes={packedMeshBytes.Length}");
        modelAsset.Ps2PackedMeshBytes = packedMeshBytes;
        File.WriteAllBytes(destinationPath, helengine.files.AssetSerializer.SerializeToBytes(modelAsset));
        using (FileStream verifyStream = File.OpenRead(destinationPath)) {
            ModelAsset verifiedModelAsset = AssetSerializer.Deserialize(verifyStream) as ModelAsset;
            int verifiedLength = verifiedModelAsset?.Ps2PackedMeshBytes?.Length ?? -1;
            Console.WriteLine($"[helengine-ps2] embed packed mesh verify path={artifact.RelativePath} bytes={verifiedLength}");
        }
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

        string currentPath = AppContext.BaseDirectory;
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

        throw new InvalidOperationException("Could not resolve the helengine-ps2 repository root from the builder assembly location.");
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
        string nativeExecutablePath = Path.Combine(repositoryRootPath, "build", "helengine_ps2.elf");
        string stagingRootPath = Path.Combine(request.WorkingRoot, "ps2-staging");
        return new Ps2BuildWorkspace(
            repositoryRootPath,
            stagingRootPath,
            request.GeneratedCoreCppRootPath,
            request.OutputRoot,
            nativeExecutablePath);
    }

    static void ValidateRequest(PlatformBuildRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.GeneratedCoreCppRootPath)) {
            throw new ArgumentException("Generated core root path must be provided for PS2 builds.", nameof(request));
        }
        if (!Directory.Exists(request.GeneratedCoreCppRootPath)) {
            throw new DirectoryNotFoundException($"Generated core root '{request.GeneratedCoreCppRootPath}' was not found.");
        }
    }
}





