using helengine.editor;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Provides one deterministic texture importer that always returns a one-pixel RGBA texture asset for builder-owned work-item tests.
/// </summary>
internal sealed class SinglePixelTextureImporter : ITextureImporter {
    /// <summary>
    /// Imports a one-pixel texture asset regardless of the source stream contents so editor-owned cook tests stay deterministic.
    /// </summary>
    /// <param name="stream">Source stream supplied by the editor import pipeline.</param>
    /// <returns>One imported texture asset with a single opaque white pixel.</returns>
    public TextureAsset ImportTexture(Stream stream) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        return new TextureAsset {
            Width = 1,
            Height = 1,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.Opaque,
            Colors = [255, 255, 255, 255],
            PaletteColors = Array.Empty<byte>()
        };
    }
}
