namespace helengine {
    /// <summary>
    /// Writes the fixed HELE file header for PS2-owned cooked asset payloads.
    /// </summary>
    public static class Ps2EngineBinaryHeaderWriter {
        /// <summary>
        /// Writes the standardized HELE header to the supplied stream.
        /// </summary>
        /// <param name="stream">Destination stream for the header.</param>
        /// <param name="header">Header metadata to write.</param>
        public static void Write(Stream stream, EngineBinaryHeader header) {
            if (stream == null) {
                throw new ArgumentNullException(nameof(stream));
            } else if (header == null) {
                throw new ArgumentNullException(nameof(header));
            }

            ValidateEndianness(header.Endianness);
            using Ps2BinaryWriterLE writer = new Ps2BinaryWriterLE(stream);
            writer.WriteByte((byte)'H');
            writer.WriteByte((byte)'E');
            writer.WriteByte((byte)'L');
            writer.WriteByte((byte)'E');
            writer.WriteByte((byte)header.Endianness);
            writer.WriteByte(header.Version);
            writer.WriteUInt16(header.FormatId);
            writer.WriteUInt16(header.RecordKind);
            writer.WriteUInt16(header.ValueKind);
        }

        /// <summary>
        /// Validates that the payload endianness code is supported.
        /// </summary>
        /// <param name="endianness">Endianness code to validate.</param>
        static void ValidateEndianness(EngineBinaryEndianness endianness) {
            if (endianness != EngineBinaryEndianness.LittleEndian &&
                endianness != EngineBinaryEndianness.BigEndian) {
                throw new InvalidOperationException($"Unsupported binary payload endianness '{(byte)endianness}'.");
            }
        }
    }
}
