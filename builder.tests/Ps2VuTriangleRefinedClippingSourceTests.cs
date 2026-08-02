using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Protects textured VU submission routing so only conservatively intersecting outer slices are refined to individual source triangles.
/// </summary>
public sealed class Ps2VuTriangleRefinedClippingSourceTests {
    /// <summary>
    /// Ensures a fast outer slice retains immutable REF submission while an intersecting slice is refined into complete bounded clipped fans.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenAnOuterSliceIsClipped_RefinesOnlyThatSlicePerSourceTriangle() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("const Ps2VuNearPlaneRoute outerRoute = outerRoutes[batchOffset];", source, StringComparison.Ordinal);
        Assert.Contains("batch->Model->GetTexturedSourceTriangleBounds(firstSourceTriangle + sourceTriangleOffset)", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuClippedTexturedTriangleFan clippedFan;", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan(", source, StringComparison.Ordinal);
        Assert.Contains("if (!clippedBatch.CanAppend(clippedFan.GetTriangleCount()))", source, StringComparison.Ordinal);
        Assert.Contains("clippedBatch.Append(clippedFan);", source, StringComparison.Ordinal);
        Assert.Contains("TexturedPretransformedMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TexturedClipMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("useClippingMicroProgram", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropClippedTexturedSlicesForDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseFastProgramForClippedSliceDiagnostics", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ForceAllTexturedSlicesThroughClipProgramDiagnostics", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures fully rejected outer slices consume no bounded source-reference capacity and resolve no source records before being omitted.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenAnOuterSliceIsRejected_SkipsSourceReservationAndResolution() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("if (useCachedSourceReference && outerRoutes[batchOffset] != Ps2VuNearPlaneRoute::Rejected)", source, StringComparison.Ordinal);

        int packetLoopIndex = source.IndexOf("const std::clock_t packetAssemblyStartTicks", StringComparison.Ordinal);
        int rejectedRouteIndex = source.IndexOf("if (outerRoute == Ps2VuNearPlaneRoute::Rejected)", packetLoopIndex, StringComparison.Ordinal);
        int runtimeModelIndex = source.IndexOf("const Ps2RuntimeModel* runtimeModel", packetLoopIndex, StringComparison.Ordinal);
        int referencedSourceIndex = source.IndexOf("TexturedPacketCache.AppendReferencedPackedTriangleSources(", packetLoopIndex, StringComparison.Ordinal);
        int copiedSourceIndex = source.IndexOf("TexturedPacketCache.ResolvePackedTriangleSources(", packetLoopIndex, StringComparison.Ordinal);

        Assert.True(rejectedRouteIndex >= 0, "The packet loop must handle a rejected outer route.");
        Assert.True(runtimeModelIndex >= 0, "The packet loop must retain runtime-model source resolution for consumable routes.");
        Assert.True(referencedSourceIndex >= 0, "The packet loop must retain bounded referenced-source construction for consumable routes.");
        Assert.True(copiedSourceIndex >= 0, "The packet loop must retain copied-source resolution when references are disabled.");
        Assert.True(rejectedRouteIndex < runtimeModelIndex, "Rejected outer routes must exit before runtime source acquisition.");
        Assert.True(rejectedRouteIndex < referencedSourceIndex, "Rejected outer routes must exit before referenced source construction.");
        Assert.True(rejectedRouteIndex < copiedSourceIndex, "Rejected outer routes must exit before copied source resolution.");
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
