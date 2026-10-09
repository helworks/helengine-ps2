namespace helengine {
    /// <summary>Adapts shared GS byte codecs to the existing version-two platform-owned texture asset without a duplicate native header.</summary>
    public static class Ps2NativeTextureCodec {
        /// <summary>Encodes an authored RGBA or already-quantized indexed source with independent owned runtime buffers.</summary>
        [NativeOwnedReturn]
        public static Ps2TextureAsset Encode(TextureAsset source, string formatId, TextureAssetAlphaPrecision alphaPrecision) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!Ps2GsTextureFormatCatalog.TryGetFormat(formatId, out Ps2GsTextureFormatDefinition format)) throw new ArgumentException("Unknown PS2 GS texture format.");
            Ps2TextureAsset asset = new Ps2TextureAsset();
            try {
                asset.Id = source.Id; asset.RuntimeAssetId = source.RuntimeAssetId; asset.AuthoringAssetId = source.AuthoringAssetId;
                asset.FormerAuthoringAssetIds = CopyFormerIds(source.FormerAuthoringAssetIds); asset.Width = source.Width; asset.Height = source.Height;
                asset.Format = (Ps2TextureFormat)format.PlatformFormatCode; asset.PixelStorageMode = (Ps2TexturePixelStorageMode)format.PlatformFormatCode;
                asset.ClutPixelStorageMode = (Ps2TexturePixelStorageMode)format.PlatformClutFormatCode;
                asset.AlphaMode = alphaPrecision == TextureAssetAlphaPrecision.Opaque ? Ps2TextureAlphaMode.Opaque : alphaPrecision == TextureAssetAlphaPrecision.Binary ? Ps2TextureAlphaMode.Binary : Ps2TextureAlphaMode.Full;
                asset.PixelData = Ps2GsTexturePixelCodec.EncodePixels(source, formatId, alphaPrecision);
                asset.PaletteData = Ps2GsTexturePixelCodec.EncodePalette(source, formatId, alphaPrecision);
            } catch {
                NativeOwnership.DisposeAndRelease(ref asset);
                throw;
            }
            return asset;
        }
        /// <summary>Decodes native sampled color channels with canonical TEXA and GS source-over alpha conventions.</summary>
        [NativeOwnedReturn]
        public static byte[] Decode(Ps2TextureAsset asset) {
            Ps2GsTextureFormatDefinition format = GetFormat(asset);
            TextureAssetAlphaPrecision alpha = asset.AlphaMode == Ps2TextureAlphaMode.Opaque ? TextureAssetAlphaPrecision.Opaque :
                asset.AlphaMode == Ps2TextureAlphaMode.Binary ? TextureAssetAlphaPrecision.Binary : format.DefaultAlphaPrecision;
            return Ps2GsTexturePixelCodec.DecodeToRgba32(asset.PixelData, asset.PaletteData, asset.Width, asset.Height, format.Id, alpha);
        }
        /// <summary>Finds the borrowed native description and rejects inconsistent platform metadata before decoding.</summary>
        [NativeBorrowedReturn]
        public static Ps2GsTextureFormatDefinition GetFormat(Ps2TextureAsset asset) {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if ((int)asset.Format != (int)asset.PixelStorageMode || !Ps2GsTextureFormatCatalog.TryGetPlatformFormat((int)asset.Format, (int)asset.ClutPixelStorageMode, out Ps2GsTextureFormatDefinition format)) throw new ArgumentException("PS2 texture format and storage metadata are inconsistent.");
            if (asset.AlphaMode != Ps2TextureAlphaMode.Opaque && asset.AlphaMode != Ps2TextureAlphaMode.Full && asset.AlphaMode != Ps2TextureAlphaMode.Binary) throw new ArgumentException("Unknown PS2 texture alpha mode.");
            return format;
        }
        /// <summary>Copies authoring aliases into independent storage while preserving their values.</summary>
        [NativeOwnedReturn]
        static string[] CopyFormerIds(string[] input) {
            if (input == null) return new string[0];
            string[] output = new string[input.Length];
            for (int index = 0; index < input.Length; index++) output[index] = input[index];
            return output;
        }
    }
}
