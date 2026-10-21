using helengine.baseplatform.Builders;
using helengine.baseplatform.Definitions;

namespace helengine.ps2.builder.tests;

/// <summary>Verifies that the layout cooker uses the same mode-dependent dimensions and pixel aspect as native PS2 output.</summary>
public sealed class Ps2ViewportCookTests {
    /// <summary>Every declared GS mode must produce a matching cook target, including low-resolution and PAL modes.</summary>
    [Fact]
    public void EveryVideoMode_ProducesMatchingLayoutTarget() {
        Ps2PlatformAssetBuilder builder = new Ps2PlatformAssetBuilder();
        Assert.True(builder.Definition.RuntimeGenerationContract.RequiresCookedViewportLayout);
        foreach (PlatformVideoMode mode in Ps2VideoModes.CreateCapability().Modes) {
            Dictionary<string, string> options = new Dictionary<string, string> { [Ps2VideoModes.SettingId] = mode.Id };
            PlatformLayoutTarget target = builder.ResolveLayoutTarget(new PlatformLayoutTargetRequest("release", "gs", options));
            Assert.Equal(mode.Width, target.Width);
            Assert.Equal(mode.Height, target.Height);
            Assert.Equal(mode.PixelAspect, target.PixelAspect);
        }
    }
    /// <summary>Missing or invalid mode selections cannot silently cook for a guessed framebuffer.</summary>
    [Fact]
    public void UnknownVideoMode_IsRejected() {
        Ps2PlatformAssetBuilder builder = new Ps2PlatformAssetBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.ResolveLayoutTarget(
            new PlatformLayoutTargetRequest("release", "gs", new Dictionary<string, string>())));
        Assert.Throws<InvalidOperationException>(() => builder.ResolveLayoutTarget(
            new PlatformLayoutTargetRequest("release", "gs", new Dictionary<string, string> { [Ps2VideoModes.SettingId] = "invalid" })));
    }
}
