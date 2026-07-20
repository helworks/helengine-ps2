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
    /// Ensures textured VU packets use STQ interpolation so the GS performs perspective-correct mapping.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingTexturedTriangles_UsesPerspectiveCorrectStqMapping() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 VU packet builder source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int texturedBuilderStart = source.IndexOf("BuildTexturedTriangleGifPacketBytes", StringComparison.Ordinal);
        int texturedBuilderEnd = source.IndexOf("Ps2VuVifPacketBuilder::~Ps2VuVifPacketBuilder()", texturedBuilderStart, StringComparison.Ordinal);

        Assert.True(texturedBuilderStart >= 0, "Expected textured GIF packet builder.");
        Assert.True(texturedBuilderEnd > texturedBuilderStart, "Expected textured GIF packet builder body boundary.");
        string texturedBuilder = source.Substring(texturedBuilderStart, texturedBuilderEnd - texturedBuilderStart);

        Assert.Contains("PRIM_MAP_ST", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("prim.mapping = 1", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("prim.mapping_type,", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("BuildTexturedTriangleStqRegisterList", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("BuildGsStqRegister", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("BuildGsRgbaqRegisterWithQ", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("const ::float2 screenTexCoordA = vertexA.TexCoord", texturedBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIM_MAP_UV", texturedBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("GIF_REG_UV", texturedBuilder, StringComparison.Ordinal);
        Assert.Contains("GIF_REG_ST", source, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_STQ", source, StringComparison.Ordinal);
        Assert.Contains("GS_SETREG_RGBAQ", source, StringComparison.Ordinal);
        Assert.Contains("std::max(-viewPosition.Z", source, StringComparison.Ordinal);
        Assert.Contains("ClipTexturedTriangleAgainstFrustum", source, StringComparison.Ordinal);
        Assert.Contains("clipPosition.X + clipPosition.W", source, StringComparison.Ordinal);
        Assert.Contains("clipPosition.W - clipPosition.X", source, StringComparison.Ordinal);
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
