using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies direct-GIF untextured submission uses the GIF packet capacity instead of the smaller VIF packet capacity.
/// </summary>
public sealed class Ps2DirectGifUntexturedBatchingSourceTests {
    /// <summary>
    /// Ensures the colored direct-GIF renderer aggregates enough source triangles to avoid splitting the colored-cubes benchmark into VIF-sized packets.
    /// </summary>
    [Fact]
    public void Ps2RenderManager3D_WhenUsingDirectGifForUntexturedGeometry_UsesTwoThousandFortyEightSourceTriangleGroups() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr std::size_t MaximumBoundedUntexturedAggregateSourceTriangleCount = 2048u;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct-GIF packet admission uses the maximum DMA tag capacity while VIF-backed submission retains its smaller VIF capacity.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenAddingDirectGifUntexturedBatches_UsesDirectGifPacketCapacity() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr std::uint16_t MaximumOpaqueUntexturedDirectGifQwords = 0xFFFFu;", source, StringComparison.Ordinal);
        Assert.Contains("const std::size_t maximumPacketQwordCount = createVifPacket", source, StringComparison.Ordinal);
        Assert.Contains("? MaximumOpaqueUntexturedPacketQwords", source, StringComparison.Ordinal);
        Assert.Contains(": MaximumOpaqueUntexturedDirectGifQwords;", source, StringComparison.Ordinal);
        Assert.Contains("if (nextPacketQwordCount > maximumPacketQwordCount)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct-GIF opaque batches publish TEST and PRIM once before one shared per-triangle vertex register stream.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifUntexturedBatches_SharesInvariantGsStateAcrossTriangles() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("DirectGifOpaqueBatchHeaderWordCount", source, StringComparison.Ordinal);
        Assert.Contains("DirectGifOpaqueTriangleVertexWordCount", source, StringComparison.Ordinal);
        Assert.Contains("DirectGifOpaqueTriangleRegisterList", source, StringComparison.Ordinal);
        Assert.Contains("GIF_SET_TAG(directGifTriangleCount, 1, 0, 0, GIF_FLG_REGLIST, 6)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("directGifPacketWords += UntexturedTriangleDirectGifPacketWordCount", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct-GIF colored geometry writes triangle words straight into the final DMA byte buffer rather than allocating and copying an intermediate packet vector.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifUntexturedBatches_WritesTriangleWordsIntoFinalPacketBuffer() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("GifPacketBytes.resize((DirectGifOpaqueBatchHeaderWordCount + (maximumDirectGifTriangleCount * DirectGifOpaqueTriangleVertexWordCount)) * sizeof(std::uint64_t));", source, StringComparison.Ordinal);
        Assert.Contains("directGifPacketWords = reinterpret_cast<std::uint64_t*>(GifPacketBytes.data()) + DirectGifOpaqueBatchHeaderWordCount;", source, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize((DirectGifOpaqueBatchHeaderWordCount + (directGifTriangleCount * DirectGifOpaqueTriangleVertexWordCount)) * sizeof(std::uint64_t));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::vector<std::array<std::uint64_t, UntexturedTriangleDirectGifPacketWordCount>> untexturedTrianglePackets;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures direct-GIF colored submission encodes compact triangle positions and color without constructing VIF-only payload state for every emitted triangle.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenEncodingDirectGifUntexturedBatches_DoesNotMaterializeVuPayloads() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const Ps2VuOpaqueSourceTriangle& sourceTriangle,", source, StringComparison.Ordinal);
        Assert.Contains("const Ps2VuFlatColor& flatColor,", source, StringComparison.Ordinal);
        Assert.Contains("if (!createVifPacket) {", source, StringComparison.Ordinal);
        Assert.Contains("BuildUntexturedTriangleDirectGifVertexWords(sourceTriangle, flatColor, projection, viewport, gsGlobal, directGifPacketWords)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures disabled per-triangle diagnostics do not add timer reads to every direct-GIF triangle in release rendering.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenDirectGifTimingDiagnosticsAreDisabled_DoesNotReadTheClockPerTriangle() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const bool recordDirectGifPhaseTiming = !createVifPacket && EnableVuPerTriangleTimingDiagnostics;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::clock_t directGifTrianglePrepStartTicks = !createVifPacket ? std::clock() : 0;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
