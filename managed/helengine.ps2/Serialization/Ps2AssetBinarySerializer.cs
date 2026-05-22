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
        public const byte CurrentVersion = 1;

        /// <summary>
        /// Lowest PS2 asset binary version supported by this serializer.
        /// </summary>
        const byte LegacyVersion = 1;

        /// <summary>
        /// First PS2 asset version that stores runtime asset ids.
        /// </summary>
        const byte RuntimeAssetIdentityVersion = 1;

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
            } else if (asset is Ps2PackedModelAsset) {
                return Ps2AssetBinaryValueKind.Ps2PackedModelAsset;
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
            } else if (asset is Ps2PackedModelAsset ps2PackedModelAsset) {
                WritePs2PackedModelAsset(writer, ps2PackedModelAsset);
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
                case Ps2AssetBinaryValueKind.Ps2PackedModelAsset:
                    return ReadPs2PackedModelAsset(reader, version);
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
            asset.AlphaMode = (Ps2TextureAlphaMode)reader.ReadByte();
            asset.PixelData = reader.ReadByteArray();
            asset.PaletteData = reader.ReadByteArray();
            return asset;
        }

        /// <summary>
        /// Writes one PS2-owned packed-model sidecar payload.
        /// </summary>
        /// <param name="writer">Destination writer for the payload.</param>
        /// <param name="asset">Packed-model asset to serialize.</param>
        static void WritePs2PackedModelAsset(EngineBinaryWriter writer, Ps2PackedModelAsset asset) {
            WriteAssetIdentity(writer, asset);
            writer.WriteByteArray(asset.PackedMeshBytes);
        }

        /// <summary>
        /// Reads one PS2-owned packed-model sidecar payload.
        /// </summary>
        /// <param name="reader">Source reader positioned at the payload.</param>
        /// <param name="version">Serialized PS2 asset version.</param>
        /// <returns>Deserialized packed-model asset.</returns>
        static Ps2PackedModelAsset ReadPs2PackedModelAsset(EngineBinaryReader reader, byte version) {
            Ps2PackedModelAsset asset = new Ps2PackedModelAsset();
            ReadAssetIdentity(reader, asset, version);
            asset.PackedMeshBytes = reader.ReadByteArray();
            return asset;
        }
    }
}
