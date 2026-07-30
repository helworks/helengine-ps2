namespace HelEngine.Builder.Tests;

/// <summary>
    /// Verifies that the textured VU path uses a source batch width supported by the active VU memory layout.
/// </summary>
public sealed class Ps2TexturedVuBatchWidthSourceTests {
    /// <summary>
    /// Confirms that the render manager and packet builder keep their shared textured VU source batch width aligned.
    /// </summary>
    [Fact]
    public void TexturedVuSourceBatchWidthUsesThirtyTwoTriangles() {
        const string renderManagerPath = "../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp";
        const string packetBuilderPath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";
        const string packetCachePath = "../../../../src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp";

        string renderManagerSource = File.ReadAllText(renderManagerPath);
        string packetBuilderSource = File.ReadAllText(packetBuilderPath);
        string packetCacheSource = File.ReadAllText(packetCachePath);

        Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 32u;", renderManagerSource);
        Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 32u;", packetBuilderSource);
        Assert.Contains("static constexpr std::size_t TexturedVuSourceSliceTriangleCapacity = 32u;", packetCacheSource);
    }
}
