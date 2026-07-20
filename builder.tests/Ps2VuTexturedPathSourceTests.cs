using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies that textured PS2 geometry remains on the validated VU submission path.
/// </summary>
public sealed class Ps2VuTexturedPathSourceTests {
    /// <summary>
    /// Ensures textured batches are not unconditionally redirected to the legacy CPU renderer.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenRenderingTexturedBatches_UsesVuPathByDefault() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render manager source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int renderOpaqueIndex = source.IndexOf("void Ps2RenderManager3D::RenderOpaqueWithVuPath(", StringComparison.Ordinal);
        Assert.True(renderOpaqueIndex >= 0, "Expected PS2 VU opaque render path.");
        string renderOpaqueBody = source.Substring(renderOpaqueIndex, Math.Min(9000, source.Length - renderOpaqueIndex));

        Assert.DoesNotContain("constexpr bool EnableLegacyCpuTexturedOpaquePath = true;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (EnableLegacyCpuTexturedOpaquePath && batch.Textured)", renderOpaqueBody, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.AddOpaqueTexturedBatches(", renderOpaqueBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured VU packet builder validates packed stream bounds before reading cooked mesh data.
    /// </summary>
    [Fact]
    public void Ps2VuPackedModel_WhenLoadingPackedBytes_ValidatesStreamOffsets() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuPackedModel.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 packed model source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("PositionBlockOffsetQwords", source, StringComparison.Ordinal);
        Assert.Contains("NormalBlockOffsetQwords", source, StringComparison.Ordinal);
        Assert.Contains("TexCoordBlockOffsetQwords", source, StringComparison.Ordinal);
        Assert.Contains("TriangleVertexCount", source, StringComparison.Ordinal);
        Assert.Contains("PackedBytes.size()", source, StringComparison.Ordinal);
        Assert.Contains("TriangleVertexCount % 3u", source, StringComparison.Ordinal);
        Assert.Contains("ValidateBlockRange", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    static string GetRepositoryRootPath() {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Makefile"))) {
            directory = directory.Parent;
        }

        return directory?.FullName ?? string.Empty;
    }
}
