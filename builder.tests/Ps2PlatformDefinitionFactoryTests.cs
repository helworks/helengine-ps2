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
        PlatformCodegenProfileDefinition codegenProfile = Assert.Single(definition.CodegenProfiles);
        PlatformSettingDefinition typeRemapSetting = Assert.Single(
            codegenProfile.Settings,
            setting => string.Equals(setting.SettingId, "type-remaps", StringComparison.Ordinal));

        Assert.Equal(
            "System.Numerics.Vector2=helengine.float2|System.Numerics.Vector3=helengine.float3|System.Numerics.Vector4=helengine.float4|System.Numerics.Quaternion=helengine.float4",
            typeRemapSetting.DefaultValue);
    }
}
