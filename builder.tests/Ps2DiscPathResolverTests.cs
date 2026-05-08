using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies logical runtime asset paths resolve to PS2-safe disc filenames.
/// </summary>
public sealed class Ps2DiscPathResolverTests {
    /// <summary>
    /// Ensures short logical scene paths preserve their stem while normalizing directory and extension casing for disc export.
    /// </summary>
    [Fact]
    public void ResolveDiscRelativePath_WhenGivenShortScenePath_UsesPs2SafeDirectoryAndExtensionNames() {
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset");

        Assert.Equal(Path.Combine("COOKED", "SCENES", "MAIN.HAS"), resolved);
    }

    /// <summary>
    /// Ensures long logical names collapse into deterministic PS2-safe aliases instead of exceeding ISO9660-friendly limits.
    /// </summary>
    [Fact]
    public void ResolveDiscRelativePath_WhenGivenLongPath_UsesDeterministicAliasedSegments() {
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/rendering/directional_shadow_plaza.hasset");
        string[] segments = resolved.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("COOKED", segments[0]);
        Assert.Equal("SCENES", segments[1]);
        Assert.Equal(8, segments[2].Length);
        Assert.Matches("^[A-Z0-9_]{8}$", segments[2]);
        Assert.Matches("^[A-Z0-9_]{8}\\.[A-Z0-9_]{3}$", segments[3]);
    }
}
