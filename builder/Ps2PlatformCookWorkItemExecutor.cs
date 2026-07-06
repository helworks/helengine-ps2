using helengine.baseplatform.Builders;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.editor;
using helengine.files;
using System.Reflection;

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
                    $"Failed to execute platform cook work item '{workItem.WorkItemId}' from '{workItem.SourceAssetPath}' (kind '{workItem.SourceAssetKind}'): {exception.Message}",
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
        TextureAssetProcessorSettings settings = Ps2TextureCookSettingsSerializer.Deserialize(workItem.SerializedPlatformSettings);
        Ps2TextureAsset cookedTexture = RuntimeTextureCooker.Cook(sourceTexture, settings);

        string destinationPath = Path.Combine(request.WorkingRoot, "ps2-staging", NormalizeRelativePath(workItem.OutputRelativePath));
        string destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        cookedTexture.Id = workItem.OutputLogicalArtifactId;
        File.WriteAllBytes(destinationPath, Ps2AssetSerializer.SerializeToBytes(cookedTexture));
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

        TextureAsset serializedTextureAsset = TryLoadSerializedTextureAsset(workItem.SourceAssetPath);
        if (serializedTextureAsset != null) {
            return serializedTextureAsset;
        }

        if (string.Equals(workItem.SourceAssetKind, "font-atlas-texture", StringComparison.OrdinalIgnoreCase)) {
            TextureAsset fontAtlasTexture = TryLoadPackagedFontAtlasSourceTexture(workItem.SourceAssetPath);
            if (fontAtlasTexture != null) {
                return fontAtlasTexture;
            }

            TextureAsset importedSourceFontTexture = TryLoadImportedSourceFontTexture(workItem.SourceAssetPath);
            if (importedSourceFontTexture != null) {
                return importedSourceFontTexture;
            }
        }

        return SourceTextureDecoder.Decode(workItem.SourceAssetPath);
    }

    /// <summary>
    /// Attempts to load one serialized raw texture asset staged by the editor under one `.hasset` or `.hetex` source path.
    /// </summary>
    /// <param name="sourceAssetPath">Absolute source asset path declared by the editor work item.</param>
    /// <returns>Serialized texture asset when the source path points at one texture asset payload; otherwise null.</returns>
    static TextureAsset TryLoadSerializedTextureAsset(string sourceAssetPath) {
        if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
            return null;
        }

        string extension = Path.GetExtension(sourceAssetPath);
        if (!string.Equals(extension, ".hasset", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".hetex", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        using FileStream stream = File.OpenRead(sourceAssetPath);
        return AssetSerializer.Deserialize(stream) as TextureAsset;
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
    /// Attempts to import one raw source font through the existing Windows editor font importer and return its generated atlas texture.
    /// </summary>
    /// <param name="sourceAssetPath">Absolute source font path declared by the editor work item.</param>
    /// <returns>Imported raw atlas texture when the source path points at one supported raw font file; otherwise null.</returns>
    static TextureAsset TryLoadImportedSourceFontTexture(string sourceAssetPath) {
        if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
            return null;
        }

        string extension = Path.GetExtension(sourceAssetPath);
        if (!string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        Assembly editorWindowsAssembly = ResolveEditorWindowsAssembly();
        Type importerType = editorWindowsAssembly.GetType("helengine.editor.GdiFontImporter", throwOnError: true)
            ?? throw new InvalidOperationException("The helengine.editor GdiFontImporter type could not be resolved.");
        object importer = Activator.CreateInstance(importerType)
            ?? throw new InvalidOperationException("The helengine.editor GdiFontImporter could not be created.");
        MethodInfo importMethod = importerType.GetMethod("ImportFont", BindingFlags.Instance | BindingFlags.Public, [typeof(Stream)])
            ?? throw new InvalidOperationException("The helengine.editor GdiFontImporter.ImportFont method could not be resolved.");

        using FileStream stream = File.OpenRead(sourceAssetPath);
        FontAsset fontAsset = importMethod.Invoke(importer, [stream]) as FontAsset;
        if (fontAsset == null || fontAsset.SourceTextureAsset == null) {
            throw new InvalidOperationException($"Raw source font '{sourceAssetPath}' did not produce an atlas texture during host import.");
        }

        return fontAsset.SourceTextureAsset;
    }

    /// <summary>
    /// Resolves the helengine Windows editor assembly that exposes the host font importer implementation.
    /// </summary>
    /// <returns>Loaded helengine.editor.windows assembly.</returns>
    static Assembly ResolveEditorWindowsAssembly() {
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < loadedAssemblies.Length; index++) {
            Assembly loadedAssembly = loadedAssemblies[index];
            if (string.Equals(loadedAssembly.GetName().Name, "helengine.editor.windows", StringComparison.OrdinalIgnoreCase)) {
                return loadedAssembly;
            }
        }

        string helengineRootPath = ResolveHelengineRootPath();
        string assemblyPath = Path.Combine(helengineRootPath, "engine", "helengine.editor.windows", "bin", "Debug", "net9.0-windows", "helengine.editor.windows.dll");
        if (!File.Exists(assemblyPath)) {
            throw new FileNotFoundException("The helengine.editor.windows assembly required for raw font atlas import was not found.", assemblyPath);
        }

        return Assembly.LoadFrom(assemblyPath);
    }

    /// <summary>
    /// Resolves the sibling helengine checkout root so the PS2 builder can load optional host-only editor helpers.
    /// </summary>
    /// <returns>Absolute helengine checkout root path.</returns>
    static string ResolveHelengineRootPath() {
        string configuredRootPath = Environment.GetEnvironmentVariable("HELENGINE_ROOT") ?? string.Empty;
        if (IsHelengineRootPath(configuredRootPath)) {
            return Path.GetFullPath(configuredRootPath);
        }

        string currentPath = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(currentPath)) {
            string siblingHelengineRootPath = Path.GetFullPath(Path.Combine(currentPath, "..", "..", "..", "..", "..", "helengine"));
            if (IsHelengineRootPath(siblingHelengineRootPath)) {
                return siblingHelengineRootPath;
            }

            DirectoryInfo parentDirectory = Directory.GetParent(currentPath);
            if (parentDirectory == null) {
                break;
            }

            currentPath = parentDirectory.FullName;
        }

        throw new InvalidOperationException("Could not resolve the sibling helengine checkout required for raw font atlas import.");
    }

    /// <summary>
    /// Returns whether the supplied path looks like one valid helengine checkout root.
    /// </summary>
    /// <param name="path">Candidate helengine checkout root.</param>
    /// <returns>True when the expected helengine editor projects exist; otherwise false.</returns>
    static bool IsHelengineRootPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        string editorProjectPath = Path.Combine(path, "engine", "helengine.editor", "helengine.editor.csproj");
        string editorWindowsProjectPath = Path.Combine(path, "engine", "helengine.editor.windows", "helengine.editor.windows.csproj");
        return File.Exists(editorProjectPath) && File.Exists(editorWindowsProjectPath);
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
