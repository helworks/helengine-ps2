using helengine.editor;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Provides one deterministic font importer that emits a packaged font asset with an embedded one-pixel atlas for PS2 end-to-end cook tests.
/// </summary>
internal sealed class SingleGlyphFontImporter : IFontImporter {
    /// <summary>
    /// Imports a minimal font asset with one glyph atlas payload so builder-owned font-atlas cooking can extract and externalize that atlas.
    /// </summary>
    /// <param name="stream">Source stream supplied by the editor import pipeline.</param>
    /// <param name="settings">Active platform font settings supplied by the editor import pipeline.</param>
    /// <returns>One imported font asset with a one-pixel raw atlas texture payload.</returns>
    public FontAsset ImportFont(Stream stream, FontAssetProcessorSettings settings) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        return new FontAsset(
            new FontInfo("BuilderTestFont", 16, 4.0f),
            null,
            new Dictionary<char, FontChar> {
                ['A'] = new FontChar(new float4(0.0f, 0.0f, 1.0f, 1.0f), 0.0f, 8.0f, 0.0f, 0.0f)
            },
            16.0f,
            1,
            1) {
            SourceTextureAsset = new TextureAsset {
                Id = "fonts/test.hefont#atlas",
                Width = 1,
                Height = 1,
                ColorFormat = TextureAssetColorFormat.Rgba32,
                AlphaPrecision = TextureAssetAlphaPrecision.Opaque,
                Colors = [255, 255, 255, 255],
                PaletteColors = Array.Empty<byte>()
            }
        };
    }
}
