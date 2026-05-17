using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>
/// Converts decoded source textures into PS2-native runtime texture assets.
/// </summary>
public sealed class Ps2RuntimeTextureCooker {
    /// <summary>
    /// Cooks one decoded source texture into a PS2-native runtime texture asset.
    /// </summary>
    /// <param name="sourceTexture">Decoded source texture.</param>
    /// <param name="settings">Resolved PS2 texture cook settings.</param>
    /// <returns>PS2-native runtime texture asset.</returns>
    public Ps2TextureAsset Cook(TextureAsset sourceTexture, TextureAssetProcessorSettings settings) {
        if (sourceTexture == null) {
            throw new ArgumentNullException(nameof(sourceTexture));
        }
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }
        if (sourceTexture.Colors == null || sourceTexture.Colors.Length == 0) {
            throw new InvalidOperationException("Decoded source textures must contain pixel data.");
        }
        if (settings.ColorFormat != TextureAssetColorFormat.Rgba32 || settings.AlphaPrecision != TextureAssetAlphaPrecision.A8) {
            throw new InvalidOperationException(
                $"PS2 does not support texture settings '{settings.ColorFormat}' + '{settings.AlphaPrecision}' yet.");
        }

        return new Ps2TextureAsset {
            Width = sourceTexture.Width,
            Height = sourceTexture.Height,
            Format = Ps2TextureFormat.Rgba32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = [.. sourceTexture.Colors],
            PaletteData = sourceTexture.PaletteColors == null ? Array.Empty<byte>() : [.. sourceTexture.PaletteColors]
        };
    }
}
