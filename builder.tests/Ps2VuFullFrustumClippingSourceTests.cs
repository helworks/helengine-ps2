using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Protects the fixed host clipping and batched pretransformed VU1 contracts that replace the inactive per-triangle VU clipper.
/// </summary>
public sealed class Ps2VuFullFrustumClippingSourceTests {
    /// <summary>
    /// Ensures five-plane clipping owns finite interpolation and bounded nine-vertex polygon storage outside VU1 microcode.
    /// </summary>
    [Fact]
    public void Ps2VuTexturedTriangleClipper_WhenClippingTheFullFrustum_UsesFiveFixedHostPlanesAndNineVertexStorage() {
        string repositoryRootPath = GetRepositoryRootPath();
        string clipperSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedTriangleClipper.cpp"));
        string polygonSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedClipPolygon.hpp"));

        Assert.Contains("static constexpr std::size_t Capacity = 9u;", polygonSource, StringComparison.Ordinal);
        Assert.Contains("ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Near, nearPlaneDistance);", clipperSource, StringComparison.Ordinal);
        Assert.Contains("ClipAgainstPlane(secondPolygon, firstPolygon, Plane::Left, nearPlaneDistance);", clipperSource, StringComparison.Ordinal);
        Assert.Contains("ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Right, nearPlaneDistance);", clipperSource, StringComparison.Ordinal);
        Assert.Contains("ClipAgainstPlane(secondPolygon, firstPolygon, Plane::Bottom, nearPlaneDistance);", clipperSource, StringComparison.Ordinal);
        Assert.Contains("ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Top, nearPlaneDistance);", clipperSource, StringComparison.Ordinal);
        Assert.Contains("return -nearPlaneDistance - vertex.ViewZ;", clipperSource, StringComparison.Ordinal);
        Assert.Contains("return vertex.ClipX + vertex.ClipW;", clipperSource, StringComparison.Ordinal);
        Assert.Contains("return vertex.ClipW - vertex.ClipX;", clipperSource, StringComparison.Ordinal);
        Assert.Contains("return vertex.ClipY + vertex.ClipW;", clipperSource, StringComparison.Ordinal);
        Assert.Contains("return vertex.ClipW - vertex.ClipY;", clipperSource, StringComparison.Ordinal);
        Assert.Contains("const float amount = std::clamp(", clipperSource, StringComparison.Ordinal);
        Assert.Contains("previousDistance / denominator,", clipperSource, StringComparison.Ordinal);
        Assert.Contains("if (!IsFinite(vertex))", clipperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("std::vector", clipperSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new", clipperSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the generated fan and pretransformed source limits derive the bounded 33-triangle VU1 batch without full-frustum scratch constants.
    /// </summary>
    [Fact]
    public void Ps2VuClippedTexturedBatch_WhenSubmittingHostGeneratedFans_UsesTheDerivedThirtyThreeTriangleVuLimit() {
        string repositoryRootPath = GetRepositoryRootPath();
        string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
        string fanSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuClippedTexturedTriangleFan.hpp"));

        Assert.Contains("TexturedVuClippedInputTriangleCapacity", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClippedOutputTriangleCapacity", limitsSource, StringComparison.Ordinal);
        Assert.Contains("static_assert(TexturedVuClippedTriangleCapacity == 33u);", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuSharedStateQwordCount", limitsSource, StringComparison.Ordinal);
        Assert.Contains("TexturedVuOutputStartQword", limitsSource, StringComparison.Ordinal);
        Assert.Contains("static constexpr std::size_t Capacity = Ps2VuTexturedClipPolygon::Capacity - 2u;", fanSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TexturedVuClipScratch", limitsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TexturedVuMaximum", limitsSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the packet builder derives exceptional packet capacity from the fixed host fan rather than the retired VU full-frustum expansion contract.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenSizingExceptionalPackets_UsesTheFixedHostFanCapacity() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("TexturedVuSourceTriangleCapacity * Ps2VuClippedTexturedTriangleFan::Capacity", source, StringComparison.Ordinal);
        Assert.Contains("TexturedPretransformedMicroProgramAddress", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TexturedVuMaximumOutputTrianglesPerClippedSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnableVuPerTriangleTimingDiagnostics", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
