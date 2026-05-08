using System.Text;
using helengine.baseplatform.Manifest;

namespace helengine.ps2.builder;

/// <summary>
/// Writes PS2-specific native runtime asset path metadata into the generated-core runtime folder.
/// </summary>
public sealed class Ps2RuntimeAssetPathManifestWriter {
    /// <summary>
    /// Writes the PS2 physical startup path and logical-to-physical asset path mappings into generated native runtime source.
    /// </summary>
    /// <param name="generatedCoreRootPath">Generated core root that receives the runtime manifest files.</param>
    /// <param name="manifest">Cooked platform build manifest that defines the startup scene.</param>
    /// <param name="logicalToPhysicalPaths">Logical-to-physical staged PS2 disc path mappings.</param>
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
            BuildSourceContents(startupPhysicalPath, logicalToPhysicalPaths));
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
            + "const char* he_get_runtime_ps2_startup_scene_path();\n"
            + "const char* he_get_runtime_ps2_asset_physical_path(const char* logicalPath);\n";
    }

    /// <summary>
    /// Builds the generated PS2 asset-path manifest implementation source.
    /// </summary>
    /// <param name="startupPhysicalPath">Resolved physical startup-scene disc path.</param>
    /// <param name="logicalToPhysicalPaths">Logical-to-physical staged PS2 disc path mappings.</param>
    /// <returns>Generated source contents for the PS2 runtime asset-path manifest.</returns>
    static string BuildSourceContents(string startupPhysicalPath, IReadOnlyDictionary<string, string> logicalToPhysicalPaths) {
        StringBuilder builder = new();
        builder.AppendLine("#include \"runtime/runtime_ps2_asset_path_manifest.hpp\"");
        builder.AppendLine();
        builder.AppendLine("#include <cstddef>");
        builder.AppendLine();
        builder.AppendLine("struct HERuntimePs2AssetPathEntry {");
        builder.AppendLine("    const char* LogicalPath;");
        builder.AppendLine("    const char* PhysicalPath;");
        builder.AppendLine("};");
        builder.AppendLine();
        builder.AppendLine("static char NormalizeRuntimePs2LogicalPathCharacter(char value) {");
        builder.AppendLine("    if (value == '\\\\') {");
        builder.AppendLine("        return '/';");
        builder.AppendLine("    }");
        builder.AppendLine("    if (value >= 'A' && value <= 'Z') {");
        builder.AppendLine("        return static_cast<char>(value - 'A' + 'a');");
        builder.AppendLine("    }");
        builder.AppendLine("    return value;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("static bool RuntimePs2LogicalPathsEqual(const char* left, const char* right) {");
        builder.AppendLine("    if (left == nullptr || right == nullptr) {");
        builder.AppendLine("        return left == right;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    while (*left != '\\0' && *right != '\\0') {");
        builder.AppendLine("        if (NormalizeRuntimePs2LogicalPathCharacter(*left) != NormalizeRuntimePs2LogicalPathCharacter(*right)) {");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine("        left++;");
        builder.AppendLine("        right++;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    return *left == '\\0' && *right == '\\0';");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("static const char kRuntimePs2StartupScenePath[] = \"" + EscapeCpp(startupPhysicalPath) + "\";");
        builder.AppendLine();
        builder.AppendLine("static const HERuntimePs2AssetPathEntry kRuntimePs2AssetPathEntries[] = {");
        HashSet<string> emittedAliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in logicalToPhysicalPaths.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
            List<string> lookupAliases = BuildLookupAliases(entry.Key);
            for (int aliasIndex = 0; aliasIndex < lookupAliases.Count; aliasIndex++) {
                string alias = lookupAliases[aliasIndex];
                if (!emittedAliases.Add(alias)) {
                    continue;
                }

                builder.AppendLine("    { \"" + EscapeCpp(alias) + "\", \"" + EscapeCpp(entry.Value.Replace('/', '\\')) + "\" },");
            }
        }
        builder.AppendLine("};");
        builder.AppendLine();
        builder.AppendLine("const char* he_get_runtime_ps2_startup_scene_path() {");
        builder.AppendLine("    return kRuntimePs2StartupScenePath;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("const char* he_get_runtime_ps2_asset_physical_path(const char* logicalPath) {");
        builder.AppendLine("    if (logicalPath == nullptr || logicalPath[0] == '\\0') {");
        builder.AppendLine("        return nullptr;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    for (std::size_t index = 0; index < sizeof(kRuntimePs2AssetPathEntries) / sizeof(kRuntimePs2AssetPathEntries[0]); index++) {");
        builder.AppendLine("        const HERuntimePs2AssetPathEntry& entry = kRuntimePs2AssetPathEntries[index];");
        builder.AppendLine("        if (RuntimePs2LogicalPathsEqual(entry.LogicalPath, logicalPath)) {");
        builder.AppendLine("            return entry.PhysicalPath;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    return nullptr;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>
    /// Builds every PS2 runtime lookup alias that should resolve to one staged physical disc path.
    /// </summary>
    /// <param name="logicalPath">Logical cooked asset path produced by the build pipeline.</param>
    /// <returns>Logical lookup aliases that should map to the same PS2 physical disc path.</returns>
    static List<string> BuildLookupAliases(string logicalPath) {
        string normalizedLogicalPath = NormalizeLogicalPath(logicalPath);
        return [
            normalizedLogicalPath,
            BuildRootedLookupPath(normalizedLogicalPath)
        ];
    }

    /// <summary>
    /// Normalizes one logical cooked path into the slash form used by the generated runtime manifest.
    /// </summary>
    /// <param name="logicalPath">Logical cooked path produced by the build pipeline.</param>
    /// <returns>Normalized slash-delimited logical path.</returns>
    static string NormalizeLogicalPath(string logicalPath) {
        if (string.IsNullOrWhiteSpace(logicalPath)) {
            throw new ArgumentException("Logical path must be provided.", nameof(logicalPath));
        }

        return logicalPath.Replace('\\', '/');
    }

    /// <summary>
    /// Builds the rooted PS2 runtime lookup alias for one logical cooked path.
    /// </summary>
    /// <param name="normalizedLogicalPath">Normalized slash-delimited logical cooked path.</param>
    /// <returns>Rooted PS2 lookup alias that matches runtime content-manager paths.</returns>
    static string BuildRootedLookupPath(string normalizedLogicalPath) {
        if (string.IsNullOrWhiteSpace(normalizedLogicalPath)) {
            throw new ArgumentException("Normalized logical path must be provided.", nameof(normalizedLogicalPath));
        }

        return "cdrom0:\\" + normalizedLogicalPath.Replace('/', '\\');
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
