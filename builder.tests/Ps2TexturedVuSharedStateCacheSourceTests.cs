namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that textured VU shared state is reused by consecutive slices of one drawable.
/// </summary>
public sealed class Ps2TexturedVuSharedStateCacheSourceTests {
    /// <summary>
    /// Confirms that transform, material, and GS state is built once per source batch rather than once per slice.
    /// </summary>
    [Fact]
    public void TexturedVuFastPathCachesSharedStatePerSourceBatch() {
        const string packetBuilderPath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";

        string packetBuilderSource = File.ReadAllText(packetBuilderPath);

        Assert.Contains("const Ps2VuOpaqueBatch* cachedSharedStateBatch = nullptr;", packetBuilderSource);
        Assert.Contains("if (batch != cachedSharedStateBatch)", packetBuilderSource);
        Assert.Contains("sharedState = cachedSharedState;", packetBuilderSource);
    }
}
