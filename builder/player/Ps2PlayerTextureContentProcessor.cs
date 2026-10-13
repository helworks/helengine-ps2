namespace helengine.ps2.builder;

/// <summary>
/// Texture processor the desktop player registers for PS2 staging content. PS2-owned GS textures are decoded back to
/// RGBA32 with alpha expanded from the GS 0..128 range to 0..255; generic engine textures are read unchanged.
/// </summary>
public sealed class Ps2PlayerTextureContentProcessor : IContentProcessor<TextureAsset> {
    /// <summary>
    /// Shared reader used for generic engine texture payloads that are not PS2-owned.
    /// </summary>
    readonly AssetContentProcessor<TextureAsset> GenericProcessor = new AssetContentProcessor<TextureAsset>();

    /// <summary>
    /// Gets the output type produced by this processor.
    /// </summary>
    public Type OutputType => typeof(TextureAsset);

    /// <summary>
    /// Reads one staged texture payload, decoding PS2-owned GS textures to RGBA32.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of the cooked texture payload.</param>
    /// <returns>Decoded texture owned by the caller.</returns>
    /// <exception cref="NotSupportedException">Thrown when the PS2 texture uses a depth-addressed GS format.</exception>
    public TextureAsset Read(Stream stream) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        if (stream.CanSeek) {
            return ReadSeekable(stream);
        }

        using MemoryStream seekableStream = Ps2PlayerHeaderProbe.CopyToSeekableStream(stream);
        return ReadSeekable(seekableStream);
    }

    /// <summary>
    /// Reads one staged texture payload and boxes the result.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of the cooked texture payload.</param>
    /// <returns>Decoded texture boxed as an object.</returns>
    object IContentProcessor.ReadObject(Stream stream) {
        return Read(stream);
    }

    /// <summary>
    /// Dispatches one seekable texture payload to the PS2 or generic decoder based on its HELE format id. Failures name
    /// the asset the content manager is loading, read from <see cref="EngineBinaryReadContext.CurrentAssetPath"/>.
    /// </summary>
    /// <param name="stream">Seekable stream positioned at the start of the payload.</param>
    /// <returns>Decoded texture owned by the caller.</returns>
    TextureAsset ReadSeekable(Stream stream) {
        if (!Ps2PlayerHeaderProbe.IsPs2Asset(stream)) {
            return GenericProcessor.Read(stream);
        }

        string assetPath = EngineBinaryReadContext.CurrentAssetPath;
        Asset asset = Ps2AssetSerializer.Deserialize(stream);
        if (asset is not Ps2TextureAsset ps2Texture) {
            throw new InvalidOperationException(
                $"PS2 texture payload '{assetPath}' deserialized as '{asset.GetType().Name}' instead of a PS2 texture.");
        }

        try {
            return DecodePs2Texture(ps2Texture, assetPath);
        } finally {
            ps2Texture.Dispose();
        }
    }

    /// <summary>
    /// Converts one PS2-owned GS texture into a generic RGBA32 texture the desktop renderer can upload.
    /// </summary>
    /// <param name="ps2Texture">Deserialized PS2 texture; the caller keeps ownership.</param>
    /// <param name="assetPath">Content path of the texture being loaded, named in failures.</param>
    /// <returns>New RGBA32 texture with 8-bit alpha.</returns>
    /// <exception cref="NotSupportedException">Thrown when the PS2 texture uses a depth-addressed GS format.</exception>
    static TextureAsset DecodePs2Texture(Ps2TextureAsset ps2Texture, string assetPath) {
        if (ps2Texture.Format == Ps2TextureFormat.Depth32
            || ps2Texture.Format == Ps2TextureFormat.Depth24
            || ps2Texture.Format == Ps2TextureFormat.Depth16
            || ps2Texture.Format == Ps2TextureFormat.Depth16S) {
            throw new NotSupportedException($"PS2 texture '{assetPath}' uses format {ps2Texture.Format}, which cannot be played on PC.");
        }

        byte[] rgba = Ps2NativeTextureCodec.Decode(ps2Texture);
        return new TextureAsset {
            Id = ps2Texture.Id,
            Width = ps2Texture.Width,
            Height = ps2Texture.Height,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = rgba
        };
    }
}
