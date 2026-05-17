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
    public Ps2TextureAsset Cook(TextureAsset sourceTexture, Ps2TextureCookSettings settings) {
        if (sourceTexture == null) {
            throw new ArgumentNullException(nameof(sourceTexture));
        }
        if (settings == null) {
            throw new ArgumentNullException(nameof(settings));
        }
        if (sourceTexture.Colors == null || sourceTexture.Colors.Length == 0) {
            throw new InvalidOperationException("Decoded source textures must contain pixel data.");
        }

        return new Ps2TextureAsset {
            Width = sourceTexture.Width,
            Height = sourceTexture.Height,
            Format = settings.Format,
            AlphaMode = settings.AlphaMode,
            PixelData = [.. sourceTexture.Colors],
            PaletteData = sourceTexture.PaletteColors == null ? Array.Empty<byte>() : [.. sourceTexture.PaletteColors]
        };
    }
}
