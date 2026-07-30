namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies that textured VU source batches fit entirely below the microprogram's GIF output buffer.
/// </summary>
public sealed class Ps2TexturedVuSourceCapacityTests {
    /// <summary>
    /// Ensures source batches remain below the GIF output buffer in the active VU double-buffer layout.
    /// </summary>
    [Fact]
    public void Ps2TexturedVuSourceBatches_StayBelowTheGifOutputBuffer() {
        string repositoryRootPath = GetRepositoryRootPath();
        string rendererSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));
        string packetBuilderSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string microprogramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 32u;", rendererSource, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 32u;", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI05, VI02, 0x00000015", microprogramSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI03, VI00, 0x00000100", microprogramSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI05, VI05, 0x00000007", microprogramSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>The absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
