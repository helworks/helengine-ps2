using helengine;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Targets;
using helengine.ps2.builder;
using ImageMagick;

namespace helengine.ps2.builder.tests;

/// <summary>Checks complete texture batches preflight, cook and publish without invalid later work damaging prior outputs.</summary>
public sealed class Ps2NativeTextureWorkItemTests : IDisposable {
    /// <summary>Uses a fixed fixture tree beneath the configured dotnet artifacts directory.</summary>
    readonly string Root = Path.Combine(AppContext.BaseDirectory, "ps2-texture-work-items");
    /// <summary>Gets the private serialized engine texture source.</summary>
    string Source => Path.Combine(Root, "source.hetex");
    /// <summary>Gets the executor staging root used to detect writes.</summary>
    string Stage => Path.Combine(Root, "working", "ps2-staging");

    /// <summary>Creates a stable source and an existing output to detect partial publication.</summary>
    public Ps2NativeTextureWorkItemTests() {
        Directory.CreateDirectory(Stage);
        Directory.CreateDirectory(Path.Combine(Root, "generated-core"));
        using TextureAsset source = Ps2NativeTextureCookTests.Source(4, 2);
        File.WriteAllBytes(Source, helengine.files.AssetSerializer.SerializeToBytes(source));
        File.WriteAllText(Path.Combine(Stage, "previous.txt"), "previous");
    }

