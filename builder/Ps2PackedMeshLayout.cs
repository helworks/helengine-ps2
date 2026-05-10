namespace helengine.ps2.builder;

/// <summary>
/// Defines the first-milestone qword-aligned packed mesh payload contract used by the PS2 VU opaque renderer path.
/// </summary>
public static class Ps2PackedMeshLayout {
    /// <summary>
    /// Stable file extension for cooked PS2 packed mesh payloads.
    /// </summary>
    public const string PackedMeshExtension = ".vup";

    /// <summary>
    /// Stable payload version for the first packed PS2 mesh layout.
    /// </summary>
    public const byte Version = 1;

    /// <summary>
    /// Size of one PS2 qword in bytes.
    /// </summary>
    public const int QwordSize = 16;
}
