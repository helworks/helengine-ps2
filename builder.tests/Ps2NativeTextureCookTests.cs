using helengine;
using helengine.editor;
using helengine.ps2.builder;

namespace helengine.ps2.builder.tests;

/// <summary>Exercises all advertised GS formats, strict settings and the retained authored generic/default contracts.</summary>
public sealed class Ps2NativeTextureCookTests {
    /// <summary>Supplies every direct, depth, high-index and CLUT variant exposed by the GS catalog.</summary>
    public static IEnumerable<object[]> NativeFormats => Ps2GsTextureFormatCatalog.Formats.Select(format => new object[] { format.Id });

    /// <summary>Checks texture and font capabilities retain approved defaults while adding all native alpha combinations.</summary>
    [Fact]
    public void CapabilitiesPreserveDefaultsAndAdvertiseAllTwentyThreeNativeFormats() {
        var definition = Ps2PlatformDefinitionFactory.Create();
        Assert.Equal(23, Ps2GsTextureFormatCatalog.Formats.Length);
        Assert.Equal(13, Ps2GsTextureFormatCatalog.Formats.Select(format => format.FormatCode).Distinct().Count());
        foreach (var capability in definition.AssetCookCapabilities) {
            var settings = Ps2TextureCookSettingsSerializer.Deserialize(capability.DefaultSerializedPlatformSettings);
            Assert.Equal(512, settings.MaxResolution);
            Assert.Equal(TextureAssetAlphaPrecision.A8, settings.AlphaPrecision);
            Assert.Equal(capability.SourceAssetKind == "texture" ? "Rgba32" : "Indexed8", settings.ColorFormatId);
            Assert.Equal(capability.SourceAssetKind == "texture" ? "" : "QuantizedIndexed", settings.IndexingMethodId);
            Assert.Equal(26, capability.TextureFormatCapabilities.SupportedColorFormatIds.Length);
            foreach (var format in Ps2GsTextureFormatCatalog.Formats) {
                Assert.Contains(format.Id, capability.TextureFormatCapabilities.SupportedColorFormatIds);
                Assert.Equal(format.SupportedAlphaPrecisions,
                    capability.TextureFormatCapabilities.SupportedCombinations.Where(item => item.ColorFormatId == format.Id).Select(item => item.AlphaPrecision));
            }
        }
    }

