using helengine;
using helengine.baseplatform.Definitions;
using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Targets;
using helengine.editor;
using helengine.files;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the editor Play path: assets-only PS2 builds stage cooked content without native compilation or packaging,
/// and repeated builds into the same working root only rewrite staged outputs whose inputs changed.
/// </summary>
[Collection(ProcessWorkingDirectoryCollection.Name)]
public sealed class Ps2AssetsOnlyBuildTests : IDisposable {
    /// <summary>
    /// Workspace-owned parent directory that holds every scratch tree created by this test class.
    /// </summary>
    const string PlayTestsRootPath = @"C:\dev\helworks\builds\helengine-ps2\play-tests";

    /// <summary>
    /// Last-write time stamped onto staged outputs between runs so a rewrite is detectable regardless of clock resolution.
    /// </summary>
    static readonly DateTime SentinelWriteTimeUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Unique scratch root for one test, deleted in <see cref="Dispose"/>.
    /// </summary>
    readonly string Root;

    /// <summary>
    /// Creates the scratch tree with a staged package root holding one scene and one cube model.
    /// </summary>
    public Ps2AssetsOnlyBuildTests() {
        Root = Path.Combine(PlayTestsRootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(SceneSourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(ModelSourcePath));
        File.WriteAllBytes(SceneSourcePath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        WriteCubeModel(1f);
    }

    /// <summary>
    /// Gets the staged package root the builder resolves cooked artifacts from (it becomes the process working directory during builds).
    /// </summary>
    string PackageRoot => Path.Combine(Root, "package");

    /// <summary>
    /// Gets the builder working root that the editor keeps between Play runs.
    /// </summary>
    string WorkingRoot => Path.Combine(Root, "working");

    /// <summary>
    /// Gets the PS2 staging tree the desktop player reads.
    /// </summary>
    string StagingRoot => Path.Combine(WorkingRoot, "ps2-staging");

    /// <summary>
    /// Gets the final output root used by full builds.
    /// </summary>
    string OutputRoot => Path.Combine(Root, "out");

    /// <summary>
    /// Gets the generic cooked scene artifact inside the package root.
    /// </summary>
    string SceneSourcePath => Path.Combine(PackageRoot, "cooked", "scenes", "main.hasset");

    /// <summary>
    /// Gets the generic cooked model artifact inside the package root.
    /// </summary>
    string ModelSourcePath => Path.Combine(PackageRoot, "cooked", "imported", "box_a.hasset");

    /// <summary>
    /// Gets the PS2-owned model payload that staging produces from the generic cube model.
    /// </summary>
    string StagedModelPath => Path.Combine(StagingRoot, "cooked", "imported", "box_a.phm");

    /// <summary>
    /// Gets the serialized engine texture used as the source of texture work items.
    /// </summary>
    string TextureSourcePath => Path.Combine(PackageRoot, "textures", "source.hetex");

    /// <summary>
    /// Gets the staged PS2 runtime texture produced by the texture work item.
    /// </summary>
    string StagedTexturePath => Path.Combine(StagingRoot, "textures", "native.ps2tex");

    /// <summary>
    /// Proves an assets-only build stages the PS2 model, never touches native build or packaging, and needs no generated core.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenAssetsOnly_StagesModelAndSkipsNativeBuild() {
        CountingPs2NativeBuildExecutor nativeBuildExecutor = new();
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], []), nativeBuildExecutor, diagnosticReporter);

        Assert.True(report.Succeeded);
        Assert.Empty(diagnosticReporter.Diagnostics);
        Assert.True(File.Exists(StagedModelPath));
        Assert.True(File.Exists(Path.Combine(StagingRoot, "cooked", "scenes", "main.hasset")));
        Assert.Equal(0, nativeBuildExecutor.BuildCallCount);
        Assert.Equal(0, nativeBuildExecutor.PackageIsoCallCount);
        Assert.False(Directory.Exists(OutputRoot));
    }

    /// <summary>
    /// Proves the Play (assets-only) build stages the boot record for the project's selected video mode, so the staged
    /// tree always carries the mode the console would boot into.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenAssetsOnly_StagesTheSelectedVideoModeBootRecord() {
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], [], Ps2TestGraphicsSelection.CreateOptionValues("pal-512x256p")),
            new CountingPs2NativeBuildExecutor(),
            diagnosticReporter);

        Assert.True(report.Succeeded);
        Assert.Empty(diagnosticReporter.Diagnostics);
        Ps2VideoModeBootRecordContents contents = Ps2VideoModeBootRecord.Decode(File.ReadAllBytes(Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath)));
        Assert.True(contents.IsPal);
        Assert.True(contents.IsProgressive);
        Assert.Equal(512, contents.Width);
        Assert.Equal(256, contents.Height);
    }

    /// <summary>
    /// Proves an old project whose graphics options still hold default-width fails the build with PS2BUILD010 naming the
    /// field and the settings file, and stages no boot record, instead of silently booting at 640x448.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenGraphicsOptionsHoldOldSizeField_FailsNamingFieldAndFile() {
        Dictionary<string, string> graphicsOptionValues = Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x448i");
        graphicsOptionValues["default-width"] = "640";
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], [], graphicsOptionValues),
            new CountingPs2NativeBuildExecutor(),
            diagnosticReporter);

        Assert.False(report.Succeeded);
        Assert.Contains(diagnosticReporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD010"
            && diagnostic.Message.Contains("default-width", StringComparison.Ordinal)
            && diagnostic.Message.Contains("settings/platform.ps2.json", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath)));
    }

    /// <summary>
    /// Proves an unknown video-mode value fails the build with PS2BUILD010 naming the settings file, the field and the value.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenVideoModeUnknown_FailsNamingFileFieldAndValue() {
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], [], Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x480p")),
            new CountingPs2NativeBuildExecutor(),
            diagnosticReporter);

        Assert.False(report.Succeeded);
        Assert.Contains(diagnosticReporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD010"
            && diagnostic.Message.Contains("settings/platform.ps2.json", StringComparison.Ordinal)
            && diagnostic.Message.Contains("video-mode", StringComparison.Ordinal)
            && diagnostic.Message.Contains("ntsc-640x480p", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves an IOException while writing the boot record (the file is held open without sharing) becomes PS2BUILD010
    /// naming the record path instead of escaping the build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenBootRecordFileIsLocked_FailsNamingTheRecordPath() {
        string recordPath = Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath);
        Directory.CreateDirectory(StagingRoot);
        RecordingDiagnosticReporter diagnosticReporter = new();
        PlatformBuildReport report;

        using (FileStream lockStream = new(recordPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
            report = await RunBuildAsync(
                CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], []),
                new CountingPs2NativeBuildExecutor(),
                diagnosticReporter);
        }

        Assert.False(report.Succeeded);
        Assert.Contains(diagnosticReporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD010"
            && diagnostic.Message.Contains(recordPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Proves an UnauthorizedAccessException while writing the boot record (a directory occupies its path) becomes
    /// PS2BUILD010 naming the record path instead of escaping the build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenBootRecordPathIsADirectory_FailsNamingTheRecordPath() {
        string recordPath = Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath);
        Directory.CreateDirectory(recordPath);
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], []),
            new CountingPs2NativeBuildExecutor(),
            diagnosticReporter);

        Assert.False(report.Succeeded);
        Assert.Contains(diagnosticReporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD010"
            && diagnostic.Message.Contains(recordPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Proves a second assets-only build with unchanged sources leaves the staged model untouched.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenAssetsOnlyRunTwiceUnchanged_DoesNotRewriteStagedModel() {
        await RunAssetsOnlyBuildExpectingSuccessAsync([]);
        File.SetLastWriteTimeUtc(StagedModelPath, SentinelWriteTimeUtc);

        await RunAssetsOnlyBuildExpectingSuccessAsync([]);

        Assert.Equal(SentinelWriteTimeUtc, File.GetLastWriteTimeUtc(StagedModelPath));
    }

    /// <summary>
    /// Proves a run that rewrote an output and then failed cannot leave a stale fingerprint behind: after the source is
    /// reverted to the content of the last successful run, the output is restaged with that content instead of being
    /// reused as up to date while it still holds the failed run's cook.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenRunFailsAfterRewritingOutputAndSourceIsReverted_RestagesOutput() {
        await RunAssetsOnlyBuildExpectingSuccessAsync([]);
        byte[] firstRunModelBytes = File.ReadAllBytes(StagedModelPath);
        WriteCubeModel(2f);
        string brokenModelSourcePath = Path.Combine(PackageRoot, "cooked", "imported", "broken.hasset");
        File.WriteAllBytes(brokenModelSourcePath, helengine.files.AssetSerializer.SerializeToBytes(new SceneAsset()));
        PlatformBuildArtifact brokenModelArtifact = new("cooked/imported/broken.hasset", "model:broken", "sha256:broken", "model", "shared");

        PlatformBuildReport failedReport = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], [brokenModelArtifact]),
            new CountingPs2NativeBuildExecutor(),
            new RecordingDiagnosticReporter());
        Assert.False(failedReport.Succeeded);
        Assert.NotEqual(firstRunModelBytes, File.ReadAllBytes(StagedModelPath));
        WriteCubeModel(1f);

        await RunAssetsOnlyBuildExpectingSuccessAsync([]);

        Assert.Equal(firstRunModelBytes, File.ReadAllBytes(StagedModelPath));
    }

    /// <summary>
    /// Proves a corrupt fingerprint store fails the build with a diagnostic naming the store file instead of being silently discarded.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenFingerprintStoreIsCorrupt_FailsNamingTheStoreFile() {
        Directory.CreateDirectory(StagingRoot);
        File.WriteAllText(Path.Combine(StagingRoot, ".helengine-cook-fingerprints.json"), "{ not json");
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, [], []),
            new CountingPs2NativeBuildExecutor(),
            diagnosticReporter);

        Assert.False(report.Succeeded);
        Assert.Contains(diagnosticReporter.Diagnostics, diagnostic => diagnostic.Code == "PS2BUILD003"
            && diagnostic.Message.Contains(".helengine-cook-fingerprints.json", StringComparison.Ordinal));
        Assert.False(File.Exists(StagedModelPath));
    }

    /// <summary>
    /// Proves a changed source model is restaged on the next assets-only build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenSourceChangesBetweenRuns_RewritesStagedModel() {
        await RunAssetsOnlyBuildExpectingSuccessAsync([]);
        File.SetLastWriteTimeUtc(StagedModelPath, SentinelWriteTimeUtc);
        WriteCubeModel(2f);

        await RunAssetsOnlyBuildExpectingSuccessAsync([]);

        Assert.NotEqual(SentinelWriteTimeUtc, File.GetLastWriteTimeUtc(StagedModelPath));
    }

    /// <summary>
    /// Proves a staged model that was deleted between runs is restaged even though its fingerprint is still recorded.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenStagedModelWasDeletedBetweenRuns_RestagesIt() {
        await RunAssetsOnlyBuildExpectingSuccessAsync([]);
        File.Delete(StagedModelPath);

        await RunAssetsOnlyBuildExpectingSuccessAsync([]);

        Assert.True(File.Exists(StagedModelPath));
    }

    /// <summary>
    /// Proves an unchanged texture work item is not re-cooked or rewritten by a second assets-only build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenTextureWorkItemUnchanged_DoesNotRewriteStagedTexture() {
        WriteTextureSource();
        await RunAssetsOnlyBuildExpectingSuccessAsync([CreateTextureWorkItem("source-hash-1", "PS2.PSMCT32")]);
        Assert.True(File.Exists(StagedTexturePath));
        File.SetLastWriteTimeUtc(StagedTexturePath, SentinelWriteTimeUtc);

        await RunAssetsOnlyBuildExpectingSuccessAsync([CreateTextureWorkItem("source-hash-1", "PS2.PSMCT32")]);

        Assert.Equal(SentinelWriteTimeUtc, File.GetLastWriteTimeUtc(StagedTexturePath));
    }

    /// <summary>
    /// Proves a texture work item whose source content hash changed is cooked and restaged.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenTextureSourceHashChanges_RewritesStagedTexture() {
        WriteTextureSource();
        await RunAssetsOnlyBuildExpectingSuccessAsync([CreateTextureWorkItem("source-hash-1", "PS2.PSMCT32")]);
        File.SetLastWriteTimeUtc(StagedTexturePath, SentinelWriteTimeUtc);

        await RunAssetsOnlyBuildExpectingSuccessAsync([CreateTextureWorkItem("source-hash-2", "PS2.PSMCT32")]);

        Assert.NotEqual(SentinelWriteTimeUtc, File.GetLastWriteTimeUtc(StagedTexturePath));
    }

    /// <summary>
    /// Proves the fingerprint store kept under the staging tree never reaches the packaged PS2 disc of a full build.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenFullBuild_DoesNotPackageFingerprintStore() {
        string generatedCoreRoot = Path.Combine(Root, "generated-core");
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");
        CountingPs2NativeBuildExecutor nativeBuildExecutor = new();
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(CreateRequest(PlatformBuildExecutionScope.Full, generatedCoreRoot, [], []), nativeBuildExecutor, diagnosticReporter);

        Assert.True(report.Succeeded);
        Assert.Equal(1, nativeBuildExecutor.PackageIsoCallCount);
        string discRoot = Path.Combine(OutputRoot, "disc");
        HashSet<string> expectedDiscFiles = new(StringComparer.OrdinalIgnoreCase) {
            "SYSTEM.CNF",
            Ps2BuildWorkspace.DiscExecutableFileName,
            Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset").Replace('\\', '/'),
            Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/imported/box_a.phm").Replace('\\', '/'),
            Ps2DiscPathResolver.ResolveDiscRelativePath(Ps2VideoModeBootRecord.LogicalPath)
        };
        HashSet<string> actualDiscFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in Directory.GetFiles(discRoot, "*", SearchOption.AllDirectories)) {
            actualDiscFiles.Add(Path.GetRelativePath(discRoot, filePath).Replace('\\', '/'));
        }
        Assert.Equal(expectedDiscFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), actualDiscFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Proves a full build names the project's selected video region in the packaged SYSTEM.CNF, not a fixed one.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenFullBuild_NamesTheSelectedVideoRegionInSystemCnf() {
        string generatedCoreRoot = Path.Combine(Root, "generated-core");
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");
        CountingPs2NativeBuildExecutor nativeBuildExecutor = new();
        RecordingDiagnosticReporter diagnosticReporter = new();

        PlatformBuildReport report = await RunBuildAsync(
            CreateRequest(PlatformBuildExecutionScope.Full, generatedCoreRoot, [], [], Ps2TestGraphicsSelection.CreateOptionValues("pal-640x512i")),
            nativeBuildExecutor,
            diagnosticReporter);

        Assert.True(report.Succeeded);
        PlatformVideoMode palMode = Ps2VideoModes.CreateCapability().Resolve("pal-640x512i", "ps2");
        Assert.Equal(Ps2DiscLayoutWriter.BuildBootConfigContents(palMode), File.ReadAllText(Path.Combine(OutputRoot, "disc", "SYSTEM.CNF")));
    }

    /// <summary>
    /// Deletes this test's scratch tree.
    /// </summary>
    public void Dispose() {
        if (!Path.GetFullPath(Root).StartsWith(PlayTestsRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Scratch cleanup escaped the PS2 play-tests root.");
        }

        if (Directory.Exists(Root)) {
            Directory.Delete(Root, true);
        }
    }

    /// <summary>
    /// Runs one assets-only build with the supplied texture work items and asserts it succeeded without diagnostics or native work.
    /// </summary>
    /// <param name="workItems">Platform cook work items to include in the manifest.</param>
    async Task RunAssetsOnlyBuildExpectingSuccessAsync(PlatformCookWorkItem[] workItems) {
        CountingPs2NativeBuildExecutor nativeBuildExecutor = new();
        RecordingDiagnosticReporter diagnosticReporter = new();
        PlatformBuildReport report = await RunBuildAsync(CreateRequest(PlatformBuildExecutionScope.AssetsOnly, string.Empty, workItems, []), nativeBuildExecutor, diagnosticReporter);
        Assert.Empty(diagnosticReporter.Diagnostics);
        Assert.True(report.Succeeded);
        Assert.Equal(0, nativeBuildExecutor.BuildCallCount);
    }

    /// <summary>
    /// Runs one build with the package root as the process working directory, restoring the previous directory afterwards.
    /// </summary>
    /// <param name="request">Build request to execute.</param>
    /// <param name="nativeBuildExecutor">Native build double handed to the builder.</param>
    /// <param name="diagnosticReporter">Receives builder diagnostics.</param>
    /// <returns>Build report returned by the builder.</returns>
    async Task<PlatformBuildReport> RunBuildAsync(PlatformBuildRequest request, CountingPs2NativeBuildExecutor nativeBuildExecutor, RecordingDiagnosticReporter diagnosticReporter) {
        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(PackageRoot);
            Ps2PlatformAssetBuilder builder = new(nativeBuildExecutor);
            return await builder.BuildAsync(request, new RecordingProgressReporter(), diagnosticReporter, CancellationToken.None);
        } finally {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    /// <summary>
    /// Creates the PS2 build request used by every test, mirroring the full-build fixture in <see cref="Ps2PlatformAssetBuilderTests"/>.
    /// </summary>
    /// <param name="executionScope">Pipeline scope to request.</param>
    /// <param name="generatedCoreRoot">Generated core root; empty for assets-only builds, as the editor passes it.</param>
    /// <param name="workItems">Platform cook work items to include.</param>
    /// <param name="extraArtifacts">Cooked artifacts staged after the scene and the cube model.</param>
    /// <returns>Configured PS2 build request.</returns>
    PlatformBuildRequest CreateRequest(PlatformBuildExecutionScope executionScope, string generatedCoreRoot, PlatformCookWorkItem[] workItems, PlatformBuildArtifact[] extraArtifacts) {
        return CreateRequest(executionScope, generatedCoreRoot, workItems, extraArtifacts, Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x448i"));
    }

    /// <summary>
    /// Creates the PS2 build request with explicit graphics option values, for tests that exercise the video-mode selection.
    /// </summary>
    /// <param name="executionScope">Pipeline scope to request.</param>
    /// <param name="generatedCoreRoot">Generated core root; empty for assets-only builds, as the editor passes it.</param>
    /// <param name="workItems">Platform cook work items to include.</param>
    /// <param name="extraArtifacts">Cooked artifacts staged after the scene and the cube model.</param>
    /// <param name="graphicsOptionValues">Graphics option values of the standard forward profile, as the project file supplies them.</param>
    /// <returns>Configured PS2 build request.</returns>
    PlatformBuildRequest CreateRequest(PlatformBuildExecutionScope executionScope, string generatedCoreRoot, PlatformCookWorkItem[] workItems, PlatformBuildArtifact[] extraArtifacts, Dictionary<string, string> graphicsOptionValues) {
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
                new PlatformBuildArtifact("cooked/imported/box_a.hasset", "model:box_a", "sha256:model", "model", "shared"),
                .. extraArtifacts
            ],
            Array.Empty<PlatformBuildCodeModule>(),
            Array.Empty<PlatformArtifactPlacement>(),
            new PlatformContainerWritePlan("ps2-disc-layout", Array.Empty<PlatformContainerArtifact>()),
            workItems,
            PlatformBuildRuntimeFeatureManifest.Empty);

        return new PlatformBuildRequest(
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
            OutputRoot,
            WorkingRoot,
            selectedBuildProfileId: "ps2-default",
            selectedGraphicsProfileId: Ps2TestGraphicsSelection.ProfileId,
            selectedCodegenProfileId: "default",
            selectedBuildOptionValues: new Dictionary<string, string>(),
            selectedGraphicsOptionValues: graphicsOptionValues,
            selectedCodegenOptionValues: new Dictionary<string, string>(),
            generatedCoreCppRootPath: generatedCoreRoot,
            selectedMediaProfileId: "ps2-install-tree",
            selectedStorageProfileId: "disc-layout",
            executionScope: executionScope);
    }

    /// <summary>
    /// Serializes a cube model of the requested size into the package root, replacing any previous model.
    /// </summary>
    /// <param name="size">Edge length of the cube; changing it changes the serialized source bytes.</param>
    void WriteCubeModel(float size) {
        ModelAsset cubeModelAsset = ModelUtils.GenerateCubeMesh(new float3(0f, 0f, 0f), new float3(size, size, size));
        File.WriteAllBytes(ModelSourcePath, helengine.files.AssetSerializer.SerializeToBytes(cubeModelAsset));
    }

    /// <summary>
    /// Serializes a small engine texture into the package root for texture work items to cook.
    /// </summary>
    void WriteTextureSource() {
        Directory.CreateDirectory(Path.GetDirectoryName(TextureSourcePath));
        using TextureAsset source = Ps2NativeTextureCookTests.Source(4, 2);
        File.WriteAllBytes(TextureSourcePath, helengine.files.AssetSerializer.SerializeToBytes(source));
    }

    /// <summary>
    /// Creates one PS2 runtime-texture work item for the package texture with the supplied source content hash.
    /// </summary>
    /// <param name="sourceContentHash">Source content hash the editor would have computed for the texture.</param>
    /// <param name="format">PS2 texture format id used in the serialized settings.</param>
    /// <returns>Texture work item targeting <c>textures/native.ps2tex</c>.</returns>
    PlatformCookWorkItem CreateTextureWorkItem(string sourceContentHash, string format) {
        string serializedSettings = Ps2TextureCookSettingsSerializer.Serialize(Ps2NativeTextureCookTests.Settings(format, TextureAssetAlphaPrecision.A8));
        return new PlatformCookWorkItem(
            "ps2:texture:textures/native.ps2tex",
            TextureSourcePath,
            "texture",
            "ps2",
            "runtime-texture",
            "textures/native.ps2tex",
            "runtime-texture:textures/native.ps2tex",
            sourceContentHash,
            "settings-hash-" + format,
            serializedSettings,
            []);
    }
}
