using System.Buffers.Binary;
using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

/// <summary>
/// The small versioned file the PS2 builder stages at the disc root so the native boot host initialises gsKit with the
/// project's video mode before the first frame. Layout (little-endian, 16 bytes): magic <c>HVMR</c>, u16 version 1,
/// u8 region (0 NTSC, 1 PAL), u8 scan (0 interlaced, 1 progressive), u16 width, u16 height, 4 zero bytes. The native
/// reader is <c>src/platform/ps2/Ps2VideoModeBootRecordReader.cpp</c>; both sides accept only the 12 PS2 modes.
/// </summary>
public static class Ps2VideoModeBootRecord {
    /// <summary>Staging-root-relative logical path of the record.</summary>
    public const string LogicalPath = "vidmode.bin";

    /// <summary>Path the native boot host opens (must equal the disc path the resolver gives <see cref="LogicalPath"/>).</summary>
    public const string RuntimePath = "cdrom0:\\VIDMODE.BIN;1";

    /// <summary>Record size in bytes.</summary>
    public const int Size = 16;

    /// <summary>The only record version this builder writes and the runtime reads.</summary>
    public const ushort CurrentVersion = 1;

    /// <summary>Magic bytes at offset 0.</summary>
    static readonly byte[] Magic = [(byte)'H', (byte)'V', (byte)'M', (byte)'R'];

    /// <summary>
    /// Encodes one PS2 mode.
    /// </summary>
    /// <param name="mode">PS2 video mode.</param>
    /// <returns>The 16-byte record.</returns>
    /// <exception cref="ArgumentException">Thrown when the mode is not shown 4:3.</exception>
    public static byte[] Encode(PlatformVideoMode mode) {
        if (mode == null) {
            throw new ArgumentNullException(nameof(mode));
        } else if (mode.DisplayAspectWidth * 3 != mode.DisplayAspectHeight * 4) {
            throw new ArgumentException($"Video mode '{mode.Id}' is shown {mode.DisplayAspectWidth}:{mode.DisplayAspectHeight}, but the PS2 runtime shows every mode 4:3 (GS_ASPECT_4_3).", nameof(mode));
        }

        byte[] bytes = new byte[Size];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), CurrentVersion);
        bytes[6] = Ps2VideoModes.IsPal(mode) ? (byte)1 : (byte)0;
        bytes[7] = mode.IsProgressive ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), checked((ushort)mode.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), checked((ushort)mode.Height));
        return bytes;
    }

    /// <summary>
    /// Decodes and validates one record.
    /// </summary>
    /// <param name="bytes">Record bytes.</param>
    /// <returns>The decoded fields.</returns>
    /// <exception cref="InvalidDataException">Thrown for a wrong length, magic, version, flag, padding or a size no PS2 mode has.</exception>
    public static Ps2VideoModeBootRecordContents Decode(byte[] bytes) {
        if (bytes == null || bytes.Length != Size) {
            throw new InvalidDataException($"A PS2 video-mode boot record is exactly {Size} bytes.");
        } else if (!bytes.AsSpan(0, 4).SequenceEqual(Magic)) {
            throw new InvalidDataException("The PS2 video-mode boot record has the wrong magic.");
        } else if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) != CurrentVersion) {
            throw new InvalidDataException($"The PS2 video-mode boot record is not version {CurrentVersion}.");
        } else if (bytes[6] > 1 || bytes[7] > 1) {
            throw new InvalidDataException("The PS2 video-mode boot record has an invalid region or scan flag.");
        } else if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) != 0u) {
            throw new InvalidDataException("The PS2 video-mode boot record's reserved bytes are not zero.");
        }

        Ps2VideoModeBootRecordContents contents = new Ps2VideoModeBootRecordContents(
            bytes[6] == 1,
            bytes[7] == 1,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10)));
        foreach (PlatformVideoMode mode in Ps2VideoModes.CreateCapability().Modes) {
            if (Ps2VideoModes.IsPal(mode) == contents.IsPal && mode.IsProgressive == contents.IsProgressive && mode.Width == contents.Width && mode.Height == contents.Height) {
                return contents;
            }
        }

        throw new InvalidDataException($"The PS2 video-mode boot record names {contents.Width}x{contents.Height}, which no PS2 mode produces.");
    }
}
