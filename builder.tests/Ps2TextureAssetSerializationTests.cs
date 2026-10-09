using helengine;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 texture payloads round-trip native texture metadata through the managed PS2 asset serializer.
/// </summary>
public sealed class Ps2TextureAssetSerializationTests {
    /// <summary>Enumerates all native selections in both metadata byte orders while pixel streams remain little-endian.</summary>
    public static TheoryData<string, EngineBinaryEndianness> NativeFormats {
        get {
            TheoryData<string, EngineBinaryEndianness> result = new();
            foreach (Ps2GsTextureFormatDefinition format in Ps2GsTextureFormatCatalog.Formats) {
                result.Add(format.Id, EngineBinaryEndianness.LittleEndian); result.Add(format.Id, EngineBinaryEndianness.BigEndian);
            }
            return result;
        }
    }
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

    /// <summary>Preserves version-two texture schema, storage modes, native bytes and preview semantics for every GS combination.</summary>
    [Theory]
    [MemberData(nameof(NativeFormats))]
    public void SerializeAndDeserialize_AllNativeCombinationsPreserveVersionTwoPayload(string id, EngineBinaryEndianness endianness) {
        Assert.True(Ps2GsTextureFormatCatalog.TryGetFormat(id, out Ps2GsTextureFormatDefinition format));
        using TextureAsset source = CreateNativeSource(format.IsIndexed);
        using Ps2TextureAsset native = Ps2NativeTextureCodec.Encode(source, id, format.DefaultAlphaPrecision);
        byte[] expected = Ps2NativeTextureCodec.Decode(native);
        Assert.Equal(source.Id, native.Id); Assert.Equal(source.RuntimeAssetId, native.RuntimeAssetId);
        Assert.Equal(source.FormerAuthoringAssetIds, native.FormerAuthoringAssetIds); Assert.NotSame(source.FormerAuthoringAssetIds, native.FormerAuthoringAssetIds);
        using MemoryStream stream = endianness == EngineBinaryEndianness.LittleEndian ? new(Ps2AssetSerializer.SerializeToBytes(native)) : CreateTextureRecord(native, endianness, 2);
        EngineBinaryHeader header = EngineBinaryHeaderSerializer.Read(stream); Assert.Equal(2, header.Version); Assert.Equal(endianness, header.Endianness);
        stream.Position = 0;
        using Ps2TextureAsset decoded = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.Deserialize(stream));
        Assert.Equal(native.Id, decoded.Id); Assert.Equal(native.RuntimeAssetId, decoded.RuntimeAssetId);
        Assert.Equal(native.Format, decoded.Format); Assert.Equal(native.PixelStorageMode, decoded.PixelStorageMode); Assert.Equal(native.ClutPixelStorageMode, decoded.ClutPixelStorageMode);
        Assert.Equal(native.AlphaMode, decoded.AlphaMode); Assert.Equal(native.PixelData, decoded.PixelData); Assert.Equal(native.PaletteData, decoded.PaletteData);
        Assert.Equal(expected, Ps2NativeTextureCodec.Decode(decoded));
        Assert.Equal(format.GetPixelByteLength(3, 2), decoded.PixelData.Length); Assert.Equal(format.PaletteByteLength, decoded.PaletteData.Length);
    }
    /// <summary>Retains legacy version-one implicit storage mappings and existing enum zero-through-two values.</summary>
    [Theory]
    [InlineData(Ps2TextureFormat.Rgba32, Ps2TexturePixelStorageMode.PsmCt32)]
    [InlineData(Ps2TextureFormat.Indexed8, Ps2TexturePixelStorageMode.PsmT8)]
    [InlineData(Ps2TextureFormat.Indexed4, Ps2TexturePixelStorageMode.PsmT4)]
    public void Deserialize_VersionOneKeepsImplicitLegacyModes(Ps2TextureFormat format, Ps2TexturePixelStorageMode storage) {
        using Ps2TextureAsset asset = new() { Id = "legacy", RuntimeAssetId = 43, Width = 1, Height = 1, Format = format, AlphaMode = Ps2TextureAlphaMode.Full, PixelData = new byte[] { 1, 2, 3, 4 }, PaletteData = new byte[] { 5, 6, 7, 8 } };
        using MemoryStream stream = CreateTextureRecord(asset, EngineBinaryEndianness.LittleEndian, 1);
        using Ps2TextureAsset decoded = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.Deserialize(stream));
        Assert.Equal(format, decoded.Format); Assert.Equal(storage, decoded.PixelStorageMode); Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, decoded.ClutPixelStorageMode);
        Assert.Equal(asset.PixelData, decoded.PixelData); Assert.Equal(asset.PaletteData, decoded.PaletteData); Assert.Equal(asset.RuntimeAssetId, decoded.RuntimeAssetId);
    }
    /// <summary>Rejects unknown format, texel storage, CLUT storage and alpha-mode bytes through the actual PS2 reader.</summary>
    [Theory]
    [InlineData(255, 0, 0, 0)] [InlineData(0, 255, 0, 0)] [InlineData(1, 1, 1, 1)] [InlineData(0, 0, 0, 255)]
    public void Deserialize_UnknownTextureMetadataRejectsBytes(byte format, byte storage, byte clut, byte alpha) {
        using Ps2TextureAsset asset = new() { Width = 1, Height = 1, Format = (Ps2TextureFormat)format, PixelStorageMode = (Ps2TexturePixelStorageMode)storage, ClutPixelStorageMode = (Ps2TexturePixelStorageMode)clut, AlphaMode = (Ps2TextureAlphaMode)alpha, PixelData = new byte[4], PaletteData = new byte[0] };
        using MemoryStream stream = CreateTextureRecord(asset, EngineBinaryEndianness.LittleEndian, 2);
        Assert.Throws<InvalidOperationException>(() => Ps2AssetSerializer.Deserialize(stream));
    }
    /// <summary>Releases owned native texture buffers exactly once while preserving catalog lifetime.</summary>
    [Fact]
    public void Dispose_NativeAssetReleasesBothOwnedArrays() {
        using TextureAsset source = CreateNativeSource(true);
        using Ps2TextureAsset native = Ps2NativeTextureCodec.Encode(source, "PS2.PSMT8", TextureAssetAlphaPrecision.A8);
        native.Dispose(); native.Dispose(); Assert.Null(native.PixelData); Assert.Null(native.PaletteData);
        Assert.True(Ps2GsTextureFormatCatalog.TryGetFormat("PS2.PSMT8", out Ps2GsTextureFormatDefinition format)); Assert.True(format.SupportsAlpha(TextureAssetAlphaPrecision.A8));
    }
    /// <summary>Builds an independent PS2 record to exercise foreign-endian metadata and legacy schema without altering native bytes.</summary>
    static MemoryStream CreateTextureRecord(Ps2TextureAsset asset, EngineBinaryEndianness endianness, byte version) {
        MemoryStream stream = new();
        EngineBinaryHeaderSerializer.Write(stream, new EngineBinaryHeader(endianness, version, Ps2AssetBinarySerializer.FormatId, (ushort)Ps2AssetBinarySerializer.RecordKind, (ushort)Ps2AssetBinaryValueKind.Ps2TextureAsset));
        using (Ps2EngineBinaryWriter writer = Ps2EngineBinaryWriter.Create(stream, endianness)) {
            writer.WriteString(asset.Id); writer.WriteUInt32((uint)asset.RuntimeAssetId); writer.WriteUInt32((uint)(asset.RuntimeAssetId >> 32));
            writer.WriteUInt16(asset.Width); writer.WriteUInt16(asset.Height); writer.WriteByte((byte)asset.Format);
            if (version >= 2) { writer.WriteByte((byte)asset.PixelStorageMode); writer.WriteByte((byte)asset.ClutPixelStorageMode); }
            writer.WriteByte((byte)asset.AlphaMode); writer.WriteByteArray(asset.PixelData); writer.WriteByteArray(asset.PaletteData);
        }
        stream.Position = 0; return stream;
    }
    /// <summary>Constructs authored direct or indexed texels with a stable identity and independently owned buffers.</summary>
    static TextureAsset CreateNativeSource(bool indexed) {
        byte[] rgba = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 255, 255, 255, 255, 0, 0, 0, 0, 128, 128, 128, 128 };
        return new TextureAsset { Id = "native-gs", RuntimeAssetId = 42, AuthoringAssetId = "authored", FormerAuthoringAssetIds = new[] { "former" }, Width = 3, Height = 2,
            ColorFormat = indexed ? TextureAssetColorFormat.Indexed4 : TextureAssetColorFormat.Rgba32, AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = indexed ? new byte[] { 0x10, 0x32, 0x54 } : rgba, PaletteColors = indexed ? rgba : new byte[0] };
    }
}
