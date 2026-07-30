using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the CPU textured renderer retains immutable cooked triangle sources and reusable direct-GIF packet storage.
/// </summary>
public sealed class Ps2PersistentTexturedPacketCacheSourceTests {
    /// <summary>
    /// Ensures the VIF packet builder owns the persistent textured source cache instead of allocating transient metadata every frame.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenSubmittingCpuTexturedBatches_OwnsPersistentTexturedPacketCache() {
        string source = File.ReadAllText(GetBuilderHeaderPath());

        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuTexturedPacketCache TexturedPacketCache;", source, StringComparison.Ordinal);
        Assert.Contains("std::vector<std::uint64_t> DirectGifPacketWords;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the CPU direct-GIF fallback resolves immutable triangle sources and reuses packet words rather than allocating per-frame packet vectors.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenSubmittingCpuTexturedDirectGif_ReusesCachedSourcesAndPacketWords() {
        string source = File.ReadAllText(GetBuilderSourcePath());

        Assert.Contains("TexturedPacketCache.ResolveTriangleSources(*batch->Model, runtimeModel)", source, StringComparison.Ordinal);
        Assert.Contains("DirectGifPacketWords.clear();", source, StringComparison.Ordinal);
        Assert.Contains("if (createVifPacket) {\n            texturedTrianglePackets.reserve(texturedTriangleCapacity);\n        }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::vector<std::uint64_t> directGifPacketWords;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the immutable source cache is bounded and only retains cooked geometry data that remains valid across frames.
    /// </summary>
    [Fact]
    public void Ps2VuTexturedPacketCache_WhenCachingModelTriangleSources_UsesBoundedLruEntries() {
        string source = File.ReadAllText(GetCacheHeaderPath());

        Assert.Contains("constexpr std::size_t MaximumEntryCount = 8u;", source, StringComparison.Ordinal);
        Assert.Contains("std::vector<Ps2VuTexturedTriangleSource>", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t LastUsedFrame", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the packet-builder header from the repository root.
    /// </summary>
    /// <returns>Absolute packet-builder header path.</returns>
    static string GetBuilderHeaderPath() {
        return Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp");
    }

    /// <summary>
    /// Resolves the packet-builder source from the repository root.
    /// </summary>
    /// <returns>Absolute packet-builder source path.</returns>
    static string GetBuilderSourcePath() {
        return Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
    }

    /// <summary>
    /// Resolves the persistent packet-cache header from the repository root.
    /// </summary>
    /// <returns>Absolute persistent packet-cache header path.</returns>
    static string GetCacheHeaderPath() {
        return Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedPacketCache.hpp");
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
