using helengine;
using helengine.baseplatform.Definitions;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Results;
using helengine.baseplatform.Player;
using helengine.editor;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the PS2 player adapter that lets the desktop C# player read PS2-staged cooked content: native texture and
/// model decoding, material translation back to logical staging paths, generic model aliases and the platform capability.
/// </summary>
[Collection(ProcessWorkingDirectoryCollection.Name)]
public sealed class Ps2PlayerAdapterTests : IDisposable {
    /// <summary>
    /// Workspace-owned parent directory that holds every scratch tree created by this test class.
    /// </summary>
    const string PlayTestsRootPath = @"C:\dev\helworks\builds\helengine-ps2\play-tests";

    /// <summary>
    /// Logical staged path of the texture referenced by the cooked test material.
    /// </summary>
    const string TextureLogicalPath = "cooked/imported/tex_a";

    /// <summary>
    /// Unique scratch root for one test, deleted in <see cref="Dispose"/>.
    /// </summary>
    readonly string Root;

    /// <summary>
    /// Creates the per-test scratch root and its empty PS2 staging tree.
    /// </summary>
    public Ps2PlayerAdapterTests() {
        Root = Path.Combine(PlayTestsRootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StagingRoot);
    }

    /// <summary>
    /// Gets the PS2 staging tree the adapter is initialized against.
    /// </summary>
    string StagingRoot => Path.Combine(Root, "ps2-staging");

