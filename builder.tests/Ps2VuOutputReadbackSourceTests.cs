namespace HelEngine.Builder.Tests;

/// <summary>
/// Protects the bounded B332 diagnostic that verifies scheduled VU1 counter loops emit only submitted textured triangles.
/// </summary>
public sealed class Ps2VuOutputReadbackSourceTests {
    /// <summary>
    /// Requires the diagnostic to synchronize VIF1 before reading the first two emitted XYZ triangles from VU1 data memory.
    /// </summary>
    [Fact]
    public void TexturedVuPath_B332CapturesOutputAfterVifAndVuBecomeIdle() {
        string header = File.ReadAllText("../../../../src/platform/ps2/rendering/Ps2RenderManager3D.hpp");
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableTexturedVuOutputReadbackDiagnostics = true;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uintptr_t Vu1DataMemoryBaseAddress = 0x1100C000u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uintptr_t Vif1StatusRegisterAddress = 0x10003C00u;", source, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::WaitForTexturedVuOutputDiagnostic()", source, StringComparison.Ordinal);
        Assert.Contains("void Ps2RenderManager3D::CaptureTexturedVuOutputDiagnostic()", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", source, StringComparison.Ordinal);
        Assert.Contains("WaitForTexturedVuOutputDiagnostic();", source, StringComparison.Ordinal);
        Assert.Contains("CaptureTexturedVuOutputDiagnostic();", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleCount() const", header, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexA0() const", header, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexB2() const", header, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexC0() const", header, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexC2() const", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires B330 to expose the captured VU1 coordinates directly on the probe overlay for HelenUI OCR.
    /// </summary>
    [Fact]
    public void Ps2BootHost_B332PublishesThreeCapturedVuTriangles() {
        string source = File.ReadAllText("../../../../src/platform/ps2/Ps2BootHost.cpp");

        Assert.Contains("constexpr const char* FrameTimingOverlayBuildNumber = \"B332\";", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexA0()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexA1()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexA2()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexB0()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexB1()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexB2()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexC0()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexC1()", source, StringComparison.Ordinal);
        Assert.Contains("GetLastVuOutputTriangleVertexC2()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires the falsified B329 Path1 drain experiment to return to ordinary VU completion synchronization.
    /// </summary>
    [Fact]
    public void TexturedVuPath_B332RetainsFlushAfterEachTexturedMicroProgram() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");
        int fastEmitterStart = source.IndexOf("void EmitTexturedFastSourceRun(", StringComparison.Ordinal);
        int clippedEmitterStart = source.IndexOf("void EmitTexturedClippedBatch(", StringComparison.Ordinal);
        int followingMethodStart = source.IndexOf("void PopulateUntexturedSharedState(", StringComparison.Ordinal);
        string fastEmitter = source.Substring(fastEmitterStart, clippedEmitterStart - fastEmitterStart);
        string clippedEmitter = source.Substring(clippedEmitterStart, followingMethodStart - clippedEmitterStart);

        Assert.Contains("packet2_vif_mscal(packet, TexturedMicroProgramAddress, 0);", fastEmitter, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_flush(packet, 0);", fastEmitter, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_vif_flusha(packet, 0);", fastEmitter, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_mscal(packet, TexturedPretransformedMicroProgramAddress, 0);", clippedEmitter, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_flush(packet, 0);", clippedEmitter, StringComparison.Ordinal);
        Assert.DoesNotContain("packet2_vif_flusha(packet, 0);", clippedEmitter, StringComparison.Ordinal);
    }
}
