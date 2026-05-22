using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in untextured PS2 VU path stays on the compact shared-state batching contract.
/// </summary>
public sealed class Ps2VuUntexturedPathSourceTests {
    /// <summary>
    /// Ensures the setup builder only emits compact position records and the VU program loops batched triangle records from shared state.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_UsesCompactSharedStateBatchingContract() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.hpp"));
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string microProgramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueDraw3D.vsm"));

        Assert.Contains("struct alignas(16) Ps2VuOpaqueSourceTriangle final", setupHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("FaceNormal", setupHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("WorldMatrix", setupHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionMatrix", setupHeader, StringComparison.Ordinal);

        Assert.Contains("constexpr std::size_t OpaqueUntexturedTriangleBatchSize = 8u;", vifSource, StringComparison.Ordinal);
        Assert.Contains("TriangleCount[4]", vifSource, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuLitTrianglePayload", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("float NormalA[4];", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("float TexCoordA[4];", vifSource, StringComparison.Ordinal);

        Assert.Contains("ilw.x VI05, 40(VI00)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI02, VI02, 0x00000003", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("__ps2_opaque_draw_3d_triangle_loop", microProgramSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
