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

        uint[] triangleIndices = ResolveTriangleIndices(modelAsset);
        int triangleVertexCount = triangleIndices.Length;
        int positionBlockQwordOffset = Ps2PackedMeshLayout.HeaderQwordCount;
        int normalBlockQwordOffset = positionBlockQwordOffset + triangleVertexCount;
        int texCoordBlockQwordOffset = normalBlockQwordOffset + triangleVertexCount;

        writer.Write((uint)Ps2PackedMeshLayout.Version);
        writer.Write(triangleVertexCount);
        writer.Write(positionBlockQwordOffset);
        writer.Write(normalBlockQwordOffset);
        writer.Write(texCoordBlockQwordOffset);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
    }

    /// <summary>
    /// Writes expanded packed position qwords where the fourth lane stores a homogeneous value of one.
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
        uint[] triangleIndices = ResolveTriangleIndices(modelAsset);
        for (int index = 0; index < triangleIndices.Length; index++) {
            int sourceIndex = (int)triangleIndices[index];
            if (sourceIndex < 0 || sourceIndex >= positions.Length) {
                throw new InvalidOperationException($"Packed PS2 mesh position index '{sourceIndex}' is outside the available position array.");
            }

            writer.Write(positions[sourceIndex].X);
            writer.Write(positions[sourceIndex].Y);
            writer.Write(positions[sourceIndex].Z);
            writer.Write(1f);
        }
    }

    /// <summary>
    /// Writes expanded packed normal qwords where the fourth lane stores zero.
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
        uint[] triangleIndices = ResolveTriangleIndices(modelAsset);
        for (int index = 0; index < triangleIndices.Length; index++) {
            int sourceIndex = (int)triangleIndices[index];
            if (sourceIndex >= 0 && sourceIndex < normals.Length) {
                writer.Write(normals[sourceIndex].X);
                writer.Write(normals[sourceIndex].Y);
                writer.Write(normals[sourceIndex].Z);
            } else {
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(0f);
            }

            writer.Write(0f);
        }
    }

    /// <summary>
    /// Writes expanded packed texture-coordinate qwords where the unused lanes store zero.
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
        uint[] triangleIndices = ResolveTriangleIndices(modelAsset);
        for (int index = 0; index < triangleIndices.Length; index++) {
            int sourceIndex = (int)triangleIndices[index];
            if (sourceIndex >= 0 && sourceIndex < texCoords.Length) {
                writer.Write(texCoords[sourceIndex].X);
                writer.Write(texCoords[sourceIndex].Y);
            } else {
                writer.Write(0f);
                writer.Write(0f);
            }

            writer.Write(0f);
            writer.Write(0f);
        }
    }

    /// <summary>
    /// Resolves the model index buffer into a unified 32-bit triangle-stream expansion source.
    /// </summary>
    /// <param name="modelAsset">Model asset being serialized.</param>
    /// <returns>Packed index buffer.</returns>
    uint[] ResolveTriangleIndices(ModelAsset modelAsset) {
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
