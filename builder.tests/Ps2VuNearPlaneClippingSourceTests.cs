using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Protects the PS2 textured routing contract that keeps safe triangles on the fast VU1 program and sends only exact host-clipped fans to the pretransformed program.
/// </summary>
public sealed class Ps2VuNearPlaneClippingSourceTests {
    /// <summary>
    /// Ensures packed models retain conservative source-slice bounds and the classifier routes unsafe slices to exact host clipping.
    /// </summary>
    [Fact]
    public void Ps2TexturedVuSlices_WhenPreparedForHostClipping_ExposeConservativeBoundsAndThreeRoutes() {
        string repositoryRootPath = GetRepositoryRootPath();
        string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
        string boundsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuSourceSliceBounds.hpp"));
        string classifierSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuNearPlaneSliceClassifier.cpp"));

        Assert.Contains("TexturedVuSourceTriangleCapacity = 32u", limitsSource, StringComparison.Ordinal);
        Assert.Contains("::float3 Center", boundsSource, StringComparison.Ordinal);
        Assert.Contains("::float3 Extents", boundsSource, StringComparison.Ordinal);
        Assert.Contains("minimumClipLeft", classifierSource, StringComparison.Ordinal);
        Assert.Contains("maximumClipRight", classifierSource, StringComparison.Ordinal);
        Assert.Contains("minimumClipBottom", classifierSource, StringComparison.Ordinal);
        Assert.Contains("maximumClipTop", classifierSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Fast", classifierSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Clipped", classifierSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Rejected", classifierSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures textured packet assembly preserves the immutable fast route while submitting exact clipped fans through the pretransformed program.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenRoutingIntersectingSlices_PreservesTheFastProgramAndUsesHostGeneratedFans() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("std::array<Ps2VuNearPlaneRoute, MaximumTexturedVuSourceBatchCount> outerRoutes", source, StringComparison.Ordinal);
        Assert.Contains("const Ps2VuNearPlaneRoute outerRoute = outerRoutes[batchOffset];", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuNearPlaneRoute::Rejected", source, StringComparison.Ordinal);
        Assert.Contains("TexturedPretransformedMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TexturedClipMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropClippedTexturedSlicesForDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseFastProgramForClippedSliceDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceAllTexturedSlicesThroughClipProgramDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableVuPerTriangleTimingDiagnostics", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the active pretransformed program is linked and uploaded within VU1 limits while the prior six-pass source remains unlinked.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenUploadingTexturedPrograms_UploadsOnlyThePretransformedExceptionalProgram() {
        string repositoryRootPath = GetRepositoryRootPath();
        string bootSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));
        string addressSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuMicroProgramAddresses.hpp"));

        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeStart", bootSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeEnd", bootSource, StringComparison.Ordinal);
        Assert.Contains("TexturedPretransformedMicroProgramAddress = 320u", addressSource, StringComparison.Ordinal);
        Assert.Contains("texturedProgramEndAddress > helengine::ps2::TexturedPretransformedMicroProgramAddress", bootSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedClipDraw3D_CodeStart", bootSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