    /// <summary>
    /// Deletes the per-test scratch tree.
    /// </summary>
    public void Dispose() {
        if (Directory.Exists(Root)) {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures direct CT32 and indexed T8/T4 cooked textures decode back to the exact authored RGBA bytes, including the
    /// GS 0..128 alpha range being expanded back to 0..255.
    /// </summary>
    [Fact]
    public void Texture_T4_T8_Ct32_DecodeToSourceRgba() {
        TextureAssetColorFormat[] formats = [
            TextureAssetColorFormat.Rgba32,
            TextureAssetColorFormat.Indexed8,
            TextureAssetColorFormat.Indexed4
        ];

        for (int formatIndex = 0; formatIndex < formats.Length; formatIndex++) {
            TextureAsset source = CreateDistinctColorTexture();
            byte[] expectedColors = (byte[])source.Colors.Clone();
            TextureAssetProcessorSettings settings = new() {
                MaxResolution = 0,
                ColorFormat = formats[formatIndex],
                AlphaPrecision = TextureAssetAlphaPrecision.A8
            };
            Ps2TextureAsset cooked = new Ps2RuntimeTextureCooker().Cook(source, settings);
            byte[] bytes = Ps2AssetSerializer.SerializeToBytes(cooked);

            TextureAsset decoded = new Ps2PlayerTextureContentProcessor().Read(new MemoryStream(bytes));

            Assert.Equal(4, decoded.Width);
            Assert.Equal(4, decoded.Height);
            Assert.Equal(TextureAssetColorFormat.Rgba32, decoded.ColorFormat);
            Assert.Equal(TextureAssetAlphaPrecision.A8, decoded.AlphaPrecision);
            Assert.Equal(expectedColors, decoded.Colors);
        }
    }

    /// <summary>
    /// Ensures generic engine textures that are not PS2-owned pass through the shared asset reader unchanged.
    /// </summary>
    [Fact]
    public void Texture_WhenGenericEngineAsset_PassesThrough() {
        TextureAsset source = CreateDistinctColorTexture();
        byte[] bytes = helengine.files.AssetSerializer.SerializeToBytes(source);

        TextureAsset decoded = new Ps2PlayerTextureContentProcessor().Read(new MemoryStream(bytes));

        Assert.Equal(source.Width, decoded.Width);
        Assert.Equal(source.Height, decoded.Height);
        Assert.Equal(source.Colors, decoded.Colors);
    }

    /// <summary>
    /// Ensures depth-addressed GS textures are rejected because the desktop player cannot sample them, naming the asset
    /// the content manager is loading.
    /// </summary>
    [Fact]
    public void Texture_WhenDepthFormat_Throws() {
        Ps2TextureAsset depthTexture = new() {
            Id = "depth",
            Width = 1,
            Height = 1,
            Format = Ps2TextureFormat.Depth32,
            PixelStorageMode = Ps2TexturePixelStorageMode.PsmZ32,
            ClutPixelStorageMode = Ps2TexturePixelStorageMode.PsmCt32,
            AlphaMode = Ps2TextureAlphaMode.Full,
            PixelData = new byte[4],
            PaletteData = Array.Empty<byte>()
        };
        byte[] bytes = Ps2AssetSerializer.SerializeToBytes(depthTexture);

        NotSupportedException exception = ReadWithCurrentAssetPath(
            "cooked/imported/depth.hasset",
            () => Assert.Throws<NotSupportedException>(() => new Ps2PlayerTextureContentProcessor().Read(new MemoryStream(bytes))));

        Assert.Contains("cooked/imported/depth.hasset", exception.Message);
    }

    /// <summary>
    /// Ensures a PS2 model whose vertex arrays differ in length is rejected with the loading asset's path in the message.
    /// </summary>
    [Fact]
    public void Model_WhenVertexArraysMismatch_ThrowsNamingAsset() {
        ModelAsset cube = ModelUtils.GenerateCubeMesh(new float3(0, 0, 0), new float3(1, 1, 1));
        Ps2ModelAsset ps2Model = new() {
            Id = "cube",
            Positions = cube.Positions,
            Normals = cube.Normals.Take(cube.Normals.Length - 1).ToArray(),
            TexCoords = cube.TexCoords,
            Indices16 = cube.Indices16,
            PackedMeshBytes = new byte[] { 1, 2, 3, 4 }
        };
        byte[] bytes = Ps2AssetSerializer.SerializeToBytes(ps2Model);

        InvalidOperationException exception = ReadWithCurrentAssetPath(
            "cooked/models/cube.hasset",
            () => Assert.Throws<InvalidOperationException>(() => new Ps2PlayerModelContentProcessor().Read(new MemoryStream(bytes))));

        Assert.Contains("cooked/models/cube.hasset", exception.Message);
    }

    /// <summary>
    /// Ensures a PS2-owned cooked model decodes back to the authored vertex arrays and indices with bounds computed from the positions.
    /// </summary>
    [Fact]
    public void Model_DecodesIndexedArraysAndBounds() {
        ModelAsset cube = ModelUtils.GenerateCubeMesh(new float3(0, 0, 0), new float3(1, 1, 1));
        Ps2ModelAsset ps2Model = new() {
            Id = "cube",
            Positions = cube.Positions,
            Normals = cube.Normals,
            TexCoords = cube.TexCoords,
            Indices16 = cube.Indices16,
            PackedMeshBytes = new byte[] { 1, 2, 3, 4 }
        };
        byte[] bytes = Ps2AssetSerializer.SerializeToBytes(ps2Model);

        ModelAsset decoded = new Ps2PlayerModelContentProcessor().Read(new MemoryStream(bytes));

        Assert.Equal("cube", decoded.Id);
        Assert.Equal(cube.Positions, decoded.Positions);
        Assert.Equal(cube.Normals, decoded.Normals);
        Assert.Equal(cube.TexCoords, decoded.TexCoords);
        Assert.Equal(cube.Indices16, decoded.Indices16);
        Assert.Equal(new float3(-0.5f, -0.5f, -0.5f), decoded.BoundsMin);
        Assert.Equal(new float3(0.5f, 0.5f, 0.5f), decoded.BoundsMax);
        Assert.Null(decoded.Submeshes);
    }

    /// <summary>
    /// Ensures a cooked PS2 material's disc texture path is translated back to the logical staged texture path.
    /// </summary>
    [Fact]
    public void Material_MapsDiscTexturePathBackToLogical() {
        StageFile(TextureLogicalPath);
        byte[] materialBytes = CookMaterial();
        Ps2PlayerAdapter adapter = new();
        adapter.Initialize(StagingRoot);

        PlayerMaterialDescription description = adapter.DecodeMaterial(new MemoryStream(materialBytes), "cooked/materials/m.hasset");

        Assert.Equal(TextureLogicalPath, description.DiffuseTextureRelativePath);
        Assert.True(description.DoubleSided);
        Assert.Equal(PlayerMaterialRenderClass.Opaque, description.RenderClass);
        Assert.Equal((byte)255, description.BaseColorR);
        Assert.Equal((byte)255, description.BaseColorA);
    }

    /// <summary>
    /// Ensures a material that references a texture missing from the staging tree fails with the material path in the message.
    /// </summary>
    [Fact]
    public void Material_WhenTextureNotStaged_Throws() {
        StageFile("cooked/imported/other");
        byte[] materialBytes = CookMaterial();
        Ps2PlayerAdapter adapter = new();
        adapter.Initialize(StagingRoot);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => adapter.DecodeMaterial(new MemoryStream(materialBytes), "cooked/materials/m.hasset"));

        Assert.Contains("cooked/materials/m.hasset", exception.Message);
    }

    /// <summary>
    /// Ensures the generic cooked-model path that scenes reference resolves to the staged PS2-owned model payload.
    /// </summary>
    [Fact]
    public void Aliases_MapHassetToStagedPhm() {
        StageFile("cooked/imported/box_a.phm");
        Ps2PlayerAdapter adapter = new();
        adapter.Initialize(StagingRoot);

        IReadOnlyDictionary<string, string> aliases = adapter.BuildContentAliases();

        Assert.Equal("cooked/imported/box_a.phm", aliases["cooked/imported/box_a.hasset"]);
    }

    /// <summary>
    /// Ensures the PS2 platform definition opts in to editor Play, names a constructible adapter type and declares the
    /// pad as its only input device, because a PS2 has no keyboard or mouse.
    /// </summary>
    [Fact]
    public void Definition_DeclaresPlayerCapability() {
        PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();

        Assert.True(definition.PlayerCapability.SupportsPlay);
        Assert.Equal(typeof(Ps2PlayerAdapter).FullName, definition.PlayerCapability.AdapterTypeName);
        Assert.Equal("ps2-staging", definition.PlayerCapability.ContentRootRelativePath);
        Assert.Equal(60d, definition.PlayerCapability.TargetFrameRate);
        Assert.Equal(PlayerInputDevices.Gamepad, definition.PlayerCapability.InputDevices);
        Assert.IsAssignableFrom<IPlatformPlayerAdapter>(Activator.CreateInstance(typeof(Ps2PlayerAdapter)));
    }

    /// <summary>
    /// Creates one 4x4 opaque RGBA texture whose sixteen texels are all distinct, so indexed formats stay lossless.
    /// </summary>
    /// <returns>Authored RGBA source texture.</returns>
    static TextureAsset CreateDistinctColorTexture() {
        byte[] colors = new byte[4 * 4 * 4];
        for (int texel = 0; texel < 16; texel++) {
            colors[texel * 4] = (byte)(texel * 16);
            colors[texel * 4 + 1] = (byte)(255 - texel * 8);
            colors[texel * 4 + 2] = (byte)(texel * 5 + 3);
            colors[texel * 4 + 3] = 255;
        }

        return new TextureAsset {
            Id = "source",
            Width = 4,
            Height = 4,
            ColorFormat = TextureAssetColorFormat.Rgba32,
            AlphaPrecision = TextureAssetAlphaPrecision.A8,
            Colors = colors,
            PaletteColors = Array.Empty<byte>()
        };
    }

    /// <summary>
    /// Cooks the textured, double-sided, opaque PS2 test material through the real builder material path.
    /// </summary>
    /// <returns>Serialized PS2-owned cooked material bytes.</returns>
    static byte[] CookMaterial() {
        PlatformMaterialCookResult result = new Ps2PlatformAssetBuilder().CookMaterial(new PlatformMaterialCookRequest(
            "m",
            "m",
            "ps2",
            "ps2-default",
            "ps2-standard-forward",
            "ps2-simple-lit-textured",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["texture-relative-path"] = TextureLogicalPath,
                ["alpha-mode"] = "opaque",
                ["double-sided"] = "true",
                ["cast-shadows"] = "false",
                ["vertex-color-mode"] = "multiply"
            }));
        return result.CookedMaterialBytes;
    }

    /// <summary>
    /// Writes one placeholder file into the staging tree at the supplied logical path.
    /// </summary>
    /// <param name="logicalPath">Staging-root-relative logical path using forward slashes.</param>
    void StageFile(string logicalPath) {
        string filePath = Path.Combine(StagingRoot, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        File.WriteAllBytes(filePath, new byte[] { 1, 2, 3 });
    }

    /// <summary>
    /// Runs one processor read the way the content manager does, with <see cref="EngineBinaryReadContext.CurrentAssetPath"/>
    /// naming the asset being loaded, and restores the previous value afterwards.
    /// </summary>
    /// <typeparam name="T">Value the read produces.</typeparam>
    /// <param name="assetPath">Asset path published while the read runs.</param>
    /// <param name="read">Read to run.</param>
    /// <returns>The read's result.</returns>
    static T ReadWithCurrentAssetPath<T>(string assetPath, Func<T> read) {
        string previousAssetPath = EngineBinaryReadContext.CurrentAssetPath;
        EngineBinaryReadContext.CurrentAssetPath = assetPath;
        try {
            return read();
        } finally {
            EngineBinaryReadContext.CurrentAssetPath = previousAssetPath;
        }
    }
}
