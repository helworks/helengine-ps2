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
        Assert.Contains(builder.Definition.ComponentSupportRules, supportRule =>
            supportRule.ComponentTypeId == "helengine.fpscomponent" &&
            supportRule.SupportKind == PlatformComponentSupportKind.Transform);
        Assert.Contains(builder.Definition.ComponentSupportRules, supportRule =>
            supportRule.ComponentTypeId == "helengine.meshcomponent" &&
            supportRule.SupportKind == PlatformComponentSupportKind.Transform);
        Assert.Contains(builder.Definition.GraphicsProfiles, profile => profile.ProfileId == "ps2-standard-forward");
        Assert.Contains(builder.Definition.GraphicsProfiles, profile => profile.ProfileId == "ps2-showcase-forward");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-unlit-textured");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-simple-lit-textured");
        Assert.Contains(builder.Definition.MaterialSchemas, schema => schema.SchemaId == "ps2-showcase-lit-textured");
        Assert.Equal(RuntimeMaterialResolutionMode.CookedPlatformOwned, builder.Definition.RuntimeGenerationContract.MaterialResolutionMode);
        Assert.False(builder.Definition.RuntimeGenerationContract.SupportsRenderManager2DTextureReleaseFlush);
        Assert.Equal(PackagedPathPolicy.RootedOrContentRelative, builder.Definition.RuntimeGenerationContract.PackagedPathPolicy);
        Assert.True(builder.Definition.HostDebugCapability.SupportsHostDebug);
        Assert.Equal(PlatformHostDebugRunnerKind.NativeExecutable, builder.Definition.HostDebugCapability.RunnerKind);
        Assert.True(builder.Definition.HostDebugCapability.RequiresPackagedExportArtifacts);
        Assert.True(builder.Definition.HostDebugCapability.SupportsSingleStepSceneLoad);
        Assert.False(builder.Definition.HostDebugCapability.SupportsSingleStepDraw);
        Assert.Equal("ps2-host-debugger", builder.Definition.HostDebugCapability.RunnerId);
    }

    /// <summary>
    /// Verifies that the PS2 lit material schemas expose one authored base-color field for project-side standard materials.
    /// </summary>
    [Fact]
    public void Definition_when_ps2_lit_material_schemas_are_exposed_includes_base_color_field() {
        Ps2PlatformAssetBuilder builder = new();

        PlatformMaterialSchemaDefinition simpleLitSchema = Assert.Single(
            builder.Definition.MaterialSchemas,
            schema => schema.SchemaId == Ps2MaterialSchemaIds.SimpleLitTextured);
        PlatformMaterialSchemaDefinition showcaseLitSchema = Assert.Single(
            builder.Definition.MaterialSchemas,
            schema => schema.SchemaId == Ps2MaterialSchemaIds.ShowcaseLitTextured);

        PlatformMaterialFieldDefinition simpleLitBaseColorField = Assert.Single(
            simpleLitSchema.Fields,
            field => field.FieldId == "base-color");
        PlatformMaterialFieldDefinition showcaseLitBaseColorField = Assert.Single(
            showcaseLitSchema.Fields,
            field => field.FieldId == "base-color");

        Assert.Equal(PlatformMaterialFieldKind.Color, simpleLitBaseColorField.FieldKind);
        Assert.Equal("#ffffff", simpleLitBaseColorField.DefaultValue);
        Assert.Equal(PlatformMaterialFieldKind.Color, showcaseLitBaseColorField.FieldKind);
        Assert.Equal("#ffffff", showcaseLitBaseColorField.DefaultValue);
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
    /// Verifies that PS2 cooked lit materials preserve the authored base-color channels used by project-side standard materials.
    /// </summary>
    [Fact]
    public void CookMaterial_when_ps2_material_includes_base_color_persists_cooked_channels() {
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
                ["vertex-color-mode"] = "multiply",
                ["base-color"] = "#FF4040FF"
            }));

        Ps2MaterialAsset materialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.DeserializeFromBytes(result.CookedMaterialBytes));
        Assert.Equal((byte)255, ReadByteField(materialAsset, "BaseColorR"));
        Assert.Equal((byte)64, ReadByteField(materialAsset, "BaseColorG"));
        Assert.Equal((byte)64, ReadByteField(materialAsset, "BaseColorB"));
        Assert.Equal((byte)255, ReadByteField(materialAsset, "BaseColorA"));
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

    /// <summary>
    /// Verifies that the packed PS2 VU mesh payload expands indexed cube geometry into one qword-aligned triangle stream with position, normal, and texture-coordinate blocks.
    /// </summary>
    [Fact]
    public void CookPackedMesh_when_model_uses_indexed_cube_geometry_expands_triangle_stream_blocks() {
        ModelAsset cubeModelAsset = ModelUtils.GenerateCubeMesh(
            new float3(0f, 0f, 0f),
            new float3(1f, 1f, 1f));
        Ps2PackedMeshCooker cooker = new();

        byte[] packedBytes = cooker.Cook(cubeModelAsset);
        int expectedTriangleVertexCount;
        if (cubeModelAsset.Indices32 != null && cubeModelAsset.Indices32.Length > 0) {
            expectedTriangleVertexCount = cubeModelAsset.Indices32.Length;
        } else if (cubeModelAsset.Indices16 != null && cubeModelAsset.Indices16.Length > 0) {
            expectedTriangleVertexCount = cubeModelAsset.Indices16.Length;
        } else {
            expectedTriangleVertexCount = 0;
        }

        Assert.NotEmpty(packedBytes);
        Assert.Equal(0, packedBytes.Length % Ps2PackedMeshLayout.QwordSize);
        Assert.Equal(Ps2PackedMeshLayout.Version, BitConverter.ToUInt32(packedBytes, 0));

        int triangleVertexCount = BitConverter.ToInt32(packedBytes, 4);
        int positionBlockQwordOffset = BitConverter.ToInt32(packedBytes, 8);
        int normalBlockQwordOffset = BitConverter.ToInt32(packedBytes, 12);
        int texCoordBlockQwordOffset = BitConverter.ToInt32(packedBytes, 16);

        Assert.Equal(expectedTriangleVertexCount, triangleVertexCount);
        Assert.Equal(2, positionBlockQwordOffset);
        Assert.Equal(
            2 + triangleVertexCount,
            normalBlockQwordOffset);
        Assert.Equal(
            2 + (triangleVertexCount * 2),
            texCoordBlockQwordOffset);
    }

    [Fact]
    public async Task BuildAsync_WhenGivenGeneratedCoreAndCookedArtifacts_ProducesElfAndCookedTree() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string secondSceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "rendering", "directional_shadow_plaza.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "imported", "box_a.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondSceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        File.WriteAllBytes(secondSceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
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
                "ps2",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                        ]),
                    new PlatformBuildScene(
                        "Scenes/Rendering/DirectionalShadowPlaza.helen",
                        "DirectionalShadowPlaza",
                        "cooked/scenes/rendering/directional_shadow_plaza.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/rendering/directional_shadow_plaza.hasset")
                        ])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/scenes/rendering/directional_shadow_plaza.hasset", "scene:plaza", "sha256:scene2", "scene", "shared"),
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
            Assert.Equal(3, progressReporter.Updates.Count);
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", "SYSTEM.CNF")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", Ps2BuildWorkspace.DiscExecutableFileName)));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset"))));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/rendering/directional_shadow_plaza.hasset"))));
            Assert.True(File.Exists(Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/imported/box_a.hasset"))));
            Assert.True(File.Exists(Path.Combine(outputRoot, "game.iso")));
            Assert.True(File.Exists(Path.Combine(generatedCoreRoot, "runtime", "runtime_ps2_asset_path_manifest.hpp")));
            Assert.True(File.Exists(Path.Combine(generatedCoreRoot, "runtime", "runtime_ps2_asset_path_manifest.cpp")));
            Assert.True(File.Exists(Path.Combine(generatedCoreRoot, "runtime", "runtime_scene_catalog_manifest.hpp")));
            Assert.True(File.Exists(Path.Combine(generatedCoreRoot, "runtime", "runtime_scene_catalog_manifest.cpp")));
            Assert.False(File.Exists(Path.Combine(workingRoot, "tmp", "ps2-build-manifest.json")));
            string runtimeManifestSource = File.ReadAllText(Path.Combine(generatedCoreRoot, "runtime", "runtime_ps2_asset_path_manifest.cpp"));
            string runtimeSceneCatalogSource = File.ReadAllText(Path.Combine(generatedCoreRoot, "runtime", "runtime_scene_catalog_manifest.cpp"));
            Assert.Contains("he_get_runtime_ps2_startup_scene_path", runtimeManifestSource, StringComparison.Ordinal);
            Assert.Contains("cdrom0:\\\\COOKED\\\\SCENES\\\\MAIN.HAS;1", runtimeManifestSource, StringComparison.Ordinal);
            Assert.Contains("he_runtime_scene_catalog_entries", runtimeSceneCatalogSource, StringComparison.Ordinal);
            Assert.Contains("\"Scenes/Rendering/DirectionalShadowPlaza.helen\"", runtimeSceneCatalogSource, StringComparison.Ordinal);
            Assert.Contains("cdrom0:\\\\COOKED\\\\SCENES\\\\REFF7C42\\\\DIA886D3.HAS;1", runtimeSceneCatalogSource, StringComparison.Ordinal);
            Assert.DoesNotContain("he_get_runtime_ps2_asset_physical_path", runtimeManifestSource, StringComparison.Ordinal);
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
    /// Verifies that PS2 builds embed a qword-aligned packed mesh payload inside staged opaque cube model assets for the first VU path milestone.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenSceneContainsOpaqueCube_EmbedsVuPackedMeshBytesInsideCookedModelAsset() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "engine", "models", "cube.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);

        SceneAsset sceneAsset = new() {
            RootEntities = [
                new SceneEntityAsset {
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.MeshComponent",
                            ComponentIndex = 0,
                            Payload = BuildMeshComponentPayloadVersion2("cooked/engine/models/cube.hasset", Array.Empty<string>())
                        }
                    ]
                }
            ]
        };
        ModelAsset cubeModelAsset = ModelUtils.GenerateCubeMesh(
            new float3(0f, 0f, 0f),
            new float3(1f, 1f, 1f));

        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllBytes(modelOutputPath, helengine.files.AssetSerializer.SerializeToBytes(cubeModelAsset));
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            PlatformBuildManifest manifest = new(
                3,
                "project",
                "1.0.0",
                "1.0.0",
                "ps2",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                        ])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/engine/models/cube.hasset", "model:cube", "sha256:model", "model", "shared")
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

            string stagedModelPath = Path.Combine(request.WorkingRoot, "ps2-staging", "cooked", "engine", "models", "cube.hasset");
            Assert.True(File.Exists(stagedModelPath));

            ModelAsset stagedModelAsset;
            using (FileStream modelStream = File.OpenRead(stagedModelPath)) {
                stagedModelAsset = Assert.IsType<ModelAsset>(helengine.files.AssetSerializer.Deserialize(modelStream));
            }

            Assert.NotNull(stagedModelAsset.Ps2PackedMeshBytes);
            Assert.NotEmpty(stagedModelAsset.Ps2PackedMeshBytes);
            Assert.Equal(0, stagedModelAsset.Ps2PackedMeshBytes.Length % 16);

            string discModelPath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/engine/models/cube.hasset"));
            Assert.True(File.Exists(discModelPath));

            ModelAsset discModelAsset;
            using (FileStream discModelStream = File.OpenRead(discModelPath)) {
                discModelAsset = Assert.IsType<ModelAsset>(helengine.files.AssetSerializer.Deserialize(discModelStream));
            }

            Assert.NotNull(discModelAsset.Ps2PackedMeshBytes);
            Assert.NotEmpty(discModelAsset.Ps2PackedMeshBytes);
            Assert.Equal(0, discModelAsset.Ps2PackedMeshBytes.Length % 16);
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

    [Fact]
    public async Task BuildAsync_WhenPackagedSceneAndMaterialUseLogicalPaths_RewritesThemToPhysicalDiscPaths() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string fontOutputPath = Path.Combine(stagingRoot, "cooked", "fonts", "DemoDiscBody.hefont");
        string materialOutputPath = Path.Combine(stagingRoot, "cooked", "materials", "menu.hasset");
        string textureOutputPath = Path.Combine(stagingRoot, "cooked", "textures", "test.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "imported", "box_a.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fontOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(materialOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(textureOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);

        SceneAsset sceneAsset = new() {
            RootEntities = [
                new SceneEntityAsset {
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.TextComponent",
                            ComponentIndex = 0,
                            Payload = BuildTextComponentPayload("cooked/fonts/DemoDiscBody.hefont")
                        },
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.MeshComponent",
                            ComponentIndex = 1,
                            Payload = BuildMeshComponentPayload("cooked/imported/box_a.hasset", "cooked/materials/menu.hasset")
                        }
                    ]
                }
            ],
            AssetReferences = [
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "cooked/fonts/DemoDiscBody.hefont",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                },
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "cooked/materials/menu.hasset",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                }
            ]
        };
        Ps2MaterialAsset materialAsset = new() {
            TextureRelativePath = "cooked/textures/test.hasset"
        };

        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllText(fontOutputPath, "font payload");
        File.WriteAllBytes(materialOutputPath, helengine.files.AssetSerializer.SerializeToBytes(materialAsset));
        File.WriteAllText(textureOutputPath, "texture payload");
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
                "ps2",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                        ])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/fonts/DemoDiscBody.hefont", "font:body", "sha256:font", "font", "shared"),
                    new PlatformBuildArtifact("cooked/materials/menu.hasset", "material:menu", "sha256:material", "material", "shared"),
                    new PlatformBuildArtifact("cooked/textures/test.hasset", "texture:test", "sha256:texture", "asset", "shared"),
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

            string discScenePath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset"));
            string discMaterialPath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/materials/menu.hasset"));
            SceneAsset packagedSceneAsset;
            using (FileStream sceneStream = File.OpenRead(discScenePath)) {
                packagedSceneAsset = Assert.IsType<SceneAsset>(AssetSerializer.Deserialize(sceneStream));
            }

            string expectedFontPath = BuildExpectedRuntimePhysicalPath("cooked/fonts/DemoDiscBody.hefont");
            string expectedMaterialPath = BuildExpectedRuntimePhysicalPath("cooked/materials/menu.hasset");
            string expectedModelPath = BuildExpectedRuntimePhysicalPath("cooked/imported/box_a.hasset");
            string expectedTexturePath = BuildExpectedRuntimePhysicalPath("cooked/textures/test.hasset");

            Assert.Equal(expectedFontPath, packagedSceneAsset.AssetReferences[0].RelativePath);
            Assert.Equal(expectedMaterialPath, packagedSceneAsset.AssetReferences[1].RelativePath);

            SceneAssetReference textFontReference = ReadTextFontReference(packagedSceneAsset.RootEntities[0].Components[0]);
            Assert.Equal(expectedFontPath, textFontReference.RelativePath);

            ReadMeshReferencesVersion2(packagedSceneAsset.RootEntities[0].Components[1], out SceneAssetReference modelReference, out SceneAssetReference[] meshMaterialReferences, out byte renderOrder3D);
            Assert.Equal(expectedModelPath, modelReference.RelativePath);
            SceneAssetReference meshMaterialReference = Assert.Single(meshMaterialReferences);
            Assert.Equal(expectedMaterialPath, meshMaterialReference.RelativePath);
            Assert.Equal(0, renderOrder3D);

            Ps2MaterialAsset packagedMaterialAsset;
            using (FileStream materialStream = File.OpenRead(discMaterialPath)) {
                packagedMaterialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.Deserialize(materialStream));
            }

            Assert.Equal(expectedTexturePath, packagedMaterialAsset.TextureRelativePath);
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

    [Fact]
    public async Task BuildAsync_WhenPackagedSceneAndMaterialUseLogicalPaths_RewritesVersion2MeshPayloadToPhysicalDiscPaths() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string fontOutputPath = Path.Combine(stagingRoot, "cooked", "fonts", "DemoDiscBody.hefont");
        string materialOutputPath = Path.Combine(stagingRoot, "cooked", "materials", "menu.hasset");
        string textureOutputPath = Path.Combine(stagingRoot, "cooked", "textures", "test.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "imported", "box_a.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fontOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(materialOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(textureOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);

        SceneAsset sceneAsset = new() {
            RootEntities = [
                new SceneEntityAsset {
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.TextComponent",
                            ComponentIndex = 0,
                            Payload = BuildTextComponentPayload("cooked/fonts/DemoDiscBody.hefont")
                        },
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.MeshComponent",
                            ComponentIndex = 1,
                            Payload = BuildMeshComponentPayloadVersion2(
                                "cooked/imported/box_a.hasset",
                                ["cooked/materials/menu.hasset"])
                        }
                    ]
                }
            ],
            AssetReferences = [
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "cooked/fonts/DemoDiscBody.hefont",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                },
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "cooked/materials/menu.hasset",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                }
            ]
        };
        Ps2MaterialAsset materialAsset = new() {
            TextureRelativePath = "cooked/textures/test.hasset"
        };

        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllText(fontOutputPath, "font payload");
        File.WriteAllBytes(materialOutputPath, helengine.files.AssetSerializer.SerializeToBytes(materialAsset));
        File.WriteAllText(textureOutputPath, "texture payload");
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
                "ps2",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                        ])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/fonts/DemoDiscBody.hefont", "font:body", "sha256:font", "font", "shared"),
                    new PlatformBuildArtifact("cooked/materials/menu.hasset", "material:menu", "sha256:material", "material", "shared"),
                    new PlatformBuildArtifact("cooked/textures/test.hasset", "texture:test", "sha256:texture", "asset", "shared"),
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

            string discScenePath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset"));
            string discMaterialPath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/materials/menu.hasset"));
            SceneAsset packagedSceneAsset;
            using (FileStream sceneStream = File.OpenRead(discScenePath)) {
                packagedSceneAsset = Assert.IsType<SceneAsset>(AssetSerializer.Deserialize(sceneStream));
            }

            string expectedFontPath = BuildExpectedRuntimePhysicalPath("cooked/fonts/DemoDiscBody.hefont");
            string expectedMaterialPath = BuildExpectedRuntimePhysicalPath("cooked/materials/menu.hasset");
            string expectedModelPath = BuildExpectedRuntimePhysicalPath("cooked/imported/box_a.hasset");
            string expectedTexturePath = BuildExpectedRuntimePhysicalPath("cooked/textures/test.hasset");

            Assert.Equal(expectedFontPath, packagedSceneAsset.AssetReferences[0].RelativePath);
            Assert.Equal(expectedMaterialPath, packagedSceneAsset.AssetReferences[1].RelativePath);

            SceneAssetReference textFontReference = ReadTextFontReference(packagedSceneAsset.RootEntities[0].Components[0]);
            Assert.Equal(expectedFontPath, textFontReference.RelativePath);

            ReadMeshReferencesVersion2(packagedSceneAsset.RootEntities[0].Components[1], out SceneAssetReference modelReference, out SceneAssetReference[] meshMaterialReferences, out byte renderOrder3D);
            Assert.Equal(expectedModelPath, modelReference.RelativePath);
            SceneAssetReference meshMaterialReference = Assert.Single(meshMaterialReferences);
            Assert.Equal(expectedMaterialPath, meshMaterialReference.RelativePath);
            Assert.Equal(0, renderOrder3D);

            Ps2MaterialAsset packagedMaterialAsset;
            using (FileStream materialStream = File.OpenRead(discMaterialPath)) {
                packagedMaterialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.Deserialize(materialStream));
            }

            Assert.Equal(expectedTexturePath, packagedMaterialAsset.TextureRelativePath);
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

    [Fact]
    public async Task BuildAsync_WhenPackagedEngineMatMaterialUsesImportedTexture_RewritesTexturePathToPhysicalDiscPath() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string materialOutputPath = Path.Combine(stagingRoot, "cooked", "engine", "mat", "Cube00", "Cube00.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "imported", "box_a.hasset");
        string importedTextureOutputPath = Path.Combine(stagingRoot, "imported", "52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149", "52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(materialOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(importedTextureOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);

        SceneAsset sceneAsset = new() {
            RootEntities = [
                new SceneEntityAsset {
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.MeshComponent",
                            ComponentIndex = 0,
                            Payload = BuildMeshComponentPayload("cooked/imported/box_a.hasset", "cooked/engine/mat/Cube00/Cube00.hasset")
                        }
                    ]
                }
            ],
            AssetReferences = [
                new SceneAssetReference {
                    SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                    RelativePath = "cooked/engine/mat/Cube00/Cube00.hasset",
                    ProviderId = string.Empty,
                    AssetId = string.Empty
                }
            ]
        };
        Ps2MaterialAsset materialAsset = new() {
            TextureRelativePath = "imported/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149.hasset"
        };

        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllBytes(materialOutputPath, helengine.files.AssetSerializer.SerializeToBytes(materialAsset));
        File.WriteAllText(modelOutputPath, "model payload");
        File.WriteAllText(importedTextureOutputPath, "texture payload");
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            PlatformBuildManifest manifest = new(
                3,
                "project",
                "1.0.0",
                "1.0.0",
                "ps2",
                "1.0.0",
                "Scenes/Main.helen",
                [
                    new PlatformBuildScene(
                        "Scenes/Main.helen",
                        "Main",
                        "cooked/scenes/main.hasset",
                        [],
                        [
                            new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                        ])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared"),
                    new PlatformBuildArtifact("cooked/engine/mat/Cube00/Cube00.hasset", "material:cube00", "sha256:material", "material", "shared"),
                    new PlatformBuildArtifact("cooked/imported/box_a.hasset", "model:box_a", "sha256:model", "model", "shared"),
                    new PlatformBuildArtifact("imported/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149.hasset", "texture:cube00", "sha256:texture", "asset", "shared")
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

            string discMaterialPath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/engine/mat/Cube00/Cube00.hasset"));
            Ps2MaterialAsset packagedMaterialAsset;
            using (FileStream materialStream = File.OpenRead(discMaterialPath)) {
                packagedMaterialAsset = Assert.IsType<Ps2MaterialAsset>(AssetSerializer.Deserialize(materialStream));
            }

            string expectedTexturePath = BuildExpectedRuntimePhysicalPath("imported/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149/52368b2561628cadf8662a7975820dcec0e2c0338ec130a4886537df258ff149.hasset");
            Assert.Equal(expectedTexturePath, packagedMaterialAsset.TextureRelativePath);
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

    static string BuildExpectedRuntimePhysicalPath(string logicalRelativePath) {
        string discRelativePath = Ps2DiscPathResolver.ResolveDiscRelativePath(logicalRelativePath).Replace('/', '\\');
        return "cdrom0:\\" + discRelativePath + ";1";
    }

    static byte ReadByteField(object instance, string fieldName) {
        if (instance == null) {
            throw new ArgumentNullException(nameof(instance));
        } else if (string.IsNullOrWhiteSpace(fieldName)) {
            throw new ArgumentException("Field name must be provided.", nameof(fieldName));
        }

        System.Reflection.FieldInfo field = instance.GetType().GetField(fieldName);
        Assert.NotNull(field);
        return Assert.IsType<byte>(field.GetValue(instance));
    }

    static byte[] BuildTextComponentPayload(string fontRelativePath) {
        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(1);
        WriteOptionalReference(writer, new SceneAssetReference {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = fontRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        });
        writer.WriteString("Demo Disc");
        writer.WriteByte(0);
        writer.WriteInt2(new int2(320, 64));
        writer.WriteByte(255);
        writer.WriteByte(255);
        writer.WriteByte(255);
        writer.WriteByte(255);
        writer.WriteFloat4(new float4(0f, 0f, 1f, 1f));
        writer.WriteSingle(0f);
        writer.WriteByte(1);
        writer.WriteByte(1);
        writer.WriteByte(0);
        return stream.ToArray();
    }

    static byte[] BuildMeshComponentPayload(string modelRelativePath, string materialRelativePath) {
        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        writer.WriteByte(1);
        WriteOptionalReference(writer, new SceneAssetReference {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = modelRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        });
        WriteOptionalReference(writer, new SceneAssetReference {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = materialRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        });
        writer.WriteByte(0);
        return stream.ToArray();
    }

    static byte[] BuildMeshComponentPayloadVersion2(string modelRelativePath, IReadOnlyList<string> materialRelativePaths) {
        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        SceneAssetReference modelReference = new() {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = modelRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        };
        SceneAssetReference[] materialReferences = new SceneAssetReference[materialRelativePaths.Count];
        for (int materialIndex = 0; materialIndex < materialRelativePaths.Count; materialIndex++) {
            materialReferences[materialIndex] = new SceneAssetReference {
                SourceKind = SceneAssetReferenceSourceKind.FileSystem,
                RelativePath = materialRelativePaths[materialIndex],
                ProviderId = string.Empty,
                AssetId = string.Empty
            };
        }

        MeshComponentScenePayloadSerializer.Write(writer, modelReference, materialReferences, 0);
        return stream.ToArray();
    }

    static SceneAssetReference ReadTextFontReference(SceneComponentAssetRecord record) {
        using MemoryStream stream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(stream, EngineBinaryEndianness.LittleEndian);
        Assert.Equal(1, reader.ReadByte());
        return ReadOptionalReference(reader);
    }

    static void ReadMeshReferencesVersion2(
        SceneComponentAssetRecord record,
        out SceneAssetReference modelReference,
        out SceneAssetReference[] materialReferences,
        out byte renderOrder3D) {
        using MemoryStream stream = new(record.Payload ?? Array.Empty<byte>(), false);
        using EngineBinaryReader reader = EngineBinaryReader.Create(stream, EngineBinaryEndianness.LittleEndian);
        MeshComponentScenePayloadSerializer.Read(reader, out modelReference, out materialReferences, out renderOrder3D);
        Assert.Equal(MeshComponentScenePayloadSerializer.CurrentVersion, record.Payload[0]);
    }

    static SceneAssetReference ReadOptionalReference(EngineBinaryReader reader) {
        if (reader.ReadByte() == 0) {
            return null;
        }

        return new SceneAssetReference {
            SourceKind = (SceneAssetReferenceSourceKind)reader.ReadInt32(),
            RelativePath = reader.ReadString(),
            ProviderId = reader.ReadString(),
            AssetId = reader.ReadString()
        };
    }

    static void WriteOptionalReference(EngineBinaryWriter writer, SceneAssetReference reference) {
        if (reference == null) {
            writer.WriteByte(0);
            return;
        }

        writer.WriteByte(1);
        writer.WriteInt32((int)reference.SourceKind);
        writer.WriteString(reference.RelativePath);
        writer.WriteString(reference.ProviderId);
        writer.WriteString(reference.AssetId);
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





