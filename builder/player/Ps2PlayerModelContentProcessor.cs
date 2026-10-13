namespace helengine.ps2.builder;

/// <summary>
/// Model processor the desktop player registers for PS2 staging content. PS2-owned models are rebuilt as generic
/// indexed models from their vertex arrays (the packed VU mesh is PS2-only and ignored); generic engine models are read unchanged.
/// </summary>
public sealed class Ps2PlayerModelContentProcessor : IContentProcessor<ModelAsset> {
    /// <summary>
    /// Shared reader used for generic engine model payloads that are not PS2-owned.
    /// </summary>
    readonly AssetContentProcessor<ModelAsset> GenericProcessor = new AssetContentProcessor<ModelAsset>();

    /// <summary>
    /// Gets the output type produced by this processor.
    /// </summary>
    public Type OutputType => typeof(ModelAsset);

    /// <summary>
    /// Reads one staged model payload, rebuilding PS2-owned models as generic indexed models.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of the cooked model payload.</param>
    /// <returns>Decoded model owned by the caller.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a PS2 model lacks vertex arrays or indices, or its vertex arrays differ in length.</exception>
    public ModelAsset Read(Stream stream) {
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
    /// Reads one staged model payload and boxes the result.
    /// </summary>
    /// <param name="stream">Stream positioned at the start of the cooked model payload.</param>
    /// <returns>Decoded model boxed as an object.</returns>
    object IContentProcessor.ReadObject(Stream stream) {
        return Read(stream);
    }

    /// <summary>
    /// Dispatches one seekable model payload to the PS2 or generic decoder based on its HELE format id. Failures name
    /// the asset the content manager is loading, read from <see cref="EngineBinaryReadContext.CurrentAssetPath"/>.
    /// </summary>
    /// <param name="stream">Seekable stream positioned at the start of the payload.</param>
    /// <returns>Decoded model owned by the caller.</returns>
    ModelAsset ReadSeekable(Stream stream) {
        if (!Ps2PlayerHeaderProbe.IsPs2Asset(stream)) {
            return GenericProcessor.Read(stream);
        }

        string assetPath = EngineBinaryReadContext.CurrentAssetPath;
        Asset asset = Ps2AssetSerializer.Deserialize(stream);
        if (asset is not Ps2ModelAsset ps2Model) {
            throw new InvalidOperationException(
                $"PS2 model payload '{assetPath}' deserialized as '{asset.GetType().Name}' instead of a PS2 model.");
        }

        return DecodePs2Model(ps2Model, assetPath);
    }

    /// <summary>
    /// Converts one PS2-owned model into a generic indexed model with bounds computed from its positions. Submeshes stay
    /// null so the player synthesizes one default submesh covering every index.
    /// </summary>
    /// <param name="ps2Model">Deserialized PS2 model.</param>
    /// <param name="assetPath">Content path of the model being loaded, named in failures.</param>
    /// <returns>New generic model sharing the PS2 model's vertex and index arrays.</returns>
    /// <exception cref="InvalidOperationException">Thrown when vertex arrays or indices are missing, or the vertex arrays differ in length.</exception>
    static ModelAsset DecodePs2Model(Ps2ModelAsset ps2Model, string assetPath) {
        if (ps2Model.Positions == null) {
            throw new InvalidOperationException($"PS2 model '{ps2Model.Id}' ('{assetPath}') has no positions.");
        } else if (ps2Model.Normals == null) {
            throw new InvalidOperationException($"PS2 model '{ps2Model.Id}' ('{assetPath}') has no normals.");
        } else if (ps2Model.TexCoords == null) {
            throw new InvalidOperationException($"PS2 model '{ps2Model.Id}' ('{assetPath}') has no texture coordinates.");
        } else if (ps2Model.Indices16 == null) {
            throw new InvalidOperationException($"PS2 model '{ps2Model.Id}' ('{assetPath}') has no 16-bit indices.");
        } else if (ps2Model.Normals.Length != ps2Model.Positions.Length || ps2Model.TexCoords.Length != ps2Model.Positions.Length) {
            throw new InvalidOperationException(
                $"PS2 model '{ps2Model.Id}' ('{assetPath}') has mismatched vertex arrays: positions={ps2Model.Positions.Length} normals={ps2Model.Normals.Length} texcoords={ps2Model.TexCoords.Length}.");
        }

        Ps2PlayerModelBounds.Compute(ps2Model.Positions, out float3 boundsMin, out float3 boundsMax);
        return new ModelAsset {
            Id = ps2Model.Id,
            Positions = ps2Model.Positions,
            Normals = ps2Model.Normals,
            TexCoords = ps2Model.TexCoords,
            Indices16 = ps2Model.Indices16,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax
        };
    }
}
