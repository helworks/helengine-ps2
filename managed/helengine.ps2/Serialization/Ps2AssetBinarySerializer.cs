using helengine.files;

namespace helengine {
    /// <summary>
    /// Serializes and deserializes PS2-owned cooked runtime asset payloads.
    /// </summary>
    public static class Ps2AssetBinarySerializer {
        /// <summary>
        /// Distinct HELE format identifier reserved for PS2-owned cooked runtime assets.
        /// </summary>
        public const ushort FormatId = 2;

        /// <summary>
        /// Record kind used for PS2-owned serialized asset payloads.
        /// </summary>
        public const EditorBinaryRecordKind RecordKind = EditorBinaryRecordKind.Asset;

        /// <summary>
        /// Current PS2 asset binary version.
        /// </summary>
        public const byte CurrentVersion = 2;

        /// <summary>
        /// Lowest PS2 asset binary version supported by this serializer.
        /// </summary>
        const byte LegacyVersion = 1;

        /// <summary>
        /// First PS2 asset version that stores runtime asset ids.
        /// </summary>
        const byte RuntimeAssetIdentityVersion = 1;

        /// <summary>
        /// First PS2 asset version that stores texture pixel-storage metadata.
        /// </summary>
        const byte TextureStorageMetadataVersion = 2;

        /// <summary>
        /// Payload endianness used by PS2-owned cooked runtime asset payloads.
        /// </summary>
        static readonly EngineBinaryEndianness PayloadEndianness = EngineBinaryEndianness.LittleEndian;

