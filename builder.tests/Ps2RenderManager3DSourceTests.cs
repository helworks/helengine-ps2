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
    /// Ensures the PS2 renderer loads one PS2-native cooked model payload directly instead of deriving a packed-model sidecar path from the generic model path.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBuildingRuntimeModels_LoadsOnePs2ModelAssetWithoutPackedSidecarDerivation() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"Ps2ModelAsset.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("::Asset* modelAssetBase = ::Ps2AssetSerializer::Deserialize(modelStream);", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2ModelAsset* modelAsset = he_cpp_try_cast<::Ps2ModelAsset>(modelAssetBase);", source, StringComparison.Ordinal);
        Assert.Contains("runtimeModel->LoadFromCooked(modelAsset);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPackedModelSidecarPath(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::ModelAsset* modelAsset = he_cpp_try_cast<::ModelAsset>(modelAssetBase);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2PackedModelAsset", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
