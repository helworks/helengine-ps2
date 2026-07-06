using System.Security.Cryptography;
using System.Text;

namespace helengine {
    /// <summary>
    /// Generates deterministic numeric runtime asset identifiers for PS2-owned cooked assets without referencing helengine.files.
    /// </summary>
    public static class Ps2RuntimeAssetIdGenerator {
        /// <summary>
        /// Generates one deterministic non-zero runtime asset id from the supplied canonical key.
        /// </summary>
        /// <param name="canonicalKey">Canonical string identity for the cooked asset or packaged subresource.</param>
        /// <returns>Deterministic non-zero runtime asset id.</returns>
        public static ulong Generate(string canonicalKey) {
            if (string.IsNullOrWhiteSpace(canonicalKey)) {
                throw new ArgumentException("Canonical asset key must be provided.", nameof(canonicalKey));
            }

            string normalized = canonicalKey.Trim().ToLowerInvariant();
            byte[] keyBytes = Encoding.UTF8.GetBytes(normalized);
            for (int index = 0; index < keyBytes.Length; index++) {
                if (keyBytes[index] == (byte)'\\') {
                    keyBytes[index] = (byte)'/';
                }
            }
            byte[] hash = SHA256.HashData(keyBytes);
            ulong runtimeAssetId = ReadUInt64LittleEndian(hash, 0);
            return runtimeAssetId == 0ul ? 1ul : runtimeAssetId;
        }

        /// <summary>
        /// Reads one unsigned 64-bit integer from a byte buffer using little-endian ordering.
        /// </summary>
        /// <param name="buffer">Byte buffer containing the value.</param>
        /// <param name="offset">Start position of the value within the buffer.</param>
        /// <returns>Decoded unsigned 64-bit integer.</returns>
        static ulong ReadUInt64LittleEndian(byte[] buffer, int offset) {
            if (buffer == null) {
                throw new ArgumentNullException(nameof(buffer));
            } else if (offset < 0 || offset > buffer.Length - 8) {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return
                (ulong)buffer[offset] |
                ((ulong)buffer[offset + 1] << 8) |
                ((ulong)buffer[offset + 2] << 16) |
                ((ulong)buffer[offset + 3] << 24) |
                ((ulong)buffer[offset + 4] << 32) |
                ((ulong)buffer[offset + 5] << 40) |
                ((ulong)buffer[offset + 6] << 48) |
                ((ulong)buffer[offset + 7] << 56);
        }
    }
}
