namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that directional-light travel direction and direct-light exposure match the PS2 VU shading path.
/// </summary>
public sealed class Ps2DirectionalLightDirectionSourceTests {
    /// <summary>
    /// Confirms that the renderer negates the directional-light entity forward vector before passing it to lit material paths.
    /// </summary>
    [Fact]
    public void DirectionalLightUsesSurfaceToLightDirection() {
        const string sourcePath = "../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp";

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const ::float3 lightTravelDirection = ::float4::RotateVector(::float3(0.0f, 0.0f, -1.0f), parent->get_Orientation());", source);
        Assert.Contains("lightDirection = ::float3(-lightTravelDirection.X, -lightTravelDirection.Y, -lightTravelDirection.Z);", source);
    }

    /// <summary>
    /// Confirms that direct light preserves color headroom instead of saturating fully illuminated surfaces to white.
    /// </summary>
    [Fact]
    public void DirectLightPeakPreservesMaterialColorHeadroom() {
        const string packetBuilderPath = "../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp";
        const string microProgramPath = "../../../../src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm";

        string packetBuilderSource = File.ReadAllText(packetBuilderPath);
        string microProgramSource = File.ReadAllText(microProgramPath);

        Assert.Contains("constexpr double CpuLightingScale = 160.0;", packetBuilderSource);
        Assert.Contains("loi           0x43200000", microProgramSource);
    }
}
