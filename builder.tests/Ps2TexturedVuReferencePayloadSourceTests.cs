namespace HelEngine.Builder.Tests;

/// <summary>
/// Verifies that opaque textured geometry is unpacked into the active VU double-buffer input region.
/// </summary>
public sealed class Ps2TexturedVuReferencePayloadSourceTests {
    /// <summary>
    /// Confirms that the packet builder addresses source data through the VIF double-buffer TOP address.
    /// </summary>
    [Fact]
    public void TexturedVuPathUsesDoubleBufferedInputMemoryForTrianglePayloads() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");
        string packetCacheHeader = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp");
        string packetCacheSource = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.cpp");

        Assert.Contains("constexpr std::uint32_t XtopGifPacketAddress = 0;", source);
        Assert.Contains("packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);", source);
        Assert.Contains("constexpr bool UseCachedTexturedVuSourceReferences = true;", source);
        Assert.Contains("packet2_utils_vu_add_unpack_data(", source);
        Assert.Contains("cachedSourceTriangles.data()", source);
        Assert.Contains("ResolveReferencedPackedTriangleSources", packetCacheHeader);
        Assert.Contains("entry.ReferencedThisFrame = true;", packetCacheSource);
        Assert.DoesNotContain("MaximumReferencedTexturedVuSourceSliceCount", source);
        Assert.DoesNotContain("std::vector<Ps2VuTexturedSourceTriangle> sourceTriangles", source);
        Assert.DoesNotContain("VuDoubleBufferBaseAddress", source);
        Assert.DoesNotContain("MaximumPrimedTexturedVuSourceSliceCount", source);
    }

    /// <summary>
    /// Confirms that the VU path limits directional diffuse light before converting it into the final vertex color.
    /// </summary>
    [Fact]
    public void TexturedVuPath_UsesReducedDirectionalDiffuseIntensity() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");

        Assert.Contains("constexpr float DirectionalLightDiffuseIntensity = 0.65f;", source);
        Assert.Contains("cachedSharedState.MaterialLighting[3] = static_cast<float>(lightingConstants.DiffuseScale) * DirectionalLightDiffuseIntensity;", source);
    }

    /// <summary>
    /// Confirms that the textured VU source path records state construction separately from VIF command encoding so packet-assembly work can be optimized from measured evidence.
    /// </summary>
    [Fact]
    public void TexturedVuPath_ProfilesSharedStateAndCommandEncodingSeparately() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");
        string header = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp");

        Assert.Contains("LastTexturedVuStateBuildMilliseconds", source);
        Assert.Contains("LastTexturedVuCommandEncodeMilliseconds", source);
        Assert.Contains("double GetLastTexturedVuStateBuildMilliseconds() const;", header);
        Assert.Contains("double GetLastTexturedVuCommandEncodeMilliseconds() const;", header);
    }

    /// <summary>
    /// Confirms consecutive source slices reuse their immutable batch state and patch only the slice-specific triangle fields.
    /// </summary>
    [Fact]
    public void TexturedVuPath_ReusesSharedStateForConsecutiveBatchSlices() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");

        Assert.Contains("const Ps2VuOpaqueBatch* cachedSharedStateBatch = nullptr;", source);
        Assert.Contains("if (cachedSharedStateBatch != batch) {", source);
        Assert.Contains("sharedState = cachedSharedState;", source);
        Assert.Contains("sharedState.TriangleCount[0] = static_cast<std::uint32_t>(batchSlice.SourceTriangleCount);", source);
    }

    /// <summary>
    /// Confirms normal textured VU rendering does not read the EE clock for every source slice when payload diagnostics are disabled.
    /// </summary>
    [Fact]
    public void TexturedVuPath_WhenPerSliceDiagnosticsAreDisabled_DoesNotReadClockForPayloadEncoding() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");

        Assert.Contains("constexpr bool EnableTexturedVuPerSliceTimingDiagnostics = false;", source);
        Assert.Contains("const std::clock_t sourcePayloadFillStartTicks = EnableTexturedVuPerSliceTimingDiagnostics ? std::clock() : 0;", source);
        Assert.Contains("if (EnableTexturedVuPerSliceTimingDiagnostics) {", source);
    }

    /// <summary>
    /// Confirms packet assembly consumes ranges of the frame-owned slice vectors instead of allocating and copying temporary packet vectors.
    /// </summary>
    [Fact]
    public void TexturedVuPath_UsesFrameOwnedSliceRangesWithoutTemporaryVectorCopies() {
        string builderSource = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");
        string rendererSource = File.ReadAllText("../../../../src/platform/ps2/rendering/Ps2RenderManager3D.cpp");

        Assert.Contains("std::size_t firstBatchIndex,", builderSource);
        Assert.Contains("std::size_t batchCount,", builderSource);
        Assert.Contains("const std::size_t batchIndex = firstBatchIndex + batchOffset;", builderSource);
        Assert.DoesNotContain("std::vector<Ps2VuOpaqueBatchSlice> packetTexturedVuBatches(", rendererSource);
        Assert.DoesNotContain("std::vector<::float4x4> packetTexturedVuWorlds(", rendererSource);
    }

    /// <summary>
    /// Confirms consecutive source slices reuse their already pinned immutable triangle vector instead of resolving the packet cache repeatedly.
    /// </summary>
    [Fact]
    public void TexturedVuPath_ReusesResolvedSourceVectorForConsecutiveBatchSlices() {
        string source = File.ReadAllText("../../../../src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp");

        Assert.Contains("const Ps2VuOpaqueBatch* cachedSourceBatch = nullptr;", source);
        Assert.Contains("const std::vector<Ps2VuTexturedPackedTriangleSource>* cachedSourceTrianglesForBatch = nullptr;", source);
        Assert.Contains("if (cachedSourceBatch != batch) {", source);
        Assert.Contains("cachedSourceTrianglesForBatch = &TexturedPacketCache.ResolveReferencedPackedTriangleSources(*batch->Model, runtimeModel);", source);
        Assert.Contains("const std::vector<Ps2VuTexturedPackedTriangleSource>& cachedSourceTriangles = *cachedSourceTrianglesForBatch;", source);
    }
}
