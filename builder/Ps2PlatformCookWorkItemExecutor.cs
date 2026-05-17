using helengine.baseplatform.Builders;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.files;

namespace helengine.ps2.builder;

/// <summary>
/// Executes builder-owned PS2 platform cook work items and writes their final runtime artifacts into staging.
/// </summary>
public sealed class Ps2PlatformCookWorkItemExecutor {
    readonly Ps2SourceTextureDecoder SourceTextureDecoder;
    readonly Ps2RuntimeTextureCooker RuntimeTextureCooker;

    /// <summary>
    /// Initializes a new PS2 platform cook work-item executor with default decode and cook services.
    /// </summary>
    public Ps2PlatformCookWorkItemExecutor() {
        SourceTextureDecoder = new Ps2SourceTextureDecoder();
        RuntimeTextureCooker = new Ps2RuntimeTextureCooker();
    }

    /// <summary>
    /// Executes the PS2-targeted platform cook work items from one manifest.
    /// </summary>
    /// <param name="request">Active platform build request.</param>
    /// <param name="diagnostics">Mutable diagnostic list.</param>
    /// <param name="diagnosticReporter">Diagnostic reporter used by the builder.</param>
    /// <param name="progressReporter">Progress reporter used by the builder.</param>
    /// <param name="cancellationToken">Cancellation token that stops work-item execution.</param>
    public void Execute(
        PlatformBuildRequest request,
        List<PlatformBuildDiagnostic> diagnostics,
        IPlatformBuildDiagnosticReporter diagnosticReporter,
        IPlatformBuildProgressReporter progressReporter,
        CancellationToken cancellationToken) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (diagnostics == null) {
            throw new ArgumentNullException(nameof(diagnostics));
        }
        if (diagnosticReporter == null) {
            throw new ArgumentNullException(nameof(diagnosticReporter));
        }
        if (progressReporter == null) {
            throw new ArgumentNullException(nameof(progressReporter));
        }

        PlatformCookWorkItem[] workItems = request.Manifest.PlatformCookWorkItems ?? Array.Empty<PlatformCookWorkItem>();
        for (int workItemIndex = 0; workItemIndex < workItems.Length; workItemIndex++) {
            cancellationToken.ThrowIfCancellationRequested();

            PlatformCookWorkItem workItem = workItems[workItemIndex];
            if (!string.Equals(workItem.TargetPlatformId, "ps2", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            try {
                ExecuteWorkItem(request, workItem);
                progressReporter.Report(new PlatformBuildProgressUpdate(
                    "Execute Platform Cook Work Items",
                    workItem.OutputLogicalArtifactId,
                    workItemIndex + 1,
                    workItems.Length,
                    $"Executed PS2 platform cook work item '{workItem.WorkItemId}'."));
            } catch (Exception exception) {
                AddDiagnostic(
                    diagnostics,
                    diagnosticReporter,
                    PlatformBuildDiagnosticSeverity.Error,
                    "PS2BUILD003",
                    $"Failed to execute platform cook work item '{workItem.WorkItemId}': {exception.Message}",
                    string.Empty,
                    workItem.OutputLogicalArtifactId,
                    workItem.SourceAssetPath);
            }
        }
    }

    /// <summary>
    /// Executes one supported PS2 work item and writes the final runtime artifact into staging.
    /// </summary>
    /// <param name="request">Active build request.</param>
    /// <param name="workItem">Work item to execute.</param>
    void ExecuteWorkItem(PlatformBuildRequest request, PlatformCookWorkItem workItem) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (workItem == null) {
            throw new ArgumentNullException(nameof(workItem));
        }

        if (string.Equals(workItem.SourceAssetKind, "texture", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workItem.SourceAssetKind, "font-atlas-texture", StringComparison.OrdinalIgnoreCase)) {
            ExecuteTextureWorkItem(request, workItem);
            return;
        }

        throw new InvalidOperationException($"PS2 does not support source asset kind '{workItem.SourceAssetKind}' yet.");
    }

