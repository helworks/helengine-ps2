using helengine.baseplatform.Definitions;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the display aspect gsKit really shows each PS2 framebuffer width at. For NTSC and PAL gsKit spans 2880 VCK and
/// magnifies by the integer <c>MagH + 1 = floor(2880 / W)</c>, so 640- and 512-wide modes cover the 2560-VCK 4:3 reference
/// while 320-wide modes cover 2880 VCK (3:2) and 256-wide modes 2816 VCK (22:15).
/// </summary>
public sealed class Ps2GsDisplayAspectTests {
    /// <summary>Each PS2 width gets gsKit's integer magnification and the reduced display aspect it produces.</summary>
    [Theory]
    [InlineData(640, 4, 4, 3)]
    [InlineData(512, 5, 4, 3)]
    [InlineData(320, 9, 3, 2)]
    [InlineData(256, 11, 22, 15)]
    public void Resolve_FollowsGsKitMagnification(int width, int expectedMagnification, int expectedAspectWidth, int expectedAspectHeight) {
        Assert.Equal(expectedMagnification, Ps2GsDisplayAspect.ResolveHorizontalMagnification(width));
        Assert.Equal(expectedAspectWidth, Ps2GsDisplayAspect.ResolveDisplayAspectWidth(width));
        Assert.Equal(expectedAspectHeight, Ps2GsDisplayAspect.ResolveDisplayAspectHeight(width));
    }

    /// <summary>
    /// The aspect is reduced from <c>4 (MagH+1) W : 3 * 2560</c> for widths gsKit does not magnify to exactly 2560 VCK,
    /// for example 600 (MagH+1 = 4, 2400 VCK, 5:4).
    /// </summary>
    [Fact]
    public void Resolve_ReducesTheRatioForAnyWidth() {
        Assert.Equal(4, Ps2GsDisplayAspect.ResolveHorizontalMagnification(600));
        Assert.Equal(5, Ps2GsDisplayAspect.ResolveDisplayAspectWidth(600));
        Assert.Equal(4, Ps2GsDisplayAspect.ResolveDisplayAspectHeight(600));
    }

    /// <summary>
    /// A width gsKit cannot magnify into the 2880-VCK span (wider than 2880, or so narrow that MAGH would exceed its 4-bit
    /// field) is refused naming the width parameter.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2881)]
    [InlineData(169)]
    public void Resolve_RejectsAWidthGsKitCannotMagnify(int width) {
        Assert.Equal("width", Assert.Throws<ArgumentOutOfRangeException>(() => Ps2GsDisplayAspect.ResolveHorizontalMagnification(width)).ParamName);
        Assert.Equal("width", Assert.Throws<ArgumentOutOfRangeException>(() => Ps2GsDisplayAspect.ResolveDisplayAspectWidth(width)).ParamName);
        Assert.Equal("width", Assert.Throws<ArgumentOutOfRangeException>(() => Ps2GsDisplayAspect.ResolveDisplayAspectHeight(width)).ParamName);
    }

    /// <summary>The widest MAGH the 4-bit field holds (15, so 16x) is accepted at width 170.</summary>
    [Fact]
    public void Resolve_AcceptsTheLargestFourBitMagnification() {
        Assert.Equal(16, Ps2GsDisplayAspect.ResolveHorizontalMagnification(170));
    }

    /// <summary>A mode created through the helper carries its fields and the gsKit-derived display aspect.</summary>
    [Fact]
    public void CreateMode_DerivesTheDisplayAspectFromTheWidth() {
        PlatformVideoMode mode = Ps2GsDisplayAspect.CreateMode("ntsc-256x224p", 256, 224, true, 59.94d, 60d);

        Assert.Equal("ntsc-256x224p", mode.Id);
        Assert.Equal(256, mode.Width);
        Assert.Equal(224, mode.Height);
        Assert.True(mode.IsProgressive);
        Assert.Equal(59.94d, mode.RefreshRateHz);
        Assert.Equal(60d, mode.PlayerFrameRate);
        Assert.Equal(22, mode.DisplayAspectWidth);
        Assert.Equal(15, mode.DisplayAspectHeight);
        Assert.Equal(77d / 60d, mode.PixelAspect, 12);
    }
}
