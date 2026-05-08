using helengine.baseplatform.Manifest;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies generated PS2 native runtime asset-path manifest output.
/// </summary>
public sealed class Ps2RuntimeAssetPathManifestWriterTests {
    /// <summary>
    /// Ensures the generated runtime manifest embeds the physical startup scene path and general asset mappings.
    /// </summary>
    [Fact]
    public void Write_WhenStartupSceneExists_EmitsPhysicalStartupPathAndAssetLookupEntries() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-runtime-asset-manifest-tests", Guid.NewGuid().ToString("N"));
        string generatedCoreRootPath = Path.Combine(rootPath, "generated-core");
        Directory.CreateDirectory(Path.Combine(generatedCoreRootPath, "runtime"));

        PlatformBuildManifest manifest = new(
            3,
            "project",
            "1.0.0",
            "1.0.0",
            "Scenes/DemoDiscMainMenu.helen",
            [
                new PlatformBuildScene(
                    "Scenes/DemoDiscMainMenu.helen",
                    "DemoDiscMainMenu",
                    "cooked/scenes/DemoDiscMainMenu.hasset",
                    [],
                    [
                        new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/DemoDiscMainMenu.hasset")
                    ])
            ],
            Array.Empty<PlatformBuildAsset>(),
            [
                new PlatformBuildArtifact("cooked/scenes/DemoDiscMainMenu.hasset", "scene:menu", "sha256:scene", "scene", "shared"),
                new PlatformBuildArtifact("cooked/fonts/DemoDiscBody.hefont", "font:body", "sha256:font", "font", "shared")
            ],
            Array.Empty<PlatformBuildCodeModule>(),
            Array.Empty<PlatformArtifactPlacement>(),
            new PlatformContainerWritePlan("ps2-disc-layout", Array.Empty<PlatformContainerArtifact>()));

        Dictionary<string, string> logicalToPhysicalPaths = new(StringComparer.OrdinalIgnoreCase) {
            ["cooked/scenes/DemoDiscMainMenu.hasset"] = "\\COOKED\\SCENES\\DEMODISC.HAS;1",
            ["cooked/fonts/DemoDiscBody.hefont"] = "\\COOKED\\FONTS\\DEMODISC.HEF;1"
        };

        Ps2RuntimeAssetPathManifestWriter writer = new();
        writer.Write(generatedCoreRootPath, manifest, logicalToPhysicalPaths);

        string source = File.ReadAllText(Path.Combine(generatedCoreRootPath, "runtime", "runtime_ps2_asset_path_manifest.cpp"));
        Assert.Contains("kRuntimePs2StartupScenePath[] = \"\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\"", source, StringComparison.Ordinal);
        Assert.Contains("{ \"cooked/scenes/DemoDiscMainMenu.hasset\", \"\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\" }", source, StringComparison.Ordinal);
        Assert.Contains("{ \"cdrom0:\\\\cooked\\\\scenes\\\\DemoDiscMainMenu.hasset\", \"\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\" }", source, StringComparison.Ordinal);
        Assert.Contains("{ \"cooked/fonts/DemoDiscBody.hefont\", \"\\\\COOKED\\\\FONTS\\\\DEMODISC.HEF;1\" }", source, StringComparison.Ordinal);
        Assert.Contains("{ \"cdrom0:\\\\cooked\\\\fonts\\\\DemoDiscBody.hefont\", \"\\\\COOKED\\\\FONTS\\\\DEMODISC.HEF;1\" }", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeRuntimePs2LogicalPathCharacter", source, StringComparison.Ordinal);
        Assert.Contains("RuntimePs2LogicalPathsEqual(entry.LogicalPath, logicalPath)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures startup scene metadata must resolve to a staged PS2 physical disc path.
    /// </summary>
    [Fact]
    public void Write_WhenStartupSceneIsMissingFromPhysicalMap_Throws() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-runtime-asset-manifest-tests", Guid.NewGuid().ToString("N"));
        string generatedCoreRootPath = Path.Combine(rootPath, "generated-core");
        Directory.CreateDirectory(Path.Combine(generatedCoreRootPath, "runtime"));

        PlatformBuildManifest manifest = new(
            3,
            "project",
            "1.0.0",
            "1.0.0",
            "Scenes/DemoDiscMainMenu.helen",
            [
                new PlatformBuildScene(
                    "Scenes/DemoDiscMainMenu.helen",
                    "DemoDiscMainMenu",
                    "cooked/scenes/DemoDiscMainMenu.hasset",
                    [],
                    [
                        new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/DemoDiscMainMenu.hasset")
                    ])
            ],
            Array.Empty<PlatformBuildAsset>(),
            Array.Empty<PlatformBuildArtifact>(),
            Array.Empty<PlatformBuildCodeModule>(),
            Array.Empty<PlatformArtifactPlacement>(),
            new PlatformContainerWritePlan("ps2-disc-layout", Array.Empty<PlatformContainerArtifact>()));

        Ps2RuntimeAssetPathManifestWriter writer = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                generatedCoreRootPath,
                manifest,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

        Assert.Contains("startup scene", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