    /// <summary>
    /// Executes one texture-like PS2 work item and writes the cooked runtime texture payload into staging.
    /// </summary>
    /// <param name="request">Active build request.</param>
    /// <param name="workItem">Texture-like work item to execute.</param>
    void ExecuteTextureWorkItem(PlatformBuildRequest request, PlatformCookWorkItem workItem) {
        TextureAsset sourceTexture = ResolveSourceTexture(workItem);
        Ps2TextureCookSettings settings = Ps2TextureCookSettingsSerializer.Deserialize(workItem.SerializedPlatformSettings);
        Ps2TextureAsset cookedTexture = RuntimeTextureCooker.Cook(sourceTexture, settings);

        string destinationPath = Path.Combine(request.WorkingRoot, "ps2-staging", NormalizeRelativePath(workItem.OutputRelativePath));
        string destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        cookedTexture.Id = workItem.OutputLogicalArtifactId;
        File.WriteAllBytes(destinationPath, helengine.files.AssetSerializer.SerializeToBytes(cookedTexture));
    }

    /// <summary>
    /// Resolves the texture payload that should be cooked for one PS2 work item.
    /// </summary>
    /// <param name="workItem">Work item whose source payload should be resolved.</param>
    /// <returns>Texture payload to cook into the final PS2 runtime texture artifact.</returns>
    TextureAsset ResolveSourceTexture(PlatformCookWorkItem workItem) {
        if (workItem == null) {
            throw new ArgumentNullException(nameof(workItem));
        }

        if (string.Equals(workItem.SourceAssetKind, "font-atlas-texture", StringComparison.OrdinalIgnoreCase)) {
            TextureAsset fontAtlasTexture = TryLoadPackagedFontAtlasSourceTexture(workItem.SourceAssetPath);
            if (fontAtlasTexture != null) {
                return fontAtlasTexture;
            }
        }

        return SourceTextureDecoder.Decode(workItem.SourceAssetPath);
    }

    /// <summary>
    /// Attempts to load the embedded raw atlas texture from one packaged font source asset.
    /// </summary>
    /// <param name="sourceAssetPath">Absolute source asset path declared by the editor work item.</param>
    /// <returns>Embedded raw atlas texture when the source is one packaged font asset; otherwise null.</returns>
    static TextureAsset TryLoadPackagedFontAtlasSourceTexture(string sourceAssetPath) {
        if (string.IsNullOrWhiteSpace(sourceAssetPath) || !string.Equals(Path.GetExtension(sourceAssetPath), ".hefont", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        using FileStream stream = File.OpenRead(sourceAssetPath);
        FontAsset fontAsset = helengine.files.FontAssetBinarySerializer.Deserialize(stream);
        if (fontAsset == null || fontAsset.SourceTextureAsset == null) {
            throw new InvalidOperationException($"Packaged font source '{sourceAssetPath}' did not contain an embedded raw atlas texture payload.");
        }

        return fontAsset.SourceTextureAsset;
    }

    /// <summary>
    /// Adds one builder diagnostic and forwards it to the active reporter.
    /// </summary>
    /// <param name="diagnostics">Mutable diagnostic list.</param>
    /// <param name="diagnosticReporter">Diagnostic reporter used by the builder.</param>
    /// <param name="severity">Diagnostic severity.</param>
    /// <param name="code">Stable diagnostic code.</param>
    /// <param name="message">Human-readable diagnostic message.</param>
    /// <param name="sceneId">Optional scene identity.</param>
    /// <param name="assetId">Optional asset identity.</param>
    /// <param name="sourceIdentity">Optional source identity.</param>
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

    /// <summary>
    /// Normalizes one runtime-relative path into a filesystem-safe relative path.
    /// </summary>
    /// <param name="path">Runtime-relative path to normalize.</param>
    /// <returns>Filesystem-safe relative path.</returns>
    static string NormalizeRelativePath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Relative path is required.", nameof(path));
        }

        return path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    }
}
