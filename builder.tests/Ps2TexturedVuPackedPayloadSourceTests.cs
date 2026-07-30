namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that immutable textured VU payload data is cached in its packet-ready layout.
/// </summary>
public sealed class Ps2TexturedVuPackedPayloadSourceTests {
    /// <summary>
    /// Confirms that the fast path copies cached packet-ready triangle payloads instead of rebuilding every field each frame.
    /// </summary>
    [Fact]
    public void TexturedVuFastPathCopiesCachedPacketReadyTrianglePayloads() {
        const string cacheHeaderPath = "../../../../src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp";
        const string packetBuilderPath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";

        string cacheHeaderSource = File.ReadAllText(cacheHeaderPath);
        string packetBuilderSource = File.ReadAllText(packetBuilderPath);

        Assert.Contains("struct alignas(16) Ps2VuTexturedPackedTriangleSource final", cacheHeaderSource);
        Assert.Contains("ResolvePackedTriangleSources", cacheHeaderSource);
        Assert.Contains("ResolvePackedTriangleSources(*batch->Model, runtimeModel)", packetBuilderSource);
        Assert.Contains("packedTriangleSources.data() + firstSourceTriangle", packetBuilderSource);
    }
}
