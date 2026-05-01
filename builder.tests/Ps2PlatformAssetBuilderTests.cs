using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Targets;
using helengine.ps2.builder;

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
    }

    [Fact]
    public async Task BuildAsync_WhenGivenPayloadFiles_CopiesThemIntoTheOutputRoot() {
        string workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outputRoot = Path.Combine(workingRoot, "out");
        string sourceRoot = Path.Combine(workingRoot, "project");
        string sceneSourcePath = Path.Combine(sourceRoot, "scenes", "startup.helen");
        string fontSourcePath = Path.Combine(sourceRoot, "assets", "fonts", "ui.font.asset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneSourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(fontSourcePath)!);
        File.WriteAllText(sceneSourcePath, "scene payload");
        File.WriteAllText(fontSourcePath, "font payload");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(sourceRoot);

            PlatformBuildManifest manifest = new(
                1,
                "project",
                "1.0.0",
                "1.0.0",
                [
                    new PlatformBuildScene(
                        "startup",
                        "Startup",
                        "scenes/startup.helen",
                        [],
                        [])
                ],
                [
                    new PlatformBuildAsset(
                        "ui-font",
                        "UI Font",
                        "assets/fonts/ui.font.asset",
                        new PlatformBuildPayloadReference("ui-font-payload", "assets/fonts/ui.font.asset"),
                        [])
                ]);

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
                Path.Combine(workingRoot, "tmp"));

            Ps2PlatformAssetBuilder builder = new();
            RecordingProgressReporter progressReporter = new();
            RecordingDiagnosticReporter diagnosticReporter = new();

            var report = await builder.BuildAsync(request, progressReporter, diagnosticReporter, CancellationToken.None);

            Assert.True(report.Succeeded);
            Assert.Empty(diagnosticReporter.Diagnostics);
            Assert.Equal(2, progressReporter.Updates.Count);
            Assert.True(File.Exists(Path.Combine(outputRoot, "scenes", "startup.helen")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "assets", "fonts", "ui.font.asset")));
            Assert.True(File.Exists(Path.Combine(workingRoot, "tmp", "ps2-build-manifest.json")));
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
}
