namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that textured VU input and GIF output use separate fixed memory regions.
/// </summary>
public sealed class Ps2TexturedVuMemoryLayoutSourceTests {
    /// <summary>
    /// Confirms that the textured microprogram reads fixed lower-memory input and writes GIF packets into upper memory.
    /// </summary>
    [Fact]
    public void TexturedVuMicroProgramSeparatesInputFromGifOutput() {
        const string microProgramPath = "../../../../src/platform/ps2/rendering/vu/programs/Ps2OpaqueTexturedDraw3D.vsm";

        string microProgramSource = File.ReadAllText(microProgramPath);

        Assert.Contains("iaddiu VI02, VI00, 0x00000000", microProgramSource);
        Assert.Contains("iaddiu VI03, VI00, 0x00000200", microProgramSource);
    }
}
