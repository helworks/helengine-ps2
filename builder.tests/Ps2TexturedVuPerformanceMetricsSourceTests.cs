namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that the aggregated textured VU path reports every builder timing exposed by the performance overlay.
/// </summary>
public sealed class Ps2TexturedVuPerformanceMetricsSourceTests {
    /// <summary>
    /// Confirms that the fast textured VU submission copies setup, preparation, emission, lighting, payload, and assembly timings.
    /// </summary>
    [Fact]
    public void AggregatedTexturedVuPathReportsAllBuilderTimings() {
        const string sourcePath = "../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp";

        string source = File.ReadAllText(sourcePath);

        int aggregatePathStart = source.IndexOf("packet2_t* texturedVuPacket = VuVifPacketBuilder.GetPacket();", StringComparison.Ordinal);
        string aggregatePathSource = source.Substring(aggregatePathStart, 1200);

        Assert.Contains("LastVuTriangleSetupMilliseconds += VuVifPacketBuilder.GetLastTriangleSetupMilliseconds();", aggregatePathSource);
        Assert.Contains("LastVuTrianglePrepMilliseconds += VuVifPacketBuilder.GetLastTrianglePrepMilliseconds();", aggregatePathSource);
        Assert.Contains("LastVuTriangleEmitMilliseconds += VuVifPacketBuilder.GetLastTriangleEmitMilliseconds();", aggregatePathSource);
        Assert.Contains("LastVuTriangleLightingMilliseconds += VuVifPacketBuilder.GetLastTriangleLightingMilliseconds();", aggregatePathSource);
        Assert.Contains("LastVuTrianglePayloadFillMilliseconds += VuVifPacketBuilder.GetLastTrianglePayloadFillMilliseconds();", aggregatePathSource);
    }
}
