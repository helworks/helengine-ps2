/// <summary>
/// Protects the PS2 solid-quad path used by loading overlays and translucent menu surfaces.
/// </summary>
public sealed class Ps2SolidQuadAlphaSourceTests {
    /// <summary>
    /// Ensures rounded-rectangle alpha uses the same source-over blend state as textured sprites.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDrawingSolidQuads_EnablesSourceOverAlphaBlending() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string source = File.ReadAllText(sourcePath);
        int drawSolidQuadStart = source.IndexOf("void DrawSolidQuad(", StringComparison.Ordinal);
        int drawTexturedQuadStart = source.IndexOf("void DrawTexturedQuad(", drawSolidQuadStart, StringComparison.Ordinal);

        Assert.True(drawSolidQuadStart >= 0, "Expected the PS2 solid-quad implementation.");
        Assert.True(drawTexturedQuadStart > drawSolidQuadStart, "Expected the textured-quad implementation after the solid-quad implementation.");
        string drawSolidQuadSource = source[drawSolidQuadStart..drawTexturedQuadStart];

        Assert.Contains("GS_SETREG_ALPHA(0, 1, 0, 1, 0)", drawSolidQuadSource, StringComparison.Ordinal);
        Assert.Contains("ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_ON;", drawSolidQuadSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the main repository root used by source-level native host tests.
    /// </summary>
    /// <returns>Absolute path of the PS2 repository under test.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
