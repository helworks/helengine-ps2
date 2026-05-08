using System.Text;
using helengine.baseplatform.Manifest;

namespace helengine.ps2.builder;

/// <summary>
/// Writes PS2-specific native startup-scene path metadata into the generated-core runtime folder.
/// </summary>
public sealed class Ps2RuntimeAssetPathManifestWriter {
    /// <summary>
    /// Writes the PS2 physical startup path into generated native runtime source.
    /// </summary>
    /// <param name="generatedCoreRootPath">Generated core root that receives the runtime manifest files.</param>
    /// <param name="manifest">Cooked platform build manifest that defines the startup scene.</param>
    /// <param name="logicalToPhysicalPaths">Logical-to-physical staged PS2 disc path mappings that must include the startup scene.</param>
    public void Write(
        string generatedCoreRootPath,
        PlatformBuildManifest manifest,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
            throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
        }
        if (manifest == null) {
            throw new ArgumentNullException(nameof(manifest));
        }
        if (logicalToPhysicalPaths == null) {
            throw new ArgumentNullException(nameof(logicalToPhysicalPaths));
        }

        string runtimeRootPath = Path.Combine(generatedCoreRootPath, "runtime");
        Directory.CreateDirectory(runtimeRootPath);

        string startupLogicalPath = ResolveStartupSceneLogicalPath(manifest);
        if (!logicalToPhysicalPaths.TryGetValue(startupLogicalPath, out string startupPhysicalPath)) {
            throw new InvalidOperationException($"The startup scene '{startupLogicalPath}' was not staged into a PS2 physical disc path.");
        }

        File.WriteAllText(Path.Combine(runtimeRootPath, "runtime_ps2_asset_path_manifest.hpp"), BuildHeaderContents());
        File.WriteAllText(
            Path.Combine(runtimeRootPath, "runtime_ps2_asset_path_manifest.cpp"),
            BuildSourceContents(BuildRuntimePhysicalPath(startupPhysicalPath)));
    }

    /// <summary>
    /// Resolves the logical cooked path stored on the configured startup scene metadata.
    /// </summary>
    /// <param name="manifest">Cooked platform build manifest that defines the startup scene.</param>
    /// <returns>Logical cooked path for the configured startup scene.</returns>
    static string ResolveStartupSceneLogicalPath(PlatformBuildManifest manifest) {
        if (manifest.Scenes == null || manifest.Scenes.Length == 0 || string.IsNullOrWhiteSpace(manifest.StartupSceneId)) {
            throw new InvalidOperationException("The cooked manifest did not define a startup scene.");
        }

        for (int sceneIndex = 0; sceneIndex < manifest.Scenes.Length; sceneIndex++) {
            PlatformBuildScene scene = manifest.Scenes[sceneIndex];
            if (!string.Equals(scene.SceneId, manifest.StartupSceneId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (scene.ResolvedMetadata == null) {
                break;
            }

            for (int metadataIndex = 0; metadataIndex < scene.ResolvedMetadata.Length; metadataIndex++) {
                KeyValuePair<string, string> entry = scene.ResolvedMetadata[metadataIndex];
                if (string.Equals(entry.Key, "cooked-relative-path", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(entry.Value)) {
                    return entry.Value.Replace('\\', '/');
                }
            }
        }

        throw new InvalidOperationException("The startup scene did not resolve a cooked-relative-path metadata entry.");
    }

    /// <summary>
    /// Builds the generated PS2 asset-path manifest header source.
    /// </summary>
    /// <returns>Header contents for the generated PS2 runtime asset-path manifest.</returns>
    static string BuildHeaderContents() {
        return
            "#pragma once\n\n"
            + "const char* he_get_runtime_ps2_startup_scene_path();\n";
    }

    /// <summary>
    /// Builds the generated PS2 startup-path manifest implementation source.
    /// </summary>
    /// <param name="startupPhysicalPath">Resolved rooted PS2 startup-scene runtime path.</param>
    /// <returns>Generated source contents for the PS2 runtime startup-path manifest.</returns>
    static string BuildSourceContents(string startupPhysicalPath) {
        StringBuilder builder = new();
        builder.AppendLine("#include \"runtime/runtime_ps2_asset_path_manifest.hpp\"");
        builder.AppendLine();
        builder.AppendLine("static const char kRuntimePs2StartupScenePath[] = \"" + EscapeCpp(startupPhysicalPath) + "\";");
        builder.AppendLine();
        builder.AppendLine("const char* he_get_runtime_ps2_startup_scene_path() {");
        builder.AppendLine("    return kRuntimePs2StartupScenePath;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    /// Builds the rooted PS2 runtime path for one staged physical disc path.
    /// </summary>
    /// <param name="physicalDiscPath">Physical staged PS2 disc path.</param>
    /// <returns>Rooted PS2 runtime path that reads directly from `cdrom0:`.</returns>
    static string BuildRuntimePhysicalPath(string physicalDiscPath) {
        if (string.IsNullOrWhiteSpace(physicalDiscPath)) {
            throw new ArgumentException("Physical disc path must be provided.", nameof(physicalDiscPath));
        }

        string normalizedPhysicalPath = physicalDiscPath.Replace('/', '\\');
        if (normalizedPhysicalPath.StartsWith("cdrom0:\\", StringComparison.OrdinalIgnoreCase)) {
            return normalizedPhysicalPath;
        }

        if (!normalizedPhysicalPath.StartsWith("\\", StringComparison.Ordinal)) {
            normalizedPhysicalPath = "\\" + normalizedPhysicalPath.TrimStart('\\');
        }

        return "cdrom0:" + normalizedPhysicalPath;
    }

    /// <summary>
    /// Escapes one string for safe embedding inside a C++ string literal.
    /// </summary>
    /// <param name="value">String value to escape.</param>
    /// <returns>Escaped C++ string literal contents.</returns>
    static string EscapeCpp(string value) {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
