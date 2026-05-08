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
        string sourceRoot = Path.Combine(workingRoot, "staging");
        string generatedCoreRoot = Path.Combine(workingRoot, "generated-core");
        string sceneSourcePath = Path.Combine(sourceRoot, "cooked", "scenes", "main.hasset");

        Directory.CreateDirectory(Path.GetDirectoryName(sceneSourcePath)!);
        Directory.CreateDirectory(generatedCoreRoot);
        File.WriteAllText(sceneSourcePath, "scene payload");
        File.WriteAllText(Path.Combine(generatedCoreRoot, "helengine_core_amalgamated.cpp"), "// generated");

        string previousDirectory = Directory.GetCurrentDirectory();
        try {
            Directory.SetCurrentDirectory(sourceRoot);

            PlatformBuildManifest manifest = new(
                3,
                "project",
                "1.0.0",
                "1.0.0",
                "startup",
                [
                    new PlatformBuildScene(
                        "startup",
                        "Startup",
                        "cooked/scenes/main.hasset",
                        [],
                        [])
                ],
                Array.Empty<PlatformBuildAsset>(),
                [
                    new PlatformBuildArtifact("cooked/scenes/main.hasset", "scene:main", "sha256:scene", "scene", "shared")
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

            Ps2PlatformAssetBuilder builder = new(new SmokeTestNativeBuildExecutor());
            var report = builder.BuildAsync(
                request,
                new NullProgressReporter(),
                new NullDiagnosticReporter(),
                CancellationToken.None).GetAwaiter().GetResult();

            if (!report.Succeeded) {
                throw new InvalidOperationException("Smoke test build failed.");
            }

            if (!File.Exists(Path.Combine(outputRoot, "cooked", "scenes", "main.hasset"))) {
                throw new InvalidOperationException("Smoke test scene output is missing.");
            }

            if (!File.Exists(Path.Combine(outputRoot, "helengine_ps2.elf"))) {
                throw new InvalidOperationException("Smoke test PS2 ELF is missing.");
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

    sealed class SmokeTestNativeBuildExecutor : IPs2NativeBuildExecutor {
        public void Build(Ps2BuildWorkspace workspace, CancellationToken cancellationToken) {
            string executableDirectoryPath = Path.GetDirectoryName(workspace.NativeExecutablePath)!;
            Directory.CreateDirectory(executableDirectoryPath);
            File.WriteAllText(workspace.NativeExecutablePath, "elf");
        }
    }
}
