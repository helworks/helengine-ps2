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
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/directional_shadow_plaza.hasset");
        string[] segments = resolved.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("COOKED", segments[0]);
        Assert.Equal("SCENES", segments[1]);
        Assert.Matches("^[A-Z0-9_]{8}\\.[A-Z0-9_]{3}$", segments[2]);
    }

    /// <summary>
    /// Ensures deep scene-local hashed runtime HEL assets collapse into a sharded short alias bucket so runtime disc lookups avoid multi-sector directory scans in the dense `COOKED\H` namespace.
    /// </summary>
    [Fact]
    public void ResolveDiscRelativePath_WhenGivenDeepSceneLocalHashedPath_UsesH1PrefixedHelAliasStemWithinShortPhysicalPathBudget() {
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/mad3ccdb/reff7c42/co5cb17f/cube14.hel");
        string runtimePhysicalPath = "\\" + resolved.Replace('/', '\\') + ";1";
        string[] segments = resolved.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("COOKED", segments[0]);
        Assert.Equal(4, segments.Length);
        Assert.Equal("H", segments[1]);
        Assert.Matches("^[A-Z0-9_]$", segments[2]);
        Assert.Matches("^H1[A-Z0-9_]{6}\\.[A-Z0-9_]{3}$", segments[3]);
        Assert.EndsWith(".HEL", segments[3], StringComparison.Ordinal);
        Assert.True(runtimePhysicalPath.Length <= 32, $"Expected a PS2-safe runtime path length, but got {runtimePhysicalPath.Length} for '{runtimePhysicalPath}'.");
    }

    /// <summary>
    /// Ensures deep scene assets collapse into a dedicated short `COOKED\SCENES` namespace instead of the generic alias bucket so startup scene paths stay compact and scene-specific.
    /// </summary>
    [Fact]
    public void ResolveDiscRelativePath_WhenGivenDeepScenePath_CollapsesToScenesDirectoryBudget() {
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/reff7c42/colored_cube_grid.hasset");
        string runtimePhysicalPath = "\\" + resolved.Replace('/', '\\') + ";1";
        string[] segments = resolved.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("COOKED", segments[0]);
        Assert.Equal("SCENES", segments[1]);
        Assert.Equal(3, segments.Length);
        Assert.Matches("^[A-Z0-9_]{8}\\.[A-Z0-9_]{3}$", segments[2]);
        Assert.EndsWith(".HAS", segments[2], StringComparison.Ordinal);
        Assert.True(runtimePhysicalPath.Length <= 32, $"Expected a PS2-safe runtime path length, but got {runtimePhysicalPath.Length} for '{runtimePhysicalPath}'.");
    }

    /// <summary>
    /// Ensures extensionless imported cooked assets collapse into a sharded short alias with an explicit extension so runtime disc lookups avoid the flat extensionless `COOKED\IMPORTED` namespace.
    /// </summary>
    [Fact]
    public void ResolveDiscRelativePath_WhenGivenExtensionlessImportedCookedPath_UsesI1PrefixedAliasWithExplicitExtension() {
        string resolved = Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/imported/ff8a0f1fafe1f1c4989f73f39db8b800512e09e26439b011cb7afb0fed44dd5a");
        string runtimePhysicalPath = "\\" + resolved.Replace('/', '\\') + ";1";
        string[] segments = resolved.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("COOKED", segments[0]);
        Assert.Equal(4, segments.Length);
        Assert.Equal("I", segments[1]);
        Assert.Matches("^[A-Z0-9_]$", segments[2]);
        Assert.Matches("^I1[A-Z0-9_]{6}\\.[A-Z0-9_]{3}$", segments[3]);
        Assert.EndsWith(".HAS", segments[3], StringComparison.Ordinal);
        Assert.True(runtimePhysicalPath.Length <= 32, $"Expected a PS2-safe runtime path length, but got {runtimePhysicalPath.Length} for '{runtimePhysicalPath}'.");
    }
}
