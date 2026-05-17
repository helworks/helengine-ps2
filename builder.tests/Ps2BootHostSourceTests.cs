using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 boot host source preserves the intended startup-scene boot flow.
/// </summary>
public sealed class Ps2BootHostSourceTests {
    /// <summary>
    /// Ensures the boot host continues into startup-scene loading after compact disc probes instead of stopping at a temporary diagnostic halt.
    /// </summary>
    [Fact]
    public void BootHost_AfterCompactDiscProbes_ContinuesToStartupSceneLoading() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("BootLogCompactDiscProbe(\"disc probe h1 pass known\", H1ProbePassKnownDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.Contains("StartupSceneLoaded = LoadPackagedStartupScene();", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(StartupSceneLoaded ? \"startup scene load succeeded\" : \"startup scene load failed\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup scene probe halt\");", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host resolves packaged font atlases through the PS2-native texture runtime path when one font omits embedded raw atlas bytes.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("font->get_CookedAtlasTextureRelativePath()", source, StringComparison.Ordinal);
        Assert.Contains("he_cpp_try_cast<::Ps2TextureAsset>(asset)", source, StringComparison.Ordinal);
        Assert.Contains("FontTextureRecords", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
