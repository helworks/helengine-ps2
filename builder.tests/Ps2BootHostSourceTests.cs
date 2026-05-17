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
        string sourcePath = ResolveBootHostSourcePath();
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("BootLogDiscProbe(\"disc probe cube model\", CubeModelDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.Contains("StartupSceneLoaded = LoadPackagedStartupScene();", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(StartupSceneLoaded ? \"startup scene load succeeded\" : \"startup scene load failed\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup scene probe halt\");", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 runtime prefixes the top FPS overlay row with the generated build stamp so launched ISOs are visually identifiable.
    /// </summary>
    [Fact]
    public void BootHost_WhenDrawingTopPerformanceRow_PrefixesGeneratedBuildStamp() {
        string sourcePath = ResolveBootHostSourcePath();
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"runtime/runtime_ps2_build_stamp.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("he_runtime_ps2_build_stamp()", source, StringComparison.Ordinal);
        Assert.Contains("currentText.rfind(\"Upd \", 0) == 0", source, StringComparison.Ordinal);
        Assert.Contains("std::string(he_runtime_ps2_build_stamp()) + \" \" + currentText", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host resolves packaged font atlases through the PS2-native texture runtime path when one font omits embedded raw atlas bytes.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath() {
        string sourcePath = ResolveBootHostSourcePath();
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("font->get_CookedAtlasTextureRelativePath()", source, StringComparison.Ordinal);
        Assert.Contains("he_cpp_try_cast<::Ps2TextureAsset>(asset)", source, StringComparison.Ordinal);
        Assert.Contains("FontTextureRecords", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the checked-in PS2 boot host source path from the test output directory.
    /// </summary>
    /// <returns>Absolute path to the PS2 boot host source file.</returns>
    static string ResolveBootHostSourcePath() {
        DirectoryInfo directory = new(Directory.GetCurrentDirectory());
        while (directory != null) {
            string candidatePath = Path.Combine(directory.FullName, "src", "platform", "ps2", "Ps2BootHost.cpp");
            if (File.Exists(candidatePath)) {
                return candidatePath;
            }

            directory = directory.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "src", "platform", "ps2", "Ps2BootHost.cpp");
    }
}
