using helengine.baseplatform.Manifest;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies generated PS2 native runtime asset-path manifest output.
/// </summary>
public sealed class Ps2RuntimeAssetPathManifestWriterTests {
    /// <summary>
    /// Environment variable used by tests to isolate the persistent PS2 runtime build-stamp counter.
    /// </summary>
    const string BuildStampCounterPathEnvironmentVariableName = "HELENGINE_PS2_BUILD_STAMP_COUNTER_PATH";

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
            "ps2",
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
        string buildStampHeader = File.ReadAllText(Path.Combine(generatedCoreRootPath, "runtime", "runtime_ps2_build_stamp.hpp"));
        string buildStampSource = File.ReadAllText(Path.Combine(generatedCoreRootPath, "runtime", "runtime_ps2_build_stamp.cpp"));
        Assert.Contains("kRuntimePs2StartupScenePath[] = \"cdrom0:\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\"", source, StringComparison.Ordinal);
        Assert.Contains("const char* he_get_runtime_ps2_startup_scene_path()", source, StringComparison.Ordinal);
        Assert.Contains("const HERuntimeSceneCatalogEntry kRuntimeSceneCatalogEntries[]", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("\"Scenes/DemoDiscMainMenu.helen\"", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("\"cdrom0:\\\\COOKED\\\\SCENES\\\\DEMODISC.HAS;1\"", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("he_runtime_scene_catalog_entries", sceneCatalogSource, StringComparison.Ordinal);
        Assert.Contains("const char* he_runtime_ps2_build_stamp()", buildStampHeader, StringComparison.Ordinal);
        Assert.Contains("static const char kRuntimePs2BuildStamp[]", buildStampSource, StringComparison.Ordinal);
        Assert.Contains("const char* he_runtime_ps2_build_stamp()", buildStampSource, StringComparison.Ordinal);
        Assert.DoesNotContain("he_get_runtime_ps2_asset_physical_path", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HERuntimePs2AssetPathEntry", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimePs2LogicalPathsEqual", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures consecutive PS2 manifest writes emit simple readable decimal build stamps that increase by one minor version.
    /// </summary>
    [Fact]
    public void Write_WhenBuildStampCounterExists_EmitsReadableIncrementingRuntimeStamp() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-runtime-asset-manifest-tests", Guid.NewGuid().ToString("N"));
        string firstGeneratedCoreRootPath = Path.Combine(rootPath, "first-generated-core");
        string secondGeneratedCoreRootPath = Path.Combine(rootPath, "second-generated-core");
        string counterPath = Path.Combine(rootPath, "counter", "runtime-build-stamp.txt");
        Directory.CreateDirectory(Path.Combine(firstGeneratedCoreRootPath, "runtime"));
        Directory.CreateDirectory(Path.Combine(secondGeneratedCoreRootPath, "runtime"));

        string previousCounterPath = Environment.GetEnvironmentVariable(BuildStampCounterPathEnvironmentVariableName);
        Environment.SetEnvironmentVariable(BuildStampCounterPathEnvironmentVariableName, counterPath);
        try {
            PlatformBuildManifest manifest = CreateSingleSceneManifest();
            Dictionary<string, string> logicalToPhysicalPaths = CreateSingleScenePhysicalPathMap();

            Ps2RuntimeAssetPathManifestWriter writer = new();
            writer.Write(firstGeneratedCoreRootPath, manifest, logicalToPhysicalPaths);
            writer.Write(secondGeneratedCoreRootPath, manifest, logicalToPhysicalPaths);

            string firstBuildStampSource = File.ReadAllText(Path.Combine(firstGeneratedCoreRootPath, "runtime", "runtime_ps2_build_stamp.cpp"));
            string secondBuildStampSource = File.ReadAllText(Path.Combine(secondGeneratedCoreRootPath, "runtime", "runtime_ps2_build_stamp.cpp"));
            Assert.Contains("kRuntimePs2BuildStamp[] = \"1.0\"", firstBuildStampSource, StringComparison.Ordinal);
            Assert.Contains("kRuntimePs2BuildStamp[] = \"1.1\"", secondBuildStampSource, StringComparison.Ordinal);
        } finally {
            Environment.SetEnvironmentVariable(BuildStampCounterPathEnvironmentVariableName, previousCounterPath);
        }
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
            "ps2",
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
            "ps2",
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

    /// <summary>
    /// Creates a minimal one-scene manifest used by generated-runtime manifest writer tests.
    /// </summary>
    /// <returns>Manifest with one startup scene and no loose assets.</returns>
    static PlatformBuildManifest CreateSingleSceneManifest() {
        return new PlatformBuildManifest(
            3,
            "project",
            "1.0.0",
            "1.0.0",
            "ps2",
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
    }

    /// <summary>
    /// Creates the physical path map required by the single-scene manifest fixture.
    /// </summary>
    /// <returns>Logical-to-physical PS2 disc path mappings for the single-scene fixture.</returns>
    static Dictionary<string, string> CreateSingleScenePhysicalPathMap() {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["cooked/scenes/DemoDiscMainMenu.hasset"] = "\\COOKED\\SCENES\\DEMODISC.HAS;1"
        };
    }
}
