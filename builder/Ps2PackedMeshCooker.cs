using helengine;

namespace helengine.ps2.builder;

/// <summary>
/// Converts raw model assets into the first packed PS2 mesh payload used by the VU opaque renderer milestone.
/// </summary>
public sealed class Ps2PackedMeshCooker {
    /// <summary>
    /// Cooks one raw model asset into a qword-aligned packed PS2 mesh payload.
    /// </summary>
    /// <param name="modelAsset">Model asset to convert.</param>
    /// <returns>Packed PS2 mesh bytes.</returns>
    public byte[] Cook(ModelAsset modelAsset) {
        if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        WriteHeader(writer, modelAsset);
        WritePackedPositions(writer, modelAsset);
        WritePackedNormals(writer, modelAsset);
        WritePackedTexCoords(writer, modelAsset);
        WritePackedIndices(writer, modelAsset);
        PadToQwordBoundary(writer);

        return stream.ToArray();
    }

    /// <summary>
    /// Writes the fixed header for the packed PS2 mesh payload.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="modelAsset">Model asset being serialized.</param>
    void WriteHeader(BinaryWriter writer, ModelAsset modelAsset) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        } else if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        int vertexCount = modelAsset.Positions?.Length ?? 0;
        int normalCount = modelAsset.Normals?.Length ?? 0;
        int texCoordCount = modelAsset.TexCoords?.Length ?? 0;
        int indexCount = ResolvePackedIndices(modelAsset).Length;

        writer.Write(Ps2PackedMeshLayout.Version);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(vertexCount);
        writer.Write(normalCount);
        writer.Write(texCoordCount);
        writer.Write(indexCount);
    }

    /// <summary>
    /// Writes packed position qwords where the fourth lane stores a homogeneous value of one.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="modelAsset">Model asset being serialized.</param>
    void WritePackedPositions(BinaryWriter writer, ModelAsset modelAsset) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        } else if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        float3[] positions = modelAsset.Positions ?? Array.Empty<float3>();
        for (int index = 0; index < positions.Length; index++) {
            writer.Write(positions[index].X);
            writer.Write(positions[index].Y);
            writer.Write(positions[index].Z);
            writer.Write(1f);
        }
    }

    /// <summary>
    /// Writes packed normal qwords where the fourth lane stores zero.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="modelAsset">Model asset being serialized.</param>
    void WritePackedNormals(BinaryWriter writer, ModelAsset modelAsset) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        } else if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        float3[] normals = modelAsset.Normals ?? Array.Empty<float3>();
        for (int index = 0; index < normals.Length; index++) {
            writer.Write(normals[index].X);
            writer.Write(normals[index].Y);
            writer.Write(normals[index].Z);
            writer.Write(0f);
        }
    }

    /// <summary>
    /// Writes packed texture-coordinate qwords where the unused lanes store zero.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="modelAsset">Model asset being serialized.</param>
    void WritePackedTexCoords(BinaryWriter writer, ModelAsset modelAsset) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        } else if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        float2[] texCoords = modelAsset.TexCoords ?? Array.Empty<float2>();
        for (int index = 0; index < texCoords.Length; index++) {
            writer.Write(texCoords[index].X);
            writer.Write(texCoords[index].Y);
            writer.Write(0f);
            writer.Write(0f);
        }
    }

    /// <summary>
    /// Writes packed indices as 32-bit values and pads the block to the next qword boundary.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    /// <param name="modelAsset">Model asset being serialized.</param>
    void WritePackedIndices(BinaryWriter writer, ModelAsset modelAsset) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        } else if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        uint[] packedIndices = ResolvePackedIndices(modelAsset);
        for (int index = 0; index < packedIndices.Length; index++) {
            writer.Write(packedIndices[index]);
        }

        PadToQwordBoundary(writer);
    }

    /// <summary>
    /// Resolves the model index buffer into a unified 32-bit packed representation.
    /// </summary>
    /// <param name="modelAsset">Model asset being serialized.</param>
    /// <returns>Packed index buffer.</returns>
    uint[] ResolvePackedIndices(ModelAsset modelAsset) {
        if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        } else if (modelAsset.Indices32 != null && modelAsset.Indices32.Length > 0) {
            return modelAsset.Indices32;
        } else if (modelAsset.Indices16 == null || modelAsset.Indices16.Length == 0) {
            return Array.Empty<uint>();
        }

        uint[] packedIndices = new uint[modelAsset.Indices16.Length];
        for (int index = 0; index < modelAsset.Indices16.Length; index++) {
            packedIndices[index] = modelAsset.Indices16[index];
        }

        return packedIndices;
    }

    /// <summary>
    /// Pads the current payload to the next qword boundary.
    /// </summary>
    /// <param name="writer">Destination writer.</param>
    void PadToQwordBoundary(BinaryWriter writer) {
        if (writer == null) {
            throw new ArgumentNullException(nameof(writer));
        }

        long remainder = writer.BaseStream.Position % Ps2PackedMeshLayout.QwordSize;
        if (remainder == 0) {
            return;
        }

        int paddingLength = Ps2PackedMeshLayout.QwordSize - (int)remainder;
        for (int index = 0; index < paddingLength; index++) {
            writer.Write((byte)0);
        }
    }
}
