namespace helengine.ps2.builder;

/// <summary>
/// Stores one PS2-specific texture cook settings payload carried through platform cook work items.
/// </summary>
public sealed class Ps2TextureCookSettings {
    /// <summary>
    /// Gets or sets the maximum allowed dimension in pixels, or zero when uncapped.
    /// </summary>
    public int MaxResolution { get; set; }

    /// <summary>
    /// Gets or sets the PS2-native runtime texture format the builder should produce.
    /// </summary>
    public Ps2TextureFormat Format { get; set; }

    /// <summary>
    /// Gets or sets the PS2-native alpha storage behavior the builder should produce.
    /// </summary>
    public Ps2TextureAlphaMode AlphaMode { get; set; }
}
