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
    /// Ensures the PS2 3D renderer consumes cooked texture payload PSM and CLUT metadata instead of assuming every PS2-owned texture is direct-color.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int textureBuilderIndex = source.IndexOf("GSTEXTURE* BuildTextureFromAsset(GSGLOBAL* gsGlobal, ::Ps2TextureAsset* data)", StringComparison.Ordinal);
        Assert.True(textureBuilderIndex >= 0, "Expected PS2 cooked texture builder helper.");
        string textureBuilder = source.Substring(textureBuilderIndex, Math.Min(2200, source.Length - textureBuilderIndex));

        Assert.Contains("ResolveGsPixelStorageMode(data->PixelStorageMode)", textureBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->ClutPSM = ResolveGsPixelStorageMode(data->ClutPixelStorageMode);", textureBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->ClutStorageMode = GS_CLUT_STORAGE_CSM1;", textureBuilder, StringComparison.Ordinal);
        Assert.Contains("if (data->PaletteData != nullptr && data->PaletteData->Length > 0)", textureBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("if (data->Format != ::Ps2TextureFormat::Rgba32)", textureBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->VramClut = gsKit_vram_alloc(", textureBuilder, StringComparison.Ordinal);
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
    /// Ensures the PS2 renderer deserializes PS2-native cooked materials instead of routing them through the generic asset serializer.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBuildingRuntimeMaterials_LoadsPs2MaterialAssetInsteadOfGenericAssetPayload() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("::Asset* asset = ::Ps2AssetSerializer::Deserialize(stream);", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2MaterialAsset* cookedMaterialAsset = he_cpp_try_cast<::Ps2MaterialAsset>(asset);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::Asset* asset = ::AssetSerializer::Deserialize(stream);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in PS2 renderer header stays aligned with the root generated-core surface that exposes only cooked material entry points.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenTargetingRootGeneratedCore_DoesNotOverrideRemovedRawMaterialSignature() {
        string headerPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp");
        Assert.True(File.Exists(headerPath), $"Expected PS2 render manager header at '{headerPath}'.");

        string header = File.ReadAllText(headerPath);

        Assert.DoesNotContain("BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset) override;", header, StringComparison.Ordinal);
        Assert.Contains("::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset) override;", header, StringComparison.Ordinal);
        Assert.Contains("::RuntimeMaterial* BuildMaterialFromCooked(std::string cookedAssetPath) override;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in PS2 runtime model consumes the root generated-core PS2-native packed model payload type.
    /// </summary>
    [Fact]
    public void Ps2RuntimeModel_WhenLoadingCookedPayloads_UsesPs2ModelAssetPackedBytesContract() {
        string headerPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.hpp");
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.cpp");
        Assert.True(File.Exists(headerPath), $"Expected PS2 runtime model header at '{headerPath}'.");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 runtime model source at '{sourcePath}'.");

        string header = File.ReadAllText(headerPath);
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("void LoadFromCooked(::Ps2ModelAsset* modelAsset);", header, StringComparison.Ordinal);
        Assert.Contains("#include \"Ps2ModelAsset.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("modelAsset->PackedMeshBytes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("modelAsset->Ps2PackedMeshBytes", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer binds to the generated float4x4 helper entry points that actually exist in generated core.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBuildingMatrices_UsesGeneratedFloat4x4HelperNames() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("::float4x4::CreateLookAt__ref0_ref1_ref2_out3(", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4::CreatePerspectiveFieldOfView__out4(", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4::CreateScale__out3(", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4::CreateFromQuaternion__ref0_out1(", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4::CreateTranslation__ref0_out1(", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::CreateLookAt(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::CreatePerspectiveFieldOfView(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::CreateScale(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::CreateTranslation(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::Multiply(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer and runtime model dispatch inherited generated setters through the generated base types.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenSettingGeneratedBaseProperties_UsesBaseQualifiedSetterCalls() {
        string rendererSourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string runtimeModelSourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.cpp");
        Assert.True(File.Exists(rendererSourcePath), $"Expected PS2 render manager source at '{rendererSourcePath}'.");
        Assert.True(File.Exists(runtimeModelSourcePath), $"Expected PS2 runtime model source at '{runtimeModelSourcePath}'.");

        string rendererSource = File.ReadAllText(rendererSourcePath);
        string runtimeModelSource = File.ReadAllText(runtimeModelSourcePath);

        Assert.Contains("renderTarget->RuntimeTexture::set_Width(width);", rendererSource, StringComparison.Ordinal);
        Assert.Contains("renderTarget->RuntimeTexture::set_Height(height);", rendererSource, StringComparison.Ordinal);
        Assert.Contains("renderTarget->RuntimeData::set_Id(", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("renderTarget->set_Width(width);", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("renderTarget->set_Height(height);", rendererSource, StringComparison.Ordinal);
        Assert.DoesNotContain("renderTarget->set_Id(", rendererSource, StringComparison.Ordinal);

        Assert.Contains("runtimeModel->RuntimeData::set_Id(modelAsset->get_Id());", runtimeModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeModel->set_Id(modelAsset->get_Id());", runtimeModelSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in PS2 renderer leaves the CPU opaque fallback disabled by default once VU runtime diagnosis is complete.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenNotRunningCpuFallbackDiagnostics_KeepsLegacyCpuOpaquePathDisabled() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableLegacyCpuOpaquePathDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("UseLegacyCpuOpaquePath(EnableLegacyCpuOpaquePathDiagnostics)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures unlit PS2 materials preserve their authored base color instead of collapsing every cube to the same diagnostic gray.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenResolvingUnlitVertexColor_UsesMaterialBaseColor() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int resolveVertexColorIndex = source.IndexOf("std::uint64_t Ps2RenderManager3D::ResolveVertexColor(", StringComparison.Ordinal);
        Assert.True(resolveVertexColorIndex >= 0, "Expected PS2 render manager to expose the vertex-color resolver.");
        string resolveVertexColorBody = source.Substring(resolveVertexColorIndex, Math.Min(2200, source.Length - resolveVertexColorIndex));

        Assert.Contains("if (material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit) {", resolveVertexColorBody, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorR()", resolveVertexColorBody, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorG()", resolveVertexColorBody, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorB()", resolveVertexColorBody, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorA()", resolveVertexColorBody, StringComparison.Ordinal);
        Assert.DoesNotContain("return GS_SETREG_RGBAQ(0xC0, 0xC0, 0xC0, 0x80, 0x00);", resolveVertexColorBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
