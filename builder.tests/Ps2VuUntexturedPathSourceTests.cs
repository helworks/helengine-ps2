using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in untextured PS2 VU path stays on the compact per-dispatch payload contract.
/// </summary>
public sealed class Ps2VuUntexturedPathSourceTests {
    /// <summary>
    /// Ensures the untextured VU packet path uses a compact per-dispatch payload with shared transform state embedded after the GIF template.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_UsesCompactPerDispatchPayloadContract() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.cpp"));
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string microProgramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueDraw3D.vsm"));

        Assert.DoesNotContain("Ps2VuOpaqueUntexturedSetupBuilder setupBuilder;", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("setupBuilder.Build(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateTrianglePayloadFromSetup(", vifSource, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuUntexturedSharedState final", vifSource, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuUntexturedTriangleRecord final", vifSource, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuUntexturedTrianglePayload final", vifSource, StringComparison.Ordinal);
        Assert.Contains("static_assert((offsetof(Ps2VuUntexturedTrianglePayload, SharedState) / 16u) == UntexturedTriangleSharedStateQwordOffset);", vifSource, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1)", vifSource, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuUntexturedTrianglePayload));", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_utils_vu_open_unpack(packet.get(), UntexturedSharedStateAddress, 1)", vifSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        xtop VI02", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI03, VI02, 0x00000003", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF01, 0(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF02, 1(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF03, 2(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF04, 14(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF05, 15(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF06, 16(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF07, 17(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF08, 18(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF09, 19(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuLitTrianglePayload", vifSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF01, 9(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF03, 11(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF02, 13(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("xgkick VI03", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);", setupSource, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);", setupSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in VU builder no longer keeps the retired helper code paths that only survive as native-build warnings.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_DoesNotKeepRetiredWarningOnlyHelpers() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.DoesNotContain("std::memset(&payload, 0, sizeof(Ps2VuLitTrianglePayload));", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildVifCode(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUntexturedTriangleGifPacketBytes(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFixedTrianglePositionRegister(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateDiagnosticOpaqueTrianglePayload(", vifSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the untextured setup builder leaves expensive projected submitted-bounds diagnostics disabled by default so normal runtime frames do not pay for debug-only screen-space work.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_DisablesProjectedSubmittedBoundsDiagnosticsByDefault() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.cpp"));

        Assert.Contains("constexpr bool EnableVuSubmittedBoundsDiagnostics = false;", setupSource, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuSubmittedBoundsDiagnostics) {", setupSource, StringComparison.Ordinal);
        Assert.Contains("const ::float3 worldPositionA = TransformPosition(positionA, world);", setupSource, StringComparison.Ordinal);
        Assert.Contains("ProjectWorldPosition(worldPositionA4, worldViewProjectionMatrix, viewport, screenAX, screenAY, screenAZ)", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedScreenBounds = ::float4(minX, minY, maxX, maxY);", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleVertexB0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);", setupSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures per-triangle timing probes stay disabled by default so the shared VU hot path does not pay `std::clock()` overhead for overlay-only diagnostics.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_DisablesPerTriangleTimingDiagnosticsByDefault() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.cpp"));
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("constexpr bool EnableVuPerTriangleTimingDiagnostics = false;", setupSource, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPerTriangleTimingDiagnostics) {", setupSource, StringComparison.Ordinal);
        Assert.Contains("trianglePrepStartTicks = std::clock();", setupSource, StringComparison.Ordinal);
        Assert.Contains("triangleEmitStartTicks = std::clock();", setupSource, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableVuPerTriangleTimingDiagnostics = false;", vifSource, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPerTriangleTimingDiagnostics) {", vifSource, StringComparison.Ordinal);
        Assert.Contains("trianglePrepStartTicks = std::clock();", vifSource, StringComparison.Ordinal);
        Assert.Contains("triangleEmitStartTicks = std::clock();", vifSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the stable untextured VU path always accumulates prep and emit timing so the FPS overlay can split the hot-path cost even while the heavier per-triangle diagnostics stay disabled.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_AlwaysAccumulatesPrepAndEmitTiming() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string untexturedBranch = ExtractSourceRange(
            vifSource,
            "} else if (!textured) {",
            "} else {");

        Assert.Contains("const std::clock_t trianglePrepStartTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t trianglePrepEndTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t triangleEmitStartTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t triangleEmitEndTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("std::clock_t trianglePrepStartTicks = 0;", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("std::clock_t triangleEmitStartTicks = 0;", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the stable untextured VU path hoists material lighting constants once per batch and resolves flat colors from normalized inputs without re-reading material state for every triangle.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_HoistsLightingConstantsOutsideTriangleLoop() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string untexturedBranch = ExtractSourceRange(
            vifSource,
            "} else if (!textured) {",
            "} else {");

        Assert.Contains("struct Ps2VuLightingConstants final", vifSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuLightingConstants lightingConstants {};", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("PopulateLightingConstants(*batch.Material, lightingConstants);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t triangleColor = ResolveTexturedVertexColor(lightingConstants, worldFaceNormal, normalizedLightDirection);", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveTexturedVertexColor(*batch.Material, worldFaceNormal, normalizedLightDirection);", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the stable untextured VU path splits emit timing into lighting work and payload-fill work using aggregated clock ticks so the coarse host-side profiling does not quantize away the hot substage.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_SplitsEmitTimingIntoLightingAndPayloadFillStages() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string untexturedBranch = ExtractSourceRange(
            vifSource,
            "} else if (!textured) {",
            "} else {");

        Assert.Contains("double GetLastTriangleLightingMilliseconds() const;", vifHeader, StringComparison.Ordinal);
        Assert.Contains("double GetLastTrianglePayloadFillMilliseconds() const;", vifHeader, StringComparison.Ordinal);
        Assert.Contains("double LastTriangleLightingMilliseconds = 0.0;", vifHeader, StringComparison.Ordinal);
        Assert.Contains("double LastTrianglePayloadFillMilliseconds = 0.0;", vifHeader, StringComparison.Ordinal);
        Assert.Contains("std::clock_t accumulatedTriangleLightingTicks = 0;", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("std::clock_t accumulatedTrianglePayloadFillTicks = 0;", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t triangleLightingStartTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t triangleLightingEndTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t trianglePayloadFillStartTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t trianglePayloadFillEndTicks = std::clock();", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the stable compact untextured payload contract still hoists batch-invariant transform state and reuses identical flat-color GIF templates instead of rebuilding both for every triangle.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_HoistsSharedStateAndCachesGifTemplates() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string untexturedBranch = ExtractSourceRange(
            vifSource,
            "} else if (!textured) {",
            "} else {");

        Assert.Contains("struct Ps2VuGifTemplateCacheEntry final", vifSource, StringComparison.Ordinal);
        Assert.Contains("std::vector<Ps2VuGifTemplateCacheEntry> gifTemplateCache;", vifSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuUntexturedSharedState sharedStateTemplate {};", vifSource, StringComparison.Ordinal);
        Assert.Contains("PopulateUntexturedSharedState(world, view, projection, viewport, sharedStateTemplate);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("CopyCachedTriangleGifPacketTemplate(batch, flatColor, gsGlobal, gifTemplateCache, payload.TriangleRecord);", untexturedBranch, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(&payload.SharedState, &sharedStateTemplate, sizeof(sharedStateTemplate));", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateUntexturedSharedState(world, view, projection, viewport, payload.SharedState);", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateTriangleGifPacketTemplate(batch, flatColor, gsGlobal, payload.TriangleRecord);", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the stable compact untextured payload path splits packet assembly into unpack-copy work and VIF dispatch work so the remaining `Enc` wall can be profiled without guessing.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_DoesNotOverwriteEmitSubstageMetricsWithPacketAssemblyTiming() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string packetAssemblyBlock = ExtractSourceRange(
            vifSource,
            "const std::clock_t packetAssemblyStartTicks = std::clock();",
            "LastCompletedPhase = 6;");

        Assert.DoesNotContain("accumulatedPacketUnpackTicks", packetAssemblyBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("accumulatedPacketDispatchTicks", packetAssemblyBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("packetUnpackStartTicks", packetAssemblyBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("packetDispatchStartTicks", packetAssemblyBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedPacketUnpackTicks);", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedPacketDispatchTicks);", vifSource, StringComparison.Ordinal);
        Assert.Contains("LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);", vifSource, StringComparison.Ordinal);
        Assert.Contains("LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);", vifSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Extracts one source range delimited by stable sentinel text.
    /// </summary>
    /// <param name="source">Full source text.</param>
    /// <param name="startSentinel">Inclusive start sentinel.</param>
    /// <param name="endSentinel">Exclusive end sentinel.</param>
    /// <returns>Substring bounded by the sentinel pair.</returns>
    static string ExtractSourceRange(string source, string startSentinel, string endSentinel) {
        int startIndex = source.IndexOf(startSentinel, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find start sentinel: {startSentinel}");
        int endIndex = source.IndexOf(endSentinel, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Could not find end sentinel: {endSentinel}");
        return source.Substring(startIndex, endIndex - startIndex);
    }
}
