using System.Text.Json;
using helengine.baseplatform.Builders;
using helengine.baseplatform.Descriptors;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Targets;
using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

public sealed class Ps2PlatformAssetBuilder : IPlatformAssetBuilder {
    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public Ps2PlatformAssetBuilder() {
        Descriptor = new PlatformBuilderDescriptor(
            "helengine.ps2.builder",
            "1.0.0",
            "ps2",
            new EngineCompatibilityRange("1.0.0", "999.0.0"),
            new ManifestCompatibilityRange(1, 1),
            ["ps2"],
            ["ps2"]);
        Definition = Ps2PlatformDefinitionFactory.Create();
    }

    public PlatformBuilderDescriptor Descriptor { get; }

    public PlatformDefinition Definition { get; }

    public Task<PlatformBuildReport> BuildAsync(
        PlatformBuildRequest request,
        IPlatformBuildProgressReporter progressReporter,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        CancellationToken cancellationToken) {
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

        List<PlatformBuildDiagnostic> diagnostics = [];
        List<PlatformBuildItemOutcome> sceneOutcomes = [];
        List<PlatformBuildItemOutcome> looseAssetOutcomes = [];
        List<Ps2BuildManifestEntry> sceneEntries = [];
        List<Ps2BuildManifestEntry> looseAssetEntries = [];

        int totalItems = request.Manifest.Scenes.Length + request.Manifest.LooseAssets.Length;
        int completedItems = 0;

        for (int sceneIndex = 0; sceneIndex < request.Manifest.Scenes.Length; sceneIndex++) {
            cancellationToken.ThrowIfCancellationRequested();

            PlatformBuildScene scene = request.Manifest.Scenes[sceneIndex];
            CopyPayload(
                scene.SceneId,
                scene.SourceIdentity,
                request.OutputRoot,
                diagnostics,
                diagnosticReporter,
                out bool copied,
                out string outputPath);

            if (copied) {
                sceneOutcomes.Add(new PlatformBuildItemOutcome(scene.SceneId, PlatformBuildItemOutcomeKind.Succeeded));
            } else {
                sceneOutcomes.Add(new PlatformBuildItemOutcome(scene.SceneId, PlatformBuildItemOutcomeKind.Failed));
            }

            completedItems++;
            progressReporter.Report(new PlatformBuildProgressUpdate(
                "Stage Payloads",
                scene.SceneId,
                completedItems,
                totalItems,
                copied ? $"Staged scene '{scene.SceneName}'." : $"Failed to stage scene '{scene.SceneName}'."));

            if (copied) {
                sceneEntries.Add(new Ps2BuildManifestEntry(scene.SceneId, scene.SourceIdentity, outputPath));
            }
        }

        for (int assetIndex = 0; assetIndex < request.Manifest.LooseAssets.Length; assetIndex++) {
            cancellationToken.ThrowIfCancellationRequested();

            PlatformBuildAsset asset = request.Manifest.LooseAssets[assetIndex];
            CopyPayload(
                asset.AssetId,
                asset.SourceIdentity,
                request.OutputRoot,
                diagnostics,
                diagnosticReporter,
                out bool copied,
                out string outputPath);

            if (copied) {
                looseAssetOutcomes.Add(new PlatformBuildItemOutcome(asset.AssetId, PlatformBuildItemOutcomeKind.Succeeded));
            } else {
                looseAssetOutcomes.Add(new PlatformBuildItemOutcome(asset.AssetId, PlatformBuildItemOutcomeKind.Failed));
            }

            completedItems++;
            progressReporter.Report(new PlatformBuildProgressUpdate(
                "Stage Payloads",
                asset.AssetId,
                completedItems,
                totalItems,
                copied ? $"Staged asset '{asset.AssetName}'." : $"Failed to stage asset '{asset.AssetName}'."));

            if (copied) {
                looseAssetEntries.Add(new Ps2BuildManifestEntry(asset.AssetId, asset.SourceIdentity, outputPath));
            }
        }

        WriteBuildManifest(request, sceneEntries, looseAssetEntries);

        bool succeeded = diagnostics.Count == 0
            && sceneOutcomes.TrueForAll(outcome => outcome.OutcomeKind == PlatformBuildItemOutcomeKind.Succeeded)
            && looseAssetOutcomes.TrueForAll(outcome => outcome.OutcomeKind == PlatformBuildItemOutcomeKind.Succeeded);

        return Task.FromResult(new PlatformBuildReport(
            succeeded,
            [.. diagnostics],
            [.. sceneOutcomes],
            [.. looseAssetOutcomes]));
    }

    void CopyPayload(
        string itemId,
        string sourceIdentity,
        string outputRoot,
        List<PlatformBuildDiagnostic> diagnostics,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        out bool copied,
        out string outputPath) {
        copied = false;
        outputPath = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceIdentity)) {
            AddDiagnostic(
                diagnostics,
                diagnosticReporter,
                PlatformBuildDiagnosticSeverity.Error,
                "PS2BUILD001",
                $"Item '{itemId}' is missing a source identity.",
                string.Empty,
                itemId,
                string.Empty);
            return;
        }

        string sourcePath = ResolveSourcePath(sourceIdentity);
        if (!File.Exists(sourcePath)) {
            AddDiagnostic(
                diagnostics,
                diagnosticReporter,
                PlatformBuildDiagnosticSeverity.Error,
                "PS2BUILD002",
                $"Payload source '{sourceIdentity}' was not found.",
                string.Empty,
                itemId,
                sourceIdentity);
            return;
        }

        outputPath = ResolveOutputPath(outputRoot, sourceIdentity);
        string destinationDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, outputPath, true);
        copied = true;
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

    void WriteBuildManifest(
        PlatformBuildRequest request,
        IReadOnlyList<Ps2BuildManifestEntry> sceneEntries,
        IReadOnlyList<Ps2BuildManifestEntry> looseAssetEntries) {
        string manifestPath = Path.Combine(request.WorkingRoot, "ps2-build-manifest.json");
        Ps2BuildManifestDocument manifest = new(
            request.Manifest.ProjectId,
            request.Manifest.ProjectVersion,
            request.Manifest.RequiredEngineVersion,
            request.OutputRoot,
            sceneEntries,
            looseAssetEntries);

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    static string ResolveOutputPath(string outputRoot, string sourceIdentity) {
        string normalizedSourceIdentity = NormalizeRelativePath(sourceIdentity);
        if (Path.IsPathRooted(normalizedSourceIdentity)) {
            normalizedSourceIdentity = Path.GetFileName(normalizedSourceIdentity);
        }

        return Path.GetFullPath(Path.Combine(outputRoot, normalizedSourceIdentity));
    }

    static string ResolveSourcePath(string sourceIdentity) {
        string normalizedSourceIdentity = NormalizeRelativePath(sourceIdentity);
        if (Path.IsPathRooted(normalizedSourceIdentity)) {
            return Path.GetFullPath(normalizedSourceIdentity);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), normalizedSourceIdentity));
    }

    static string NormalizeRelativePath(string path) {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    sealed record Ps2BuildManifestDocument(
        string ProjectId,
        string ProjectVersion,
        string RequiredEngineVersion,
        string OutputRoot,
        IReadOnlyList<Ps2BuildManifestEntry> Scenes,
        IReadOnlyList<Ps2BuildManifestEntry> LooseAssets);

    sealed record Ps2BuildManifestEntry(
        string ItemId,
        string SourceIdentity,
        string OutputPath);
}
