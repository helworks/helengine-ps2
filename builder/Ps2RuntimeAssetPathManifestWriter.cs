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
        File.WriteAllText(Path.Combine(runtimeRootPath, "runtime_scene_catalog_manifest.hpp"), BuildSceneCatalogHeaderContents());
        File.WriteAllText(
            Path.Combine(runtimeRootPath, "runtime_scene_catalog_manifest.cpp"),
            BuildSceneCatalogSourceContents(manifest, logicalToPhysicalPaths));
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
    /// Builds the generated runtime scene-catalog manifest header that the shared core bootstrap consumes.
    /// </summary>
    /// <returns>Header contents for the generated runtime scene-catalog manifest.</returns>
    static string BuildSceneCatalogHeaderContents() {
        StringBuilder builder = new();
        builder.AppendLine("#pragma once");
        builder.AppendLine();
        builder.AppendLine("#include <cstddef>");
        builder.AppendLine();
        builder.AppendLine("struct HERuntimeSceneCatalogEntry {");
        builder.AppendLine("    const char* SceneId;");
        builder.AppendLine("    const char* CookedRelativePath;");
        builder.AppendLine("};");
        builder.AppendLine();
        builder.AppendLine("const HERuntimeSceneCatalogEntry* he_runtime_scene_catalog_entries(std::size_t* count);");
        builder.AppendLine("const char* he_runtime_scene_cooked_relative_path(const char* sceneId);");
        return builder.ToString();
    }

    /// <summary>
    /// Builds the generated runtime scene-catalog manifest implementation using rooted PS2 disc paths.
    /// </summary>
    /// <param name="manifest">Cooked platform build manifest that defines the staged scenes.</param>
    /// <param name="logicalToPhysicalPaths">Logical-to-physical staged PS2 disc path mappings.</param>
    /// <returns>Generated source contents for the PS2 runtime scene-catalog manifest.</returns>
    static string BuildSceneCatalogSourceContents(
        PlatformBuildManifest manifest,
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        if (manifest == null) {
            throw new ArgumentNullException(nameof(manifest));
        }
        if (logicalToPhysicalPaths == null) {
            throw new ArgumentNullException(nameof(logicalToPhysicalPaths));
        }

        StringBuilder builder = new();
        builder.AppendLine("#include \"runtime/runtime_scene_catalog_manifest.hpp\"");
        builder.AppendLine();
        builder.AppendLine("#include <cstring>");
        builder.AppendLine("#include <stdexcept>");
        builder.AppendLine();

        if (manifest.Scenes == null || manifest.Scenes.Length == 0) {
            builder.AppendLine("static const HERuntimeSceneCatalogEntry* kRuntimeSceneCatalogEntries = nullptr;");
            builder.AppendLine("static const std::size_t kRuntimeSceneCatalogEntryCount = 0;");
        } else {
            builder.AppendLine("static const HERuntimeSceneCatalogEntry kRuntimeSceneCatalogEntries[] = {");
            for (int index = 0; index < manifest.Scenes.Length; index++) {
                PlatformBuildScene scene = manifest.Scenes[index];
                string logicalScenePath = ResolveSceneLogicalPath(scene);
                if (!logicalToPhysicalPaths.TryGetValue(logicalScenePath, out string physicalScenePath)) {
                    throw new InvalidOperationException($"The scene '{scene.SceneId}' did not resolve to a staged PS2 physical disc path.");
                }

                builder.Append("    { \"");
                builder.Append(EscapeCpp(scene.SceneId));
                builder.Append("\", \"");
                builder.Append(EscapeCpp(BuildRuntimePhysicalPath(physicalScenePath)));
                builder.AppendLine("\" },");
            }

            builder.AppendLine("};");
            builder.AppendLine("static const std::size_t kRuntimeSceneCatalogEntryCount = sizeof(kRuntimeSceneCatalogEntries) / sizeof(kRuntimeSceneCatalogEntries[0]);");
        }

        builder.AppendLine();
        builder.AppendLine("const HERuntimeSceneCatalogEntry* he_runtime_scene_catalog_entries(std::size_t* count) {");
        builder.AppendLine("    if (count != nullptr) {");
        builder.AppendLine("        *count = kRuntimeSceneCatalogEntryCount;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    return kRuntimeSceneCatalogEntries;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("const char* he_runtime_scene_cooked_relative_path(const char* sceneId) {");
        builder.AppendLine("    if (sceneId == nullptr || sceneId[0] == '\\0') {");
        builder.AppendLine("        throw std::invalid_argument(\"Runtime scene id is required.\");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    for (std::size_t index = 0; index < kRuntimeSceneCatalogEntryCount; index++) {");
        builder.AppendLine("        const HERuntimeSceneCatalogEntry& entry = kRuntimeSceneCatalogEntries[index];");
        builder.AppendLine("        if (std::strcmp(entry.SceneId, sceneId) == 0) {");
        builder.AppendLine("            return entry.CookedRelativePath;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    throw std::runtime_error(\"Runtime scene id was not found in the scene catalog manifest.\");");
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

    /// <summary>
    /// Resolves the logical cooked scene path for one runtime scene entry.
    /// </summary>
    /// <param name="scene">Resolved scene whose cooked relative path should be inspected.</param>
    /// <returns>Normalized logical cooked path for the supplied scene.</returns>
    static string ResolveSceneLogicalPath(PlatformBuildScene scene) {
        if (scene == null) {
            throw new ArgumentNullException(nameof(scene));
        }
        if (scene.ResolvedMetadata == null) {
            throw new InvalidOperationException($"The scene '{scene.SceneId}' did not define resolved metadata.");
        }

        for (int metadataIndex = 0; metadataIndex < scene.ResolvedMetadata.Length; metadataIndex++) {
            KeyValuePair<string, string> entry = scene.ResolvedMetadata[metadataIndex];
            if (string.Equals(entry.Key, PlatformBuildSceneMetadataKeys.CookedRelativePath, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Value)) {
                return entry.Value.Replace('\\', '/');
            }
        }

        throw new InvalidOperationException($"The scene '{scene.SceneId}' did not define a cooked-relative-path metadata entry.");
    }
}
