using System.Drawing;

namespace helengine.ps2.builder;

/// <summary>
/// Decodes source images and serialized engine textures into independently owned RGBA assets for PS2 cooking.
/// </summary>
public sealed class Ps2SourceTextureDecoder {
    /// <summary>
    /// Decodes one source image file into an uncooked raw texture asset.
    /// </summary>
    /// <param name="sourceAssetPath">Absolute source image path.</param>
    /// <returns>Raw texture asset containing RGBA pixel bytes.</returns>
    public TextureAsset Decode(string sourceAssetPath) {
        if (string.IsNullOrWhiteSpace(sourceAssetPath)) {
            throw new ArgumentException("Source asset path is required.", nameof(sourceAssetPath));
        }
        if (!File.Exists(sourceAssetPath)) {
            throw new FileNotFoundException("Source texture file was not found.", sourceAssetPath);
        }
        byte[] bytes = File.ReadAllBytes(sourceAssetPath);
        if (bytes.Length >= 4 && bytes[0] == 'H' && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'E') {
            Asset asset = helengine.files.AssetSerializer.DeserializeFromBytes(bytes);
            if (asset is not TextureAsset texture) throw new InvalidOperationException("PS2 cook source did not contain a texture asset.");
            try {
                TextureAsset result = new() {
                    Width = texture.Width,
                    Height = texture.Height,
                    ColorFormat = TextureAssetColorFormat.Rgba32,
                    AlphaPrecision = TextureAssetAlphaPrecision.A8,
                    Colors = TextureAssetPixelCodec.DecodeToRgba32(texture),
                    PaletteColors = Array.Empty<byte>()
                };
                Ps2RuntimeTextureCooker.CopyIdentity(texture, result);
                return result;
            } finally { texture.Dispose(); }
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1)) throw new PlatformNotSupportedException("Authored image decoding requires the Windows image decoder; serialized textures are portable.");
        using MemoryStream imageStream = new(bytes, false);
        using Bitmap bitmap = new(imageStream);
        if (bitmap.Width < 1 || bitmap.Height < 1) {
            throw new InvalidOperationException("Source textures must have positive dimensions.");
        }

        ushort width = checked((ushort)bitmap.Width);
        ushort height = checked((ushort)bitmap.Height);
        byte[] rgbaBytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        int writeOffset = 0;
        for (int y = 0; y < bitmap.Height; y++) {
            for (int x = 0; x < bitmap.Width; x++) {
                Color pixel = bitmap.GetPixel(x, y);
                rgbaBytes[writeOffset++] = pixel.R;
                rgbaBytes[writeOffset++] = pixel.G;
                rgbaBytes[writeOffset++] = pixel.B;
                rgbaBytes[writeOffset++] = pixel.A;
            }
        }

        return new TextureAsset {
            Width = width,
            Height = height,
            Colors = rgbaBytes,
            PaletteColors = Array.Empty<byte>(),
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };
    }
}
