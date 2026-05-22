using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 3D renderer source consumes PS2-native runtime texture payloads for material texture resolution.
/// </summary>
public sealed class Ps2RenderManager3DSourceTests {
    /// <summary>
    /// Ensures the PS2 renderer deserializes PS2-native cooked textures instead of assuming generic raw texture assets.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenResolvingMaterialTextures_LoadsPs2TextureAssetInsteadOfTextureAsset() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"Ps2AssetSerializer.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"Ps2TextureAsset.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2AssetSerializer::Deserialize(stream)", source, StringComparison.Ordinal);
        Assert.Contains("he_cpp_try_cast<::Ps2TextureAsset>(asset)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("he_cpp_try_cast<::TextureAsset>(asset)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer derives the short sibling packed-model sidecar path from disc-runtime model paths without keeping the trailing `;1` version suffix.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBuildingPackedModelSidecarPath_StripsDiscVersionSuffixBeforeAppendingSidecarExtension() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const std::size_t versionSuffixIndex = normalizedPath.rfind(\";1\");", source, StringComparison.Ordinal);
        Assert.Contains("normalizedPath = normalizedPath.substr(0, versionSuffixIndex);", source, StringComparison.Ordinal);
        Assert.Contains("return normalizedPath.substr(0, extensionIndex) + \".PSM\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return cookedModelPath.substr(0, extensionIndex) + \".PSM\";", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
