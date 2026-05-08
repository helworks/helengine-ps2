using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies staged PS2 disc layout generation.
/// </summary>
public sealed class Ps2DiscLayoutWriterTests {
    /// <summary>
    /// Ensures the staged disc layout contains the boot config, boot ELF, and copied cooked content.
    /// </summary>
    [Fact]
    public void WriteLayout_WritesBootConfigAndBootElfIntoDiscRoot() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-disc-layout-tests", Guid.NewGuid().ToString("N"));
        string outputRootPath = Path.Combine(rootPath, "out");
        string stagedCookedRootPath = Path.Combine(rootPath, "staging");
        string nativeElfPath = Path.Combine(rootPath, "native", "helengine_ps2.elf");

        Directory.CreateDirectory(Path.Combine(stagedCookedRootPath, "cooked", "scenes"));
        Directory.CreateDirectory(Path.GetDirectoryName(nativeElfPath)!);
        File.WriteAllText(Path.Combine(stagedCookedRootPath, "cooked", "scenes", "main.hasset"), "scene");
        File.WriteAllText(nativeElfPath, "elf");

        Ps2BuildWorkspace workspace = new(
            repositoryRootPath: rootPath,
            stagingRootPath: stagedCookedRootPath,
            generatedCoreRootPath: Path.Combine(rootPath, "generated"),
            outputRootPath: outputRootPath,
            nativeExecutablePath: nativeElfPath);

        Ps2DiscLayoutWriter writer = new();
        writer.Write(workspace);

        Assert.True(File.Exists(Path.Combine(outputRootPath, "disc", "SYSTEM.CNF")));
        Assert.True(File.Exists(Path.Combine(outputRootPath, "disc", "HELENGINE.ELF")));
        Assert.True(File.Exists(Path.Combine(outputRootPath, "disc", "cooked", "scenes", "main.hasset")));
        Assert.Contains(
            "BOOT2 = cdrom0:\\HELENGINE.ELF;1",
            File.ReadAllText(Path.Combine(outputRootPath, "disc", "SYSTEM.CNF")));
    }
}
