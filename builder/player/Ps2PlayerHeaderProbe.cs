namespace helengine.ps2.builder;

/// <summary>
/// Distinguishes PS2-owned cooked payloads from generic engine payloads in the PS2 staging tree by inspecting the HELE
/// header format id, so the desktop player's content processors can pick the matching decoder.
/// </summary>
public static class Ps2PlayerHeaderProbe {
    /// <summary>
    /// Reports whether the seekable stream starts with a HELE header carrying the PS2-owned format id. The stream position
    /// is restored before returning, so the caller can deserialize from the same stream afterwards.
    /// </summary>
    /// <param name="stream">Seekable stream positioned at the start of one cooked payload.</param>
    /// <returns>True when the payload is a PS2-owned cooked asset; false for generic payloads or streams shorter than a header.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not seekable; use <see cref="CopyToSeekableStream"/> first.</exception>
    public static bool IsPs2Asset(Stream stream) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        } else if (!stream.CanSeek) {
            throw new ArgumentException("The PS2 header probe requires a seekable stream.", nameof(stream));
        }

        long startPosition = stream.Position;
        bool hasHeader = EngineBinaryHeaderSerializer.TryRead(stream, out EngineBinaryHeader header);
        stream.Position = startPosition;
        return hasHeader && header.FormatId == Ps2AssetBinarySerializer.FormatId;
    }

    /// <summary>
    /// Copies the remaining content of a non-seekable stream into a new seekable memory stream positioned at its start.
    /// </summary>
    /// <param name="stream">Non-seekable source stream.</param>
    /// <returns>Seekable copy owned by the caller.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is null.</exception>
    public static MemoryStream CopyToSeekableStream(Stream stream) {
        if (stream == null) {
            throw new ArgumentNullException(nameof(stream));
        }

        MemoryStream copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }
}
