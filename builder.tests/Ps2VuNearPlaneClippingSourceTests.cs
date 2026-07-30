using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Protects the source contracts that keep the PS2 textured VU1 fast path unchanged while routing camera-plane intersections to a clipping program.
/// </summary>
public sealed class Ps2VuNearPlaneClippingSourceTests {
    /// <summary>
    /// Ensures textured VU source slices share one capacity and carry conservative local bounds for near-plane routing.
    /// </summary>
    [Fact]
    public void Ps2TexturedVuSlices_WhenPreparedForNearPlaneRouting_ExposeSharedLimitsAndBounds() {
        string repositoryRootPath = GetRepositoryRootPath();
        string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
        string boundsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuSourceSliceBounds.hpp"));
        string routeSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuNearPlaneRoute.hpp"));

        Assert.Contains("TexturedVuSourceTriangleCapacity = 32u", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClippedSourceTriangleCapacity = 8u", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuMaximumClipPolygonVertexCount = 8u", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuMaximumClippedTriangleCount = TexturedVuClippedSourceTriangleCapacity", limitsSource, StringComparison.Ordinal);
        Assert.Contains("::float3 Center", boundsSource, StringComparison.Ordinal);
        Assert.Contains("::float3 Extents", boundsSource, StringComparison.Ordinal);
        Assert.Contains("Fast", routeSource, StringComparison.Ordinal);
        Assert.Contains("Clipped", routeSource, StringComparison.Ordinal);
        Assert.Contains("Rejected", routeSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures packed PS2 models calculate textured VU slice bounds once during load and expose them by source range.
    /// </summary>
    [Fact]
    public void Ps2VuPackedModel_WhenLoaded_CachesTexturedSourceSliceBounds() {
        string repositoryRootPath = GetRepositoryRootPath();
        string headerSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.hpp"));
        string implementationSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.cpp"));

        Assert.Contains("GetTexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
        Assert.Contains("BuildTexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
        Assert.Contains("std::vector<Ps2VuSourceSliceBounds> TexturedSourceSliceBounds", headerSource, StringComparison.Ordinal);
        Assert.Contains("BuildTexturedSourceSliceBounds();", implementationSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuSourceTriangleCapacity", implementationSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures routing checks every unsafe homogeneous plane so side-plane overflow and camera-plane crossings cannot reach perspective division.
    /// </summary>
    [Fact]
    public void Ps2VuNearPlaneSliceClassifier_WhenBoundsAreClassified_UsesConservativeFrustumIntervals() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuNearPlaneSliceClassifier.cpp"));

        Assert.Contains("centerClipZ", source, StringComparison.Ordinal);
        Assert.Contains("radiusClipZ", source, StringComparison.Ordinal);
        Assert.Contains("centerClipW", source, StringComparison.Ordinal);
        Assert.Contains("radiusClipW", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipLeft", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipLeft", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipRight", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipRight", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipBottom", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipBottom", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipTop", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipTop", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipZ >= NearPlaneClassificationEpsilon", source, StringComparison.Ordinal);
        Assert.Contains("minimumClipW >= CameraPlaneClassificationEpsilon", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipZ < -NearPlaneClassificationEpsilon", source, StringComparison.Ordinal);
        Assert.Contains("maximumClipW < CameraPlaneClassificationEpsilon", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Clipped", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured packet assembly rejects hidden slices and selects a separate clipping microprogram only for intersecting slices.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenRoutingNearPlaneSlices_PreservesTheFastProgram() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("Ps2VuNearPlaneSliceClassifier::Classify", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Rejected", source, StringComparison.Ordinal);
        Assert.Contains("TexturedClipMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.Contains("route == Ps2VuNearPlaneRoute::Clipped", source, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClippedSourceTriangleCapacity", source, StringComparison.Ordinal);
        Assert.Contains("submissionSourceTriangleCapacity", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool DropClippedTexturedSlicesForDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool UseFastProgramForClippedSliceDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool ForceAllTexturedSlicesThroughClipProgramDiagnostics = true;", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_mscal(packet.get(), microProgramAddress, 0);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the clipping VU program clips against every unsafe homogeneous plane before reciprocal W and triangulates the resulting polygon.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenTriangleCrossesTheCameraFrustum_ClipsBeforePerspectiveDivision() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        int classificationIndex = source.IndexOf("texturedClipClassifyTriangle:", StringComparison.Ordinal);
        int divisionIndex = source.IndexOf("div           Q", StringComparison.Ordinal);

        Assert.True(classificationIndex >= 0);
        Assert.True(divisionIndex > classificationIndex);
        Assert.Contains("texturedClipPolygonAgainstPlane:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipDistanceLeft:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipDistanceRight:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipDistanceBottom:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipDistanceTop:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipPolygonOverflow:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipValidateTriangleA:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipValidateTriangleB:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipValidateTriangleC:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipEmitTriangleFanLoop:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipHardwareClipFlagDiagnostics:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipHardwareRejectTriangle:", source, StringComparison.Ordinal);
        Assert.Contains("clipw.xyz", source, StringComparison.Ordinal);
        Assert.Contains("fcand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedClipEmitNearInsideTriangleDiagnostics:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedClipEmitUnclippedTriangleDiagnostics:", source, StringComparison.Ordinal);
        Assert.Contains("texturedClipRestoreGifTagEndFlag:", source, StringComparison.Ordinal);
        Assert.Contains("isw.x VI07, 7(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("xgkick VI04", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the clipping VU program is linked, uploaded at its shared address, and bounded by both VU1 memory contracts.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenUploadingTexturedPrograms_UploadsTheNearClipProgramWithinVuLimits() {
        string repositoryRootPath = GetRepositoryRootPath();
        string bootSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));
        string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
        string addressSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuMicroProgramAddresses.hpp"));

        Assert.Contains("Ps2OpaqueTexturedClipDraw3D_CodeStart", bootSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedClipDraw3D_CodeEnd", bootSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuMicroProgramAddresses.hpp", bootSource, StringComparison.Ordinal);
        Assert.Contains("TexturedClipMicroProgramAddress = 320u", addressSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount", limitsSource, StringComparison.Ordinal);
        Assert.Contains("texturedProgramEndAddress > helengine::ps2::TexturedClipMicroProgramAddress", bootSource, StringComparison.Ordinal);
        Assert.Contains("texturedClipProgramEndAddress > Vu1MicroMemoryInstructionCount", bootSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures near-plane diagnostics report source-slice routes without enabling per-triangle timing calls.
    /// </summary>
    [Fact]
    public void Ps2NearPlaneDiagnostics_WhenDisplayed_ReportSliceRoutesWithoutPerTriangleTimers() {
        string repositoryRootPath = GetRepositoryRootPath();
        string rendererSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));
        string packetBuilderSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string bootSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));

        Assert.Contains("LastFastTexturedSliceCount", rendererSource, StringComparison.Ordinal);
        Assert.Contains("LastClippedTexturedSliceCount", rendererSource, StringComparison.Ordinal);
        Assert.Contains("LastRejectedTexturedSliceCount", rendererSource, StringComparison.Ordinal);
        Assert.Contains("Fast ", bootSource, StringComparison.Ordinal);
        Assert.Contains("Clip ", bootSource, StringComparison.Ordinal);
        Assert.Contains("Rej ", bootSource, StringComparison.Ordinal);
        Assert.Contains("FrameTimingOverlayBuildNumber = \"B297\"", bootSource, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableVuPerTriangleTimingDiagnostics = false;", packetBuilderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