    /// <summary>Checks native texture and legacy font settings are both published with their declared runtime identities.</summary>
    [Fact]
    public void TextureAndFontAtlasCookAndPublishWithDeclaredIdentity() {
        byte[] original = File.ReadAllBytes(Source);
        Assert.Empty(Execute([Item("textures/native.ps2tex"), Item("fonts/atlas.ps2tex", "font-atlas-texture", "Indexed8")]));
        foreach (string path in new[] { "textures/native.ps2tex", "fonts/atlas.ps2tex" }) {
            using Ps2TextureAsset cooked = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.DeserializeFromBytes(File.ReadAllBytes(Path.Combine(Stage, path))));
            Assert.Equal(path, cooked.Id);
            Assert.NotEqual(0ul, cooked.RuntimeAssetId);
            Assert.Equal(4 * 2 * 4, Ps2NativeTextureCodec.Decode(cooked).Length);
        }
        Assert.Equal(original, File.ReadAllBytes(Source));
    }

    /// <summary>Imports an actual PNG source through the builder and preserves exact RGBA pixels.</summary>
    [Fact]
    public void PngSourceCooksAndPublishesWithoutWindowsEditorHost() {
        string source = Path.Combine(Root, "source.png");
        using (MagickImage image = new MagickImage(MagickColors.Red, 1, 1)) {
            image.Write(source, MagickFormat.Png);
        }
        Assert.Empty(Execute([Item("textures/png.ps2tex", source: source)]));
        using Ps2TextureAsset cooked = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.DeserializeFromBytes(File.ReadAllBytes(Path.Combine(Stage, "textures/png.ps2tex"))));
        Assert.Equal("textures/png.ps2tex", cooked.Id);
        Assert.NotEqual(0ul, cooked.RuntimeAssetId);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Ps2NativeTextureCodec.Decode(cooked));
    }

    /// <summary>Rasterizes the explicit source face through the portable host and publishes a nonempty GS atlas.</summary>
    [Fact]
    public void RawFontAtlasCooksAndPublishesWithoutWindowsEditorHost() {
        string source = Path.Combine(AppContext.BaseDirectory, "fixtures", "Synthetic.ttf");
        Assert.Empty(Execute([Item("fonts/portable.ps2tex", "font-atlas-texture", source: source)]));
        using Ps2TextureAsset cooked = Assert.IsType<Ps2TextureAsset>(Ps2AssetSerializer.DeserializeFromBytes(File.ReadAllBytes(Path.Combine(Stage, "fonts/portable.ps2tex"))));
        Assert.Equal("fonts/portable.ps2tex", cooked.Id);
        Assert.NotEqual(0ul, cooked.RuntimeAssetId);
        Assert.True(cooked.Width > 0 && cooked.Height > 0);
        Assert.Contains(Ps2NativeTextureCodec.Decode(cooked), channel => channel != 0);
    }

    /// <summary>Rejects a bad later destination before an earlier valid texture can be written.</summary>
    [Theory]
    [InlineData("../outside.ps2tex")]
    [InlineData("C:/outside.ps2tex")]
    [InlineData("bad.ps2tex:stream")]
    [InlineData("NUL.ps2tex")]
    [InlineData("bad?.ps2tex")]
    public void InvalidPathsWriteNothing(string path) {
        Assert.Single(Execute([Item("first.ps2tex"), Item(path)]));
        Assert.False(File.Exists(Path.Combine(Stage, "first.ps2tex")));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(Stage, "previous.txt")));
    }

    /// <summary>Rejects exact and file/directory output collisions before publishing the batch.</summary>
    [Theory]
    [InlineData("same.ps2tex", "SAME.ps2tex")]
    [InlineData("texture", "texture/child.ps2tex")]
    [InlineData("first.ps2tex", "previous.txt/child.ps2tex")]
    public void NamespaceConflictsWriteNothing(string first, string second) {
        Assert.Single(Execute([Item(first), Item(second)]));
        Assert.False(File.Exists(Path.Combine(Stage, first)));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(Stage, "previous.txt")));
    }

    /// <summary>Reserves both manifest paths and the platform-specific model extension against texture outputs.</summary>
    [Theory]
    [InlineData("textures/native.ps2tex", "texture", "TEXTURES/native.ps2tex")]
    [InlineData("models/cube.model", "model", "models/cube.phm")]
    public void ManifestCollisionPreventsPublication(string artifactPath, string kind, string outputPath) {
        var artifact = new PlatformBuildArtifact(artifactPath, "artifact", "hash", kind, "ps2-default");
        Assert.Single(Execute([Item(outputPath)], [artifact]));
        Assert.False(File.Exists(Path.Combine(Stage, outputPath)));
    }

    /// <summary>Preserves earlier outputs when decoding a later source fails after an earlier cook succeeded.</summary>
    [Fact]
    public void LaterDecodeFailureWritesNothing() {
        string bad = Path.Combine(Root, "bad.hetex");
        File.WriteAllBytes(bad, [1, 2, 3]);
        Assert.Single(Execute([Item("first.ps2tex"), Item("second.ps2tex", source: bad)]));
        Assert.False(File.Exists(Path.Combine(Stage, "first.ps2tex")));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(Stage, "previous.txt")));
    }

    /// <summary>Rejects an unsupported cook contract instead of silently skipping the work item.</summary>
    [Theory]
    [InlineData("material", "ps2", "runtime-texture")]
    [InlineData("texture", "x360", "runtime-texture")]
    [InlineData("texture", "ps2", "runtime-material")]
    public void UnsupportedContractWritesNothing(string sourceKind, string platform, string artifactKind) {
        var settings = Ps2NativeTextureCookTests.Settings("PS2.PSMCT32", TextureAssetAlphaPrecision.A8);
        var item = new PlatformCookWorkItem("bad", Source, sourceKind, platform, artifactKind, "first.ps2tex", "first", "source", "settings",
            Ps2TextureCookSettingsSerializer.Serialize(settings), []);
        Assert.Single(Execute([item]));
        Assert.False(File.Exists(Path.Combine(Stage, "first.ps2tex")));
    }

    /// <summary>Checks the builder invokes complete cook preparation before copying unrelated manifest artifacts.</summary>
    [Fact]
    public async Task BuilderPreflightsBeforeStagingManifestArtifacts() {
        var artifact = new PlatformBuildArtifact("not-found.heasset", "artifact", "hash", "scene", "ps2-default");
        var request = Request([Item("../outside.ps2tex")], [artifact]);
        var reporter = new RecordingDiagnosticReporter();
        var report = await new Ps2PlatformAssetBuilder().BuildAsync(request, new RecordingProgressReporter(), reporter, CancellationToken.None);
        Assert.False(report.Succeeded);
        Assert.Contains(reporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD003");
        Assert.DoesNotContain(reporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD002");
        Assert.Equal("previous", File.ReadAllText(Path.Combine(Stage, "previous.txt")));
    }

    /// <summary>Builds a texture or atlas work item using validated native or supported legacy settings.</summary>
    PlatformCookWorkItem Item(string output, string kind = "texture", string format = "PS2.PSMCT32", string source = null) {
        return new PlatformCookWorkItem(output, source ?? Source, kind, "ps2", "runtime-texture", output, output, "source", "settings",
            Ps2TextureCookSettingsSerializer.Serialize(Ps2NativeTextureCookTests.Settings(format, TextureAssetAlphaPrecision.A8)), []);
    }

    /// <summary>Executes a batch through the public diagnostic boundary with no native build or image importer.</summary>
    List<PlatformBuildDiagnostic> Execute(PlatformCookWorkItem[] items, PlatformBuildArtifact[] artifacts = null) {
        List<PlatformBuildDiagnostic> diagnostics = [];
        new Ps2PlatformCookWorkItemExecutor().Execute(Request(items, artifacts), diagnostics, new RecordingDiagnosticReporter(), new RecordingProgressReporter(), CancellationToken.None);
        return diagnostics;
    }

    /// <summary>Creates the minimal PS2 target request required by cook and builder preflight.</summary>
    PlatformBuildRequest Request(PlatformCookWorkItem[] items, PlatformBuildArtifact[] artifacts) {
        var manifest = new PlatformBuildManifest(3, "project", "1.0.0", "1.0.0", "ps2", "1.0.0", "",
            [], [], artifacts ?? [], [], [], new PlatformContainerWritePlan("ps2-disc-layout", []), items, PlatformBuildRuntimeFeatureManifest.Empty);
        return new PlatformBuildRequest(manifest,
            [new PlatformBuildTargetVariant("ps2-default", "ps2", "ps2", "ps2-default")],
            [new PlatformCookProfile("ps2-default", "PS2", new PlatformCookProfileCapabilities("ps2", "raw", "pcm", "ps2-scene-v1", PlatformSerializationEndianness.LittleEndian))],
            Path.Combine(Root, "output"), Path.Combine(Root, "working"), "ps2-default", "ps2-standard-forward", "default", null, null, null, Path.Combine(Root, "generated-core"));
    }

    /// <summary>Removes only this class's contained fixed fixture directory after the test completes.</summary>
    public void Dispose() {
        string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "ps2-texture-work-items"));
        if (!Path.GetFullPath(Root).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture cleanup escaped the test artifacts root.");
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}
