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
    /// Ensures textured batches use bounded aggregate packet rendering so tessellated models cannot exceed a single VU packet.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedVuBatches_EnablesBoundedBatchAggregation() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableTexturedBatchAggregation = true;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t MaximumBoundedTexturedAggregateSourceTriangleCount = 2048u;", source, StringComparison.Ordinal);
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
    /// Ensures the PS2 renderer stores its latest performance sample in one record shared by runtime diagnostics consumers.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenPublishingPerformanceData_UsesDedicatedMetricsRecord() {
        string repositoryRootPath = GetRepositoryRootPath();
        string headerPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp");
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string metricsPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderPerformanceMetrics.hpp");

        Assert.True(File.Exists(metricsPath));
        Assert.Contains("const Ps2RenderPerformanceMetrics& GetLastPerformanceMetrics() const;", File.ReadAllText(headerPath), StringComparison.Ordinal);
        Assert.Contains("Ps2RenderPerformanceMetrics LastPerformanceMetrics;", File.ReadAllText(headerPath), StringComparison.Ordinal);

        string metrics = File.ReadAllText(metricsPath);
        Assert.Contains("double VifReuseWaitMilliseconds = 0.0;", metrics, StringComparison.Ordinal);
        Assert.Contains("double GifDrainMilliseconds = 0.0;", metrics, StringComparison.Ordinal);
        Assert.Contains("double LegacyOpaqueMilliseconds = 0.0;", metrics, StringComparison.Ordinal);
        Assert.Contains("std::size_t CompatibleUntexturedGroupCount = 0u;", metrics, StringComparison.Ordinal);

        string source = File.ReadAllText(sourcePath);
        Assert.Contains("const Ps2RenderPerformanceMetrics& Ps2RenderManager3D::GetLastPerformanceMetrics() const", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures renderer timing boundaries populate the shared metrics record during every frame.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenDrawingFrame_PopulatesDedicatedMetricsRecord() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("LastPerformanceMetrics = Ps2RenderPerformanceMetrics {};", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.ProxySyncMilliseconds = ResolveMillisecondsFromClockTicks(proxySyncStartTicks, proxySyncEndTicks);", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.FramePlanMilliseconds = ResolveMillisecondsFromClockTicks(framePlanStartTicks, framePlanEndTicks);", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.VuBatchBuildMilliseconds = ResolveMillisecondsFromClockTicks(vuBatchBuildStartTicks, vuBatchBuildEndTicks);", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.PacketEncodeMilliseconds += ResolveMillisecondsFromClockTicks(vuPacketEncodeStartTicks, vuPacketEncodeEndTicks);", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.VifSubmitMilliseconds += ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the host can report the required GIF drain boundary without accepting invalid elapsed durations.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRecordingGifDrain_RejectsNegativeDuration() {
        string repositoryRootPath = GetRepositoryRootPath();
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.Contains("void SetLastGifDrainMilliseconds(double milliseconds);", header, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::SetLastGifDrainMilliseconds(double milliseconds)", source, StringComparison.Ordinal);
        Assert.Contains("if (milliseconds < 0.0) {", source, StringComparison.Ordinal);
        Assert.Contains("throw std::invalid_argument(\"PS2 GIF drain duration cannot be negative.\");", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.GifDrainMilliseconds = milliseconds;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures supported opaque work that falls back to the CPU renderer is visible in the performance record.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenUsingLegacyOpaqueRoute_RecordsElapsedTimeAndTriangles() {
        string repositoryRootPath = GetRepositoryRootPath();
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.Contains("void DrawOpaqueProxyLegacyTimed(", header, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::DrawOpaqueProxyLegacyTimed(", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.LegacyOpaqueMilliseconds += ResolveMillisecondsFromClockTicks", source, StringComparison.Ordinal);
        Assert.Contains("LastPerformanceMetrics.LegacyOpaqueTriangleCount += packedModel->GetTriangleVertexCount() / 3u;", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacyTimed(*batch.Proxy, view, projection, viewport, nearPlaneDistance);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 host records the existing GIF drain boundary in the renderer metrics even when the compact overlay shows higher-priority counters.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenCollectingFrameTiming_RecordsGifDrainInRendererMetrics() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("RenderManager3DBackend.SetLastGifDrainMilliseconds(", source, StringComparison.Ordinal);
        Assert.Contains("GetLastPerformanceMetrics()", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingGifDrainMilliseconds += metrics.GifDrainMilliseconds;", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingVifPacketByteCount += static_cast<double>(metrics.VifPacketByteCount);", source, StringComparison.Ordinal);
        Assert.Contains("Leg ", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the two-row PS2 FPS overlay uses its second visible row for packet timing detail instead of publishing detail into unsupported hidden rows.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenPresentingPacketTimings_ReusesTheSecondVisibleFpsRow() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("textDrawable->set_Text(FrameTimingOverlayDetailLine);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("textDrawable->set_Text(FrameTimingOverlayLine2);", source, StringComparison.Ordinal);
        Assert.Contains("currentText.rfind(\"Enc\", 0) == 0", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the visible PS2 FPS overlay begins with the manually incremented build number used to distinguish stale ISO images.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenPublishingFrameTiming_PrefixesTheFpsRowWithBuildNumberB61() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr const char* FrameTimingOverlayBuildNumber = \"B61\";", source, StringComparison.Ordinal);
        Assert.Contains("std::string(FrameTimingOverlayBuildNumber)\n            + \" \"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the focused Colored Cubes performance build boots directly into that scene and exposes each packet-stage measurement required for the two-millisecond target.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenProfilingColoredCubes_BootsTheSceneAndPublishesPacketStageMetrics() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr const char* StartupSceneDiagnosticOverrideId = \"colored_cube_grid\";", source, StringComparison.Ordinal);
        Assert.Contains("const double averageFrameMilliseconds = averageFramesPerSecond <= 0.0 ? 0.0 : 1000.0 / averageFramesPerSecond;", source, StringComparison.Ordinal);
        Assert.Contains("+ \" ms\"", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingOverlayDetailLine =\n            std::string(\"Enc \")", source, StringComparison.Ordinal);
        Assert.Contains("+ \" Vif \"", source, StringComparison.Ordinal);
        Assert.Contains("+ \" Gif \"\n            + FormatOverlayMilliseconds(averageGifDrainMilliseconds)", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingOverlayAdditionalText =\n            std::string(\"Pkt \")", source, StringComparison.Ordinal);
        Assert.Contains("+ \" Grp \"", source, StringComparison.Ordinal);
        Assert.Contains("+ \" Tri \"", source, StringComparison.Ordinal);
        Assert.Contains("+ \" B \"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the compact PS2 overlay reports the actual submitted triangle count and payload size for performance diagnosis.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenPresentingPacketTimings_IncludesSubmittedTrianglesAndPacketBytes() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("FrameTimingSubmittedTriangleCount += static_cast<double>(metrics.SubmittedTriangleCount);", source, StringComparison.Ordinal);
        Assert.Contains("const double averageSubmittedTriangleCount = FrameTimingSubmittedTriangleCount / sampledFrameCount;", source, StringComparison.Ordinal);
        Assert.Contains("+ \" Tri \"\n            + std::to_string(static_cast<int>(averageSubmittedTriangleCount))", source, StringComparison.Ordinal);
        Assert.Contains("+ \" B \"\n            + std::to_string(static_cast<int>(averageVifPacketByteCount))", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures consecutive PS2 text glyphs retain their alpha state while solid quads and frame completion restore opaque rendering state.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDrawingConsecutiveTexturedQuads_BatchesAlphaStateTransitions() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("bool TexturedQuadAlphaStateActive;", source, StringComparison.Ordinal);
        Assert.Contains("if (!TexturedQuadAlphaStateActive) {", source, StringComparison.Ordinal);
        Assert.Contains("if (TexturedQuadAlphaStateActive) {", source, StringComparison.Ordinal);
        Assert.Contains("TexturedQuadAlphaStateActive = false;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the production build restores the native PS2 framebuffer size after the B21 fill-rate experiment found no performance improvement.
    /// </summary>
    [Fact]
    public void Ps2BootHost_AfterTheB21FillRateExperiment_RestoresTheNativeFramebufferResolution() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr int Ps2DefaultFramebufferWidth = 640;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr int Ps2DefaultFramebufferHeight = 448;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures disabled fine-grained VU diagnostics do not spend PS2 EE time reading the clock for every untextured triangle.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPerTriangleTimingIsDisabled_DoesNotReadClockForEachUntexturedTriangle() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int untexturedBranchStartIndex = source.IndexOf("std::vector<Ps2VuUntexturedClipVertex> clippedUntexturedVertices;", StringComparison.Ordinal);
        int texturedBranchStartIndex = source.IndexOf("std::vector<Ps2VuTexturedClipVertex> clippedTexturedVertices;", untexturedBranchStartIndex, StringComparison.Ordinal);
        Assert.True(untexturedBranchStartIndex >= 0, "Expected the untextured VU triangle encoding branch.");
        Assert.True(texturedBranchStartIndex > untexturedBranchStartIndex, "Expected the textured VU triangle encoding branch after the untextured branch.");
        string untexturedBranch = source[untexturedBranchStartIndex..texturedBranchStartIndex];

        Assert.DoesNotContain("const std::clock_t trianglePrepStartTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPerTriangleTimingDiagnostics) {", untexturedBranch, StringComparison.Ordinal);
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
    public void Ps2RenderManager3D_WhenRenderingTexturedOpaqueBatches_UsesVuPathByDefault() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int renderOpaqueIndex = source.IndexOf("void Ps2RenderManager3D::RenderOpaqueWithVuPath(", StringComparison.Ordinal);
        Assert.True(renderOpaqueIndex >= 0, "Expected PS2 render manager VU opaque render path.");
        string renderOpaqueBody = source.Substring(renderOpaqueIndex, Math.Min(9000, source.Length - renderOpaqueIndex));

        Assert.Contains("constexpr bool EnableLegacyCpuTexturedOpaquePath = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableLegacyCpuTexturedOpaquePath && batch.Textured) {", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacyTimed(*batch.Proxy, view, projection, viewport, nearPlaneDistance);", renderOpaqueBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured opaque VU route aggregates work into source-triangle-bounded packet slices instead of submitting one VIF packet per batch.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedOpaqueBatches_UsesBoundedAggregatedVuPackets() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableTexturedBatchAggregation = true;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t MaximumBoundedTexturedAggregateSourceTriangleCount = 2048u;", source, StringComparison.Ordinal);
        Assert.Contains("std::size_t ResolveBoundedTexturedAggregatePacketEnd(", source, StringComparison.Ordinal);
        Assert.Contains("while (firstTexturedBatchIndex < texturedBatches.size()) {", source, StringComparison.Ordinal);
        Assert.Contains("std::vector<Ps2VuOpaqueBatchSlice> packetTexturedBatches(", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.AddOpaqueTexturedBatches(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures production textured batches bypass the pass-through VU microprogram and submit their already encoded GIF stream once per bounded packet slice.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenSubmittingTexturedBatches_UsesOneDirectGifDmaSubmissionPerSlice() {
        string renderManagerPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string packetBuilderPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string renderManagerSource = File.ReadAllText(renderManagerPath);
        string packetBuilderSource = File.ReadAllText(packetBuilderPath);

        Assert.Contains("constexpr bool UseDirectGifTexturedSubmission = true;", renderManagerSource, StringComparison.Ordinal);
        Assert.Contains("!UseDirectGifTexturedSubmission);", renderManagerSource, StringComparison.Ordinal);
        Assert.Contains("if (UseDirectGifTexturedSubmission) {", renderManagerSource, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(gifPacket, DMA_CHANNEL_GIF, true);", renderManagerSource, StringComparison.Ordinal);
        Assert.Contains("bool createVifPacket", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("if (!createVifPacket) {", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize(texturedTrianglePackets.size() * TexturedTrianglePacketByteCount);", packetBuilderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct GIF textured submission writes the proven TEST and TEX1 state once for each material batch while retaining every complete triangle GIF payload.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenDirectGifSubmissionIsEnabled_BatchesInvariantStatePerMaterial() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("std::vector<std::uint64_t> directGifPacketWords;", source, StringComparison.Ordinal);
        Assert.Contains("const std::size_t firstTexturedTrianglePacketIndex = texturedTrianglePackets.size();", source, StringComparison.Ordinal);
        Assert.Contains("if (!createVifPacket && texturedTrianglePackets.size() > firstTexturedTrianglePacketIndex) {", source, StringComparison.Ordinal);
        Assert.Contains("directGifPacketWords.insert(directGifPacketWords.end(), firstTrianglePacket.begin(), firstTrianglePacket.begin() + 8u);", source, StringComparison.Ordinal);
        Assert.Contains("directGifPacketWords.insert(directGifPacketWords.end(), trianglePacket.begin() + 8u, trianglePacket.end());", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPerspectiveTextureVertexRegisterList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("directGifPacketWords.push_back(GIF_SET_TAG(3, 1, 0, 0, GIF_FLG_PACKED, 3));", source, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize(directGifPacketWords.size() * sizeof(std::uint64_t));", source, StringComparison.Ordinal);
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
        Assert.Contains("const float gsDepth = 1.0f - screenZ;", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint32_t gsZ = static_cast<std::uint32_t>(gsDepth * static_cast<float>(MaximumGsDepth));", source, StringComparison.Ordinal);
        Assert.Contains("vertex.xyz2 = vertex_to_XYZ2(gsGlobal, screenX, screenY, static_cast<int>(gsZ));", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_list_triangle_goraud_texture_stq_3d(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured VU GIF packets preserve perspective-correct coordinates instead of reverting to affine UV interpolation.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingTexturedTriangles_UsesPerspectiveCorrectStqCoordinates() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("GS_ST", source, StringComparison.Ordinal);
        Assert.Contains("PRIM_MAP_ST", source, StringComparison.Ordinal);
        Assert.Contains("vertex_to_STQ", source, StringComparison.Ordinal);
        Assert.Contains("rgba_to_RGBAQ", source, StringComparison.Ordinal);
        Assert.Contains("gsGlobal->PrimAAEnable,\n                0,\n                gsGlobal->PrimContext", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packetWords[packetWordIndex++] = triangleColor;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("static_cast<std::uint64_t>(GIF_REG_UV) << 4u", source, StringComparison.Ordinal);
        Assert.DoesNotContain("prim.mapping_type = PRIM_MAP_UV;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures fully visible textured vertices classify their frustum membership and pack their GS position from one projection calculation.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingVisibleTexturedTriangles_DoesNotProjectEachVertexTwice() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("bool TryClassifyAndBuildTexturedVertexPositionRegister(", source, StringComparison.Ordinal);
        Assert.Contains("const bool texturedVertexAProjected = TryClassifyAndBuildTexturedVertexPositionRegister(", source, StringComparison.Ordinal);
        Assert.Contains("if (texturedVertexAInside && texturedVertexBInside && texturedVertexCInside) {", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!TryBuildVertexPositionRegister(viewPositionA", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured lighting transforms the packed normal stream used by the PS2 renderer without redundantly resolving and normalizing a second copy of each face normal.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenLightingTexturedTriangles_UsesThePackedFaceNormalDirectly() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("packet2_t* Ps2VuVifPacketBuilder::GetPacket() const", methodStartIndex, StringComparison.Ordinal);
        string texturedAggregateEncoder = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);

        Assert.Contains("TransformPosition(faceNormal4, world)", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const ::float3 sourceTriangleNormal", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::vector<::float3>* runtimeNormals", texturedAggregateEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured aggregate encoder normalizes each face direction only after the world-space transform needed for lighting.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenLightingTexturedTriangles_DoesNotNormalizeFaceNormalsBeforeWorldTransform() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("packet2_t* Ps2VuVifPacketBuilder::GetPacket() const", methodStartIndex, StringComparison.Ordinal);
        string texturedAggregateEncoder = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);

        Assert.Contains("const ::float3 faceNormal(", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const ::float3 faceNormal = NormalizeOrFallback(", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.Contains("NormalizeOrFallback(\n                    TransformPosition(faceNormal4, world)", texturedAggregateEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured VU triangles receive the same complete screen-frustum clipping that protects untextured geometry near the camera.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenTexturedTriangleCrossesCameraBoundary_ClipsTheCompleteScreenFrustum() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("IsTexturedVertexInsideScreenFrustum", source, StringComparison.Ordinal);
        Assert.Contains("ClipTexturedTriangleAgainstScreenFrustum", source, StringComparison.Ordinal);
        Assert.Contains("ClipTexturedPolygonAgainstScreenFrustumPlane", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures opaque textured VU triangles reject backfaces after clipping has produced stable projected vertices.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingOpaqueTexturedTriangles_CullsBackfacesAfterProjection() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("if (!IsFrontFacingTriangle(screenAX, screenAY, screenBX, screenBY, screenCX, screenCY)) {", source, StringComparison.Ordinal);
        Assert.Contains("texturedCullRejectCount++;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the production textured aggregate encoder does not call the expensive clock function for every triangle while detailed diagnostics are disabled.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPerTriangleTimingDiagnosticsAreDisabled_DoesNotClockEachTexturedAggregateTriangle() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("packet2_t* Ps2VuVifPacketBuilder::GetPacket() const", methodStartIndex, StringComparison.Ordinal);

        Assert.True(methodStartIndex >= 0, "Expected the textured aggregate encoder method.");
        Assert.True(methodEndIndex > methodStartIndex, "Expected the next VU packet builder method after the textured aggregate encoder.");

        string texturedAggregateEncoder = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);

        Assert.Contains("constexpr bool EnableVuPerTriangleTimingDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::clock_t trianglePrepStartTicks = std::clock();", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::clock_t triangleEmitStartTicks = std::clock();", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::clock_t triangleLightingStartTicks = std::clock();", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::clock_t trianglePayloadFillStartTicks = std::clock();", texturedAggregateEncoder, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPerTriangleTimingDiagnostics) {", texturedAggregateEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures large textured triangles are clipped to every screen frustum plane before their projected coordinates are packed into the PS2 GS vertex registers.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenClippingTexturedTriangles_ClipsTheScreenFrustumBeforeGsPacking() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("enum class Ps2ScreenFrustumPlane", source, StringComparison.Ordinal);
        Assert.Contains("void ClipPolygonAgainstScreenFrustumPlane(", source, StringComparison.Ordinal);
        Assert.Contains("void ClipTriangleAgainstScreenFrustum(", source, StringComparison.Ordinal);
        Assert.Contains("Ps2ScreenFrustumPlane::Left", source, StringComparison.Ordinal);
        Assert.Contains("Ps2ScreenFrustumPlane::Right", source, StringComparison.Ordinal);
        Assert.Contains("Ps2ScreenFrustumPlane::Bottom", source, StringComparison.Ordinal);
        Assert.Contains("Ps2ScreenFrustumPlane::Top", source, StringComparison.Ordinal);
        Assert.Contains("ClipTriangleAgainstScreenFrustum(vertexA, vertexB, vertexC, nearPlaneDistance, projection, clippedVertices);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures oversized opaque models are represented by bounded triangle slices instead of being rejected as indivisible VU packet inputs.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenOpaqueModelExceedsPacketCapacity_UsesValidatedTriangleSlices() {
        string repositoryRootPath = GetRepositoryRootPath();
        string sliceHeaderPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueBatchSlice.hpp");
        string rendererSourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string packetBuilderSourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");

        Assert.True(File.Exists(sliceHeaderPath), $"Expected PS2 opaque triangle slice header at '{sliceHeaderPath}'.");

        string sliceHeader = File.ReadAllText(sliceHeaderPath);
        string rendererSource = File.ReadAllText(rendererSourcePath);
        string packetBuilderSource = File.ReadAllText(packetBuilderSourcePath);

        Assert.Contains("struct Ps2VuOpaqueBatchSlice final", sliceHeader, StringComparison.Ordinal);
        Assert.Contains("std::size_t FirstSourceTriangle", sliceHeader, StringComparison.Ordinal);
        Assert.Contains("std::size_t SourceTriangleCount", sliceHeader, StringComparison.Ordinal);
        Assert.Contains("static Ps2VuOpaqueBatchSlice Create", sliceHeader, StringComparison.Ordinal);
        Assert.Contains("CreateOpaqueBatchSlices", rendererSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuOpaqueBatchSlice", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("FirstSourceTriangle * 3u", packetBuilderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures opaque untextured batches with distinct colors can share one packet whenever their GS alpha state is identical.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenBatchingUntexturedOpaqueMaterials_GroupsCompatibleAlphaModesWithoutRequiringMaterialIdentity() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string source = File.ReadAllText(sourcePath);
        int groupStartIndex = source.IndexOf("std::size_t BuildCompatibleUntexturedGroups(", StringComparison.Ordinal);
        int groupEndIndex = source.IndexOf("Ps2RenderManager3D::Ps2RenderManager3D(", groupStartIndex, StringComparison.Ordinal);

        Assert.True(groupStartIndex >= 0, "Expected the untextured batch-grouping function.");
        Assert.True(groupEndIndex > groupStartIndex, "Expected the next renderer member after the untextured batch-grouping function.");

        string untexturedGroupBuilder = source.Substring(groupStartIndex, groupEndIndex - groupStartIndex);

        Assert.Contains("bool AreUntexturedBatchStatesCompatible(", source, StringComparison.Ordinal);
        Assert.Contains("first.Material->GetAlphaMode() == candidate.Material->GetAlphaMode()", source, StringComparison.Ordinal);
        Assert.Contains("!AreUntexturedBatchStatesCompatible(*compatibleBatches.front(), candidate)", untexturedGroupBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("candidate.Material != material", untexturedGroupBuilder, StringComparison.Ordinal);
        Assert.Contains("WaitForVif1BeforePacketReuse();\n                    dma_channel_wait(DMA_CHANNEL_GIF, 0);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures untextured packet capacity remains a bounded allocation limit now that direct GIF packets own their final triangle data.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenAggregatingColoredCubes_KeepsTheBoundedVifPacketBudget() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr std::uint16_t MaximumOpaqueUntexturedPacketQwords = 4096u;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_vif_flusha", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct untextured aggregate submission emits complete GIF triangle records instead of retaining payloads in VU1 memory.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenDirectGifUntexturedSubmissionIsEnabled_EmitsFinalTriangleRegisters() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("std::size_t Ps2VuVifPacketBuilder::AddOpaqueUntexturedBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", methodStartIndex, StringComparison.Ordinal);

        Assert.True(methodStartIndex >= 0, "Expected the untextured aggregate builder.");
        Assert.True(methodEndIndex > methodStartIndex, "Expected the textured aggregate builder after the untextured builder.");

        string untexturedDirectGifEncoder = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);
        int directGifStartIndex = untexturedDirectGifEncoder.IndexOf("if (!createVifPacket) {", StringComparison.Ordinal);
        int directGifEndIndex = untexturedDirectGifEncoder.IndexOf("GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);", directGifStartIndex, StringComparison.Ordinal);

        Assert.Contains("bool createVifPacket", source, StringComparison.Ordinal);
        Assert.True(directGifStartIndex >= 0, "Expected the untextured direct-GIF encoder branch.");
        Assert.True(directGifEndIndex > directGifStartIndex, "Expected the VIF fallback after the direct-GIF encoder branch.");
        Assert.Contains("BuildUntexturedTriangleGifPacketBytes(", source, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize(untexturedTrianglePackets.size() * UntexturedTriangleDirectGifPacketByteCount);", untexturedDirectGifEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);", untexturedDirectGifEncoder.Substring(directGifStartIndex, directGifEndIndex - directGifStartIndex), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct GIF triangles preserve the CPU triangle A/B/C winding used by the working direct textured path.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPatchingDirectGifPositions_PreservesCpuWindingOrder() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int helperStartIndex = source.IndexOf("bool BuildUntexturedTriangleGifPacketBytes(", StringComparison.Ordinal);
        int helperEndIndex = source.IndexOf("::float2 ResolveGsTextureCoordinate", helperStartIndex, StringComparison.Ordinal);

        Assert.True(helperStartIndex >= 0, "Expected the untextured direct-GIF triangle encoder.");
        Assert.True(helperEndIndex > helperStartIndex, "Expected the next helper after the direct-GIF triangle encoder.");

        string triangleEncoder = source.Substring(helperStartIndex, helperEndIndex - helperStartIndex);
        Assert.Contains("packetWords[packetWordIndex++] = positionARegister;", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = positionBRegister;", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = positionCRegister;", triangleEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct GIF submission owns its GS state and register-list payload rather than reusing the VU staging template.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifTriangles_UsesExplicitGifRegisterList() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int helperStartIndex = source.IndexOf("bool BuildUntexturedTriangleGifPacketBytes(", StringComparison.Ordinal);
        int helperEndIndex = source.IndexOf("::float2 ResolveGsTextureCoordinate", helperStartIndex, StringComparison.Ordinal);

        Assert.True(helperStartIndex >= 0, "Expected the untextured direct-GIF triangle encoder.");
        Assert.True(helperEndIndex > helperStartIndex, "Expected the next helper after the direct-GIF triangle encoder.");

        string triangleEncoder = source.Substring(helperStartIndex, helperEndIndex - helperStartIndex);
        Assert.Contains("UntexturedTriangleDirectGifPacketWordCount", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t UntexturedTriangleDirectGifPacketWordCount = 18u;", source, StringComparison.Ordinal);
        Assert.Contains("GIF_SET_TAG(1, 1, 0, 0, GIF_FLG_REGLIST, 8)", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("UntexturedTriangleDirectGifRegisterList", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_PRIM(", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_RGBAQ(", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = positionARegister;", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = positionBRegister;", triangleEncoder, StringComparison.Ordinal);
        Assert.Contains("packetWords[packetWordIndex++] = positionCRegister;", triangleEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("templateQwords", triangleEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("GifPacketTemplate", triangleEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct GIF triangle projection receives view-space positions while the VU fallback retains local positions for its WVP transform.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifTriangles_UsesViewSpacePositions() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("std::size_t Ps2VuVifPacketBuilder::AddOpaqueUntexturedBatches(", StringComparison.Ordinal);
        int directGifStartIndex = source.IndexOf("if (!createVifPacket) {", methodStartIndex, StringComparison.Ordinal);

        Assert.True(methodStartIndex >= 0, "Expected the untextured aggregate builder.");
        Assert.True(directGifStartIndex > methodStartIndex, "Expected the direct-GIF aggregate branch.");

        string untexturedAggregateBuilder = source.Substring(methodStartIndex, directGifStartIndex - methodStartIndex);
        Assert.Contains("createVifPacket ? packedPositionA : vertexA.ViewPosition", untexturedAggregateBuilder, StringComparison.Ordinal);
        Assert.Contains("createVifPacket ? packedPositionB : vertexB.ViewPosition", untexturedAggregateBuilder, StringComparison.Ordinal);
        Assert.Contains("createVifPacket ? packedPositionC : vertexC.ViewPosition", untexturedAggregateBuilder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct-GIF untextured aggregates retain near-plane clipping without using the faulty CPU side-frustum reject path.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifUntexturedAggregates_ClipsOnlyTheNearPlane() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);
        int methodStartIndex = source.IndexOf("std::size_t Ps2VuVifPacketBuilder::AddOpaqueUntexturedBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", methodStartIndex, StringComparison.Ordinal);

        Assert.True(methodStartIndex >= 0, "Expected the untextured aggregate builder.");
        Assert.True(methodEndIndex > methodStartIndex, "Expected the textured aggregate builder after the untextured builder.");

        string untexturedAggregateEncoder = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);
        Assert.Contains("ClipUntexturedTriangleAgainstNearPlane(vertexA, vertexB, vertexC, nearPlaneDistance, clippedUntexturedVertices);", untexturedAggregateEncoder, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipUntexturedTriangleAgainstScreenFrustum(vertexA, vertexB, vertexC, nearPlaneDistance, projection, clippedUntexturedVertices);", untexturedAggregateEncoder, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct untextured aggregate submission owns final GIF data and does not use VIF packet slots whose payload lifetime is shorter than VU execution.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenSubmittingUntexturedAggregates_UsesOwnedDirectGifPackets() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string source = File.ReadAllText(sourcePath);
        int routeStartIndex = source.IndexOf("if (!EnableUntexturedAggregatePacketDiagnostics", StringComparison.Ordinal);
        int routeEndIndex = source.IndexOf("batchIndex = followingBatchIndex - 1u;", routeStartIndex, StringComparison.Ordinal);

        Assert.True(routeStartIndex >= 0, "Expected the untextured aggregate renderer route.");
        Assert.True(routeEndIndex > routeStartIndex, "Expected the untextured aggregate route completion.");

        string untexturedAggregateRoute = source.Substring(routeStartIndex, routeEndIndex - routeStartIndex);
        int directGifStartIndex = untexturedAggregateRoute.IndexOf("if (UseDirectGifUntexturedSubmission) {", StringComparison.Ordinal);
        int directGifEndIndex = untexturedAggregateRoute.IndexOf("} else {\n                        WaitForVif1BeforePacketReuse();", directGifStartIndex, StringComparison.Ordinal);

        Assert.Contains("constexpr bool UseDirectGifUntexturedSubmission = true;", source, StringComparison.Ordinal);
        Assert.Contains("!UseDirectGifUntexturedSubmission);", untexturedAggregateRoute, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(gifPacket, DMA_CHANNEL_GIF, true);", untexturedAggregateRoute, StringComparison.Ordinal);
        Assert.True(directGifStartIndex >= 0, "Expected the untextured direct-GIF submit branch.");
        Assert.True(directGifEndIndex > directGifStartIndex, "Expected the diagnostic VIF fallback after direct GIF submission.");
        string untexturedDirectGifSubmit = untexturedAggregateRoute.Substring(directGifStartIndex, directGifEndIndex - directGifStartIndex);
        Assert.DoesNotContain("VuPacketSlots[ActiveVuPacketSlotIndex] = VuVifPacketBuilder.ReleasePacket();", untexturedDirectGifSubmit, StringComparison.Ordinal);
        Assert.DoesNotContain("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", untexturedDirectGifSubmit, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
