using helengine;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Results;
using helengine.baseplatform.Targets;
using helengine.editor;
using helengine.files;
using helengine.ps2.builder;
using System.Reflection;
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
    /// Verifies that the PS2 builder publishes builder-owned texture cook capabilities with default serialized settings.
    /// </summary>
    [Fact]
    public void Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_texture_and_font_atlas_defaults() {
        Ps2PlatformAssetBuilder builder = new();

        PlatformAssetCookCapabilityDefinition[] capabilities = builder.Definition.AssetCookCapabilities
            .OrderBy(capability => capability.SourceAssetKind, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            capabilities,
            fontAtlasCapability => {
                Assert.Equal("font-atlas-texture", fontAtlasCapability.SourceAssetKind);
                Assert.Equal("runtime-texture", fontAtlasCapability.TargetArtifactKind);
                Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, fontAtlasCapability.OwnershipKind);
                Assert.Equal("ps2-font-atlas-texture", fontAtlasCapability.SettingsContractId);
                Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", fontAtlasCapability.DefaultSerializedPlatformSettings);
            },
            textureCapability => {
                Assert.Equal("texture", textureCapability.SourceAssetKind);
                Assert.Equal("runtime-texture", textureCapability.TargetArtifactKind);
                Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, textureCapability.OwnershipKind);
                Assert.Equal("ps2-texture", textureCapability.SettingsContractId);
                Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", textureCapability.DefaultSerializedPlatformSettings);
            });
    }

    /// <summary>
    /// Verifies that the PS2 builder publishes generic texture-format capability metadata for both image textures and font atlas textures.
    /// </summary>
    [Fact]
    public void Definition_when_ps2_builder_owned_texture_capabilities_are_published_exposes_generic_texture_format_metadata() {
        Ps2PlatformAssetBuilder builder = new();

        Assert.Collection(
            builder.Definition.AssetCookCapabilities.OrderBy(capability => capability.SourceAssetKind, StringComparer.Ordinal),
            capability => {
                Assert.Equal("font-atlas-texture", capability.SourceAssetKind);
                Assert.Equal("runtime-texture", capability.TargetArtifactKind);
                Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, capability.OwnershipKind);
                Assert.Equal("ps2-font-atlas-texture", capability.SettingsContractId);
                Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", capability.DefaultSerializedPlatformSettings);
                AssertPs2TextureFormatCapabilities(capability.TextureFormatCapabilities);
            },
            capability => {
                Assert.Equal("texture", capability.SourceAssetKind);
                Assert.Equal("runtime-texture", capability.TargetArtifactKind);
                Assert.Equal(PlatformAssetCookOwnershipKind.BuilderOwned, capability.OwnershipKind);
                Assert.Equal("ps2-texture", capability.SettingsContractId);
                Assert.Equal("{\"maxResolution\":0,\"colorFormat\":\"Rgba32\",\"alphaPrecision\":\"A8\"}", capability.DefaultSerializedPlatformSettings);
                AssertPs2TextureFormatCapabilities(capability.TextureFormatCapabilities);
            });
    }

    /// <summary>
    /// Verifies that the PS2 builder executes one texture work item and writes the runtime payload to the declared output path.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string sourceTexturePath = Path.Combine(workingRoot, "source", "logo.png");
        string outputRelativePath = "cooked/textures/logo.hasset";

        Directory.CreateDirectory(Path.GetDirectoryName(sourceTexturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        File.WriteAllBytes(sourceTexturePath, CreateSinglePixelPngBytes());
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            Ps2PlatformAssetBuilder builder = new(new FakePs2NativeBuildExecutor());
            PlatformBuildManifest manifest = CreateManifestWithTextureWorkItem(
                sourceTexturePath,
                "texture",
                outputRelativePath,
                "runtime-texture:logo",
                Ps2TextureCookSettingsSerializer.Serialize(new TextureAssetProcessorSettings {
                    MaxResolution = 32,
                    ColorFormat = TextureAssetColorFormat.Rgba32,
                    AlphaPrecision = TextureAssetAlphaPrecision.A8
                }));
            PlatformBuildRequest request = CreateBuildRequest(manifest, outputRoot, workingRoot, generatedCoreRoot);
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            PlatformBuildReport report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded);
            string stagedTexturePath = Path.Combine(request.WorkingRoot, "ps2-staging", "cooked", "textures", "logo.hasset");
            Assert.True(File.Exists(stagedTexturePath));
            using FileStream textureStream = File.OpenRead(stagedTexturePath);
            Ps2TextureAsset textureAsset = Assert.IsType<Ps2TextureAsset>(AssetSerializer.Deserialize(textureStream));
            Assert.Equal((ushort)1, textureAsset.Width);
            Assert.Equal((ushort)1, textureAsset.Height);
            Assert.Equal(Ps2TextureFormat.Rgba32, textureAsset.Format);
            Assert.Equal(Ps2TextureAlphaMode.Full, textureAsset.AlphaMode);
            Assert.NotNull(textureAsset.PixelData);
            Assert.NotEmpty(textureAsset.PixelData);
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);

            if (Directory.Exists(workingRoot)) {
                Directory.Delete(workingRoot, true);
            }
        }
    }

    /// <summary>
    /// Verifies one PS2 texture cook capability advertises the expected supported formats and valid combinations.
    /// </summary>
    /// <param name="textureFormatCapabilities">Texture capability metadata to validate.</param>
    static void AssertPs2TextureFormatCapabilities(PlatformTextureFormatCapabilityDefinition textureFormatCapabilities) {
        Assert.NotNull(textureFormatCapabilities);
        Assert.Equal(
            [TextureAssetColorFormat.Rgba32],
            textureFormatCapabilities.SupportedColorFormats);
        Assert.Equal(
            [TextureAssetAlphaPrecision.A8],
            textureFormatCapabilities.SupportedAlphaPrecisions);
        Assert.Collection(
            textureFormatCapabilities.SupportedCombinations,
            combination => {
                Assert.Equal(TextureAssetColorFormat.Rgba32, combination.ColorFormat);
                Assert.Equal(TextureAssetAlphaPrecision.A8, combination.AlphaPrecision);
            });
    }

    /// <summary>
    /// Verifies that the PS2 builder can execute one texture work item using the published capability default settings payload.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenTextureCookWorkItemUsesCapabilityDefaults_WritesPs2RuntimeTextureWithDefaultSettings() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string sourceTexturePath = Path.Combine(workingRoot, "source", "default-logo.png");
        string outputRelativePath = "cooked/textures/default-logo.hasset";

        Directory.CreateDirectory(Path.GetDirectoryName(sourceTexturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        File.WriteAllBytes(sourceTexturePath, CreateSinglePixelPngBytes());
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            Ps2PlatformAssetBuilder builder = new(new FakePs2NativeBuildExecutor());
            PlatformAssetCookCapabilityDefinition textureCapability = Assert.Single(
                builder.Definition.AssetCookCapabilities,
                capability => capability.SourceAssetKind == "texture");
            PlatformBuildManifest manifest = CreateManifestWithTextureWorkItem(
                sourceTexturePath,
                "texture",
                outputRelativePath,
                "runtime-texture:logo",
                textureCapability.DefaultSerializedPlatformSettings);
            PlatformBuildRequest request = CreateBuildRequest(manifest, outputRoot, workingRoot, generatedCoreRoot);
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            PlatformBuildReport report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded);
            string stagedTexturePath = Path.Combine(request.WorkingRoot, "ps2-staging", "cooked", "textures", "default-logo.hasset");
            Assert.True(File.Exists(stagedTexturePath));
            using FileStream textureStream = File.OpenRead(stagedTexturePath);
            Ps2TextureAsset textureAsset = Assert.IsType<Ps2TextureAsset>(AssetSerializer.Deserialize(textureStream));
            Assert.Equal(Ps2TextureFormat.Rgba32, textureAsset.Format);
            Assert.Equal(Ps2TextureAlphaMode.Full, textureAsset.AlphaMode);
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);

            if (Directory.Exists(workingRoot)) {
                Directory.Delete(workingRoot, true);
            }
        }
    }

    /// <summary>
    /// Verifies that the PS2 builder executes one editor-owned font-atlas texture work item into the declared runtime output path.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenFontAtlasTextureCookWorkItemIsPresent_WritesPs2RuntimeTextureToDeclaredOutputPath() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string sourceTexturePath = Path.Combine(workingRoot, "source", "body-atlas.png");
        string outputRelativePath = "cooked/fonts/body-atlas.ps2tex";

        Directory.CreateDirectory(Path.GetDirectoryName(sourceTexturePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        File.WriteAllBytes(sourceTexturePath, CreateSinglePixelPngBytes());
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(stagingRoot);

            Ps2PlatformAssetBuilder builder = new(new FakePs2NativeBuildExecutor());
            PlatformAssetCookCapabilityDefinition textureCapability = Assert.Single(
                builder.Definition.AssetCookCapabilities,
                capability => capability.SourceAssetKind == "font-atlas-texture");
            PlatformBuildManifest manifest = CreateManifestWithTextureWorkItem(
                sourceTexturePath,
                "font-atlas-texture",
                outputRelativePath,
                "runtime-texture:body-atlas",
                textureCapability.DefaultSerializedPlatformSettings);
            PlatformBuildRequest request = CreateBuildRequest(manifest, outputRoot, workingRoot, generatedCoreRoot);
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            PlatformBuildReport report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded);
            string stagedTexturePath = Path.Combine(request.WorkingRoot, "ps2-staging", "cooked", "fonts", "body-atlas.ps2tex");
            Assert.True(File.Exists(stagedTexturePath));
            using FileStream textureStream = File.OpenRead(stagedTexturePath);
            Ps2TextureAsset textureAsset = Assert.IsType<Ps2TextureAsset>(AssetSerializer.Deserialize(textureStream));
            Assert.Equal((ushort)1, textureAsset.Width);
            Assert.Equal((ushort)1, textureAsset.Height);
            Assert.Equal(Ps2TextureFormat.Rgba32, textureAsset.Format);
            Assert.Equal(Ps2TextureAlphaMode.Full, textureAsset.AlphaMode);
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);

            if (Directory.Exists(workingRoot)) {
                Directory.Delete(workingRoot, true);
            }
        }
    }

    /// <summary>
    /// Verifies that one real editor-owned PS2 manifest emits texture work items and that the PS2 builder consumes them into PS2-native packaged outputs.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenManifestComesFromEditorCookService_ConsumesPs2TextureWorkItemsAndPackagesPs2NativeOutputs() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string projectRoot = Path.Combine(workingRoot, "project");
        string buildRoot = Path.Combine(workingRoot, "editor-build");
        string outputRoot = Path.Combine(workingRoot, "out");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneId = "Scenes/Main.helen";
        string textureRelativePath = "Textures/Cube00.png";
        string fontRelativePath = "Fonts/Body.hefont";

        Directory.CreateDirectory(Path.Combine(projectRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "cache", "shader-cache"));
        Directory.CreateDirectory(buildRoot);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string textureSourcePath = ResolveProjectAssetPath(projectRoot, textureRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(textureSourcePath)!);
        File.WriteAllBytes(textureSourcePath, CreateSinglePixelPngBytes());
        WritePackagedSourceFontAsset(projectRoot, fontRelativePath);
        WriteSceneAssetWithSpriteAndFont(projectRoot, sceneId, textureRelativePath, fontRelativePath);

        Ps2PlatformAssetBuilder builder = new(new FakePs2NativeBuildExecutor());
        PlatformBuildManifest manifest = InvokeEditorPlatformAssetCookServiceCook(projectRoot, buildRoot, builder.Definition, builder);
        PlatformCookWorkItem textureWorkItem = Assert.Single(
            manifest.PlatformCookWorkItems,
            workItem => string.Equals(workItem.SourceAssetKind, "texture", StringComparison.OrdinalIgnoreCase));
        PlatformCookWorkItem fontAtlasWorkItem = Assert.Single(
            manifest.PlatformCookWorkItems,
            workItem => string.Equals(workItem.SourceAssetKind, "font-atlas-texture", StringComparison.OrdinalIgnoreCase));

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(buildRoot);

            PlatformBuildRequest request = CreateBuildRequest(manifest, outputRoot, workingRoot, generatedCoreRoot);
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            PlatformBuildReport report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded, string.Join(Environment.NewLine, report.Diagnostics.Select(diagnostic => diagnostic.Message)));

            using FileStream textureStream = File.OpenRead(ResolveRelativeOutputPathInDirectory(Path.Combine(workingRoot, "ps2-staging"), textureWorkItem.OutputRelativePath));
            Ps2TextureAsset runtimeTexture = Assert.IsType<Ps2TextureAsset>(AssetSerializer.Deserialize(textureStream));
            Assert.Equal(Ps2TextureFormat.Rgba32, runtimeTexture.Format);

            using FileStream fontAtlasStream = File.OpenRead(ResolveRelativeOutputPathInDirectory(Path.Combine(workingRoot, "ps2-staging"), fontAtlasWorkItem.OutputRelativePath));
            Ps2TextureAsset fontAtlasTexture = Assert.IsType<Ps2TextureAsset>(AssetSerializer.Deserialize(fontAtlasStream));
            Assert.Equal(Ps2TextureFormat.Rgba32, fontAtlasTexture.Format);

            PlatformBuildArtifact cookedFontArtifact = Assert.Single(
                manifest.CookedArtifacts,
                artifact => artifact.RelativePath.EndsWith(".hefont", StringComparison.OrdinalIgnoreCase));
            using FileStream cookedFontStream = File.OpenRead(ResolveRelativeOutputPathInDirectory(buildRoot, cookedFontArtifact.RelativePath));
            FontAsset cookedFont = helengine.files.FontAssetBinarySerializer.Deserialize(cookedFontStream);
            Assert.Equal(fontAtlasWorkItem.OutputRelativePath, cookedFont.CookedAtlasTextureRelativePath);
            Assert.Null(cookedFont.SourceTextureAsset);
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);

            if (Directory.Exists(workingRoot)) {
                Directory.Delete(workingRoot, true);
            }
        }
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
    public async Task BuildAsync_WhenGeneratedMeshReferencesUseLogicalPaths_RewritesGeneratedModelAndMaterialPathsToPhysicalDiscPaths() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string stagingRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneOutputPath = Path.Combine(stagingRoot, "cooked", "scenes", "main.hasset");
        string materialOutputPath = Path.Combine(stagingRoot, "cooked", "engine", "materials", "standard.hasset");
        string modelOutputPath = Path.Combine(stagingRoot, "cooked", "engine", "models", "cube.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(materialOutputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelOutputPath)!);
        Directory.CreateDirectory(generatedCoreRoot);

        SceneAsset sceneAsset = new() {
            RootEntities = [
                new SceneEntityAsset {
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.MeshComponent",
                            ComponentIndex = 0,
                            Payload = BuildMeshComponentPayloadVersion2(
                                CreateSceneReference(
                                    SceneAssetReferenceSourceKind.Generated,
                                    "cooked/engine/models/cube.hasset",
                                    "engine",
                                    "engine:model:cube"),
                                [
                                    CreateSceneReference(
                                        SceneAssetReferenceSourceKind.Generated,
                                        "cooked/engine/materials/standard.hasset",
                                        "engine",
                                        "engine:material:standard")
                                ])
                        }
                    ]
                }
            ],
            AssetReferences = [
                CreateSceneReference(
                    SceneAssetReferenceSourceKind.Generated,
                    "cooked/engine/models/cube.hasset",
                    "engine",
                    "engine:model:cube"),
                CreateSceneReference(
                    SceneAssetReferenceSourceKind.Generated,
                    "cooked/engine/materials/standard.hasset",
                    "engine",
                    "engine:material:standard")
            ]
        };
        ModelAsset cubeModelAsset = ModelUtils.GenerateCubeMesh(
            new float3(0f, 0f, 0f),
            new float3(1f, 1f, 1f));
        Ps2MaterialAsset materialAsset = new() {
            RendererFamilyId = "ps2-standard-forward",
            AlphaMode = Ps2MaterialAlphaMode.Opaque,
            RenderClass = Ps2RenderClass.Opaque,
            LightingMode = Ps2MaterialLightingMode.SimpleLit,
            BaseColorR = 0xFF,
            BaseColorG = 0xFF,
            BaseColorB = 0xFF,
            BaseColorA = 0xFF,
            TextureRelativePath = string.Empty
        };

        File.WriteAllBytes(sceneOutputPath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
        File.WriteAllBytes(modelOutputPath, helengine.files.AssetSerializer.SerializeToBytes(cubeModelAsset));
        File.WriteAllBytes(materialOutputPath, helengine.files.AssetSerializer.SerializeToBytes(materialAsset));
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
                    new PlatformBuildArtifact("cooked/engine/materials/standard.hasset", "material:standard", "sha256:material", "material", "shared"),
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

            string discScenePath = Path.Combine(outputRoot, "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset"));
            SceneAsset packagedSceneAsset;
            using (FileStream sceneStream = File.OpenRead(discScenePath)) {
                packagedSceneAsset = Assert.IsType<SceneAsset>(AssetSerializer.Deserialize(sceneStream));
            }

            string expectedModelPath = BuildExpectedRuntimePhysicalPath("cooked/engine/models/cube.hasset");
            string expectedMaterialPath = BuildExpectedRuntimePhysicalPath("cooked/engine/materials/standard.hasset");

            ReadMeshReferencesVersion2(packagedSceneAsset.RootEntities[0].Components[0], out SceneAssetReference modelReference, out SceneAssetReference[] meshMaterialReferences, out byte renderOrder3D);
            Assert.Equal(SceneAssetReferenceSourceKind.Generated, modelReference.SourceKind);
            Assert.Equal(expectedModelPath, modelReference.RelativePath);
            Assert.Equal("engine", modelReference.ProviderId);
            Assert.Equal("engine:model:cube", modelReference.AssetId);

            SceneAssetReference meshMaterialReference = Assert.Single(meshMaterialReferences);
            Assert.Equal(SceneAssetReferenceSourceKind.Generated, meshMaterialReference.SourceKind);
            Assert.Equal(expectedMaterialPath, meshMaterialReference.RelativePath);
            Assert.Equal("engine", meshMaterialReference.ProviderId);
            Assert.Equal("engine:material:standard", meshMaterialReference.AssetId);
            Assert.Equal(0, renderOrder3D);

            Assert.Equal(expectedModelPath, packagedSceneAsset.AssetReferences[0].RelativePath);
            Assert.Equal(expectedMaterialPath, packagedSceneAsset.AssetReferences[1].RelativePath);
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

    static byte[] BuildTaggedTextComponentPayload(string fontRelativePath) {
        EditorTaggedSceneComponentFieldWriter writer = new();
        writer.WriteField("FontReference", fieldWriter => SceneComponentBinaryFieldEncoding.WriteOptionalReference(fieldWriter, new SceneAssetReference {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = fontRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        }));
        writer.WriteField("Text", fieldWriter => fieldWriter.WriteString("Demo Disc"));
        writer.WriteField("WrapText", fieldWriter => fieldWriter.WriteByte(0));
        writer.WriteField("Size", fieldWriter => fieldWriter.WriteInt2(new int2(320, 64)));
        writer.WriteField("Color", fieldWriter => SceneComponentBinaryFieldEncoding.WriteByte4(fieldWriter, new byte4(255, 255, 255, 255)));
        writer.WriteField("SourceRect", fieldWriter => fieldWriter.WriteFloat4(new float4(0f, 0f, 1f, 1f)));
        writer.WriteField("Rotation", fieldWriter => fieldWriter.WriteSingle(0f));
        writer.WriteField("RenderOrder2D", fieldWriter => fieldWriter.WriteByte(1));
        writer.WriteField("LayerMask", fieldWriter => fieldWriter.WriteByte(1));
        writer.WriteField("SelectionEnabled", fieldWriter => fieldWriter.WriteByte(0));
        return writer.BuildPayload();
    }

    static byte[] BuildTaggedSpriteComponentPayload(string textureRelativePath) {
        EditorTaggedSceneComponentFieldWriter writer = new();
        writer.WriteField("TextureReference", fieldWriter => SceneComponentBinaryFieldEncoding.WriteOptionalReference(fieldWriter, new SceneAssetReference {
            SourceKind = SceneAssetReferenceSourceKind.FileSystem,
            RelativePath = textureRelativePath,
            ProviderId = string.Empty,
            AssetId = string.Empty
        }));
        writer.WriteField("SourceRect", fieldWriter => fieldWriter.WriteFloat4(new float4(0f, 0f, 1f, 1f)));
        writer.WriteField("Size", fieldWriter => fieldWriter.WriteInt2(new int2(64, 64)));
        writer.WriteField("Color", fieldWriter => SceneComponentBinaryFieldEncoding.WriteByte4(fieldWriter, new byte4(255, 255, 255, 255)));
        writer.WriteField("Rotation", fieldWriter => fieldWriter.WriteSingle(0f));
        writer.WriteField("RenderOrder2D", fieldWriter => fieldWriter.WriteByte(0));
        writer.WriteField("LayerMask", fieldWriter => fieldWriter.WriteByte(1));
        return writer.BuildPayload();
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
        SceneAssetReference modelReference = CreateSceneReference(SceneAssetReferenceSourceKind.FileSystem, modelRelativePath, string.Empty, string.Empty);
        SceneAssetReference[] materialReferences = new SceneAssetReference[materialRelativePaths.Count];
        for (int materialIndex = 0; materialIndex < materialRelativePaths.Count; materialIndex++) {
            materialReferences[materialIndex] = CreateSceneReference(SceneAssetReferenceSourceKind.FileSystem, materialRelativePaths[materialIndex], string.Empty, string.Empty);
        }

        MeshComponentScenePayloadSerializer.Write(writer, modelReference, materialReferences, 0);
        return stream.ToArray();
    }

    static byte[] BuildMeshComponentPayloadVersion2(SceneAssetReference modelReference, IReadOnlyList<SceneAssetReference> materialReferences) {
        using MemoryStream stream = new();
        using EngineBinaryWriter writer = EngineBinaryWriter.Create(stream, EngineBinaryEndianness.LittleEndian);
        MeshComponentScenePayloadSerializer.Write(writer, modelReference, materialReferences.ToArray(), 0);
        return stream.ToArray();
    }

    static SceneAssetReference CreateSceneReference(SceneAssetReferenceSourceKind sourceKind, string relativePath, string providerId, string assetId) {
        return new SceneAssetReference {
            SourceKind = sourceKind,
            RelativePath = relativePath,
            ProviderId = providerId,
            AssetId = assetId
        };
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

    /// <summary>
    /// Resolves one runtime-relative output path beneath one base directory.
    /// </summary>
    /// <param name="baseDirectory">Base directory that owns the runtime-relative file.</param>
    /// <param name="relativePath">Runtime-relative path stored in manifests and work items.</param>
    /// <returns>Absolute filesystem path for the runtime-relative file.</returns>
    static string ResolveRelativeOutputPathInDirectory(string baseDirectory, string relativePath) {
        if (string.IsNullOrWhiteSpace(baseDirectory)) {
            throw new ArgumentException("Base directory must be provided.", nameof(baseDirectory));
        }
        if (string.IsNullOrWhiteSpace(relativePath)) {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        return Path.Combine(baseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Writes one minimal Wavefront OBJ source file that the editor cook service can import for mesh packaging.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source asset.</param>
    /// <param name="modelRelativePath">Project-relative model path to create.</param>
    static void WriteSourceModelAsset(string projectRoot, string modelRelativePath) {
        string sourceModelPath = ResolveProjectAssetPath(projectRoot, modelRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceModelPath)!);
        File.WriteAllText(sourceModelPath, "o Triangle\nv 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\nf 1/1/1 2/2/1 3/3/1\n");
    }

    /// <summary>
    /// Writes one packaged source font asset into the project assets root so the editor cook service can externalize its atlas through a PS2 work item.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source asset.</param>
    /// <param name="fontRelativePath">Project-relative source font path to create.</param>
    static void WritePackagedSourceFontAsset(string projectRoot, string fontRelativePath) {
        string fontSourcePath = ResolveProjectAssetPath(projectRoot, fontRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fontSourcePath)!);

        FontAsset fontAsset = new SingleGlyphFontImporter().ImportFont(new MemoryStream(new byte[] { 0x01 }));
        using FileStream stream = new(fontSourcePath, FileMode.Create, FileAccess.Write, FileShare.None);
        helengine.files.FontAssetBinarySerializer.Serialize(stream, fontAsset);
    }

    /// <summary>
    /// Writes one material settings document that resolves through the PS2 builder's standard-shader translation path.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source asset.</param>
    /// <param name="materialRelativePath">Project-relative material path to write.</param>
    /// <param name="diffuseTextureAssetId">Imported texture asset id referenced by the material.</param>
    static void WritePs2StandardMaterialAsset(string projectRoot, string materialRelativePath, string diffuseTextureAssetId) {
        string materialPath = ResolveProjectAssetPath(projectRoot, materialRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath)!);

        MaterialAssetImportSettings settings = new() {
            Importer = new AssetImporterSettings {
                ImporterId = "helengine.material",
                SourceChecksum = string.Empty,
                AssetId = materialRelativePath
            },
            Processor = new MaterialAssetProcessorPlatformSettings()
        };
        settings.Processor.Platforms["ps2"] = new MaterialAssetProcessorSettings {
            SchemaId = "standard-shader",
            FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["use-custom-shader"] = "false",
                ["texture-id"] = diffuseTextureAssetId ?? string.Empty,
                ["casts-shadow"] = "true",
                ["receives-shadow"] = "true",
                ["base-color"] = "#FFFFFFFF"
            }
        };

        MaterialAssetSettingsService settingsService = new();
        settingsService.Save(materialPath, settings);
    }

    /// <summary>
    /// Writes one scene asset that references a source texture and a packaged source font through tagged sprite and text component payloads.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source scene.</param>
    /// <param name="sceneId">Scene asset identifier to write.</param>
    /// <param name="textureRelativePath">Project-relative texture path referenced by the sprite component.</param>
    /// <param name="fontRelativePath">Project-relative font path referenced by the text component.</param>
    static void WriteSceneAssetWithSpriteAndFont(
        string projectRoot,
        string sceneId,
        string textureRelativePath,
        string fontRelativePath) {
        string scenePath = ResolveProjectAssetPath(projectRoot, sceneId);
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);

        SceneAsset sceneAsset = new() {
            Id = sceneId,
            RootEntities = [
                new SceneEntityAsset {
                    Id = 1u,
                    Name = "SpriteRoot",
                    LocalPosition = float3.Zero,
                    LocalScale = float3.One,
                    LocalOrientation = float4.Identity,
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.SpriteComponent",
                            ComponentIndex = 0,
                            Payload = BuildTaggedSpriteComponentPayload(textureRelativePath)
                        }
                    ],
                    Children = Array.Empty<SceneEntityAsset>()
                },
                new SceneEntityAsset {
                    Id = 2u,
                    Name = "TextRoot",
                    LocalPosition = float3.Zero,
                    LocalScale = float3.One,
                    LocalOrientation = float4.Identity,
                    Components = [
                        new SceneComponentAssetRecord {
                            ComponentTypeId = "helengine.TextComponent",
                            ComponentIndex = 0,
                            Payload = BuildTaggedTextComponentPayload(fontRelativePath)
                        }
                    ],
                    Children = Array.Empty<SceneEntityAsset>()
                }
            ]
        };

        File.WriteAllBytes(scenePath, helengine.files.AssetSerializer.SerializeToBytes(sceneAsset));
    }

    /// <summary>
    /// Writes one source texture file and returns the editor-generated imported texture asset id for the selected target platform.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source asset.</param>
    /// <param name="textureRelativePath">Project-relative texture path to create.</param>
    /// <param name="extension">Registered texture importer extension for the test asset.</param>
    /// <param name="platformId">Current target platform id used while generating import settings.</param>
    /// <returns>Importer-resolved texture asset id for the written source texture.</returns>
    static string WriteSourceTextureAssetAndReturnAssetId(string projectRoot, string textureRelativePath, string extension, string platformId) {
        string textureSourcePath = ResolveProjectAssetPath(projectRoot, textureRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(textureSourcePath)!);
        File.WriteAllBytes(textureSourcePath, [1, 2, 3, 4]);

        ContentManager contentManager = new(projectRoot);
        AssetImportManager assetImportManager = new(projectRoot, contentManager);
        assetImportManager.CurrentPlatformId = platformId;
        assetImportManager.RegisterTextureImporter(new TextureImporterRegistration("test-texture", new SinglePixelTextureImporter(), [extension]));

        TextureAssetImportSettings settings;
        Assert.True(assetImportManager.TryLoadOrCreateTextureImportSettings(textureSourcePath, out settings));
        Assert.NotNull(settings);
        Assert.NotNull(settings.Importer);
        Assert.False(string.IsNullOrWhiteSpace(settings.Importer.AssetId));
        return settings.Importer.AssetId;
    }

    /// <summary>
    /// Invokes the editor-owned platform asset cook service through reflection so PS2 builder tests can verify the real manifest-to-builder flow without widening editor internals.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source assets.</param>
    /// <param name="buildRoot">Build root where the editor cook service writes cooked artifacts.</param>
    /// <param name="platformDefinition">Platform definition supplied to the cook service.</param>
    /// <param name="materialBuilder">Builder used when translating schema-driven material settings.</param>
    /// <returns>Editor-owned build manifest emitted by the cook service.</returns>
    static PlatformBuildManifest InvokeEditorPlatformAssetCookServiceCook(
        string projectRoot,
        string buildRoot,
        PlatformDefinition platformDefinition,
        helengine.baseplatform.Builders.IPlatformAssetBuilder materialBuilder) {
        Assembly editorAssembly = typeof(TextureImporterRegistration).Assembly;
        Type serviceType = editorAssembly.GetType("helengine.editor.EditorPlatformAssetCookService", true)
            ?? throw new InvalidOperationException("EditorPlatformAssetCookService type was not found.");
        object service = Activator.CreateInstance(
            serviceType,
            projectRoot,
            "1.0.0-engine",
            "game",
            "1.0.0",
            new IAssetImporterRegistration[] {
                new TextureImporterRegistration("test-texture", new SinglePixelTextureImporter(), [".png"]),
                new FontImporterRegistration("test-font", new SingleGlyphFontImporter(), [".hefont"])
            },
            new SingleGlyphFontImporter().ImportFont(new MemoryStream(new byte[] { 0x01 })),
            null,
            null) ?? throw new InvalidOperationException("EditorPlatformAssetCookService could not be created.");
        MethodInfo cookMethod = serviceType.GetMethod("Cook", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("EditorPlatformAssetCookService.Cook was not found.");
        object manifest = cookMethod.Invoke(
            service,
            [
                platformDefinition,
                new[] { "Main" },
                buildRoot,
                new[] { "ps2" },
                materialBuilder,
                string.Empty,
                string.Empty
            ]) ?? throw new InvalidOperationException("EditorPlatformAssetCookService.Cook returned null.");

        return Assert.IsType<PlatformBuildManifest>(manifest);
    }

    /// <summary>
    /// Resolves one project-relative source asset path beneath the project `assets` directory.
    /// </summary>
    /// <param name="projectRoot">Project root that owns the source assets.</param>
    /// <param name="relativePath">Project-relative asset path.</param>
    /// <returns>Absolute path beneath the project `assets` directory.</returns>
    static string ResolveProjectAssetPath(string projectRoot, string relativePath) {
        if (string.IsNullOrWhiteSpace(projectRoot)) {
            throw new ArgumentException("Project root must be provided.", nameof(projectRoot));
        }
        if (string.IsNullOrWhiteSpace(relativePath)) {
            throw new ArgumentException("Relative path must be provided.", nameof(relativePath));
        }

        return Path.Combine(projectRoot, "assets", relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Creates a manifest that carries one PS2 texture cook work item and no staged cooked artifacts.
    /// </summary>
    /// <param name="sourceTexturePath">Absolute source image path.</param>
    /// <param name="outputRelativePath">Runtime-relative output path the builder must write.</param>
    /// <param name="serializedSettings">Serialized PS2 texture settings payload.</param>
    /// <returns>Manifest configured for one texture work item.</returns>
    static PlatformBuildManifest CreateManifestWithTextureWorkItem(
        string sourceTexturePath,
        string sourceAssetKind,
        string outputRelativePath,
        string outputLogicalArtifactId,
        string serializedSettings) {
        return new PlatformBuildManifest(
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
                    Array.Empty<PlatformBuildPayloadReference>(),
                    [
                        new KeyValuePair<string, string>("cooked-relative-path", "cooked/scenes/main.hasset")
                    ])
            ],
            Array.Empty<PlatformBuildAsset>(),
            [
                new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "ps2-default")
            ],
            Array.Empty<PlatformBuildCodeModule>(),
            Array.Empty<PlatformArtifactPlacement>(),
            new PlatformContainerWritePlan("ps2-disc-layout", Array.Empty<PlatformContainerArtifact>()),
            [
                new PlatformCookWorkItem(
                    sourceAssetKind + ":logo",
                    sourceTexturePath,
                    sourceAssetKind,
                    "ps2",
                    "runtime-texture",
                    outputRelativePath,
                    outputLogicalArtifactId,
                    "sha256:source",
                    "sha256:settings",
                    serializedSettings,
                    Array.Empty<PlatformCookWorkItemMetadata>())
            ]);
    }

    /// <summary>
    /// Creates the standard PS2 build request used by builder tests that operate on staged work-item outputs.
    /// </summary>
    /// <param name="manifest">Build manifest supplied to the builder.</param>
    /// <param name="outputRoot">Root output directory for the build.</param>
    /// <param name="workingRoot">Working directory used by the builder.</param>
    /// <param name="generatedCoreRoot">Generated core root supplied to the builder.</param>
    /// <returns>Configured PS2 build request.</returns>
    static PlatformBuildRequest CreateBuildRequest(
        PlatformBuildManifest manifest,
        string outputRoot,
        string workingRoot,
        string generatedCoreRoot) {
        return new PlatformBuildRequest(
            manifest,
            [
                new PlatformBuildTargetVariant("ps2-default", "ps2", "ps2", "ps2-default")
            ],
            [
                new PlatformCookProfile(
                    "ps2-default",
                    "PS2 Default",
                    new PlatformCookProfileCapabilities(
                        "ps2",
                        "raw",
                        "pcm",
                        "ps2-scene-v1",
                        PlatformSerializationEndianness.LittleEndian))
            ],
            outputRoot,
            workingRoot,
            "ps2-default",
            "ps2-standard-forward",
            "default",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            generatedCoreRoot,
            "ps2-install-tree",
            "disc-layout");
    }

    /// <summary>
    /// Creates a one-pixel PNG payload used by work-item execution tests.
    /// </summary>
    /// <returns>One-pixel PNG file bytes.</returns>
    static byte[] CreateSinglePixelPngBytes() {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");
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





