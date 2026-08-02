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
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
