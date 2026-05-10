using helengine.baseplatform.Manifest;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies generated PS2 native runtime asset-path manifest output.
/// </summary>
public sealed class Ps2RuntimeAssetPathManifestWriterTests {
    /// <summary>
    /// Ensures the generated runtime manifests embed rooted physical startup and scene-catalog paths without logical-path lookup tables.
    /// </summary>
    [Fact]
    public void Write_WhenStartupSceneExists_EmitsRootedPhysicalStartupAndSceneCatalogPaths() {
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
        string sceneCatalogSource = File.ReadAllText(Path.Combine(generatedCoreRootPath, "runtime", "runtime_scene_catalog_manifest.cpp"));
        Assert.Contains("kRuntimePs2StartupScenePath[] = \"cdrom0:\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\"", source, StringComparison.Ordinal);
        Assert.Contains("const char* he_get_runtime_ps2_startup_scene_path()", source, StringComparison.Ordinal);
        Assert.Contains("const HERuntimeSceneCatalogEntry kRuntimeSceneCatalogEntries[]", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("\"Scenes/DemoDiscMainMenu.helen\"", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("\"cdrom0:\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\"", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("he_runtime_scene_catalog_entries", sceneCatalogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("he_get_runtime_ps2_asset_physical_path", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HERuntimePs2AssetPathEntry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimePs2LogicalPathsEqual", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures startup and scene catalog metadata must resolve to staged PS2 physical disc paths.
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

    /// <summary>
    /// Ensures every runtime scene must resolve to a staged PS2 physical disc path.
    /// </summary>
    [Fact]
    public void Write_WhenNonStartupSceneIsMissingFromPhysicalMap_Throws() {
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
                    ]),
                new PlatformBuildScene(
                    "Scenes/DirectionalShadow.helen",
                    "DirectionalShadow",
                    "cooked/scenes/DirectionalShadow.hasset",
                    [],
                    [
                        new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/DirectionalShadow.hasset")
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
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                    ["cooked/scenes/DemoDiscMainMenu.hasset"] = "\\COOKED\\SCENES\\DEMODISC.HAS;1"
                }));

        Assert.Contains("DirectionalShadow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
