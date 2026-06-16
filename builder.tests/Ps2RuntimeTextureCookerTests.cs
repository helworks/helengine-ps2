using helengine;
using helengine.editor;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 runtime texture cooking applies platform processor settings before serializing native texture payloads.
/// </summary>
public sealed class Ps2RuntimeTextureCookerTests {
    /// <summary>
    /// Ensures one max-resolution cap resizes the larger source axis before the PS2-native texture payload is emitted.
    /// </summary>
    [Fact]
    public void Cook_WhenMaxResolutionCapsLargerAxis_ResizesTextureBeforeSerializing() {
        Ps2RuntimeTextureCooker cooker = new();
        TextureAsset sourceTexture = new() {
            Width = 1024,
            Height = 512,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = new byte[1024 * 512 * 4]
        };
        TextureAssetProcessorSettings settings = new() {
            MaxResolution = 256,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };

        Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

        Assert.Equal((ushort)256, cookedTexture.Width);
        Assert.Equal((ushort)128, cookedTexture.Height);
        Assert.Equal(256 * 128 * 4, cookedTexture.PixelData.Length);
    }

    /// <summary>
    /// Ensures one Indexed8 texture setting produces one PS2-owned indexed runtime payload plus palette data.
    /// </summary>
    [Fact]
    public void Cook_WhenSettingsUseIndexed8_ProducesIndexed8Ps2TextureAsset() {
        Ps2RuntimeTextureCooker cooker = new();
        TextureAsset sourceTexture = CreatePaletteFriendlyTextureAsset();
        TextureAssetProcessorSettings settings = new() {
            MaxResolution = 0,
            ColorFormat = TextureAssetColorFormat.Indexed8,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };

        Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

        Assert.Equal(Ps2TextureFormat.Indexed8, cookedTexture.Format);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmT8, cookedTexture.PixelStorageMode);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, cookedTexture.ClutPixelStorageMode);
        Assert.NotEmpty(cookedTexture.PixelData);
        Assert.NotEmpty(cookedTexture.PaletteData);
    }

    /// <summary>
    /// Ensures one Indexed4 texture setting produces one PS2-owned indexed runtime payload plus palette data.
    /// </summary>
    [Fact]
    public void Cook_WhenSettingsUseIndexed4_ProducesIndexed4Ps2TextureAsset() {
        Ps2RuntimeTextureCooker cooker = new();
        TextureAsset sourceTexture = CreateSmallFourColorTextureAsset();
        TextureAssetProcessorSettings settings = new() {
            MaxResolution = 0,
            ColorFormat = TextureAssetColorFormat.Indexed4,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };

        Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

        Assert.Equal(Ps2TextureFormat.Indexed4, cookedTexture.Format);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmT4, cookedTexture.PixelStorageMode);
        Assert.Equal(Ps2TexturePixelStorageMode.PsmCt32, cookedTexture.ClutPixelStorageMode);
        Assert.NotEmpty(cookedTexture.PixelData);
        Assert.NotEmpty(cookedTexture.PaletteData);
    }

    /// <summary>
    /// Ensures Indexed8 PS2 palette payloads are written in the GS CSM1 CLUT order instead of the logical palette order produced by the shared texture processor.
    /// </summary>
    [Fact]
    public void Cook_WhenSettingsUseIndexed8_SwizzlesPaletteIntoPs2Csm1Order() {
        Ps2RuntimeTextureCooker cooker = new();
        TextureAsset sourceTexture = CreateThirtyTwoColorPaletteTextureAsset();
        TextureAssetProcessorSettings settings = new() {
            MaxResolution = 0,
            ColorFormat = TextureAssetColorFormat.Indexed8,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };

        Ps2TextureAsset cookedTexture = cooker.Cook(sourceTexture, settings);

        Assert.Equal(BuildPaletteEntryBytes(0), cookedTexture.PaletteData.AsSpan(0, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(7), cookedTexture.PaletteData.AsSpan(7 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(16), cookedTexture.PaletteData.AsSpan(8 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(23), cookedTexture.PaletteData.AsSpan(15 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(8), cookedTexture.PaletteData.AsSpan(16 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(15), cookedTexture.PaletteData.AsSpan(23 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(24), cookedTexture.PaletteData.AsSpan(24 * 4, 4).ToArray());
        Assert.Equal(BuildPaletteEntryBytes(31), cookedTexture.PaletteData.AsSpan(31 * 4, 4).ToArray());
    }

    /// <summary>
    /// Creates one simple two-color checkerboard texture that fits inside one Indexed8 palette.
    /// </summary>
    /// <returns>Palette-friendly RGBA32 texture source payload.</returns>
    static TextureAsset CreatePaletteFriendlyTextureAsset() {
        return new TextureAsset {
            Width = 16,
            Height = 16,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = BuildTwoColorCheckerboard()
        };
    }

    /// <summary>
    /// Creates one simple four-color texture that fits inside one Indexed4 palette.
    /// </summary>
    /// <returns>Four-color RGBA32 texture source payload.</returns>
    static TextureAsset CreateSmallFourColorTextureAsset() {
        return new TextureAsset {
            Width = 8,
            Height = 8,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = BuildFourColorBlocks()
        };
    }

    /// <summary>
    /// Creates one 32-color texture source whose palette order is easy to assert after PS2 CLUT swizzling.
    /// </summary>
    /// <returns>RGBA32 texture source containing 32 unique colors.</returns>
    static TextureAsset CreateThirtyTwoColorPaletteTextureAsset() {
        return new TextureAsset {
            Width = 8,
            Height = 4,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = BuildThirtyTwoColorBlocks()
        };
    }

    /// <summary>
    /// Builds one two-color checkerboard RGBA32 byte payload.
    /// </summary>
    /// <returns>Two-color checkerboard texel bytes.</returns>
    static byte[] BuildTwoColorCheckerboard() {
        byte[] colors = new byte[16 * 16 * 4];
        for (int y = 0; y < 16; y++) {
            for (int x = 0; x < 16; x++) {
                int colorIndex = ((y * 16) + x) * 4;
                bool white = ((x + y) & 1) == 0;
                colors[colorIndex] = white ? (byte)255 : (byte)0;
                colors[colorIndex + 1] = white ? (byte)255 : (byte)0;
                colors[colorIndex + 2] = white ? (byte)255 : (byte)0;
                colors[colorIndex + 3] = 255;
            }
        }

        return colors;
    }

    /// <summary>
    /// Builds one four-color RGBA32 block pattern.
    /// </summary>
    /// <returns>Four-color block texel bytes.</returns>
    static byte[] BuildFourColorBlocks() {
        byte[] colors = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++) {
            for (int x = 0; x < 8; x++) {
                int colorIndex = ((y * 8) + x) * 4;
                bool right = x >= 4;
                bool bottom = y >= 4;
                colors[colorIndex] = right ? (byte)255 : (byte)0;
                colors[colorIndex + 1] = bottom ? (byte)255 : (byte)0;
                colors[colorIndex + 2] = (!right && !bottom) ? (byte)255 : (byte)0;
                colors[colorIndex + 3] = 255;
            }
        }

        return colors;
    }

    /// <summary>
    /// Builds one 32-color RGBA32 texture payload that maps each pixel to one unique palette entry.
    /// </summary>
    /// <returns>Thirty-two unique RGBA32 texels.</returns>
    static byte[] BuildThirtyTwoColorBlocks() {
        byte[] colors = new byte[8 * 4 * 4];
        for (int pixelIndex = 0; pixelIndex < 32; pixelIndex++) {
            byte[] paletteEntry = BuildPaletteEntryBytes(pixelIndex);
            int colorByteOffset = pixelIndex * 4;
            colors[colorByteOffset] = paletteEntry[0];
            colors[colorByteOffset + 1] = paletteEntry[1];
            colors[colorByteOffset + 2] = paletteEntry[2];
            colors[colorByteOffset + 3] = paletteEntry[3];
        }

        return colors;
    }

    /// <summary>
    /// Builds one deterministic RGBA32 palette entry payload for the supplied logical palette index.
    /// </summary>
    /// <param name="paletteIndex">Zero-based logical palette index.</param>
    /// <returns>RGBA32 palette entry bytes.</returns>
    static byte[] BuildPaletteEntryBytes(int paletteIndex) {
        return [
            (byte)paletteIndex,
            (byte)(paletteIndex + 1),
            (byte)(paletteIndex + 2),
            (byte)(0x80 + paletteIndex)
        ];
    }

}
