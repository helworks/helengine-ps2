using helengine.editor;

namespace helengine.ps2.builder;

/// <summary>Resizes an independent RGBA source and encodes native GS pixels and CLUTs through the managed adapter.</summary>
public sealed class Ps2RuntimeTextureCooker {
    /// <summary>Reuses shared aspect-preserving resize and the configured indexed quantization strategy.</summary>
    readonly TextureAssetProcessor Processor = new();

    /// <summary>Preserves source buffers and authored metadata while producing one independent PS2-owned payload.</summary>
    public Ps2TextureAsset Cook(TextureAsset sourceTexture, TextureAssetProcessorSettings settings) {
        ArgumentNullException.ThrowIfNull(sourceTexture);
        Ps2TextureCookSettingsSerializer.Validate(settings);
        string formatId = Ps2TextureCookSettingsSerializer.ResolveNativeFormatId(settings.ColorFormatId);
        Ps2GsTextureFormatCatalog.TryGetFormat(formatId, out Ps2GsTextureFormatDefinition format);
        using TextureAsset rgba = new() {
            Width = sourceTexture.Width,
            Height = sourceTexture.Height,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = TextureAssetPixelCodec.DecodeToRgba32(sourceTexture),
            PaletteColors = Array.Empty<byte>()
        };
        CopyIdentity(sourceTexture, rgba);
        TextureAsset resized = null;
        TextureAsset indexed = null;
        try {
            resized = Processor.Apply(rgba, new TextureAssetProcessorSettings {
                MaxResolution = settings.MaxResolution,
                ColorFormat = TextureAssetColorFormat.Rgba32,
                AlphaPrecision = TextureAssetAlphaPrecision.A8
            });
            TextureAsset encodingSource = resized;
            if (format.IsIndexed) {
                indexed = Processor.Apply(resized, new TextureAssetProcessorSettings {
                    MaxResolution = 0,
                    ColorFormat = format.IndexBitDepth == 4 ? TextureAssetColorFormat.Indexed4 : TextureAssetColorFormat.Indexed8,
                    AlphaPrecision = settings.AlphaPrecision,
                    IndexingMethodId = settings.IndexingMethodId
                });
                CopyIdentity(sourceTexture, indexed);
                encodingSource = indexed;
            }
            Ps2TextureAsset result = Ps2NativeTextureCodec.Encode(encodingSource, formatId, settings.AlphaPrecision);
            CopyIdentity(sourceTexture, result);
            return result;
        } finally {
            indexed?.Dispose();
            if (resized != null && !ReferenceEquals(resized, rgba)) resized.Dispose();
        }
    }

    /// <summary>Copies the identity history into an independent container without sharing mutable metadata arrays.</summary>
    internal static void CopyIdentity(Asset source, Asset destination) {
        destination.Id = source.Id;
        destination.RuntimeAssetId = source.RuntimeAssetId;
        destination.AuthoringAssetId = source.AuthoringAssetId;
        destination.FormerAuthoringAssetIds = source.FormerAuthoringAssetIds == null ? null : (string[])source.FormerAuthoringAssetIds.Clone();
        if (source is TextureAsset sourceTexture && destination is TextureAsset destinationTexture) destinationTexture.IsEngineOwned = sourceTexture.IsEngineOwned;
    }
}
