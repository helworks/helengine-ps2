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

        Assert.Contains("BootLog(std::string(\"startup scene load begin \") + Ps2BootVersionStamp);", source, StringComparison.Ordinal);
        Assert.Contains("StartupSceneLoaded = LoadPackagedStartupScene();", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(StartupSceneLoaded ? \"startup scene load succeeded\" : \"startup scene load failed\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup scene probe halt\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup diagnostic halt after non-null\");", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"startup scene invoking scene load service\");", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures normal runtime initialization no longer emits unconditional compact-disc probe spam and startup-scene runtime exceptions route through the shared boot-log helper.
    /// </summary>
    [Fact]
    public void BootHost_RuntimeExceptionLogging_UsesSharedHelperAndSkipsDefaultDiscProbeSpam() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("void BootLogRuntimeException(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube model\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material early\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material late\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe scene local material cooked\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe scene local material root\"", source, StringComparison.Ordinal);
        Assert.Contains("BootLogRuntimeException(\"startup scene\"", source, StringComparison.Ordinal);
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
        Assert.Contains("#include \"Ps2AssetSerializer.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2AssetSerializer::Deserialize(stream)", source, StringComparison.Ordinal);
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
