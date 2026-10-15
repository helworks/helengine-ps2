using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 platform definition keeps the expected codegen contract defaults.
/// </summary>
public sealed class Ps2PlatformDefinitionFactoryTests {
    /// <summary>
    /// Ensures the default PS2 codegen profile forwards the System.Numerics remaps required by the generated BEPU runtime.
    /// </summary>
    [Fact]
    public void Create_WhenBuildingDefaultPs2CodegenProfile_DeclaresSystemNumericsTypeRemaps() {
        PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();
        PlatformCodegenProfileDefinition codegenProfile = Assert.Single(definition.CodegenProfiles, profile => profile.ProfileId == "default");
        PlatformSettingDefinition typeRemapSetting = Assert.Single(
            codegenProfile.Settings,
            setting => string.Equals(setting.SettingId, "type-remaps", StringComparison.Ordinal));

        Assert.Equal(
            "System.Numerics.Vector2=helengine.float2|System.Numerics.Vector3=helengine.float3|System.Numerics.Vector4=helengine.float4|System.Numerics.Quaternion=helengine.float4",
            typeRemapSetting.DefaultValue);
    }

    /// <summary>
    /// Ensures the default PS2 codegen profile enables the generated custom file-system hook that remaps logical packaged paths onto disc-layout runtime paths.
    /// </summary>
    [Fact]
    public void Create_WhenBuildingDefaultPs2CodegenProfile_DeclaresCustomFileSystemDefaults() {
        PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();
        PlatformCodegenProfileDefinition codegenProfile = Assert.Single(definition.CodegenProfiles, profile => profile.ProfileId == "default");
        PlatformSettingDefinition headerSetting = Assert.Single(
            codegenProfile.Settings,
            setting => string.Equals(setting.SettingId, "native-file-system-header", StringComparison.Ordinal));
        PlatformSettingDefinition typeSetting = Assert.Single(
            codegenProfile.Settings,
            setting => string.Equals(setting.SettingId, "native-file-system-type", StringComparison.Ordinal));

        Assert.Equal("\"platform/ps2/Ps2DiscFileSystem.hpp\"", headerSetting.DefaultValue);
        Assert.Equal("helengine::ps2::Ps2DiscFileSystem", typeSetting.DefaultValue);
    }

    /// <summary>
    /// Ensures the PS2 Play capability declares the platform identity the PS2 native runtime reports, so the desktop
    /// player reports <c>ps2</c> / <c>1.0.0</c> to the core like the console does.
    /// </summary>
    [Fact]
    public void Create_DeclaresPs2RuntimePlatformIdentityForPlay() {
        PlatformPlayerCapability capability = Ps2PlatformDefinitionFactory.Create().PlayerCapability;

        Assert.True(capability.SupportsPlay);
        Assert.Equal("ps2", capability.RuntimePlatformName);
        Assert.Equal("1.0.0", capability.RuntimePlatformVersion);
    }

    /// <summary>
    /// The PS2 offers exactly the 12 NTSC and PAL modes, selected by a graphics choice whose values equal the capability
    /// ids and default to today's 640x448 interlaced NTSC mode.
    /// </summary>
    [Fact]
    public void Create_DeclaresTheTwelvePs2VideoModes() {
        PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();
        string[] expectedIds = [
            "ntsc-640x448i", "ntsc-512x448i", "ntsc-640x224p", "ntsc-512x224p", "ntsc-320x224p", "ntsc-256x224p",
            "pal-640x512i", "pal-512x512i", "pal-640x256p", "pal-512x256p", "pal-320x256p", "pal-256x256p"
        ];

        Assert.Equal(expectedIds, definition.VideoModeCapability.GetModeIds());
        foreach (PlatformGraphicsProfileDefinition profile in definition.GraphicsProfiles) {
            PlatformSettingDefinition setting = Assert.Single(profile.Settings, candidate => candidate.SettingId == "video-mode");
            Assert.Equal(expectedIds, setting.AllowedValues);
            Assert.Equal("ntsc-640x448i", setting.DefaultValue);
            Assert.DoesNotContain(profile.Settings, candidate => candidate.SettingId == "default-width" || candidate.SettingId == "default-height");
        }
    }

