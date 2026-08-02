namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies that the clipped textured batch builder preserves the required allocation-free matrix-to-fan contract.
/// </summary>
public sealed class Ps2VuHybridClippedBatchSourceTests {
    /// <summary>
    /// Requires the builder to transform packed local vertices through view and homogeneous clip space before clipping and fan generation.
    /// </summary>
    [Fact]
    public void Ps2VuClippedTexturedBatchBuilder_TransformsClipsAndBuildsBoundedFansWithoutAllocation() {
        string repositoryRootPath = GetRepositoryRootPath();
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuClippedTexturedBatchBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("TransformPosition(sourceTriangle.PositionA, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("TransformPosition(sourceTriangle.PositionB, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("TransformPosition(sourceTriangle.PositionC, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionA, projection)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionB, projection)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionC, projection)", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuTexturedTriangleClipper::ClipTriangle", source, StringComparison.Ordinal);
        Assert.Contains("clippedPolygon.GetVertexCount() < 3u", source, StringComparison.Ordinal);
        Assert.Contains("!std::isfinite(clippedVertex.ClipW) || clippedVertex.ClipW <= 0.0001f", source, StringComparison.Ordinal);
        Assert.Contains("outputFan.BuildFromClippedPolygon(clippedPolygon, sourceTriangle.FaceNormal);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::vector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new", source, StringComparison.Ordinal);
        Assert.DoesNotContain("malloc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("realloc", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>The absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
