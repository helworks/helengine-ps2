namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that untextured VU batch packing reserves its complete payload capacity before emitting triangles.
/// </summary>
public sealed class Ps2UntexturedPayloadCapacitySourceTests {
    /// <summary>
    /// Confirms that accepted batches accumulate a maximum emitted-triangle count and reserve the payload vector once per packet.
    /// </summary>
    [Fact]
    public void UntexturedVuBatchPackingReservesMaximumTrianglePayloadCapacity() {
        const string sourcePath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("std::size_t maximumUntexturedTrianglePayloadCount = 0u;", source);
        Assert.Contains("maximumUntexturedTrianglePayloadCount += maximumEmittedTriangleCount;", source);
        Assert.Contains("untexturedTrianglePayloads.reserve(maximumUntexturedTrianglePayloadCount);", source);
    }
}