    /// <summary>
    /// Every mode resolves to its size, scan, rates and the display aspect gsKit really shows it at: 4:3 for the 640- and
    /// 512-wide modes, 3:2 for the 320-wide modes and 22:15 for the 256-wide modes (integer MagH + 1 = floor(2880 / W)).
    /// The pixel aspect is pinned as an exact fraction, <c>(MagH + 1) * H / 1920</c>.
    /// </summary>
    [Theory]
    [InlineData("ntsc-640x448i", 640, 448, false, 59.94d, 60d, 4, 3, 14, 15)]
    [InlineData("ntsc-512x448i", 512, 448, false, 59.94d, 60d, 4, 3, 7, 6)]
    [InlineData("ntsc-640x224p", 640, 224, true, 59.94d, 60d, 4, 3, 7, 15)]
    [InlineData("ntsc-512x224p", 512, 224, true, 59.94d, 60d, 4, 3, 7, 12)]
    [InlineData("ntsc-320x224p", 320, 224, true, 59.94d, 60d, 3, 2, 21, 20)]
    [InlineData("ntsc-256x224p", 256, 224, true, 59.94d, 60d, 22, 15, 77, 60)]
    [InlineData("pal-640x512i", 640, 512, false, 50d, 50d, 4, 3, 16, 15)]
    [InlineData("pal-512x512i", 512, 512, false, 50d, 50d, 4, 3, 4, 3)]
    [InlineData("pal-640x256p", 640, 256, true, 50d, 50d, 4, 3, 8, 15)]
    [InlineData("pal-512x256p", 512, 256, true, 50d, 50d, 4, 3, 2, 3)]
    [InlineData("pal-320x256p", 320, 256, true, 50d, 50d, 3, 2, 6, 5)]
    [InlineData("pal-256x256p", 256, 256, true, 50d, 50d, 22, 15, 22, 15)]
    public void VideoModes_ResolveEveryId(string id, int width, int height, bool progressive, double refresh, double playerRate, int displayAspectWidth, int displayAspectHeight, int pixelAspectNumerator, int pixelAspectDenominator) {
        PlatformVideoMode mode = Ps2PlatformDefinitionFactory.Create().VideoModeCapability.Resolve(id, "ps2");

        Assert.Equal(width, mode.Width);
        Assert.Equal(height, mode.Height);
        Assert.Equal(progressive, mode.IsProgressive);
        Assert.Equal(refresh, mode.RefreshRateHz);
        Assert.Equal(playerRate, mode.PlayerFrameRate);
        Assert.Equal(displayAspectWidth, mode.DisplayAspectWidth);
        Assert.Equal(displayAspectHeight, mode.DisplayAspectHeight);
        Assert.Equal((double)pixelAspectNumerator / pixelAspectDenominator, mode.PixelAspect, 12);
        Assert.Equal(id.StartsWith("pal-", StringComparison.Ordinal), Ps2VideoModes.IsPal(mode));
    }

    /// <summary>An unknown mode id is rejected naming it.</summary>
    [Fact]
    public void VideoModes_RejectUnknownId() {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Ps2PlatformDefinitionFactory.Create().VideoModeCapability.Resolve("ntsc-640x480p", "ps2"));

        Assert.Contains("ntsc-640x480p", exception.Message);
    }

    /// <summary>
    /// The region test refuses a missing mode, and refuses a mode whose refresh is neither PS2 rate instead of calling it
    /// NTSC, so nothing downstream (boot record, SYSTEM.CNF) can name the wrong region.
    /// </summary>
    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData(60d, typeof(ArgumentException))]
    public void IsPal_RefusesAMissingOrForeignMode(double? refreshRateHz, Type expectedException) {
        PlatformVideoMode mode = refreshRateHz == null
            ? null
            : new PlatformVideoMode("foreign-640x480p", 640, 480, true, refreshRateHz.Value, 60d, 4, 3);

        ArgumentException exception = (ArgumentException)Assert.Throws(expectedException, () => Ps2VideoModes.IsPal(mode));

        Assert.Equal("mode", exception.ParamName);
        if (mode != null) {
            Assert.Contains("foreign-640x480p", exception.Message);
        }
    }

    /// <summary>Play follows the project's mode by default and takes its frame rate from the mode.</summary>
    [Fact]
    public void Create_PlayVideoModeDefaultsToProject() {
        PlatformPlayerCapability capability = Ps2PlatformDefinitionFactory.Create().PlayerCapability;

        PlatformSettingDefinition playSetting = capability.FindPlaySetting("video-mode");
        Assert.Equal("project", playSetting.DefaultValue);
        Assert.Equal("project", playSetting.AllowedValues[0]);
        Assert.Equal(13, playSetting.AllowedValues.Length);
        Assert.True(capability.FrameRateFromVideoMode);
    }
}
