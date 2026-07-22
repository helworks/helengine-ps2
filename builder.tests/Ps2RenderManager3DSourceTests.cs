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
    /// Ensures deferred PS2 3D asset flushes clear cached gsKit textures so later draws cannot retain stale GS VRAM pointers after one global texture-region reset.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenFlushingReleasedAssets_ClearsCachedTextures() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int flushReleasedAssetsIndex = source.IndexOf("void Ps2RenderManager3D::FlushReleasedAssets()", StringComparison.Ordinal);
        int clearCachedTexturesIndex = source.IndexOf("void Ps2RenderManager3D::ClearCachedTextures()", StringComparison.Ordinal);
        Assert.True(flushReleasedAssetsIndex >= 0, "Expected PS2 render manager deferred asset release flush.");
        Assert.True(clearCachedTexturesIndex >= 0, "Expected PS2 render manager cached texture clear helper.");

        string flushReleasedAssetsMethod = source.Substring(flushReleasedAssetsIndex, Math.Min(800, source.Length - flushReleasedAssetsIndex));
        string clearCachedTexturesMethod = source.Substring(clearCachedTexturesIndex, Math.Min(500, source.Length - clearCachedTexturesIndex));

        Assert.Contains("void ReleaseTextureRecord(GSTEXTURE* texture)", source, StringComparison.Ordinal);
        Assert.Contains("ClearCachedTextures();", flushReleasedAssetsMethod, StringComparison.Ordinal);
        Assert.Contains("for (auto& textureEntry : TextureRecords)", clearCachedTexturesMethod, StringComparison.Ordinal);
        Assert.Contains("ReleaseTextureRecord(textureEntry.second);", clearCachedTexturesMethod, StringComparison.Ordinal);
        Assert.Contains("TextureRecords.clear();", clearCachedTexturesMethod, StringComparison.Ordinal);
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
    /// Ensures PS2 runtime model disposal releases the packed VU model owned by each cooked model instance.
    /// </summary>
    [Fact]
    public void Ps2RuntimeModel_WhenDisposed_ReleasesPackedVuModelOwnership() {
        string headerPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.hpp");
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.cpp");
        Assert.True(File.Exists(headerPath), $"Expected PS2 runtime model header at '{headerPath}'.");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 runtime model source at '{sourcePath}'.");

        string header = File.ReadAllText(headerPath);
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("void Dispose() override;", header, StringComparison.Ordinal);
        int disposeIndex = source.IndexOf("void Ps2RuntimeModel::Dispose()", StringComparison.Ordinal);
        Assert.True(disposeIndex >= 0, "Expected PS2 runtime model disposal implementation.");
        string disposeMethod = source.Substring(disposeIndex, Math.Min(500, source.Length - disposeIndex));
        Assert.Contains("delete VuPackedModel;", disposeMethod, StringComparison.Ordinal);
        Assert.Contains("VuPackedModel = nullptr;", disposeMethod, StringComparison.Ordinal);
        Assert.Contains("::RuntimeModel::Dispose();", disposeMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer deserializes PS2-native cooked materials instead of routing them through the generic asset serializer.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBuildingRuntimeMaterials_LoadsPs2MaterialAssetInsteadOfGenericAssetPayload() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"runtime/runtime_ps2_asset_path_manifest.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("ResolvePs2CookedAssetOpenPath(const std::string& path)", source, StringComparison.Ordinal);
        Assert.Contains("const std::string resolvedCookedAssetPath = ResolvePs2CookedAssetOpenPath(cookedAssetPath);", source, StringComparison.Ordinal);
        Assert.Contains("::Asset* asset = ::Ps2AssetSerializer::Deserialize(stream);", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2MaterialAsset* cookedMaterialAsset = he_cpp_try_cast<::Ps2MaterialAsset>(asset);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::Asset* asset = ::AssetSerializer::Deserialize(stream);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer roots disc-relative cooked texture payloads through the shared helper before opening them from the packaged runtime content root.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenResolvingMaterialTextures_RootsDiscRelativeCookedPathsBeforeOpenRead() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const bool isCookedDiscRelativePath = normalizedPath.rfind(\"COOKED\\\\\", 0) == 0 || normalizedPath.rfind(\"\\\\COOKED\\\\\", 0) == 0;", source, StringComparison.Ordinal);
        Assert.Contains("return std::string(\"cdrom0:\\\\\") + normalizedPath;", source, StringComparison.Ordinal);
        Assert.Contains("normalizedPath.compare(normalizedPath.size() - 2u, 2u, \";1\") != 0", source, StringComparison.Ordinal);
        Assert.Contains("const std::string resolvedTexturePath = ResolvePs2CookedAssetOpenPath(textureRelativePath);", source, StringComparison.Ordinal);
        Assert.Contains("stream = ::File::OpenRead(resolvedTexturePath);", source, StringComparison.Ordinal);
        Assert.Contains("resolvedPath='", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured opaque VU batches stay on the normal VIF submission path unless the explicit direct-GIF diagnostic switch is enabled.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedVuBatches_DoesNotForceDirectGifDiagnosticDispatch() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const bool useDirectGifDispatchDiagnostics = EnableVuDirectGifDispatchDiagnostics;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const bool useDirectGifDispatchDiagnostics = EnableVuDirectGifDispatchDiagnostics || batch.Textured;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured batches use the stable per-batch VIF submission path until aggregate packet rendering is proven safe for larger scenes.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedVuBatches_DisablesExperimentalBatchAggregation() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableTexturedBatchAggregationDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr bool EnableTexturedBatchAggregationDiagnostics = true;", source, StringComparison.Ordinal);
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
    /// Ensures the PS2 renderer publishes the coarse untextured VU profiling split through the existing overlay fields without changing the packet-builder runtime contract.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenPublishingPerformanceOverlay_UsesAssemblyLightingAndPayloadFillMetricsWithoutLayoutChanges() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int publishIndex = source.IndexOf("void Ps2RenderManager3D::PublishPerformanceOverlayMetrics() const", StringComparison.Ordinal);
        Assert.True(publishIndex >= 0, "Expected PS2 render manager to publish performance overlay metrics.");
        string publishBody = source.Substring(publishIndex, Math.Min(1200, source.Length - publishIndex));

        Assert.Contains("LastVuPacketAssemblyMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.Contains("LastVuTriangleLightingMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.Contains("LastVuTrianglePayloadFillMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.DoesNotContain("LastVuPacketEncodeMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.DoesNotContain("LastVuSubmitMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.DoesNotContain("LastVuWaitMilliseconds,", publishBody, StringComparison.Ordinal);
        Assert.Contains("LastVuTriangleLightingMilliseconds += VuVifPacketBuilder.GetLastTriangleLightingMilliseconds();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuTrianglePayloadFillMilliseconds += VuVifPacketBuilder.GetLastTrianglePayloadFillMilliseconds();", source, StringComparison.Ordinal);
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
    /// Ensures textured VU batches pass the resolved GS texture into the packet builder so the textured GIF payload can encode TEX0 instead of relying on an external bind side effect.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenEncodingVuTexturedBatches_PassesResolvedTextureAndEncodesTex0InPacketBuilder() {
        string rendererSourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string packetBuilderSourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        Assert.True(File.Exists(rendererSourcePath), $"Expected PS2 render manager source at '{rendererSourcePath}'.");
        Assert.True(File.Exists(packetBuilderSourcePath), $"Expected PS2 VU packet builder source at '{packetBuilderSourcePath}'.");

        string rendererSource = File.ReadAllText(rendererSourcePath);
        int renderOpaqueIndex = rendererSource.IndexOf("void Ps2RenderManager3D::RenderOpaqueWithVuPath(", StringComparison.Ordinal);
        Assert.True(renderOpaqueIndex >= 0, "Expected PS2 render manager VU opaque render path.");
        string renderOpaqueBody = rendererSource.Substring(renderOpaqueIndex, Math.Min(3200, rendererSource.Length - renderOpaqueIndex));

        string packetBuilderSource = File.ReadAllText(packetBuilderSourcePath);

        Assert.DoesNotContain("#include <gsToolkit.h>", rendererSource, StringComparison.Ordinal);
        Assert.Contains("GSTEXTURE* batchTexture = nullptr;", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("batchTexture = ResolveTexture(GsGlobal, batch.Material->GetTextureRelativePath());", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("batchTextureWidth = static_cast<int>(batchTexture->Width);", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("batchTextureHeight = static_cast<int>(batchTexture->Height);", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("GsGlobal,\n                batchTexture,\n                batchTextureWidth,\n                batchTextureHeight);", renderOpaqueBody, StringComparison.Ordinal);
        Assert.DoesNotContain("gsKit_TexManager_bind(GsGlobal, batchTexture);", renderOpaqueBody, StringComparison.Ordinal);

        Assert.Contains("BuildTexturedTriangleGifPacketBytes(\n            GSGLOBAL* gsGlobal,\n            const GSTEXTURE* texture,", packetBuilderSource, StringComparison.Ordinal);
        int texturedPacketIndex = packetBuilderSource.IndexOf("BuildTexturedTriangleGifPacketBytes(", StringComparison.Ordinal);
        Assert.True(texturedPacketIndex >= 0, "Expected textured GIF packet builder helper.");
        string texturedPacketBuilder = packetBuilderSource.Substring(texturedPacketIndex, Math.Min(3200, packetBuilderSource.Length - texturedPacketIndex));
        Assert.Contains("if (textured) {\n            gsKit_set_texfilter(gsGlobal, texture->Filter);\n        }", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("ResolveGsTextureDimensionExponent(texture->Width)", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("ResolveGsTextureDimensionExponent(texture->Height)", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("GIF_TAG_TRIANGLE_GORAUD_TEXTURED(1)", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("GIF_TAG_TRIANGLE_GORAUD_TEXTURED_REGS(gsGlobal->PrimContext)", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("prim.shading = PRIM_SHADE_GOURAUD;", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("prim.shading = PRIM_SHADE_FLAT;", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = ResolveOpaqueUntexturedTestRegister(gsGlobal);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = GS_REG_TEST;", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = GS_SET_TEX1(0, 0, texture->Filter, texture->Filter, 0, 0, 0);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = GS_REG_TEX1;", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t uvRegisterA = BuildGsUvRegister(screenTexCoordA);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("const std::uint32_t ix1 = static_cast<std::uint32_t>((2048.0f + screenAX) * 16.0f);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t positionARegister = GS_SETREG_XYZ2(ix1, iy1, iz1);", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_TEX0(", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_PRIM(", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->Vram / 256", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->TBW", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->PSM", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.Contains("texture->VramClut / 256", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("draw_prim_start(", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("draw_prim_end(", texturedPacketBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("GsGlobal,\n                0,\n                0);", renderOpaqueBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured opaque proxies use the established CPU/GIF path while the experimental textured VU packet path remains unsafe for scene geometry.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedOpaqueBatches_UsesLegacyCpuPath() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int renderOpaqueIndex = source.IndexOf("void Ps2RenderManager3D::RenderOpaqueWithVuPath(", StringComparison.Ordinal);
        Assert.True(renderOpaqueIndex >= 0, "Expected PS2 render manager VU opaque render path.");
        string renderOpaqueBody = source.Substring(renderOpaqueIndex, Math.Min(9000, source.Length - renderOpaqueIndex));

        Assert.Contains("constexpr bool EnableLegacyCpuTexturedOpaquePath = true;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableLegacyCpuTexturedOpaquePath && batch.Textured) {", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(*batch.Proxy, view, projection, viewport, nearPlaneDistance);", renderOpaqueBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the active CPU textured primitive path uses the GS STQ perspective-correction mode rather than affine UV coordinates.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenDrawingOpaqueTexturedTriangles_UsesPerspectiveCorrectStqCoordinates() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("GSPRIMSTQPOINT ResolvePerspectiveTextureVertex(", source, StringComparison.Ordinal);
        Assert.Contains("vertex.stq = vertex_to_STQ(normalizedTexCoord.X * q, normalizedTexCoord.Y * q);", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_list_triangle_goraud_texture_stq_3d(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
