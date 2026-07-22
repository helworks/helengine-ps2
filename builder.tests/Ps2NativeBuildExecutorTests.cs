using System.Reflection;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the remaining generated-core validation behavior required by the PS2 native build executor.
/// </summary>
public sealed class Ps2NativeBuildExecutorTests {
    /// <summary>
    /// Verifies that the builder smoke-test entrypoint still completes after generated runtime manifest requirements change.
    /// </summary>
    [Fact]
    public void ProgramRunSmokeTest_WhenInvoked_CompletesWithoutException() {
        MethodInfo runSmokeTestMethod = typeof(helengine.ps2.builder.Program).GetMethod(
            "RunSmokeTest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(runSmokeTestMethod);

        Exception exception = Record.Exception(() => runSmokeTestMethod!.Invoke(null, null));

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that the opaque untextured VU program assembles one contiguous GS packet and kicks it once after processing the batch.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_ShouldAssembleOneContiguousPacketPerBatch() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("__ps2_opaque_draw_3d_output_start_loop:", source, StringComparison.Ordinal);
        Assert.Contains("sqi VF10, (VI03++)", source, StringComparison.Ordinal);
        Assert.Contains("sqi VF16, (VI03++)", source, StringComparison.Ordinal);
        Assert.Contains("xgkick VI06", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the current compact opaque VU path writes the XYZ2 ADC lane into the output packet before storing XYZ data.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactPacketPath_ShouldWriteAdcWordsIntoXyz2Slots() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("isw.w", source, StringComparison.Ordinal);
        Assert.Contains("2(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("4(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("6(VI04)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF01", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF02", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF03", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the current compact opaque VU path writes triangle vertices into packet slots using the CPU-facing winding order.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactPacketPath_ShouldSwapSecondAndThirdVertexStores() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("sq.xyz VF01, 2(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF03, 4(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF02, 6(VI04)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF02, 4(VI04)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF03, 6(VI04)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the compact untextured VU path preserves the host-built flat-color GIF template instead of overwriting RGBAQ slots in the microprogram.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("sq VF12, 21(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq VF12, 23(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq VF12, 25(VI00)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the compact untextured VU path keeps flat color host-owned and does not execute VU-side face-normal lighting math.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("opmula.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("opmsub.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF16, 30(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF19, 33(VI00)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the current compact opaque VU baseline stays on the non-clip-flag path that preserved the visible cube test.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingLastVisibleCubeTestBaseline_ShouldStayOnCompactPacketPath() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("clipw.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fcand", source, StringComparison.Ordinal);
        Assert.Contains("iadd VI04, VI03, VI00", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 native build consumes the editor's generated-core unity translation unit name instead of the retired amalgamated file name.
    /// </summary>
    [Fact]
    public void Ps2NativeBuildExecutor_WhenUsingCurrentEditorGeneratedCoreOutput_ShouldCompileUnityTranslationUnit() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string makefilePath = Path.Combine(repositoryRootPath, "Makefile");

        string source = File.ReadAllText(makefilePath);

        Assert.Contains("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 render-manager contract validator accepts the current generated-core layout that exposes cooked platform materials plus raw-asset loading without the removed raw-material virtual.
    /// </summary>
    [Fact]
    public void Ps2NativeBuildExecutor_WhenGeneratedCoreUsesCookedPlatformMaterialAndRawAssetEntry_DoesNotRequireRemovedRawMaterialVirtual() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string repositoryRoot = Path.Combine(workingRoot, "repo");
        string generatedHeaderPath = Path.Combine(generatedCoreRoot, "RenderManager3D.hpp");
        string nativeHeaderDirectoryPath = Path.Combine(repositoryRoot, "src", "platform", "ps2", "rendering");
        string nativeHeaderPath = Path.Combine(nativeHeaderDirectoryPath, "Ps2RenderManager3D.hpp");

        Directory.CreateDirectory(generatedCoreRoot);
        Directory.CreateDirectory(nativeHeaderDirectoryPath);
        File.WriteAllText(
            generatedHeaderPath,
            """
class RuntimeMaterial;
class PlatformMaterialAsset;
class ContentManager;

class RenderManager3D
{
public:
    virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);
    virtual ::RuntimeMaterial* BuildMaterialFromCooked(std::string cookedAssetPath);
    virtual ::RuntimeMaterial* BuildMaterialFromRawAsset(::ContentManager* assetContentManager, std::string contentRootPath, std::string materialAssetPath);
};
""");
        File.WriteAllText(
            nativeHeaderPath,
            """
class RuntimeMaterial;
class PlatformMaterialAsset;

class Ps2RenderManager3D
{
public:
    ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset) override;
    ::RuntimeMaterial* BuildMaterialFromCooked(std::string cookedAssetPath) override;
};
""");

        Exception exception = Record.Exception(() => InvokeValidateRenderManager3DContractPairing(repositoryRoot, generatedCoreRoot));

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that the PS2 renderer publishes its VU counters into the core-owned FPS overlay metrics used by the visible runtime HUD.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenDrawingVuPath_ShouldPublishCorePerformanceOverlayMetrics() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string rendererSourcePath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "Ps2RenderManager3D.cpp");
        string rendererHeaderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "Ps2RenderManager3D.hpp");

        string source = File.ReadAllText(rendererSourcePath);
        string header = File.ReadAllText(rendererHeaderPath);

        Assert.Contains("void PublishPerformanceOverlayMetrics() const;", header, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::PublishPerformanceOverlayMetrics() const", source, StringComparison.Ordinal);
        Assert.Contains("Core::get_Instance()->SetPerformanceOverlayMetrics(", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int>(LastSubmittedTriangleCount)", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int>(LastVuBatchDispatchCount)", source, StringComparison.Ordinal);
        Assert.Contains("PublishPerformanceOverlayMetrics();\n            return;", source, StringComparison.Ordinal);
        Assert.Contains("RenderOpaqueWithVuPath(plan, view, projection, viewport, camera->get_NearPlaneDistance());\n                PublishPerformanceOverlayMetrics();", source, StringComparison.Ordinal);
        Assert.Contains("DrawSoftwareDepthPass(\n            plan,\n            view,\n            projection,\n            viewport,\n            camera->get_NearPlaneDistance(),\n            cameraPosition,\n            cameraForward);\n        DrawHdrGlowPass(GsGlobal);\n        PublishPerformanceOverlayMetrics();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU setup builder does not rebuild per-batch payload constants for every triangle.
    /// </summary>
    [Fact]
    public void Ps2VuOpaqueUntexturedSetupBuilder_WhenBuildingTriangleSetups_ShouldHoistInvariantPayloadState() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuOpaqueUntexturedSetupBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string runtimeBranch = ExtractSourceRange(
            source,
            "const Ps2RuntimeModel* runtimeModel = batch.Proxy != nullptr ? batch.Proxy->GetModel() : nullptr;",
            "const std::clock_t triangleSetupEndTicks = std::clock();");
        string triangleLoop = ExtractSourceRange(
            runtimeBranch,
            "for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {",
            "                const std::clock_t triangleEmitEndTicks = std::clock();");

        Assert.Contains("TriangleSetups.reserve(triangleVertexCount / 3u);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("CopyMatrix(worldViewProjectionMatrix, worldViewProjectionMatrixWords);", runtimeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::Multiply(", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyMatrix(world, triangleSetup.WorldMatrix);", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("triangleSetup.GsScale[0] = viewport.Z * 0.5f;", triangleLoop, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that untextured VU packet encoding reuses resolved GIF templates instead of rebuilding duplicate templates per triangle.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedTriangles_ShouldReuseGifTemplatesByFlatColor() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string untexturedSetupBranch = ExtractSourceRange(
            source,
            "setupBuilder.Build(batch, world, view, projection, viewport, normalizedLightDirection, nearPlaneDistance, gsGlobal);",
            "} else {");
        string packetLoop = ExtractSourceRange(
            source,
            "for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : *untexturedTriangleSetups) {",
            "                if (EnableVuSingleDispatchDiagnostic) {");

        Assert.Contains("struct Ps2VuGifTemplateCacheEntry final", source, StringComparison.Ordinal);
        Assert.Contains("std::vector<Ps2VuGifTemplateCacheEntry> gifTemplateCache;", source, StringComparison.Ordinal);
        Assert.Contains("gifTemplateCache.reserve(untexturedTriangleSetups->size());", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.Contains("PopulateTrianglePayloadFromSetup(batch, triangleSetup, gsGlobal, gifTemplateCache, *trianglePayload);", packetLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateTriangleGifPacketTemplate(batch, flatColor, gsGlobal, *trianglePayload);", packetLoop, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that untextured VU packet encoding constructs large triangle payloads in vector storage instead of copying stack payloads into the staging vector.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedTriangles_ShouldConstructPayloadsInPlace() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string untexturedSetupBranch = ExtractSourceRange(
            source,
            "setupBuilder.Build(batch, world, view, projection, viewport, normalizedLightDirection, nearPlaneDistance, gsGlobal);",
            "} else {");
        string packetLoop = ExtractSourceRange(
            source,
            "for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : *untexturedTriangleSetups) {",
            "                if (EnableVuSingleDispatchDiagnostic) {");

        Assert.DoesNotContain("trianglePayloads.emplace_back();", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuLitTrianglePayload& payload = trianglePayloads.back();", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuLitTrianglePayload payload {};", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("trianglePayloads.push_back(payload);", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.Contains("Ps2VuLitTrianglePayload* trianglePayload = reinterpret_cast<Ps2VuLitTrianglePayload*>(packet.get()->next);", packetLoop, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the normal untextured VU path streams payloads directly into the final VIF packet instead of staging a second payload vector.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedTriangles_ShouldStreamPayloadsIntoPacketMemory() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string untexturedSetupBranch = ExtractSourceRange(
            source,
            "setupBuilder.Build(batch, world, view, projection, viewport, normalizedLightDirection, nearPlaneDistance, gsGlobal);",
            "} else {");

        Assert.Contains("const std::vector<Ps2VuOpaqueUntexturedTriangleSetup>* untexturedTriangleSetups = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("untexturedTriangleSetups = &setupBuilder.GetTriangleSetups();", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("trianglePayloads.emplace_back();", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateTrianglePayloadFromSetup(", untexturedSetupBranch, StringComparison.Ordinal);
        Assert.Contains("for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : *untexturedTriangleSetups) {", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuLitTrianglePayload* trianglePayload = reinterpret_cast<Ps2VuLitTrianglePayload*>(packet.get()->next);", source, StringComparison.Ordinal);
        Assert.Contains("PopulateTrianglePayloadFromSetup(batch, triangleSetup, gsGlobal, gifTemplateCache, *trianglePayload);", source, StringComparison.Ordinal);
        Assert.Contains("} else if (EnableVuFixedTriangleDiagnostics) {\n            for (const Ps2VuLitTrianglePayload& trianglePayload : trianglePayloads) {", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that untextured VU GIF-template caching happens before flat-color lighting resolution so duplicate cube face triangles avoid repeated lighting work.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedTriangles_ShouldCacheTemplatesByLightingInputs() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string payloadFromSetup = ExtractSourceRange(
            source,
            "void PopulateTrianglePayloadFromSetup(",
            "            std::memcpy(payload.GsOffset, triangleSetup.GsOffset, sizeof(triangleSetup.GsOffset));\n        }");
        string cacheHelper = ExtractSourceRange(
            source,
            "void PopulateTriangleGifPacketTemplateFromCache(",
            "        }\n\n        void PopulateTrianglePayloadFromSetup(");

        Assert.Contains("float FaceNormal[4];", source, StringComparison.Ordinal);
        Assert.Contains("float LightDirection[4];", source, StringComparison.Ordinal);
        Assert.Contains("AreLightingInputsEqual(entry, faceNormal, lightDirection)", cacheHelper, StringComparison.Ordinal);
        Assert.Contains("ResolveTexturedVertexColor(*batch.Material, faceNormal, lightDirection)", cacheHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveTexturedVertexColor", payloadFromSetup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the normal untextured VU path does not copy diagnostic GIF bytes when direct GIF diagnostics are disabled.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingNormalUntexturedPath_ShouldNotPopulateDiagnosticGifBytes() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string normalUntexturedPacketLoop = ExtractSourceRange(
            source,
            "for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : *untexturedTriangleSetups) {",
            "                if (EnableVuSingleDispatchDiagnostic) {");

        Assert.DoesNotContain("GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);", normalUntexturedPacketLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("std::memcpy(GifPacketBytes.data(), trianglePayload->GifPacketTemplate, TriangleGifPacketTemplateByteCount);", normalUntexturedPacketLoop, StringComparison.Ordinal);
        Assert.Contains("const std::vector<std::uint8_t>& Ps2VuVifPacketBuilder::GetGifPacketBytes() const", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the normal untextured VU packet path streams one payload per VU invocation on the last known-good single-payload contract.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingNormalUntexturedPath_ShouldUseSinglePayloadContract() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string normalUntexturedPacketBranch = ExtractSourceRange(
            source,
            "} else {\n            for (const Ps2VuLitTrianglePayload& trianglePayload : trianglePayloads) {",
            "        }\n\n        LastCompletedPhase = 6;");

        Assert.DoesNotContain("EnableVuTwoTriangleBatchDiagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VuDiagnosticBatchTriangleCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuTriangleBatchHeader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("batchHeader", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("for (const Ps2VuLitTrianglePayload& trianglePayload : trianglePayloads) {", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuLitTrianglePayload));", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_mscal(packet.get(), UntexturedMicroProgramAddress, 0);", normalUntexturedPacketBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU microprogram consumes one payload from `xtop` using the last known-good fixed offsets.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingSinglePayloadContract_ShouldConsumeOnePayloadFromXtop() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("NOP                                                        xtop VI02", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI03, VI02, 0x00000010", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF01, 40(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF02, 41(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF03, 42(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF04, 52(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF05, 53(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF06, 54(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF07, 55(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF08, 56(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF09, 57(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        isw.w VI04, 22(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        isw.w VI04, 24(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        isw.w VI04, 26(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        sq.xyz VF01, 22(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        sq.xyz VF03, 24(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        sq.xyz VF02, 26(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        xgkick VI03", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__ps2_opaque_draw_3d_triangle_loop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sqi VF10, (VI03++)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the timed VU packet encode window only builds the packet and does not include dead program-registry resolution.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenEncodingVuPacket_ShouldNotResolveUnusedProgramKindInsideTimedWindow() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string rendererPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "Ps2RenderManager3D.cpp");

        string source = File.ReadAllText(rendererPath);
        string timedEncodeWindow = ExtractSourceRange(
            source,
            "const std::clock_t vuPacketEncodeStartTicks = std::clock();",
            "const std::clock_t vuPacketEncodeEndTicks = std::clock();");

        Assert.Contains("VuVifPacketBuilder.AddOpaqueBatch(", timedEncodeWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("VuProgramRegistry.ResolveOpaqueProgram(batch)", timedEncodeWindow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that VU submission does not immediately block on VIF1 completion after the packet is sent.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenSubmittingVuPacket_ShouldDeferVifCompletionWaitToNextFrame() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string rendererPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "Ps2RenderManager3D.cpp");

        string source = File.ReadAllText(rendererPath);
        string normalSubmitBranch = ExtractSourceRange(
            source,
            "} else {\n                LastVuPacketPhase = 201;",
            "            }\n            (void)viewport;");

        Assert.Contains("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("LastVuPacketPhase = 202;", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("LastVuSubmitMilliseconds += ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);", normalSubmitBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque untextured VU packet builder clips triangles at the CPU near plane and leaves front-face rejection to the VU path.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldClipNearPlaneWithoutCpuFaceCulling() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string untexturedBranch = ExtractSourceRange(
            source,
            "} else if (!textured) {",
            "} else {");

        Assert.Contains("ClipUntexturedTriangleAgainstScreenFrustum(", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFrontFacingTriangle(", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque-untextured VU packet template kicks only the draw giftag and payload, with GS state emitted outside the kicked packet.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldKickOnlyDrawPayload() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);

        Assert.Contains("packet2_utils_gs_add_prim_giftag(gifPacket.get(), &prim, 3u, UntexturedTriangleRegisterList, 2u, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_gif_add_set(gifPacket.get(), 1);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_gs_add_lod(gifPacket.get(), &lod);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GS_REG_TEST", source, StringComparison.Ordinal);
        Assert.DoesNotContain("draw_prim_end(gifPacket.get()->next, 2, UntexturedTriangleRegisterList)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the VU GIF state encoder mirrors the active depth-test state without injecting a separate direct-GIF DMA packet outside the VIF-driven opaque path.
    /// </summary>
    [Fact]
    public void Ps2VuGifStateEncoder_WhenEmittingOpaqueState_ShouldRespectActiveDepthStateWithoutDirectGifSubmission() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuGifStateEncoder.cpp");

        string source = File.ReadAllText(builderPath);

        Assert.Contains("if (gsGlobal == nullptr) {", source, StringComparison.Ordinal);
        Assert.Contains("if (gsGlobal->ZBuffering == GS_SETTING_ON) {", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_test(gsGlobal, GS_ZTEST_ON);", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_test(gsGlobal, GS_ZTEST_OFF);", source, StringComparison.Ordinal);
        Assert.Contains("const Ps2RuntimeMaterial* material = batch.Material;", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_test(gsGlobal, GS_ATEST_OFF);", source, StringComparison.Ordinal);
        Assert.Contains("material == nullptr || material->GetAlphaMode() == ::Ps2MaterialAlphaMode::Opaque", source, StringComparison.Ordinal);
        Assert.Contains("material->GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaTest", source, StringComparison.Ordinal);
        Assert.Contains("material->GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaBlend", source, StringComparison.Ordinal);
        Assert.Contains("material->GetAlphaMode() == ::Ps2MaterialAlphaMode::Additive", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_primalpha(gsGlobal, GS_BLEND_BACK2FRONT, 0);", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 2, 2, 1, 0x80), 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_create(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_gif_add_set(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("dma_channel_send_packet2(packet.get(), DMA_CHANNEL_GIF, true);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated content-manager material registration now fails fast instead of being rewritten back onto the cooked-platform material contract.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenContentManagerUsesRawMaterialContract_ThrowsInsteadOfRewriting() {
        const string source = """
#include "RuntimeContentManagerConfiguration.hpp"
#include "ContentManager.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "AssetContentProcessor.hpp"
#include "MaterialAsset.hpp"

void RuntimeContentManagerConfiguration::ConfigureSharedAssetContentManager(::ContentManager* contentManager)
{
RegisterProcessorIfMissing<MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
}
""";

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source));

        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("should already register cooked platform materials", exception.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated scene material resolution now fails fast instead of being rewritten back onto the cooked-platform material contract.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesCurrentRawMaterialContract_ThrowsInsteadOfRewriting() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "MaterialAsset.hpp"
#include "ShaderAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);
::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);
this->TrackOwnedMaterial(runtimeMaterial);
this->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);
return runtimeMaterial;}
""";

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source));

        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("should already resolve cooked platform-owned materials", exception.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the older ownership-guarded raw resolver shape now fails fast instead of being rewritten back onto the cooked-platform material contract.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesLegacyGuardedRawMaterialContract_ThrowsInsteadOfRewriting() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "runtime/finally.hpp"
#include "MaterialAsset.hpp"
#include "ShaderAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
auto __releaseMaterialAssetGuard = he_cpp_make_scope_exit([&]() {
ReleaseTransientMaterialAsset(materialAsset);
});
::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);
auto __releaseShaderAssetGuard = he_cpp_make_scope_exit([&]() {
ReleaseTransientShaderAsset(shaderAsset);
});
::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);
this->TrackOwnedMaterial(runtimeMaterial);
this->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);
return runtimeMaterial;}
""";

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source));

        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("should already resolve cooked platform-owned materials", exception.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager headers now fail fast instead of being rewritten back onto the cooked-platform material contract.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesRawMaterialContract_ThrowsInsteadOfRewriting() {
        const string source = """
#pragma once
class RuntimeMaterial;
class MaterialAsset;
class ShaderAsset;
class RuntimeModel;
class ModelAsset;

class RenderManager3D
{
public:
    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);
    virtual ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) = 0;
};
""";

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeNormalizeGeneratedCoreSource("RenderManager3D.hpp", source));

        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("should already expose the cooked platform material contract", exception.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager sources now fail fast instead of being rewritten back onto the cooked-platform material contract.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesRawMaterialContract_ThrowsInsteadOfRewriting() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source));

        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("should already expose the cooked platform material default implementation", exception.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated render-manager headers using the generic cooked-platform-material seam remain unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#pragma once
class RuntimeMaterial;
class MaterialAsset;
class PlatformMaterialAsset;
class ShaderAsset;
class RuntimeModel;
class ModelAsset;

class RenderManager3D
{
public:
    virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);
    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);
    virtual ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) = 0;
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.hpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated render-manager sources using the generic cooked-platform-material seam remain unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)
{
throw new NotSupportedException("This renderer does not support platform-owned cooked material creation.");
}

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated runtime content-manager registration using the generic cooked-platform-material seam remains unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenContentManagerUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RuntimeContentManagerConfiguration.hpp"
#include "ContentManager.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "AssetContentProcessor.hpp"
#include "PlatformMaterialAsset.hpp"

void RuntimeContentManagerConfiguration::ConfigureSharedAssetContentManager(::ContentManager* contentManager)
{
RegisterProcessorIfMissing<PlatformMaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::PlatformMaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated scene material resolution using the generic cooked-platform-material seam remains unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "runtime/native_exceptions.hpp"
#include "runtime/native_string.hpp"
#include "ModelAsset.hpp"
#include "ContentManager.hpp"
#include "RuntimeModel.hpp"
#include "PlatformMaterialAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"
#include "TextureAsset.hpp"
#include "RuntimeTexture.hpp"
#include "RenderManager2D.hpp"
#include "MaterialPropertyBlock.hpp"
#include "FontAsset.hpp"
#include "system/io/path.hpp"
#include "system/io/file.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "StandardMaterialTextureBindingDefaults.hpp"
#include "SceneAssetReferenceSourceKind.hpp"
#include "ShaderTargetNames.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
    if (reference == nullptr)
    {
throw new ArgumentNullException("reference");
    }
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::PlatformMaterialAsset *materialAsset = this->AssetContentManager->Load<PlatformMaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
return Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that already-disabled runtime graphics manifests pass through unchanged instead of being treated as rewrite failures.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRuntimeGraphicsManifestAlreadyUsesDisabledValues_LeavesSourceUnchanged() {
        const string source = """
const HERuntimeGraphicsRendererManifest RuntimeGraphicsRendererManifest =
{
    false,
    HERuntimePostProcessTier::Disabled,
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("runtime", "runtime_graphics_renderer_manifest.cpp"), source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated light-component includes already use the plain light-type header name and require no PS2 rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenLightComponentAlreadyUsesPlainHeaderName_LeavesSourceUnchanged() {
        const string source = """
#include "AmbientLightComponent.hpp"
#include "LightType.hpp"
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("AmbientLightComponent.cpp", source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that generated-core validation leaves valid files untouched on disk instead of normalizing line endings and rewriting them.
    /// </summary>
    [Fact]
    public void ValidateGeneratedCoreSources_WhenValidFileUsesCrLf_DoesNotRewriteFile() {
        string generatedCoreRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(generatedCoreRootPath);

        try {
            string sourcePath = Path.Combine(generatedCoreRootPath, "RenderManager3D.hpp");
            const string source = "virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);\r\n";
            File.WriteAllText(sourcePath, source);

            InvokeValidateGeneratedCoreSources(generatedCoreRootPath);

            string currentContents = File.ReadAllText(sourcePath);
            Assert.Equal(source, currentContents);
        } finally {
            Directory.Delete(generatedCoreRootPath, true);
        }
    }

    /// <summary>
    /// Verifies that current generated scroll components already use value-default `int2` construction and require no PS2 rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenScrollComponentAlreadyUsesValueDefaultInt2Construction_LeavesSourceUnchanged() {
        const string source = """
ScrollComponent::ScrollComponent() : ScrollOffsetChanged(), ScrollOffset(0), SizeValue(), ItemCountValue(0)
{
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("ScrollComponent.cpp", source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated font-asset loops already use standard map entry access and require no PS2 rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFontAssetAlreadyUsesStandardMapEntryAccess_LeavesSourceUnchanged() {
        const string source = """
for (const auto& entry : this->Characters->Items()) {
    const char key = entry.first;
    ::FontChar value = entry.second;
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FontAsset.cpp", source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated FPS components already call the overlay helper through the instance and require no PS2 rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFpsComponentAlreadyUsesInstanceOverlayHelper_LeavesSourceUnchanged() {
        const string source = """
class FPSComponent
{
private:
    std::string FormatOverlaySecondaryLine(std::string baseRenderText);
};

void FPSComponent::ApplyCurrentOverlayText()
{
    this->RenderTextComponent->set_Text(this->FormatOverlaySecondaryLine(this->RenderFpsText));
}
""";

        string normalizedHeader = InvokeNormalizeGeneratedCoreSource("FPSComponent.hpp", source);
        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FPSComponent.cpp", source);

        Assert.Equal(source, normalizedHeader);
        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated scene-manager trace helpers no longer require the PS2-specific array helper rename.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenSceneManagerDoesNotUseLegacyArrayDeletionHelper_LeavesSourceUnchanged() {
        const string source = """
void SceneManager::ReleaseOwnedTextures()
{
    DeleteGeneratedArray_SceneManager(cachedTextureArray);
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("SceneManager.cpp", source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated file-stream sources already own the PS2 direct-disc read path and require no PS2 build rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFileStreamAlreadyOwnsPs2DirectReadSupport_LeavesSourceUnchanged() {
        const string source = """
#include <cstdio>  // For std::FILE*
#include <algorithm>
#include <cerrno>
#include <fcntl.h>
#if defined(_WIN32)
#include <io.h>
#else
#include <unistd.h>
#endif

#if HE_CPP_PLATFORM_PS2
namespace {
    bool FileStreamSupportStartsWithPs2CdromPrefix(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0;
    }
}
#endif

FileStream::FileStream(const char* path, FileMode mode)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true) {
#if HE_CPP_PLATFORM_PS2
    std::string resolvedPs2ReadPath = FileStreamSupportResolvePs2DiscReadPath(path != nullptr ? path : "");
    bool usesPs2DirectRead = mode == FileMode::Open && FileStreamSupportStartsWithPs2CdromPrefix(resolvedPs2ReadPath);
    if (usesPs2DirectRead) {
        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);
        ownsMemoryBuffer = true;
        writable = false;
        length = memoryBuffer.size();
        return;
    }
#endif
    file = std::fopen(path, GetFileMode(mode));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("system", "io", "file-stream.cpp"), source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Verifies that current generated file-stream headers already use the owned-memory layout directly and require no PS2 build rewrite.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFileStreamHeaderAlreadyUsesOwnedMemoryLayout_LeavesSourceUnchanged() {
        const string source = """
class FileStream : public Stream {
private:
    std::FILE* file;
    std::vector<uint8_t> memoryBuffer;
    size_t position;
    size_t length;
    bool ownsMemoryBuffer;
    bool writable;
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("system", "io", "file-stream.hpp"), source);

        Assert.Equal(source, normalizedSource);
    }

    /// <summary>
    /// Invokes the private generated-core validation entry point so tests can assert the PS2 generated-source contract checks.
    /// </summary>
    /// <param name="fileName">Generated source file name passed to the validation routine.</param>
    /// <param name="contents">Generated source contents passed to the validation routine.</param>
    /// <returns>The original generated source contents when validation succeeds.</returns>
    static string InvokeNormalizeGeneratedCoreSource(string fileName, string contents) {
        MethodInfo normalizeMethod = typeof(Ps2NativeBuildExecutor).GetMethod(
            "ValidateGeneratedCoreSource",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ValidateGeneratedCoreSource reflection lookup failed.");

        normalizeMethod.Invoke(null, [fileName, contents]);
        return contents;
    }

    /// <summary>
    /// Invokes the private generated-core directory validation entry point so tests can assert that valid files are not rewritten on disk.
    /// </summary>
    /// <param name="generatedCoreRootPath">Generated-core root passed to the validation routine.</param>
    static void InvokeValidateGeneratedCoreSources(string generatedCoreRootPath) {
        MethodInfo validateMethod = typeof(Ps2NativeBuildExecutor).GetMethod(
            "ValidateGeneratedCoreSources",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ValidateGeneratedCoreSources reflection lookup failed.");

        validateMethod.Invoke(null, [generatedCoreRootPath]);
    }

    /// <summary>
    /// Invokes the private render-manager contract validator so tests can assert compatibility decisions against generated-core headers.
    /// </summary>
    /// <param name="repositoryRootPath">Repository root path passed to the validator.</param>
    /// <param name="generatedCoreRootPath">Generated-core root path passed to the validator.</param>
    static void InvokeValidateRenderManager3DContractPairing(string repositoryRootPath, string generatedCoreRootPath) {
        MethodInfo validateMethod = typeof(Ps2NativeBuildExecutor).GetMethod(
            "ValidateRenderManager3DContractPairing",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ValidateRenderManager3DContractPairing reflection lookup failed.");

        validateMethod.Invoke(null, [repositoryRootPath, generatedCoreRootPath]);
    }

    /// <summary>
    /// Ensures a VIF packet remains renderer-owned until VIF1 has completed its DMA read.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPacketIsSubmitted_TransfersPacketOwnershipToRendererSlot() {
        string root = ResolveRepositoryRoot();
        string builderHeader = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        string builderSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string managerHeader = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.hpp"));
        string managerSource = File.ReadAllText(Path.Combine(root, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.Contains("packet2_t* ReleasePacket();", builderHeader, StringComparison.Ordinal);
        Assert.Contains("packet2_t* Ps2VuVifPacketBuilder::ReleasePacket()", builderSource, StringComparison.Ordinal);
        Assert.Contains("packet2_t* VuPacketSlots[2] = { nullptr, nullptr };", managerHeader, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::ReleaseVuPacketSlot", managerSource, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.ReleasePacket()", managerSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Extracts a contiguous source section bounded by exact start and end markers so tests can reason about a single branch without matching unrelated helper code elsewhere in the file.
    /// </summary>
    /// <param name="source">Full source text.</param>
    /// <param name="startMarker">Exact text where the desired section begins.</param>
    /// <param name="endMarker">Exact text that terminates the desired section.</param>
    /// <returns>The substring between the provided markers, including the start marker and excluding the end marker.</returns>
    static string ExtractSourceRange(string source, string startMarker, string endMarker) {
        int startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0) {
            throw new InvalidOperationException($"Start marker '{startMarker}' was not found.");
        }

        int endIndex = source.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        if (endIndex < 0) {
            throw new InvalidOperationException($"End marker '{endMarker}' was not found.");
        }

        return source[startIndex..endIndex];
    }
}
