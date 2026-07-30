namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that untextured VU batch packing reuses flat-lighting results for repeated packed face normals.
/// </summary>
public sealed class Ps2UntexturedFlatLightingCacheSourceTests {
    /// <summary>
    /// Confirms that the CPU untextured path keys a per-batch cache from the packed face normal before running lighting math.
    /// </summary>
    [Fact]
    public void UntexturedVuBatchPackingCachesRepeatedFlatLightingResults() {
        const string sourcePath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("struct Ps2VuFlatLightingCacheEntry final", source);
        Assert.Contains("std::vector<Ps2VuFlatLightingCacheEntry> flatLightingCache;", source);
        Assert.Contains("flatLightingCache.push_back(Ps2VuFlatLightingCacheEntry", source);
    }
}
