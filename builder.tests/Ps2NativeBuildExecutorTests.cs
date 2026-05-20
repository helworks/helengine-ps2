using System.Reflection;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the remaining generated-core normalization behavior required by the PS2 native build executor.
/// </summary>
public sealed class Ps2NativeBuildExecutorTests {
    /// <summary>
    /// Verifies that the opaque untextured VU program starts from the current header-driven batch contract.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_ShouldStartAsKickOnlyBaseline() {
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

        string source = ReadSourceText(programPath);

        Assert.Contains("xtop VI02", source, StringComparison.Ordinal);
        Assert.Contains("ilw.x VI05, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("iadd VI07, VI02, VI03", source, StringComparison.Ordinal);
        Assert.Contains("xgkick VI07", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the transform-only opaque VU path writes the XYZ2 ADC lane into packet memory before storing XYZ data.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldWriteAdcWordsIntoXyz2Slots() {
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

        string source = ReadSourceText(programPath);

        Assert.Contains("isw.w", source, StringComparison.Ordinal);
        Assert.Contains("6(VI08)", source, StringComparison.Ordinal);
        Assert.Contains("8(VI08)", source, StringComparison.Ordinal);
        Assert.Contains("10(VI08)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF01", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF02", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF03", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the transform-only opaque VU path writes triangle vertices into packet slots using the CPU-facing winding order.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldSwapSecondAndThirdVertexStores() {
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

        string source = ReadSourceText(programPath);

        Assert.Contains("sq.xyz VF01, 6(VI08)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF03, 8(VI08)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF02, 10(VI08)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF02, 8(VI08)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF03, 10(VI08)", source, StringComparison.Ordinal);
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

        string source = ReadSourceText(programPath);

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

        string source = ReadSourceText(programPath);

        Assert.DoesNotContain("opmula.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("opmsub.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF16, 30(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF19, 33(VI00)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the last visible rotating cube_test VU baseline stays on the earlier transform-only path instead of the later VU clip-flag variant that regressed to a black frame.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingLastVisibleCubeTestBaseline_ShouldStayOnTransformOnlyPath() {
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

        string source = ReadSourceText(programPath);

        Assert.DoesNotContain("clipw.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fcand", source, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI04, VI00, 0x00000000", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 native build consumes the editor's generated-core unity translation unit name instead of the retired amalgamated file name.
    /// </summary>
    [Fact]
    public void Ps2NativeBuildExecutor_WhenUsingCurrentEditorGeneratedCoreOutput_ShouldCompileUnityTranslationUnit() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string makefilePath = Path.Combine(repositoryRootPath, "Makefile");

        string source = ReadSourceText(makefilePath);

        Assert.Contains("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
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

        string source = ReadSourceText(rendererSourcePath);
        string header = ReadSourceText(rendererHeaderPath);

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

        string source = ReadSourceText(builderPath);
        string runtimeBranch = ExtractSourceRange(
            source,
            "const Ps2RuntimeModel* runtimeModel = batch.Proxy != nullptr ? batch.Proxy->GetModel() : nullptr;",
            "const std::clock_t triangleSetupEndTicks = std::clock();");
        string triangleLoop = ExtractSourceRange(
            runtimeBranch,
            "for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {",
            "                const std::clock_t triangleEmitEndTicks = std::clock();");

        Assert.Contains("TriangleSetups.reserve(triangleVertexCount / 3u);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply(worldCopy, viewCopy, worldViewMatrix);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);", runtimeBranch, StringComparison.Ordinal);
        Assert.Contains("CopyMatrix(worldViewProjectionMatrix, worldViewProjectionMatrixWords);", runtimeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::Multiply(", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyMatrix(world, triangleSetup.WorldMatrix);", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("triangleSetup.GsScale[0] = viewport.Z * 0.5f;", triangleLoop, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that untextured VU setup avoids world-position transforms for triangles that only need packed local positions in the VU input stream.
    /// </summary>
    [Fact]
    public void Ps2VuOpaqueUntexturedSetupBuilder_WhenBuildingTriangleSetups_ShouldOnlyTransformDebugProbeTriangles() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuOpaqueUntexturedSetupBuilder.cpp");

        string source = ReadSourceText(builderPath);
        string triangleLoop = ExtractSourceRange(
            source,
            "for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {",
            "                const std::clock_t triangleEmitEndTicks = std::clock();");

        Assert.Contains("const bool captureDebugTriangle = SubmittedTriangleCount < 2u;", triangleLoop, StringComparison.Ordinal);
        Assert.Contains("if (captureDebugTriangle) {", triangleLoop, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleVertexA0 = ::float4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);", triangleLoop, StringComparison.Ordinal);
        Assert.Contains("else if (SubmittedTriangleCount == 2u)", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("const ::float4 worldPositionA4", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("const ::float4 worldPositionB4", triangleLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("const ::float4 worldPositionC4", triangleLoop, StringComparison.Ordinal);
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

        string source = ReadSourceText(builderPath);
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
    /// Verifies that the normal untextured VU path does not reserve diagnostic or textured staging vectors before it knows those paths are active.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingNormalUntexturedPath_ShouldNotReserveUnusedStagingVectors() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);
        string beforeBranch = ExtractSourceRange(
            source,
            "const bool textured = batch.Textured && textureWidth > 0 && textureHeight > 0;",
            "if (EnableVuFixedTriangleDiagnostics) {");
        string fixedDiagnosticBranch = ExtractSourceRange(
            source,
            "if (EnableVuFixedTriangleDiagnostics) {",
            "} else if (!textured) {");
        string texturedBranch = ExtractSourceRange(
            source,
            "} else {",
            "        }\n        const std::clock_t triangleSetupEndTicks = std::clock();");

        Assert.DoesNotContain("trianglePayloads.reserve", beforeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedTrianglePackets.reserve", beforeBranch, StringComparison.Ordinal);
        Assert.Contains("trianglePayloads.reserve(1u);", fixedDiagnosticBranch, StringComparison.Ordinal);
        Assert.Contains("texturedTrianglePackets.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);", texturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that untextured VU template construction writes the fixed GIF qwords directly instead of allocating a gsKit packet for every distinct lit face.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPopulatingUntexturedGifTemplate_ShouldWriteTemplateQwordsDirectly() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);
        string templateBuilder = ExtractSourceRange(
            source,
            "void PopulateTriangleGifPacketTemplate(",
            "        ::float2 ResolveGsTextureCoordinate");

        Assert.Contains("void WriteGifTemplateQword(std::uint8_t* templateBytes, std::size_t qwordIndex, std::uint64_t lowWord, std::uint64_t highWord)", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t BuildPackedAdGiftag(std::uint32_t loopCount)", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t BuildUntexturedPrimitiveGiftag(const prim_t& prim)", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t BuildUntexturedTex1Register()", source, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 0u, BuildPackedAdGiftag(1u), GIF_REG_AD);", templateBuilder, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 1u, ResolveOpaqueUntexturedTestRegister(gsGlobal), GS_REG_TEST);", templateBuilder, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 4u, BuildUntexturedPrimitiveGiftag(prim), UntexturedTriangleRegisterList);", templateBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("std::unique_ptr<packet2_t", templateBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_gs_add_prim_giftag", templateBuilder, StringComparison.Ordinal);
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

        string source = ReadSourceText(builderPath);
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

        string source = ReadSourceText(builderPath);
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

        string source = ReadSourceText(builderPath);
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

        string source = ReadSourceText(builderPath);
        string normalUntexturedPacketLoop = ExtractSourceRange(
            source,
            "for (const Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup : *untexturedTriangleSetups) {",
            "                if (EnableVuSingleDispatchDiagnostic) {");

        Assert.DoesNotContain("GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);", normalUntexturedPacketLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("std::memcpy(GifPacketBytes.data(), trianglePayload->GifPacketTemplate, TriangleGifPacketTemplateByteCount);", normalUntexturedPacketLoop, StringComparison.Ordinal);
        Assert.Contains("const std::vector<std::uint8_t>& Ps2VuVifPacketBuilder::GetGifPacketBytes() const", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the normal untextured VU packet path uses the cube-sized production batch contract instead of the two-triangle diagnostic contract.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingNormalUntexturedPath_ShouldUseCubeSizedBatchContract() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);

        Assert.Contains("constexpr std::uint32_t MaxUntexturedBatchTriangleCount = 12u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableVuSingleTriangleDispatchFallback = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableVuTwoTriangleBatchDiagnostic = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VuDiagnosticBatchTriangleCount", source, StringComparison.Ordinal);
        Assert.Contains("UntexturedBatchHeaderQwordCount", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU microprogram consumes one cube-sized payload batch through the runtime batch header.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCubeSizedBatchContract_ShouldConsumeTwelvePayloads() {
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

        string source = ReadSourceText(programPath);

        Assert.Contains("NOP                                                        xtop VI02", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.x VI05, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.y VI03, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.z VI10, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.w VI06, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("__ps2_opaque_draw_3d_build_batch_loop:", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI08, VI08, 0x0000000b", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI09, VI09, 0x00000004", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI05, VI05, -1", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ibne VI05, VI00, __ps2_opaque_draw_3d_build_batch_loop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__ps2_opaque_draw_3d_triangle_pair_loop", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU packet builder uploads an explicit batch header and reserves contiguous GIF output memory for one batch kick.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedBatch_ShouldUploadBatchHeaderAndOutputArea() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string layoutPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuOpaqueUntexturedBatchLayout.hpp");

        string source = ReadSourceText(builderPath);
        string layout = ReadSourceText(layoutPath);
        string normalUntexturedPacketBranch = ExtractSourceRange(
            source,
            "if (!EnableVuSingleTriangleDispatchFallback) {",
            "            } else {");

        Assert.Contains("struct alignas(16) Ps2VuUntexturedBatchHeader final", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t TriangleCount;", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t OutputGifQwordOffset;", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t ConstantsQwordOffset;", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t TriangleInputQwordOffset;", layout, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t UntexturedBatchHeaderQwordCount = 1u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t UntexturedBatchOutputGifQwordCount = MaxUntexturedBatchTriangleCount * TriangleGifPacketTemplateQwordCount;", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuUntexturedBatchHeader* batchHeader = reinterpret_cast<Ps2VuUntexturedBatchHeader*>(packet.get()->next);", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("batchHeader->TriangleCount = MaxUntexturedBatchTriangleCount;", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("batchHeader->OutputGifQwordOffset = UntexturedBatchHeaderQwordCount;", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("batchHeader->ConstantsQwordOffset = UntexturedBatchConstantsQwordOffset;", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("batchHeader->TriangleInputQwordOffset = UntexturedBatchTriangleInputQwordOffset;", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(packet.get()->next, outputTemplates[batchTriangleIndex].data(), TriangleGifPacketTemplateByteCount);", normalUntexturedPacketBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that one-XGKICK untextured batches clear primitive GIF termination on intermediate triangle templates so the GS consumes the whole generated stream.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedBatch_ShouldOnlyTerminateFinalGifTemplate() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);
        string normalUntexturedPacketBranch = ExtractSourceRange(
            source,
            "if (!EnableVuSingleTriangleDispatchFallback) {",
            "            } else {");

        Assert.Contains("constexpr std::size_t TrianglePrimitiveGiftagQwordIndex = 4u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint64_t GiftagEopBit = 1ull << 15u;", source, StringComparison.Ordinal);
        Assert.Contains("void SetTriangleGifTemplateEop(std::array<std::uint8_t, TriangleGifPacketTemplateByteCount>& outputTemplate, bool enabled)", source, StringComparison.Ordinal);
        Assert.Contains("giftag &= ~GiftagEopBit;", source, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(giftagBytes, &giftag, sizeof(giftag));", source, StringComparison.Ordinal);
        Assert.Contains("SetTriangleGifTemplateEop(outputTemplates[batchTriangleIndex], batchTriangleIndex == MaxUntexturedBatchTriangleCount - 1u);", normalUntexturedPacketBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU microprogram builds the complete batch GIF stream before issuing one XGKICK.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenBuildingBatchGifStream_ShouldKickOnceAfterBatchLoop() {
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

        string source = ReadSourceText(programPath);
        string batchLoop = ExtractSourceRange(
            source,
            "__ps2_opaque_draw_3d_build_batch_loop:",
            "         NOP                                                        ibne VI05, VI00, __ps2_opaque_draw_3d_build_batch_loop");

        Assert.Equal(1, source.Split(new[] { "xgkick" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("__ps2_opaque_draw_3d_build_batch_loop:", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.x VI05, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.y VI03, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.z VI10, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.w VI06, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iadd VI07, VI02, VI03", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        xgkick VI07", source, StringComparison.Ordinal);
        Assert.DoesNotContain("xgkick", batchLoop, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the normal untextured VU batch uploads compact per-batch constants and per-triangle inputs instead of full per-triangle payloads.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingUntexturedBatch_ShouldUseCompactBatchInputs() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");
        string layoutPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuOpaqueUntexturedBatchLayout.hpp");

        string source = ReadSourceText(builderPath);
        string layout = ReadSourceText(layoutPath);
        string normalUntexturedPacketBranch = ExtractSourceRange(
            source,
            "if (!EnableVuSingleTriangleDispatchFallback) {",
            "            } else {");

        Assert.Contains("struct alignas(16) Ps2VuUntexturedBatchConstants final", layout, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuUntexturedTriangleInput final", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t ConstantsQwordOffset;", layout, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t TriangleInputQwordOffset;", layout, StringComparison.Ordinal);
        Assert.Contains("Ps2VuUntexturedBatchConstants batchConstants {};", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("std::array<Ps2VuUntexturedTriangleInput, MaxUntexturedBatchTriangleCount> triangleInputs {};", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("PopulateUntexturedBatchConstants((*untexturedTriangleSetups)[triangleIndex], batchConstants);", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.Contains("PopulateUntexturedTriangleInput(triangleSetup, triangleInputs[batchTriangleIndex]);", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("std::array<Ps2VuLitTrianglePayload, MaxUntexturedBatchTriangleCount>", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuLitTrianglePayload));", normalUntexturedPacketBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateTrianglePayloadFromSetup(batch, triangleSetup, gsGlobal, gifTemplateCache, batchPayloads[batchTriangleIndex]);", normalUntexturedPacketBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the untextured VU microprogram loads batch constants once and consumes compact triangle inputs inside the loop.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactBatchInputs_ShouldLoadConstantsOutsideLoop() {
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

        string source = ReadSourceText(programPath);
        string beforeBatchLoop = ExtractSourceRange(
            source,
            "__ps2_opaque_draw_3d:",
            "__ps2_opaque_draw_3d_build_batch_loop:");
        string batchLoop = ExtractSourceRange(
            source,
            "__ps2_opaque_draw_3d_build_batch_loop:",
            "         NOP                                                        ibne VI05, VI00, __ps2_opaque_draw_3d_build_batch_loop");

        Assert.Contains("NOP                                                        ilw.z VI10, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        ilw.w VI06, 0(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iadd VI10, VI02, VI10", source, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF04, 0(VI10)", beforeBatchLoop, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF08, 4(VI10)", beforeBatchLoop, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF01, 0(VI09)", batchLoop, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF02, 1(VI09)", batchLoop, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF03, 2(VI09)", batchLoop, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI09, VI09, 0x00000004", batchLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF04, 52(VI09)", batchLoop, StringComparison.Ordinal);
        Assert.DoesNotContain("iaddiu VI09, VI09, 0x0000003a", batchLoop, StringComparison.Ordinal);
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

        string source = ReadSourceText(rendererPath);
        string timedEncodeWindow = ExtractSourceRange(
            source,
            "const std::clock_t vuPacketEncodeStartTicks = std::clock();",
            "const std::clock_t vuPacketEncodeEndTicks = std::clock();");

        Assert.Contains("VuVifPacketBuilder.AddOpaqueBatch(", timedEncodeWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("VuProgramRegistry.ResolveOpaqueProgram(batch)", timedEncodeWindow, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that VU submission waits before packet dispatch and does not immediately block after the packet is sent.
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

        string source = ReadSourceText(rendererPath);
        string normalSubmitBranch = ExtractSourceRange(
            source,
            "} else {\n                LastVuPacketPhase = 201;",
            "            }\n            (void)viewport;");

        Assert.Contains("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.True(
            normalSubmitBranch.IndexOf("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", StringComparison.Ordinal)
            < normalSubmitBranch.IndexOf("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", StringComparison.Ordinal));
        Assert.Contains("LastVuPacketPhase = 202;", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("const double vuSubmitOnlyMilliseconds = ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("LastVuSubmitMilliseconds += vuSubmitOnlyMilliseconds;", normalSubmitBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that VU packet ownership moves out of the builder after encoding so the renderer can keep submitted DMA packets alive without blocking before the next encode.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenPacketIsSubmitted_ShouldReleasePacketOwnershipToRenderer() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderHeaderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.hpp");
        string builderSourcePath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string header = ReadSourceText(builderHeaderPath);
        string source = ReadSourceText(builderSourcePath);
        string releaseMethod = ExtractSourceRange(
            source,
            "packet2_t* Ps2VuVifPacketBuilder::ReleasePacket()",
            "    std::size_t Ps2VuVifPacketBuilder::GetPacketByteCount() const");

        Assert.Contains("packet2_t* ReleasePacket();", header, StringComparison.Ordinal);
        Assert.Contains("packet2_t* releasedPacket = Packet;", releaseMethod, StringComparison.Ordinal);
        Assert.Contains("Packet = nullptr;", releaseMethod, StringComparison.Ordinal);
        Assert.Contains("return releasedPacket;", releaseMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the VU renderer waits after packet encoding, not before it, so CPU packet assembly can overlap the prior VIF/VU submission.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenEncodingVuPacket_ShouldOverlapPreviousVifSubmission() {
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

        string source = ReadSourceText(rendererSourcePath);
        string header = ReadSourceText(rendererHeaderPath);
        string vuPath = ExtractSourceRange(
            source,
            "void Ps2RenderManager3D::RenderOpaqueWithVuPath(",
            "    void Ps2RenderManager3D::DrawOpaqueProxy(");
        string beforeEncodeWindow = ExtractSourceRange(
            vuPath,
            "for (const Ps2VuOpaqueBatch& batch : batches) {",
            "const std::clock_t vuPacketEncodeStartTicks = std::clock();");
        string normalSubmitBranch = ExtractSourceRange(
            vuPath,
            "} else {\n                LastVuPacketPhase = 201;",
            "            }\n            (void)viewport;");

        Assert.Contains("~Ps2RenderManager3D() override;", header, StringComparison.Ordinal);
        Assert.Contains("void ReleaseSubmittedVuPackets();", header, StringComparison.Ordinal);
        Assert.Contains("std::vector<packet2_t*> SubmittedVuPackets;", header, StringComparison.Ordinal);
        Assert.DoesNotContain("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", beforeEncodeWindow, StringComparison.Ordinal);
        Assert.Contains("packet2_t* packet = VuVifPacketBuilder.ReleasePacket();", vuPath, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t vuCompletionWaitStartTicks = std::clock();", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("ReleaseSubmittedVuPackets();", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("SubmittedVuPackets.push_back(packet);", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("packet = nullptr;", normalSubmitBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that VU timing separates DMA wait, completion wait, and submit-only costs before aggregating them into the existing overlay values.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenMeasuringVuSubmission_ShouldSplitWaitAndSubmitTiming() {
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

        string source = ReadSourceText(rendererSourcePath);
        string header = ReadSourceText(rendererHeaderPath);
        string vuPath = ExtractSourceRange(
            source,
            "void Ps2RenderManager3D::RenderOpaqueWithVuPath(",
            "    void Ps2RenderManager3D::DrawOpaqueProxy(");
        string normalSubmitBranch = ExtractSourceRange(
            vuPath,
            "} else {\n                LastVuPacketPhase = 201;",
            "            }\n            (void)viewport;");

        Assert.Contains("double GetLastVuInitialWaitMilliseconds() const;", header, StringComparison.Ordinal);
        Assert.Contains("double GetLastVuCompletionWaitMilliseconds() const;", header, StringComparison.Ordinal);
        Assert.Contains("double GetLastVuSubmitOnlyMilliseconds() const;", header, StringComparison.Ordinal);
        Assert.Contains("double LastVuInitialWaitMilliseconds;", header, StringComparison.Ordinal);
        Assert.Contains("double LastVuCompletionWaitMilliseconds;", header, StringComparison.Ordinal);
        Assert.Contains("double LastVuSubmitOnlyMilliseconds;", header, StringComparison.Ordinal);
        Assert.Contains("double Ps2RenderManager3D::GetLastVuInitialWaitMilliseconds() const", source, StringComparison.Ordinal);
        Assert.Contains("double Ps2RenderManager3D::GetLastVuCompletionWaitMilliseconds() const", source, StringComparison.Ordinal);
        Assert.Contains("double Ps2RenderManager3D::GetLastVuSubmitOnlyMilliseconds() const", source, StringComparison.Ordinal);
        Assert.Contains("LastVuInitialWaitMilliseconds = 0.0;\n        LastVuCompletionWaitMilliseconds = 0.0;\n        LastVuSubmitOnlyMilliseconds = 0.0;\n        LastVuWaitMilliseconds = 0.0;\n        LastVuSubmitMilliseconds = 0.0;", vuPath, StringComparison.Ordinal);
        Assert.Contains("const double vuCompletionWaitMilliseconds = ResolveMillisecondsFromClockTicks(vuCompletionWaitStartTicks, vuCompletionWaitEndTicks);\n                LastVuCompletionWaitMilliseconds += vuCompletionWaitMilliseconds;\n                LastVuWaitMilliseconds += vuCompletionWaitMilliseconds;", normalSubmitBranch, StringComparison.Ordinal);
        Assert.Contains("const double vuSubmitOnlyMilliseconds = ResolveMillisecondsFromClockTicks(vuSubmitStartTicks, vuSubmitEndTicks);\n                LastVuSubmitOnlyMilliseconds += vuSubmitOnlyMilliseconds;\n                LastVuSubmitMilliseconds += vuSubmitOnlyMilliseconds;", normalSubmitBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableVuDirectGifDispatchDiagnostics", normalSubmitBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("dma_channel_wait(DMA_CHANNEL_GIF, 0);", normalSubmitBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque untextured VU packet builder does not use CPU near-plane clipping or CPU front-face rejection in the untextured VU path.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);
        string untexturedBranch = ExtractSourceRange(
            source,
            "} else if (!textured) {",
            "} else {");

        Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFrontFacingTriangle(", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque-untextured VU packet template uses an explicit VU-owned header instead of the draw_prim helper packet seam.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);

        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 0u, BuildPackedAdGiftag(1u), GIF_REG_AD);", source, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 1u, ResolveOpaqueUntexturedTestRegister(gsGlobal), GS_REG_TEST);", source, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 2u, BuildPackedAdGiftag(1u), GIF_REG_AD);", source, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 3u, BuildUntexturedTex1Register(), GS_REG_TEX1);", source, StringComparison.Ordinal);
        Assert.Contains("GS_REG_TEST", source, StringComparison.Ordinal);
        Assert.Contains("WriteGifTemplateQword(payload.GifPacketTemplate, 4u, BuildUntexturedPrimitiveGiftag(prim), UntexturedTriangleRegisterList);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("draw_prim_end(gifPacket.get()->next, 2, UntexturedTriangleRegisterList)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque-untextured VU packet header mirrors the active depth-test state instead of hardcoding all-pass depth behavior.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = ReadSourceText(builderPath);

        Assert.Contains("gsGlobal != nullptr && gsGlobal->ZBuffering == GS_SETTING_ON", source, StringComparison.Ordinal);
        Assert.Contains("rendererZTestMethod", source, StringComparison.Ordinal);
        Assert.Contains("rendererDepthTestEnabled", source, StringComparison.Ordinal);
        Assert.Contains("rendererDepthTestEnabled ? DRAW_ENABLE : DRAW_DISABLE", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 generated-core normalization rewrites ScrollComponent size initialization to the current value-type int2 API.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenScrollComponentUsesPointerSizedInt2_RewritesToValueInitialization() {
        const string source = "ScrollComponent::ScrollComponent() : SizeValue(new int2())\n{\n}\n";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("ScrollComponent.cpp", source);

        Assert.Contains("SizeValue(int2())", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeValue(new int2())", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated content-manager material registration is normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenContentManagerUsesRawMaterialContract_RewritesToCookedPlatformContract() {
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

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("RegisterProcessorIfMissing<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterProcessorIfMissing<MaterialAsset*>", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated scene material resolution is normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesCurrentRawMaterialContract_RewritesToCookedPlatformContract() {
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

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Load<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the older ownership-guarded raw resolver shape is also normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesLegacyGuardedRawMaterialContract_RewritesToCookedPlatformContract() {
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

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Load<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager headers are normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesRawMaterialContract_RewritesToCookedPlatformContract() {
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

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.hpp", source);

        Assert.Contains("class PlatformMaterialAsset;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager sources are normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source);

        Assert.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("platform-owned cooked material creation", normalizedSource, StringComparison.Ordinal);
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
    /// Verifies that the PS2 generated runtime graphics manifest stays on the simplest supported graphics path.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRuntimeGraphicsManifestEnablesHdrAndHighPostProcess_RewritesToDisabledValues() {
        const string source = """
const HERuntimeGraphicsRendererManifest RuntimeGraphicsRendererManifest =
{
    true,
    HERuntimePostProcessTier::High,
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("runtime", "runtime_graphics_renderer_manifest.cpp"), source);

        Assert.Contains("false", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("HERuntimePostProcessTier::Disabled", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated font scaling loops are normalized from managed dictionary accessors onto native std::pair field access.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFontAssetUsesManagedPairAccessors_RewritesToStdPairFields() {
        const string source = """
for (const auto& entry : *Characters)
{
::FontChar glyph = entry.get_Value();
glyph.SourceX = glyph.SourceX * widthScale;
(*scaledCharacters)[entry.get_Key()] = glyph;
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FontAsset.cpp", source);

        Assert.Contains("::FontChar glyph = entry.second;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("(*scaledCharacters)[entry.first] = glyph;", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.get_Value()", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.get_Key()", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated FPS component headers declare the private overlay helper referenced by the translated PS2 source.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFpsComponentHeaderMissesOverlayHelperDeclaration_InsertsDeclaration() {
        const string source = """
private:
    std::string FormatFpsValue(double fps);

    std::string FormatRenderFpsText(double renderFps, double drawMilliseconds);

    void RefreshOverlayActivation();
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FPSComponent.hpp", source);

        Assert.Contains("std::string FormatOverlaySecondaryLine(std::string baseRenderText);", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated FPS component sources call the overlay helper through the component instance so the PS2 native compiler can resolve the private member.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFpsComponentSourceCallsOverlayHelperUnqualified_RewritesToThisCall() {
        const string source = """
if (this->RenderTextComponent != nullptr)
{
this->RenderTextComponent->set_Text(FormatOverlaySecondaryLine(this->RenderFpsText));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FPSComponent.cpp", source);

        Assert.Contains("this->RenderTextComponent->set_Text(this->FormatOverlaySecondaryLine(this->RenderFpsText));", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Text(FormatOverlaySecondaryLine(", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated light-component includes are normalized when the current engine rewrite emits the invalid `LightType::hpp` form.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenLightComponentIncludesUseScopedHeaderToken_RewritesToPlainHeaderName() {
        const string source = """
#include "AmbientLightComponent.hpp"
#include "LightType::hpp"
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("AmbientLightComponent.cpp", source);

        Assert.Contains("#include \"LightType.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LightType::hpp", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated PS2 direct-disc file streams become readable owned memory streams without injecting constructor fields that the generated header does not declare.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenPs2FileStreamUsesDirectDiscBuffer_MarksMemoryBufferReadableOwnedAndReadOnlyWithoutInjectingUndefinedFields() {
        const string source = """
FileStream::FileStream(const uint8_t* data, size_t dataLength)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), writable(false) {
}

FileStream::FileStream(const char* path, FileMode mode)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true) {
#if HE_CPP_PLATFORM_PS2
    std::string resolvedPs2ReadPath = FileStreamSupportResolvePs2DiscReadPath(path);
    bool usesPs2DirectRead = FileStreamSupportStartsWithPs2CdromPrefix(resolvedPs2ReadPath) || resolvedPs2ReadPath.find(';') != std::string::npos;
    if (usesPs2DirectRead) {
        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);
        usesMemoryBuffer = true;
        length = memoryBuffer.size();
        return;
    }
#endif
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("system", "io", "file-stream.cpp"), source);

        Assert.DoesNotContain("usesMemoryBuffer", normalizedSource, StringComparison.Ordinal);
        Assert.Contains(": file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), writable(false)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains(": file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("ownsMemoryBuffer = true;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("writable = false;", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Invokes the private generated-core normalization entry point so tests can assert the PS2 build-specific source rewrite contract.
    /// </summary>
    /// <param name="fileName">Generated source file name passed to the normalization routine.</param>
    /// <param name="contents">Generated source contents passed to the normalization routine.</param>
    /// <returns>Normalized generated source contents.</returns>
    static string InvokeNormalizeGeneratedCoreSource(string fileName, string contents) {
        MethodInfo normalizeMethod = typeof(Ps2NativeBuildExecutor).GetMethod(
            "NormalizeGeneratedCoreSource",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeGeneratedCoreSource reflection lookup failed.");

        return (string)(normalizeMethod.Invoke(null, [fileName, contents])
            ?? throw new InvalidOperationException("NormalizeGeneratedCoreSource returned null."));
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Reads a source file using stable line endings so source-shape tests are not affected by checkout settings.
    /// </summary>
    /// <param name="path">Absolute path to the source file.</param>
    /// <returns>Source text with Windows line endings normalized to line-feed characters.</returns>
    static string ReadSourceText(string path) {
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
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
