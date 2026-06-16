using helengine;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 texture payloads round-trip native texture metadata through the managed PS2 asset serializer.
/// </summary>
public sealed class Ps2TextureAssetSerializationTests {
    /// <summary>
    /// Ensures indexed PS2 texture payload metadata survives one serializer round trip.
    /// </summary>
    [Fact]
    public void SerializeAndDeserialize_WhenTextureUsesIndexed8_RoundTripsPixelStorageAndClutMetadata() {
        Ps2TextureAsset asset = new() {
            Id = "runtime-texture:test",
            Width = 16,
            Height = 16,
            Format = Ps2TextureFormat.Indexed8,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmT8,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = new byte[256],
            PaletteData = new byte[16 * 4]
        };

        byte[] bytes = Ps2AssetSerializer.SerializeToBytes(asset);
        Ps2TextureAsset roundTripped = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.Deserialize(new MemoryStream(bytes)));

        Assert.Equal(Ps2TextureFormat.Indexed8, roundTripped.Format);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmT8, roundTripped.PixelStorageMode);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, roundTripped.ClutPixelStorageMode);
        Assert.Equal(256, roundTripped.PixelData.Length);
        Assert.Equal(16 * 4, roundTripped.PaletteData.Length);
    }
}
