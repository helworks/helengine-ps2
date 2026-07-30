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
        Assert.Contains("sharedState.MaterialLighting[3] = static_cast<float>(lightingConstants.DiffuseScale) * DirectionalLightDiffuseIntensity;", source);
    }
}