    /// <summary>Runs the native cooker, platform serializer and preview adapter with independent identity and source storage.</summary>
    [Theory]
    [MemberData(nameof(NativeFormats))]
    public void EveryNativeFormatCooksAndRoundTripsWithoutSourceMutation(string formatId) {
        using TextureAsset source = Source(5, 3);
        byte[] original = (byte[])source.Colors.Clone();
        Ps2GsTextureFormatCatalog.TryGetFormat(formatId, out var format);
        var settings = Settings(formatId, format.DefaultAlphaPrecision);
        settings = Ps2TextureCookSettingsSerializer.Deserialize(Ps2TextureCookSettingsSerializer.Serialize(settings));
        using Ps2TextureAsset cooked = new Ps2RuntimeTextureCooker().Cook(source, settings);
        Assert.Equal(format.PlatformFormatCode, (int)cooked.Format);
        Assert.Equal(format.PlatformClutFormatCode, (int)cooked.ClutPixelStorageMode);
        Assert.Equal(format.ComputeCpuPixelByteLength(5, 3), cooked.PixelData.Length);
        Assert.Equal(format.IsIndexed ? format.PaletteByteLength : 0, cooked.PaletteData.Length);
        Assert.Equal(source.Id, cooked.Id);
        Assert.Equal(source.RuntimeAssetId, cooked.RuntimeAssetId);
        Assert.Equal(source.AuthoringAssetId, cooked.AuthoringAssetId);
        Assert.Equal(source.FormerAuthoringAssetIds, cooked.FormerAuthoringAssetIds);
        Assert.NotSame(source.FormerAuthoringAssetIds, cooked.FormerAuthoringAssetIds);
        Assert.Equal(original, source.Colors);
        using Ps2TextureAsset read = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.DeserializeFromBytes(Ps2AssetSerializer.SerializeToBytes(cooked)));
        Assert.Equal(cooked.PixelData, read.PixelData);
        Assert.Equal(cooked.PaletteData, read.PaletteData);
        byte[] preview = Ps2NativeTextureCodec.Decode(read);
        Assert.Equal(5 * 3 * 4, preview.Length);
        Assert.Equal((byte)(format.DefaultAlphaPrecision == TextureAssetAlphaPrecision.Opaque ? 255 : 0), preview[3]);
        Assert.Equal((byte)255, preview[7]);
    }

    /// <summary>Ensures old persisted generic settings continue producing their original GS layouts and full alpha range.</summary>
    [Theory]
    [InlineData("Rgba32", TextureAssetAlphaPrecision.A8, 0)]
    [InlineData("Rgba32", TextureAssetAlphaPrecision.Opaque, 0)]
    [InlineData("Indexed8", TextureAssetAlphaPrecision.A8, 1)]
    [InlineData("Indexed4", TextureAssetAlphaPrecision.A8, 2)]
    public void LegacySettingsStillCook(string id, TextureAssetAlphaPrecision alpha, int logicalFormat) {
        using TextureAsset source = Source(4, 2);
        var settings = Settings(id, alpha);
        using Ps2TextureAsset cooked = new Ps2RuntimeTextureCooker().Cook(source, settings);
        Assert.Equal(logicalFormat, (int)cooked.Format);
        Assert.Equal(4 * 2 * 4, Ps2NativeTextureCodec.Decode(cooked).Length);
    }

    /// <summary>Checks resizing is applied once in RGBA space before compact native storage, preserving source dimensions.</summary>
    [Fact]
    public void NativeResizePreservesAspectAndSource() {
        using TextureAsset source = Source(8, 4);
        var settings = Settings("PS2.PSMCT24", TextureAssetAlphaPrecision.Opaque);
        settings.MaxResolution = 4;
        using Ps2TextureAsset cooked = new Ps2RuntimeTextureCooker().Cook(source, settings);
        Assert.Equal((ushort)4, cooked.Width);
        Assert.Equal((ushort)2, cooked.Height);
        Assert.Equal(4 * 2 * 3, cooked.PixelData.Length);
        Assert.Equal((ushort)8, source.Width);
        Assert.Equal((ushort)4, source.Height);
        Assert.Equal((byte)0, source.Colors[3]);
        Assert.Equal(TextureAssetAlphaPrecision.A8, source.AlphaPrecision);
    }

    /// <summary>Rejects malformed, missing, unknown or numeric enum settings without silently selecting defaults.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"maxResolution\":0,\"colorFormat\":\"PS2.PSMCT32\",\"alphaPrecision\":\"3\"}")]
    [InlineData("{\"maxResolution\":-1,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}")]
    [InlineData("{\"maxResolution\":0,\"colorFormat\":\"PS2.UNKNOWN\",\"alphaPrecision\":\"A8\"}")]
    [InlineData("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\",\"unknown\":0}")]
    [InlineData("{\"maxResolution\":0,\"maxResolution\":1,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}")]
    public void InvalidSettingsAreRejected(string json) {
        Assert.ThrowsAny<Exception>(() => Ps2TextureCookSettingsSerializer.Deserialize(json));
    }

    /// <summary>Rejects Xbox-only two-bit alpha for every PS2 native and legacy format.</summary>
    [Fact]
    public void TwoBitAlphaIsRejectedEverywhere() {
        foreach (string id in Ps2GsTextureFormatCatalog.Formats.Select(format => format.Id).Concat(["Rgba32", "Indexed4", "Indexed8"])) {
            Assert.Throws<InvalidOperationException>(() => Ps2TextureCookSettingsSerializer.Serialize(Settings(id, TextureAssetAlphaPrecision.A2)));
        }
    }

    /// <summary>Requires a supported quantizer only on indexed formats and honors each native format's alpha policy.</summary>
    [Theory]
    [InlineData("PS2.PSMCT24", TextureAssetAlphaPrecision.A8, "")]
    [InlineData("PS2.PSMCT16", TextureAssetAlphaPrecision.A8, "")]
    [InlineData("PS2.PSMCT32", TextureAssetAlphaPrecision.A8, "QuantizedIndexed")]
    [InlineData("PS2.PSMT8", TextureAssetAlphaPrecision.A8, "UnknownQuantizer")]
    public void IncompatibleAlphaOrIndexingIsRejected(string id, TextureAssetAlphaPrecision alpha, string indexing) {
        var settings = Settings(id, alpha);
        settings.IndexingMethodId = indexing;
        Assert.Throws<InvalidOperationException>(() => Ps2TextureCookSettingsSerializer.Serialize(settings));
    }

    /// <summary>Builds exact settings without altering the default authored font or texture preferences.</summary>
    public static TextureAssetProcessorSettings Settings(string id, TextureAssetAlphaPrecision alpha) {
        Ps2GsTextureFormatCatalog.TryGetFormat(Ps2TextureCookSettingsSerializer.ResolveNativeFormatId(id), out var format);
        return new TextureAssetProcessorSettings {
            MaxResolution = 0, ColorFormatId = id, AlphaPrecision = alpha,
            IndexingMethodId = format != null && format.IsIndexed ? "QuantizedIndexed" : ""
        };
    }

    /// <summary>Creates a two-color RGBA source with transparent/opaque texels and authored identity aliases.</summary>
    public static TextureAsset Source(ushort width, ushort height) {
        byte[] colors = new byte[width * height * 4];
        for (int index = 0; index < width * height; index++) {
            colors[index * 4] = index % 2 == 0 ? (byte)0 : (byte)255;
            colors[index * 4 + 1] = 0;
            colors[index * 4 + 2] = 0;
            colors[index * 4 + 3] = index % 2 == 0 ? (byte)0 : (byte)255;
        }
        return new TextureAsset {
            Id = "textures/authored", RuntimeAssetId = 123, AuthoringAssetId = "authoring",
            FormerAuthoringAssetIds = ["old-authoring"], Width = width, Height = height,
            ColorFormat = TextureAssetColorFormat.Rgba32, AlphaPrecision = TextureAssetAlphaPrecision.A8, Colors = colors
        };
    }
}
