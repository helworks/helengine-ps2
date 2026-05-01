using helengine.baseplatform.Manifest;
using helengine.baseplatform.Profiles;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Targets;

namespace helengine.ps2.builder;

public static class Program {
    public static int Main(string[] args) {
        if (args.Length > 0 && string.Equals(args[0], "--smoke-test", StringComparison.OrdinalIgnoreCase)) {
            RunSmokeTest();
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "--describe", StringComparison.OrdinalIgnoreCase)) {
            Ps2PlatformAssetBuilder builder = new();
            Console.WriteLine(builder.Descriptor.BuilderId);
            Console.WriteLine(builder.Descriptor.TargetPlatformId);
            Console.WriteLine(builder.Definition.DisplayName);
            Console.WriteLine(builder.Definition.BuildProfiles.Length);
            Console.WriteLine(builder.Definition.GraphicsProfiles.Length);
            return 0;
        }

        Console.WriteLine("helengine.ps2.builder --describe | --smoke-test");
        return 0;
    }

    static void RunSmokeTest() {
        string workingRoot = Path.Combine(Path.GetTempPath(), "helengine-ps2-builder-smoke-" + Guid.NewGuid().ToString("N"));
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
            var report = builder.BuildAsync(
                request,
                new NullProgressReporter(),
                new NullDiagnosticReporter(),
                CancellationToken.None).GetAwaiter().GetResult();

            if (!report.Succeeded) {
                throw new InvalidOperationException("Smoke test build failed.");
            }

            if (!File.Exists(Path.Combine(outputRoot, "scenes", "startup.helen"))) {
                throw new InvalidOperationException("Smoke test scene output is missing.");
            }

            if (!File.Exists(Path.Combine(outputRoot, "assets", "fonts", "ui.font.asset"))) {
                throw new InvalidOperationException("Smoke test asset output is missing.");
            }

            if (!File.Exists(Path.Combine(workingRoot, "tmp", "ps2-build-manifest.json"))) {
                throw new InvalidOperationException("Smoke test build manifest is missing.");
            }

            Console.WriteLine("Smoke test passed.");
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

    sealed class NullProgressReporter : helengine.baseplatform.Builders.IPlatformBuildProgressReporter {
        public void Report(helengine.baseplatform.Reporting.PlatformBuildProgressUpdate update) {
        }
    }

    sealed class NullDiagnosticReporter : helengine.baseplatform.Builders.IPlatformBuildDiagnosticReporter {
        public void Report(helengine.baseplatform.Reporting.PlatformBuildDiagnostic diagnostic) {
        }
    }
}
