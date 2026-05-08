using helengine;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Results;
using helengine.baseplatform.Targets;
using helengine.files;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

public class Ps2PlatformAssetBuilderTests {
    [Fact]
    public void Descriptor_and_definition_return_ps2_metadata() {
        Ps2PlatformAssetBuilder builder = new();

        Assert.Equal("helengine.ps2.builder", builder.Descriptor.BuilderId);
        Assert.Equal("ps2", builder.Descriptor.TargetPlatformId);
        Assert.Contains("ps2", builder.Descriptor.SupportedRuntimeBackendIds);
        Assert.Equal("ps2", builder.Definition.PlatformId);
        Assert.Contains(builder.Definition.BuildProfiles, profile => profile.ProfileId == "ps2-default");
        Assert.Contains(builder.Definition.StorageProfiles, profile =>
            profile.ProfileId == "disc-layout" &&
            profile.RuntimeSpecializationId == "ps2-disc-layout");
        Assert.Contains(builder.Definition.ComponentCompatibilities, compatibility =>
            compatibility.ComponentTypeId == "helengine.fpscomponent" &&
            compatibility.CompatibilityKind == PlatformComponentCompatibilityKind.Transform);
        Assert.Contains(builder.Definition.ComponentCompatibilities, compatibility =>
            compatibility.ComponentTypeId == "helengine.meshcomponent" &&
            compatibility.CompatibilityKind == PlatformComponentCompatibilityKind.Transform);
        Assert.Contains(builder.Definition.GraphicsProfiles, profile => profile.ProfileId == "ps2-standard-forward");
        Assert.Contains(builder.Definition.GraphicsProfiles, profile => profile.ProfileId == "ps2-showcase-forward");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-unlit-textured");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-simple-lit-textured");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-showcase-lit-textured");
    }

    [Fact]
    public void CookMaterial_when_using_ps2_simple_lit_schema_returns_ps2_material_asset() {
        Ps2PlatformAssetBuilder builder = new();

        PlatformMaterialCookResult result = builder.CookMaterial(new PlatformMaterialCookRequest(
            "Materials/Test.helmat",
            "Materials/Test.helmat",
            "ps2",
            "ps2-default",
            "ps2-standard-forward",
            "ps2-simple-lit-textured",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["texture-relative-path"] = "cooked/textures/test.hasset",
                ["alpha-mode"] = "opaque",
                ["double-sided"] = "false",
                ["cast-shadows"] = "false",
                ["vertex-color-mode"] = "multiply"
            }));

        Ps2MaterialAsset materialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.DeserializeFromBytes(result.CookedMaterialBytes));
        Assert.Equal("ps2-standard-forward", materialAsset.RendererFamilyId);
        Assert.Equal(Ps2MaterialLightingMode.SimpleLit, materialAsset.LightingMode);
        Assert.Equal(Ps2MaterialAlphaMode.Opaque, materialAsset.AlphaMode);
        Assert.Equal(Ps2RenderClass.Opaque, materialAsset.RenderClass);
        Assert.Equal("cooked/textures/test.hasset", materialAsset.TextureRelativePath);
        Assert.False(materialAsset.DoubleSided);
        Assert.False(materialAsset.CastShadows);
        Assert.True(materialAsset.UseVertexColor);
        Assert.False(materialAsset.ExpensiveModeAllowed);
        Assert.Empty(result.ReferencedShaderAssetIds);
    }

    /// <summary>
    /// Verifies that the PS2 cooker preserves the double-sided material flag and maps translucent materials into the transparent render class.
    /// </summary>
    [Fact]
    public void CookMaterial_when_using_ps2_showcase_schema_preserves_double_sided_flag() {
        Ps2PlatformAssetBuilder builder = new();

        PlatformMaterialCookResult result = builder.CookMaterial(new PlatformMaterialCookRequest(
            "Materials/Test.helmat",
            "Materials/Test.helmat",
            "ps2",
            "ps2-default",
            "ps2-standard-forward",
            "ps2-showcase-lit-textured",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["texture-relative-path"] = "cooked/textures/test.hasset",
                ["alpha-mode"] = "alpha-blend",
                ["double-sided"] = "true",
                ["cast-shadows"] = "true",
                ["vertex-color-mode"] = "multiply",
                ["expensive-mode-allowed"] = "true",
                ["roughness"] = "0.22",
                ["specular-strength"] = "0.88",
                ["emissive-strength"] = "0.15"
            }));

        Ps2MaterialAsset materialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.DeserializeFromBytes(result.CookedMaterialBytes));
        Assert.Equal(Ps2MaterialAlphaMode.AlphaBlend, materialAsset.AlphaMode);
        Assert.Equal(Ps2RenderClass.Transparent, materialAsset.RenderClass);
        Assert.True(materialAsset.DoubleSided);
        Assert.True(materialAsset.CastShadows);
        Assert.True(materialAsset.ExpensiveModeAllowed);
        Assert.Equal(0.22f, materialAsset.Roughness);
        Assert.Equal(0.88f, materialAsset.SpecularStrength);
        Assert.Equal(0.15f, materialAsset.EmissiveStrength);
    }

    /// <summary>
    /// Verifies that alpha-test materials remain classified as a dedicated PS2 render class during cooking.
    /// </summary>
    [Fact]
    public void CookMaterial_when_using_ps2_simple_lit_schema_with_alpha_test_maps_to_alpha_test_render_class() {
        Ps2PlatformAssetBuilder builder = new();

        PlatformMaterialCookResult result = builder.CookMaterial(new PlatformMaterialCookRequest(
            "Materials/Test.helmat",
            "Materials/Test.helmat",
            "ps2",
            "ps2-default",
            "ps2-standard-forward",
            "ps2-simple-lit-textured",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["texture-relative-path"] = "cooked/textures/test.hasset",
                ["alpha-mode"] = "alpha-test",
                ["double-sided"] = "false",
                ["cast-shadows"] = "false",
                ["vertex-color-mode"] = "multiply"
            }));

        Ps2MaterialAsset materialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.DeserializeFromBytes(result.CookedMaterialBytes));
        Assert.Equal(Ps2MaterialAlphaMode.AlphaTest, materialAsset.AlphaMode);
        Assert.Equal(Ps2RenderClass.AlphaTest, materialAsset.RenderClass);
        Assert.False(materialAsset.DoubleSided);
        Assert.False(materialAsset.CastShadows);
    }

    [Fact]
    public async Task BuildAsync_WhenGivenGeneratedCoreAndCookedArtifacts_ProducesElfAndCookedTree() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "imported", "box_a.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllText(sceneOutputPath, "scene payload");
        File.WriteAllText(modelOutputPath, "model payload");
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            PlatformBuildManifest manifest = new(
                3,
                "project",
                "1.0.0",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/imported/box_a.hasset", "model:box_a", "sha256:model", "model", "shared")
                ],
                Array.Empty<PlatformBuildCodeModule>(),
                Array.Empty<PlatformArtifactPlacement>(),
                new PlatformContainerWritePlan("ps2-disc-layout", Array.Empty<PlatformContainerArtifact>()));

            PlatformBuildRequest request = new(
                manifest,
                [new PlatformBuildTargetVariant("ps2-default", "ps2", "ps2", "ps2-default")],
                [new PlatformCookProfile(
                    "ps2-default",
                    "PS2 Default",
                    new PlatformCookProfileCapabilities(
                        "ps2",
                        "raw",
                        "pcm",
                        "ps2-scene-v1",
                        PlatformSerializationEndianness.LittleEndian))],
                outputRoot,
                Path.Combine(workingRoot, "tmp"),
                selectedBuildProfileId: "ps2-default",
                selectedGraphicsProfileId: "ps2-standard-forward",
                selectedCodegenProfileId: "default",
                selectedBuildOptionValues: new Dictionary<string, string>(),
                selectedGraphicsOptionValues: new Dictionary<string, string>(),
                selectedCodegenOptionValues: new Dictionary<string, string>(),
                generatedCoreCppRootPath: generatedCoreRoot,
                selectedMediaProfileId: "ps2-install-tree",
                selectedStorageProfileId: "disc-layout");

            FakePs2NativeBuildExecutor nativeBuildExecutor = new();
            Ps2PlatformAssetBuilder builder = new(nativeBuildExecutor);
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            PlatformBuildReport report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded);
            Assert.Empty(diagnosticReporter.Diagnostics);
            Assert.Equal(2, progressReporter.Updates.Count);
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", "SYSTEM.CNF")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", Ps2BuildWorkspace.DiscExecutableFileName)));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", "cooked", "scenes", "main.hasset")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", "cooked", "imported", "box_a.hasset")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "game.iso")));
            Assert.False(File.Exists(Path.Combine(workingRoot, "tmp", "ps2-build-manifest.json")));
            Assert.Equal(generatedCoreRoot, nativeBuildExecutor.LastWorkspace.GeneratedCoreRootPath);
            Assert.True(nativeBuildExecutor.PackageIsoCalled);
        } finally {
            try {
                Directory.SetCurrentDirectory(previousDirectory);
            } catch {
            }

            try {
                if (Directory.Exists(workingRoot)) {
                    Directory.Delete(workingRoot, recursive: true);
                }
            } catch {
            }
        }
    }

    /// <summary>
    /// Verifies that the ISO packaging command opts into ISO9660 level 2 so the staged boot filename remains addressable by the PS2 BIOS.
    /// </summary>
    [Fact]
    public void CreatePackageIsoArguments_WhenUsingHelengineBootFilename_UsesIsoLevel2() {
        string outputRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Ps2BuildWorkspace workspace = new(
            "C:\\repo",
            "C:\\repo\\staging",
            "C:\\generated-core",
            outputRootPath,
            "C:\\repo\\build\\helengine_ps2.elf");

        IReadOnlyList<string> arguments = Ps2NativeBuildExecutor.CreatePackageIsoArguments(workspace);

        Assert.Contains("-iso-level", arguments);
        Assert.Contains("2", arguments);
        Assert.Equal("/export/disc", arguments[^1]);
    }

    sealed class FakePs2NativeBuildExecutor : IPs2NativeBuildExecutor {
        public Ps2BuildWorkspace LastWorkspace { get; private set; }
        public bool PackageIsoCalled { get; private set; }

        public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
            LastWorkspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            string executableDirectoryPath = Path.GetDirectoryName(workspace.NativeExecutablePath)!;
            Directory.CreateDirectory(executableDirectoryPath);
            File.WriteAllText(workspace.NativeExecutablePath, "elf");
        }

        public void PackageIso(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
            LastWorkspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            PackageIsoCalled = true;
            Directory.CreateDirectory(Path.GetDirectoryName(workspace.IsoOutputPath)!);
            File.WriteAllText(workspace.IsoOutputPath, "iso");
        }
    }
}
