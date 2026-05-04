using helengine.baseplatform.Manifest;
using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Reporting;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Targets;
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
        Assert.Contains(builder.Definition.GraphicsProfiles, profile => profile.ProfileId == "gs-kit");
        Assert.Contains(builder.Definition.StorageProfiles, profile =>
            profile.ProfileId == "disc-layout" &&
            profile.RuntimeSpecializationId == "ps2-disc-layout");
        Assert.Contains(builder.Definition.ComponentCompatibilities, compatibility =>
            compatibility.ComponentTypeId == "helengine.fpscomponent" &&
            compatibility.CompatibilityKind == PlatformComponentCompatibilityKind.Transform);
        Assert.Contains(builder.Definition.ComponentCompatibilities, compatibility =>
            compatibility.ComponentTypeId == "helengine.meshcomponent" &&
            compatibility.CompatibilityKind == PlatformComponentCompatibilityKind.Transform);
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
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_unity.cpp"), "// generated");

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
                selectedGraphicsProfileId: "gs-kit",
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
            Assert.True(File.Exists(Path.Combine(outputRoot, "helengine_ps2.elf")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "cooked", "scenes", "main.hasset")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "cooked", "imported", "box_a.hasset")));
            Assert.False(File.Exists(Path.Combine(workingRoot, "tmp", "ps2-build-manifest.json")));
            Assert.Equal(generatedCoreRoot, nativeBuildExecutor.LastWorkspace.GeneratedCoreRootPath);
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

    sealed class FakePs2NativeBuildExecutor : IPs2NativeBuildExecutor {
        public Ps2BuildWorkspace LastWorkspace { get; private set; }

        public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
            LastWorkspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            string executableDirectoryPath = Path.GetDirectoryName(workspace.NativeExecutablePath)!;
            Directory.CreateDirectory(executableDirectoryPath);
            File.WriteAllText(workspace.NativeExecutablePath, "elf");
        }
    }
}
