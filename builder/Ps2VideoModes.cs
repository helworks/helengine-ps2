using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

/// <summary>
/// The PS2 video modes: NTSC (59.94 Hz, Play paced at 60) and PAL (50 Hz) framebuffers that the GS renders into and the
/// PCRTC magnifies on output. A single id carries region, size and scan, so impossible combinations cannot be expressed.
/// </summary>
public static class Ps2VideoModes {
    /// <summary>Graphics setting (and Play setting) id that selects the mode.</summary>
    public const string SettingId = "video-mode";

    /// <summary>Default mode: today's 640x448 interlaced NTSC output.</summary>
    public const string DefaultModeId = "ntsc-640x448i";

    /// <summary>Refresh of NTSC output.</summary>
    const double NtscRefreshHz = 59.94d;

    /// <summary>Desktop Play frame rate for NTSC modes (kept at 60 so it divides desktop refresh rates evenly).</summary>
    const double NtscPlayerFrameRate = 60d;

    /// <summary>Refresh of PAL output and its Play frame rate.</summary>
    const double PalRate = 50d;

    /// <summary>
    /// Width term of the display aspect. The PS2 runtime shows every mode 4:3 (<c>GS_ASPECT_4_3</c>), so the TV stretches
    /// each framebuffer to that shape and its pixels are not square unless the framebuffer is itself 4:3.
    /// </summary>
    const int DisplayAspectWidth = 4;

    /// <summary>Height term of the 4:3 display aspect every PS2 mode is shown at.</summary>
    const int DisplayAspectHeight = 3;

    /// <summary>
    /// Creates the capability listing all 12 modes in display order.
    /// </summary>
    /// <returns>New PS2 video-mode capability.</returns>
    public static PlatformVideoModeCapability CreateCapability() {
        return new PlatformVideoModeCapability(SettingId, [
            new PlatformVideoMode("ntsc-640x448i", 640, 448, false, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("ntsc-512x448i", 512, 448, false, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("ntsc-640x224p", 640, 224, true, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("ntsc-512x224p", 512, 224, true, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("ntsc-320x224p", 320, 224, true, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("ntsc-256x224p", 256, 224, true, NtscRefreshHz, NtscPlayerFrameRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-640x512i", 640, 512, false, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-512x512i", 512, 512, false, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-640x256p", 640, 256, true, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-512x256p", 512, 256, true, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-320x256p", 320, 256, true, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight),
            new PlatformVideoMode("pal-256x256p", 256, 256, true, PalRate, PalRate, DisplayAspectWidth, DisplayAspectHeight)
        ]);
    }

    /// <summary>
    /// Creates the graphics-profile choice that selects the console's mode.
    /// </summary>
    /// <returns>New setting declaration.</returns>
    public static PlatformSettingDefinition CreateGraphicsSetting() {
        return new PlatformSettingDefinition(SettingId, "Video Mode", PlatformSettingKind.Choice, DefaultModeId, true, CreateCapability().GetModeIds());
    }

    /// <summary>
    /// Creates the Play setting that chooses how Play renders: the project's mode (default) or an explicit one.
    /// </summary>
    /// <returns>New Play setting declaration.</returns>
    public static PlatformSettingDefinition CreatePlaySetting() {
        string[] modeIds = CreateCapability().GetModeIds();
        string[] values = new string[modeIds.Length + 1];
        values[0] = PlayerCommonPlaySettings.ProjectValue;
        Array.Copy(modeIds, 0, values, 1, modeIds.Length);
        return new PlatformSettingDefinition(SettingId, "Video Mode", PlatformSettingKind.Choice, PlayerCommonPlaySettings.ProjectValue, true, values);
    }

    /// <summary>
    /// Reports whether a PS2 mode outputs PAL (50 Hz) rather than NTSC.
    /// </summary>
    /// <param name="mode">PS2 video mode.</param>
    /// <returns>True for PAL.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mode"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the refresh is neither PS2 rate.</exception>
    public static bool IsPal(PlatformVideoMode mode) {
        if (mode == null) {
            throw new ArgumentNullException(nameof(mode));
        } else if (mode.RefreshRateHz == PalRate) {
            return true;
        } else if (mode.RefreshRateHz == NtscRefreshHz) {
            return false;
        }

        throw new ArgumentException($"Video mode '{mode.Id}' has refresh {mode.RefreshRateHz}, which is neither PS2 NTSC nor PAL.", nameof(mode));
    }
}
