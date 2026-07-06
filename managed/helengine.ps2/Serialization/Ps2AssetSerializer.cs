namespace helengine {
    /// <summary>
    /// Provides HELE serialization helpers for PS2-owned cooked runtime assets.
    /// </summary>
    public static class Ps2AssetSerializer {
        /// <summary>
        /// Serializes one PS2-owned asset into the supplied stream.
        /// </summary>
        /// <param name="stream">Destination stream for the encoded payload.</param>
        /// <param name="asset">PS2-owned asset to serialize.</param>
        public static void Serialize(Stream stream, Asset asset) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            } else if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }

            Ps2AssetBinarySerializer.Serialize(stream, asset);
        }

        /// <summary>
        /// Deserializes one PS2-owned asset from the supplied stream.
        /// </summary>
        /// <param name="stream">Source stream containing the encoded payload.</param>
        /// <returns>Deserialized PS2-owned asset instance.</returns>
        public static Asset Deserialize(Stream stream) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            }

            return Ps2AssetBinarySerializer.Deserialize(stream);
        }

        /// <summary>
        /// Serializes one PS2-owned asset into a new byte array.
        /// </summary>
        /// <param name="asset">PS2-owned asset to serialize.</param>
        /// <returns>Encoded PS2 asset bytes.</returns>
        public static byte[] SerializeToBytes(Asset asset) {
            if (asset == null) {
                throw new ArgumentNullException(nameof(asset));
            }

            using MemoryStream stream = new MemoryStream();
            Serialize(stream, asset);
            return stream.ToArray();
        }

        /// <summary>
        /// Deserializes one PS2-owned asset from a byte array.
        /// </summary>
        /// <param name="data">Encoded PS2 asset bytes.</param>
        /// <returns>Deserialized PS2-owned asset instance.</returns>
        public static Asset DeserializeFromBytes(byte[] data) {
            if (data == null) {
                throw new ArgumentNullException(nameof(data));
            }

            using MemoryStream stream = new MemoryStream(data, false);
            return Deserialize(stream);
        }
    }
}
