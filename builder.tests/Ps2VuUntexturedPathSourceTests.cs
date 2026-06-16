using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in untextured PS2 VU path stays on the last known-good single-payload contract.
/// </summary>
public sealed class Ps2VuUntexturedPathSourceTests {
    /// <summary>
    /// Ensures the setup builder emits one payload per untextured triangle and the VU program consumes the original single-payload layout.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_UsesSinglePayloadContract() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.hpp"));
        string setupSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.cpp"));
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string microProgramSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueDraw3D.vsm"));

        Assert.Contains("struct alignas(16) Ps2VuOpaqueSourceTriangle final", setupHeader, StringComparison.Ordinal);
        Assert.Contains("float FaceNormal[4];", setupHeader, StringComparison.Ordinal);
        Assert.Contains("float LightDirection[4];", setupHeader, StringComparison.Ordinal);
        Assert.Contains("float WorldViewProjectionMatrix[16];", setupHeader, StringComparison.Ordinal);

        Assert.DoesNotContain("constexpr std::uint32_t VuDiagnosticBatchTriangleCount = 2u;", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr bool EnableVuTwoTriangleBatchDiagnostic", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("struct alignas(16) Ps2VuTriangleBatchHeader final", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("batchHeader->TriangleCount", vifSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        xtop VI02", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        iaddiu VI03, VI02, 0x00000010", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF01, 40(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF02, 41(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF03, 42(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF04, 52(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF05, 53(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF06, 54(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF07, 55(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF08, 56(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("NOP                                                        lq VF09, 57(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1)", vifSource, StringComparison.Ordinal);
        Assert.Contains("Ps2VuLitTrianglePayload", vifSource, StringComparison.Ordinal);
        Assert.Contains("float FaceNormal[4];", vifSource, StringComparison.Ordinal);
        Assert.Contains("float LightDirection[4];", vifSource, StringComparison.Ordinal);
        Assert.Contains("float WorldViewProjectionMatrix[16];", vifSource, StringComparison.Ordinal);
        Assert.Contains("float NormalA[4];", vifSource, StringComparison.Ordinal);
        Assert.Contains("float TexCoordA[4];", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("sqi VF10, (VI03++)", microProgramSource, StringComparison.Ordinal);
        Assert.DoesNotContain("sqi VF16, (VI03++)", microProgramSource, StringComparison.Ordinal);
        Assert.DoesNotContain("__ps2_opaque_draw_3d_triangle_loop", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF01, 22(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF03, 24(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF02, 26(VI02)", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("xgkick VI03", microProgramSource, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);", setupSource, StringComparison.Ordinal);
        Assert.Contains("::float4x4::Multiply__ref0_ref1_out2(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);", setupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::Multiply(worldCopy, viewCopy, worldViewMatrix);", setupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4x4::Multiply(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);", setupSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in VU builder no longer keeps the retired helper code paths that only survive as native-build warnings.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_DoesNotKeepRetiredWarningOnlyHelpers() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string vifSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.DoesNotContain("std::memset(&payload, 0, sizeof(Ps2VuLitTrianglePayload));", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildVifCode(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUntexturedTriangleGifPacketBytes(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFixedTrianglePositionRegister(", vifSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateDiagnosticOpaqueTrianglePayload(", vifSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the compact untextured setup builder still records real projected screen bounds for runtime diagnostics instead of leaving the VU path at zeroed bounds.
    /// </summary>
    [Fact]
    public void UntexturedVuPath_PopulatesProjectedScreenBoundsForRuntimeDiagnostics() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string setupSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueUntexturedSetupBuilder.cpp"));

        Assert.Contains("bool ProjectWorldPosition(", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedScreenBounds = ::float4(minX, minY, maxX, maxY);", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleBoundsA = ::float4(minX, minY, maxX, maxY);", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleVertexA0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);", setupSource, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleVertexB0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);", setupSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
