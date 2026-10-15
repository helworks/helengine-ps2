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
    /// Creates the capability listing all 12 modes in display order. Each mode's display aspect comes from
    /// <see cref="Ps2GsDisplayAspect"/>: the shape gsKit's integer horizontal magnification really gives the picture
    /// (4:3 for 640- and 512-wide modes, 3:2 for 320-wide modes, 22:15 for 256-wide modes).
    /// </summary>
    /// <returns>New PS2 video-mode capability.</returns>
    public static PlatformVideoModeCapability CreateCapability() {
        return new PlatformVideoModeCapability(SettingId, [
            Ps2GsDisplayAspect.CreateMode("ntsc-640x448i", 640, 448, false, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("ntsc-512x448i", 512, 448, false, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("ntsc-640x224p", 640, 224, true, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("ntsc-512x224p", 512, 224, true, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("ntsc-320x224p", 320, 224, true, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("ntsc-256x224p", 256, 224, true, NtscRefreshHz, NtscPlayerFrameRate),
            Ps2GsDisplayAspect.CreateMode("pal-640x512i", 640, 512, false, PalRate, PalRate),
            Ps2GsDisplayAspect.CreateMode("pal-512x512i", 512, 512, false, PalRate, PalRate),
            Ps2GsDisplayAspect.CreateMode("pal-640x256p", 640, 256, true, PalRate, PalRate),
            Ps2GsDisplayAspect.CreateMode("pal-512x256p", 512, 256, true, PalRate, PalRate),
            Ps2GsDisplayAspect.CreateMode("pal-320x256p", 320, 256, true, PalRate, PalRate),
            Ps2GsDisplayAspect.CreateMode("pal-256x256p", 256, 256, true, PalRate, PalRate)
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
