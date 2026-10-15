using System.Numerics;
using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder;

/// <summary>
/// Derives the display aspect a PS2 NTSC or PAL framebuffer is really shown at from gsKit's integer horizontal
/// magnification (gsKit 8ef73d05, <c>gsKit_set_buffer_attributes</c>). For <c>GS_MODE_NTSC</c> and <c>GS_MODE_PAL</c>
/// gsKit spans 2880 VCK and sets <c>MagH + 1 = floor(2880 / W)</c> with <c>MagV = 0</c>, so the picture is
/// <c>(MagH + 1) * W</c> VCK wide and always the mode's full height. The 640x448 picture, 2560 VCK wide, is the 4:3
/// reference; a picture of another width is shown at <c>4 (MagH + 1) W : 3 * 2560</c>. That gives 4:3 for 640- and
/// 512-wide modes, 3:2 for 320-wide modes (2880 VCK) and 22:15 for 256-wide modes (2816 VCK).
/// </summary>
public static class Ps2GsDisplayAspect {
    /// <summary>Width, in VCK, of the span gsKit magnifies NTSC and PAL framebuffers into (its <c>DW</c>).</summary>
    public const int GsKitNtscPalDisplayWidthVck = 2880;

    /// <summary>Width, in VCK, of the picture that counts as 4:3: the 640x448 framebuffer magnified 4x.</summary>
    public const int ReferenceDisplayWidthVck = 2560;

    /// <summary>Width term of the aspect the reference picture is shown at.</summary>
    public const int ReferenceAspectWidth = 4;

    /// <summary>Height term of the aspect the reference picture is shown at.</summary>
    public const int ReferenceAspectHeight = 3;

    /// <summary>Largest horizontal magnification the 4-bit DISPLAY.MAGH field holds (MAGH 15, so 16x).</summary>
    public const int MaximumHorizontalMagnification = 16;

    /// <summary>
    /// Creates a PS2 NTSC or PAL video mode whose display aspect is the one gsKit really shows its width at.
    /// </summary>
    /// <param name="id">Stable mode id stored in project settings.</param>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    /// <param name="isProgressive">True for progressive scan, false for interlaced.</param>
    /// <param name="refreshRateHz">Refresh the console outputs.</param>
    /// <param name="playerFrameRate">Frame rate the desktop Play player paces to.</param>
    /// <returns>The video mode, carrying the gsKit-derived display aspect.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when gsKit cannot magnify <paramref name="width"/> into its span.</exception>
    public static PlatformVideoMode CreateMode(string id, int width, int height, bool isProgressive, double refreshRateHz, double playerFrameRate) {
        return new PlatformVideoMode(id, width, height, isProgressive, refreshRateHz, playerFrameRate, ResolveDisplayAspectWidth(width), ResolveDisplayAspectHeight(width));
    }

    /// <summary>
    /// Resolves gsKit's integer horizontal magnification for a framebuffer width: <c>MagH + 1 = floor(2880 / W)</c>.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <returns>The magnification, <c>MagH + 1</c> (4 for 640, 5 for 512, 9 for 320, 11 for 256).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the width is wider than gsKit's span or so narrow that MAGH would not fit its 4-bit field.</exception>
    public static int ResolveHorizontalMagnification(int width) {
        if (width < 1 || width > GsKitNtscPalDisplayWidthVck) {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"A PS2 NTSC or PAL framebuffer width must be between 1 and {GsKitNtscPalDisplayWidthVck} pixels.");
        }

        int magnification = GsKitNtscPalDisplayWidthVck / width;
        if (magnification > MaximumHorizontalMagnification) {
            throw new ArgumentOutOfRangeException(nameof(width), width, $"A {width}-pixel framebuffer would need {magnification}x horizontal magnification, but DISPLAY.MAGH allows at most {MaximumHorizontalMagnification}x.");
        }

        return magnification;
    }

    /// <summary>
    /// Resolves the width term of the reduced display aspect <c>4 (MagH + 1) W : 3 * 2560</c>.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <returns>The reduced width term (4 for 640 and 512, 3 for 320, 22 for 256).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when gsKit cannot magnify <paramref name="width"/> into its span.</exception>
    public static int ResolveDisplayAspectWidth(int width) {
        int shownWidth = ReferenceAspectWidth * ResolveHorizontalMagnification(width) * width;
        int shownHeight = ReferenceAspectHeight * ReferenceDisplayWidthVck;
        return shownWidth / (int)BigInteger.GreatestCommonDivisor(shownWidth, shownHeight);
    }

    /// <summary>
    /// Resolves the height term of the reduced display aspect <c>4 (MagH + 1) W : 3 * 2560</c>.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <returns>The reduced height term (3 for 640 and 512, 2 for 320, 15 for 256).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when gsKit cannot magnify <paramref name="width"/> into its span.</exception>
    public static int ResolveDisplayAspectHeight(int width) {
        int shownWidth = ReferenceAspectWidth * ResolveHorizontalMagnification(width) * width;
        int shownHeight = ReferenceAspectHeight * ReferenceDisplayWidthVck;
        return shownHeight / (int)BigInteger.GreatestCommonDivisor(shownWidth, shownHeight);
    }
}
