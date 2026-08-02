using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Protects the on-demand textured source-triangle bounds API used only when an outer VU slice requires refinement.
/// </summary>
public sealed class Ps2VuPackedModelTriangleBoundsSourceTests {
    /// <summary>
    /// Ensures unaligned source triangles calculate exact three-position bounds by value while the aligned cached-slice API keeps its alignment invariant.
    /// </summary>
    [Fact]
    public void Ps2VuPackedModel_WhenReadingOneTexturedSourceTriangle_ComputesExactBoundsWithoutWeakeningSliceAlignment() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string headerSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.hpp"));
        string implementationSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.cpp"));

        Assert.Contains("Ps2VuSourceSliceBounds GetTexturedSourceTriangleBounds(std::size_t sourceTriangleIndex) const;", headerSource, StringComparison.Ordinal);
        Assert.Matches(@"Ps2VuSourceSliceBounds\s+Ps2VuPackedModel::GetTexturedSourceTriangleBounds\s*\(\s*std::size_t\s+sourceTriangleIndex\s*\)\s+const", implementationSource);
        Assert.Matches(@"if\s*\(\s*sourceTriangleIndex\s*>=\s*sourceTriangleTotal\s*\)\s*\{\s*throw\s+std::out_of_range", implementationSource);
        Assert.Contains("const std::size_t firstSourceVertex = sourceTriangleIndex * 3u;", implementationSource, StringComparison.Ordinal);
        Assert.Contains("const std::size_t finalSourceVertex = firstSourceVertex + 3u;", implementationSource, StringComparison.Ordinal);
        Assert.Contains("for (std::size_t sourceVertex = firstSourceVertex; sourceVertex < finalSourceVertex; sourceVertex++)", implementationSource, StringComparison.Ordinal);
        Assert.Contains("return Ps2VuSourceSliceBounds { center, extents };", implementationSource, StringComparison.Ordinal);
        Assert.Contains("if ((firstSourceTriangle % TexturedVuSourceTriangleCapacity) != 0u)", implementationSource, StringComparison.Ordinal);
        Assert.Contains("Packed PS2 mesh textured slice start must align to the VU1 source capacity.", implementationSource, StringComparison.Ordinal);
    }
}
