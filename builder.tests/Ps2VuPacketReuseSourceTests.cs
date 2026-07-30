namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that the textured VU renderer retains DMA packet allocations across frames.
/// </summary>
public sealed class Ps2VuPacketReuseSourceTests {
    /// <summary>
    /// Confirms that completed VIF packets are reset and reused after their DMA slot becomes safe.
    /// </summary>
    [Fact]
    public void TexturedVuPathReusesCompletedPacketSlots() {
        string managerSource = File.ReadAllText("../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp");
        string builderSource = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");

        Assert.Contains("VuVifPacketBuilder.ReusePacket(VuPacketSlots[ActiveVuPacketSlotIndex]);", managerSource);
        Assert.Contains("packet2_reset(Packet, 0);", builderSource);
    }
}