        /// <summary>
        /// Serializes one PS2-owned cooked runtime asset into the supplied stream.
        /// </summary>
        /// <param name="stream">Destination stream for the encoded payload.</param>
        /// <param name="asset">PS2-owned asset instance to serialize.</param>
        public static void Serialize(Stream stream, Asset asset) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            } else if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }

            Ps2AssetBinaryValueKind valueKind = GetValueKind(asset);
            EngineBinaryHeader header = new EngineBinaryHeader(
                PayloadEndianness,
                CurrentVersion,
                FormatId,
                (ushort)RecordKind,
                (ushort)valueKind);

            EngineBinaryHeaderSerializer.Write(stream, header);
            using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, PayloadEndianness);
            WriteAssetPayload(writer, asset);
        }

        /// <summary>
        /// Deserializes one PS2-owned cooked runtime asset from the supplied stream.
        /// </summary>
        /// <param name="stream">Source stream containing the encoded payload.</param>
        /// <returns>Deserialized PS2-owned asset instance.</returns>
        public static Asset Deserialize(Stream stream) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            }

            EngineBinaryHeader header = EngineBinaryHeaderSerializer.Read(stream);
            return Deserialize(stream, header);
        }

        /// <summary>
        /// Deserializes one PS2-owned cooked runtime asset after the HELE header has already been read.
        /// </summary>
        /// <param name="stream">Source stream positioned at the payload body.</param>
        /// <param name="header">Previously decoded HELE header.</param>
        /// <returns>Deserialized PS2-owned asset instance.</returns>
        public static Asset Deserialize(Stream stream, EngineBinaryHeader header) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            } else if (header == null) {
                throw new ArgumentNullException(nameof(header));
            }

            ValidateHeader(header);
            using EngineBinaryReader reader = EngineBinaryReader.Create(stream, header.Endianness);
            return ReadAssetPayload(reader, (Ps2AssetBinaryValueKind)header.ValueKind, header.Version);
        }

        /// <summary>
        /// Validates that one HELE header addresses the PS2-owned cooked runtime asset format.
        /// </summary>
        /// <param name="header">Header metadata to validate.</param>
        static void ValidateHeader(EngineBinaryHeader header) {
            if (header.FormatId != FormatId) {
                throw new InvalidOperationException($"Unsupported PS2 asset binary format id '{header.FormatId}'.");
            } else if (header.RecordKind != (ushort)RecordKind) {
                throw new InvalidOperationException($"Unexpected PS2 asset record kind '{header.RecordKind}'.");
            } else if (header.Version < LegacyVersion || header.Version > CurrentVersion) {
                throw new InvalidOperationException($"Unsupported PS2 asset binary version '{header.Version}'.");
            }
        }

        /// <summary>
        /// Resolves the PS2-owned value kind identifier for one asset instance.
        /// </summary>
        /// <param name="asset">Asset instance to classify.</param>
        /// <returns>PS2-owned value kind identifier.</returns>
        static Ps2AssetBinaryValueKind GetValueKind(Asset asset) {
            if (asset is Ps2MaterialAsset) {
                return Ps2AssetBinaryValueKind.Ps2MaterialAsset;
            } else if (asset is Ps2TextureAsset) {
                return Ps2AssetBinaryValueKind.Ps2TextureAsset;
            } else if (asset is Ps2ModelAsset) {
                return Ps2AssetBinaryValueKind.Ps2ModelAsset;
            }

            throw new InvalidOperationException($"Asset type '{asset.GetType().Name}' is not supported by the PS2 asset serializer.");
        }

        /// <summary>
        /// Writes the payload body for one PS2-owned asset instance.
        /// </summary>
        /// <param name="writer">Destination writer for the payload.</param>
        /// <param name="asset">Asset instance to serialize.</param>
        static void WriteAssetPayload(EngineBinaryWriter writer, Asset asset) {
            if (asset is Ps2MaterialAsset ps2MaterialAsset) {
                WritePs2MaterialAsset(writer, ps2MaterialAsset);
                return;
            } else if (asset is Ps2TextureAsset ps2TextureAsset) {
                WritePs2TextureAsset(writer, ps2TextureAsset);
                return;
            } else if (asset is Ps2ModelAsset ps2ModelAsset) {
                WritePs2ModelAsset(writer, ps2ModelAsset);
                return;
            }

            throw new InvalidOperationException($"Asset type '{asset.GetType().Name}' is not supported by the PS2 asset serializer.");
        }

        /// <summary>
        /// Reads the payload body for one PS2-owned asset instance.
        /// </summary>
        /// <param name="reader">Source reader positioned at the payload body.</param>
        /// <param name="valueKind">PS2-owned value kind identifier.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        /// <returns>Deserialized PS2-owned asset instance.</returns>
        static Asset ReadAssetPayload(EngineBinaryReader reader, Ps2AssetBinaryValueKind valueKind, byte version) {
            switch (valueKind) {
                case Ps2AssetBinaryValueKind.Ps2MaterialAsset:
                    return ReadPs2MaterialAsset(reader, version);
                case Ps2AssetBinaryValueKind.Ps2TextureAsset:
                    return ReadPs2TextureAsset(reader, version);
                case Ps2AssetBinaryValueKind.Ps2ModelAsset:
                    return ReadPs2ModelAsset(reader, version);
                default:
                    throw new InvalidOperationException($"Unsupported PS2 asset value kind '{(ushort)valueKind}'.");
            }
        }

        /// <summary>
        /// Writes one shared top-level asset identity payload.
        /// </summary>
        /// <param name="writer">Destination writer for the identity.</param>
        /// <param name="asset">Asset whose identity should be serialized.</param>
        static void WriteAssetIdentity(EngineBinaryWriter writer, Asset asset) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            } else if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }

            EnsureRuntimeAssetIdentity(asset);
            writer.WriteString(asset.Id);
            WriteRuntimeAssetId(writer, asset.RuntimeAssetId);
        }

        /// <summary>
        /// Reads one shared top-level asset identity payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the identity payload.</param>
        /// <param name="asset">Asset instance receiving the decoded identity.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        static void ReadAssetIdentity(EngineBinaryReader reader, Asset asset, byte version) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            } else if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }

            asset.Id = reader.ReadString();
            asset.RuntimeAssetId = version >= RuntimeAssetIdentityVersion
                ? ReadRuntimeAssetId(reader)
                : 0ul;
        }

        /// <summary>
        /// Writes one runtime asset id using the same eight-byte little-endian layout as the generic asset serializers.
        /// </summary>
        /// <param name="writer">Destination writer for the runtime asset id payload.</param>
        /// <param name="runtimeAssetId">Runtime asset id value to encode.</param>
        static void WriteRuntimeAssetId(EngineBinaryWriter writer, ulong runtimeAssetId) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            uint lower = (uint)(runtimeAssetId & 0xFFFFFFFFul);
            uint upper = (uint)(runtimeAssetId >> 32);
            writer.WriteUInt32(lower);
            writer.WriteUInt32(upper);
        }

        /// <summary>
        /// Reads one runtime asset id from the shared eight-byte little-endian identity payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the runtime asset id payload.</param>
        /// <returns>Decoded runtime asset id.</returns>
        static ulong ReadRuntimeAssetId(EngineBinaryReader reader) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            }

            ulong lower = reader.ReadUInt32();
            ulong upper = reader.ReadUInt32();
            return lower | (upper << 32);
        }

        /// <summary>
        /// Ensures one asset has a deterministic runtime asset id before serialization.
        /// </summary>
        /// <param name="asset">Asset whose runtime asset id should be populated when needed.</param>
        static void EnsureRuntimeAssetIdentity(Asset asset) {
            if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }
            if (asset.RuntimeAssetId != 0ul || string.IsNullOrWhiteSpace(asset.Id)) {
                return;
            }

            asset.RuntimeAssetId = RuntimeAssetIdGenerator.Generate(asset.Id);
        }

        /// <summary>
        /// Writes one PS2-owned cooked material payload.
        /// </summary>
        /// <param name="writer">Destination writer for the payload.</param>
        /// <param name="asset">PS2 material asset to serialize.</param>
        static void WritePs2MaterialAsset(EngineBinaryWriter writer, Ps2MaterialAsset asset) {
            WriteAssetIdentity(writer, asset);
            writer.WriteString(asset.RendererFamilyId ?? string.Empty);
            writer.WriteInt32((int)asset.LightingMode);
            writer.WriteInt32((int)asset.AlphaMode);
            writer.WriteInt32((int)asset.RenderClass);
            writer.WriteByte(asset.BaseColorR);
            writer.WriteByte(asset.BaseColorG);
            writer.WriteByte(asset.BaseColorB);
            writer.WriteByte(asset.BaseColorA);
            writer.WriteString(asset.TextureRelativePath ?? string.Empty);
            writer.WriteByte(asset.DoubleSided ? (byte)1 : (byte)0);
            writer.WriteByte(asset.CastShadows ? (byte)1 : (byte)0);
            writer.WriteByte(asset.UseVertexColor ? (byte)1 : (byte)0);
            writer.WriteByte(asset.ExpensiveModeAllowed ? (byte)1 : (byte)0);
            writer.WriteSingle(asset.Roughness);
            writer.WriteSingle(asset.SpecularStrength);
            writer.WriteSingle(asset.EmissiveStrength);
        }

        /// <summary>
        /// Reads one PS2-owned cooked material payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the payload.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        /// <returns>Deserialized PS2 material asset.</returns>
        static Ps2MaterialAsset ReadPs2MaterialAsset(EngineBinaryReader reader, byte version) {
            Ps2MaterialAsset asset = new Ps2MaterialAsset();
            ReadAssetIdentity(reader, asset, version);
            asset.RendererFamilyId = reader.ReadString();
            asset.LightingMode = (Ps2MaterialLightingMode)reader.ReadInt32();
            asset.AlphaMode = (Ps2MaterialAlphaMode)reader.ReadInt32();
            asset.RenderClass = (Ps2RenderClass)reader.ReadInt32();
            asset.BaseColorR = reader.ReadByte();
            asset.BaseColorG = reader.ReadByte();
            asset.BaseColorB = reader.ReadByte();
            asset.BaseColorA = reader.ReadByte();
            asset.TextureRelativePath = reader.ReadString();
            asset.DoubleSided = reader.ReadByte() != 0;
            asset.CastShadows = reader.ReadByte() != 0;
            asset.UseVertexColor = reader.ReadByte() != 0;
            asset.ExpensiveModeAllowed = reader.ReadByte() != 0;
            asset.Roughness = reader.ReadSingle();
            asset.SpecularStrength = reader.ReadSingle();
            asset.EmissiveStrength = reader.ReadSingle();
            return asset;
        }

        /// <summary>
        /// Writes one PS2-owned cooked texture payload.
        /// </summary>
        /// <param name="writer">Destination writer for the payload.</param>
        /// <param name="asset">PS2 texture asset to serialize.</param>
        static void WritePs2TextureAsset(EngineBinaryWriter writer, Ps2TextureAsset asset) {
            WriteAssetIdentity(writer, asset);
            writer.WriteUInt16(asset.Width);
            writer.WriteUInt16(asset.Height);
            writer.WriteByte((byte)asset.Format);
            writer.WriteByte((byte)asset.PixelStorageMode);
            writer.WriteByte((byte)asset.ClutPixelStorageMode);
            writer.WriteByte((byte)asset.AlphaMode);
            writer.WriteByteArray(asset.PixelData);
            writer.WriteByteArray(asset.PaletteData);
        }

        /// <summary>
        /// Reads one PS2-owned cooked texture payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the payload.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        /// <returns>Deserialized PS2 texture asset.</returns>
        static Ps2TextureAsset ReadPs2TextureAsset(EngineBinaryReader reader, byte version) {
            Ps2TextureAsset asset = new Ps2TextureAsset();
            ReadAssetIdentity(reader, asset, version);
            asset.Width = reader.ReadUInt16();
            asset.Height = reader.ReadUInt16();
            asset.Format = (Ps2TextureFormat)reader.ReadByte();
            if (version >= TextureStorageMetadataVersion) {
                asset.PixelStorageMode = (Ps2TexturePixelStorageMode)reader.ReadByte();
                asset.ClutPixelStorageMode = (Ps2TexturePixelStorageMode)reader.ReadByte();
            } else {
                asset.PixelStorageMode = ResolveLegacyTexturePixelStorageMode(asset.Format);
                asset.ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32;
            }
            asset.AlphaMode = (Ps2TextureAlphaMode)reader.ReadByte();
            asset.PixelData = reader.ReadByteArray();
            asset.PaletteData = reader.ReadByteArray();
            return asset;
        }

        /// <summary>
        /// Resolves the implicit pixel-storage mode used by legacy PS2 texture payloads that predate explicit storage metadata.
        /// </summary>
        /// <param name="format">Legacy PS2 texture format code.</param>
        /// <returns>Implicit GS pixel storage mode for the legacy payload.</returns>
        static Ps2TexturePixelStorageMode ResolveLegacyTexturePixelStorageMode(Ps2TextureFormat format) {
            if (format == Ps2TextureFormat.Rgba32) {
                return Ps2TexturePixelStorageMode.PsmCt32;
            } else if (format == Ps2TextureFormat.Indexed8) {
                return Ps2TexturePixelStorageMode.PsmT8;
            } else if (format == Ps2TextureFormat.Indexed4) {
                return Ps2TexturePixelStorageMode.PsmT4;
            }

            throw new InvalidOperationException($"Unsupported legacy PS2 texture format '{format}'.");
        }

        /// <summary>
        /// Writes one three-component floating-point value inside a serialized PS2 model payload array.
        /// </summary>
        /// <param name="writer">Destination writer for the current array element.</param>
        /// <param name="value">Three-component value to serialize.</param>
        static void WriteFloat3Value(EngineBinaryWriter writer, float3 value) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteFloat3(value);
        }

        /// <summary>
        /// Writes one two-component floating-point value inside a serialized PS2 model payload array.
        /// </summary>
        /// <param name="writer">Destination writer for the current array element.</param>
        /// <param name="value">Two-component value to serialize.</param>
        static void WriteFloat2Value(EngineBinaryWriter writer, float2 value) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteFloat2(value);
        }

        /// <summary>
        /// Writes one 16-bit index value inside a serialized PS2 model payload array.
        /// </summary>
        /// <param name="writer">Destination writer for the current array element.</param>
        /// <param name="value">16-bit index to serialize.</param>
        static void WriteUInt16Value(EngineBinaryWriter writer, ushort value) {
            if (writer == null) {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteUInt16(value);
        }

        /// <summary>
        /// Reads one three-component floating-point value from a serialized PS2 model payload array.
        /// </summary>
        /// <param name="reader">Source reader positioned at the current array element.</param>
        /// <returns>Decoded three-component value.</returns>
        static float3 ReadFloat3Value(EngineBinaryReader reader) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            }

            return reader.ReadFloat3();
        }

        /// <summary>
        /// Reads one two-component floating-point value from a serialized PS2 model payload array.
        /// </summary>
        /// <param name="reader">Source reader positioned at the current array element.</param>
        /// <returns>Decoded two-component value.</returns>
        static float2 ReadFloat2Value(EngineBinaryReader reader) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            }

            return reader.ReadFloat2();
        }

        /// <summary>
        /// Reads one 16-bit index value from a serialized PS2 model payload array.
        /// </summary>
        /// <param name="reader">Source reader positioned at the current array element.</param>
        /// <returns>Decoded 16-bit index value.</returns>
        static ushort ReadUInt16Value(EngineBinaryReader reader) {
            if (reader == null) {
                throw new ArgumentNullException(nameof(reader));
            }

            return reader.ReadUInt16();
        }

        /// <summary>
        /// Writes one PS2-owned cooked model payload.
        /// </summary>
        /// <param name="writer">Destination writer for the payload.</param>
        /// <param name="asset">PS2 model asset to serialize.</param>
        static void WritePs2ModelAsset(EngineBinaryWriter writer, Ps2ModelAsset asset) {
            WriteAssetIdentity(writer, asset);
            writer.WriteArray(asset.Positions, WriteFloat3Value);
            writer.WriteArray(asset.Normals, WriteFloat3Value);
            writer.WriteArray(asset.TexCoords, WriteFloat2Value);
            writer.WriteArray(asset.Indices16, WriteUInt16Value);
            writer.WriteByteArray(asset.PackedMeshBytes);
        }

        /// <summary>
        /// Reads one PS2-owned cooked model payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the payload.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        /// <returns>Deserialized PS2 model asset.</returns>
        static Ps2ModelAsset ReadPs2ModelAsset(EngineBinaryReader reader, byte version) {
            Ps2ModelAsset asset = new Ps2ModelAsset();
            ReadAssetIdentity(reader, asset, version);
            asset.Positions = reader.ReadArray(ReadFloat3Value);
            asset.Normals = reader.ReadArray(ReadFloat3Value);
            asset.TexCoords = reader.ReadArray(ReadFloat2Value);
            asset.Indices16 = reader.ReadArray(ReadUInt16Value);
            asset.PackedMeshBytes = reader.ReadByteArray();
            return asset;
        }
    }
}
