namespace helengine.ps2.builder;

/// <summary>
/// The decoded fields of one PS2 video-mode boot record.
/// </summary>
public sealed class Ps2VideoModeBootRecordContents {
    /// <summary>
    /// Initializes the decoded fields.
    /// </summary>
    /// <param name="isPal">True for PAL output, false for NTSC.</param>
    /// <param name="isProgressive">True for progressive scan, false for interlaced.</param>
    /// <param name="width">Framebuffer width.</param>
    /// <param name="height">Framebuffer height.</param>
    public Ps2VideoModeBootRecordContents(bool isPal, bool isProgressive, int width, int height) {
        IsPal = isPal;
        IsProgressive = isProgressive;
        Width = width;
        Height = height;
    }

    /// <summary>Gets whether the output is PAL.</summary>
    public bool IsPal { get; }

    /// <summary>Gets whether the scan is progressive.</summary>
    public bool IsProgressive { get; }

    /// <summary>Gets the framebuffer width.</summary>
    public int Width { get; }

    /// <summary>Gets the framebuffer height.</summary>
    public int Height { get; }
}
