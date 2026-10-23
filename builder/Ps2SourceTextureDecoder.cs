using helengine.editor;

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
        string extension = Path.GetExtension(sourceAssetPath);
        TextureImporterRegistration registration = PortableImporterFactory.CreateDefault()
            .OfType<TextureImporterRegistration>()
            .FirstOrDefault(importer => importer.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No portable texture importer supports source extension '{extension}'.");
        using MemoryStream imageStream = new(bytes, false);
        using TextureAsset importedTexture = registration.Importer.ImportTexture(imageStream)
            ?? throw new InvalidOperationException($"Texture importer '{registration.ImporterId}' did not return a texture asset.");
        if (importedTexture.Width < 1 || importedTexture.Height < 1) {
            throw new InvalidOperationException("Source textures must have positive dimensions.");
        }
        TextureAsset decodedTexture = new TextureAsset {
            Width = importedTexture.Width,
            Height = importedTexture.Height,
            Colors = TextureAssetPixelCodec.DecodeToRgba32(importedTexture),
            PaletteColors = Array.Empty<byte>(),
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8
        };
        Ps2RuntimeTextureCooker.CopyIdentity(importedTexture, decodedTexture);
        return decodedTexture;
    }
}
