namespace helengine.ps2.builder;

/// <summary>
/// Single source of truth for the platform identity the PS2 runtime reports to the core through <c>PlatformInfo</c>.
/// The PS2 Play capability declares these values so the desktop player reports the same identity, and
/// <see cref="Ps2RuntimePlatformInfoHeaderWriter"/> writes them into the generated native runtime header that the native
/// boot host reads, so the console and the player cannot drift apart.
/// </summary>
public static class Ps2RuntimePlatformIdentity {
    /// <summary>
    /// Platform name the PS2 runtime reports to the core; game code branches on it through <c>PlatformInfo.Name</c>.
    /// </summary>
    public const string Name = "ps2";

    /// <summary>
    /// Platform version the PS2 runtime reports to the core through <c>PlatformInfo.Version</c>.
    /// </summary>
    public const string Version = "1.0.0";
}
