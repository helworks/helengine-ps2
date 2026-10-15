using helengine.baseplatform.Definitions;
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
            artifactRootPath: Path.Combine(rootPath, "artifacts"),
            nativeExecutablePath: nativeElfPath);

        Ps2DiscLayoutWriter writer = new();
        IReadOnlyDictionary<string, string> logicalToPhysicalPaths = writer.Write(workspace, Ps2VideoModes.CreateCapability().Resolve("ntsc-640x448i", "ps2"));

        Assert.True(File.Exists(Path.Combine(rootPath, "artifacts", "disc", "SYSTEM.CNF")));
        Assert.True(File.Exists(Path.Combine(rootPath, "artifacts", "disc", Ps2BuildWorkspace.DiscExecutableFileName)));
        Assert.True(File.Exists(Path.Combine(rootPath, "artifacts", "disc", Ps2DiscPathResolver.ResolveDiscRelativePath("cooked/scenes/main.hasset"))));
        Assert.Contains(
            "BOOT2 = cdrom0:\\" + Ps2BuildWorkspace.DiscExecutableFileName + ";1",
            File.ReadAllText(Path.Combine(rootPath, "artifacts", "disc", "SYSTEM.CNF")));
        Assert.Contains("VMODE = NTSC", File.ReadAllText(Path.Combine(rootPath, "artifacts", "disc", "SYSTEM.CNF")));
        Assert.Equal("\\COOKED\\SCENES\\MAIN.HAS;1", logicalToPhysicalPaths["cooked/scenes/main.hasset"]);
    }

    /// <summary>SYSTEM.CNF names the video region of the selected mode on its own CRLF line.</summary>
    [Theory]
    [InlineData("ntsc-320x224p", "VMODE = NTSC")]
    [InlineData("ntsc-640x448i", "VMODE = NTSC")]
    [InlineData("pal-640x512i", "VMODE = PAL")]
    [InlineData("pal-256x256p", "VMODE = PAL")]
    public void BuildBootConfigContents_NamesTheVideoRegion(string modeId, string expectedLine) {
        PlatformVideoMode mode = Ps2VideoModes.CreateCapability().Resolve(modeId, "ps2");

        string contents = Ps2DiscLayoutWriter.BuildBootConfigContents(mode);

        Assert.Equal("BOOT2 = cdrom0:\\" + Ps2BuildWorkspace.DiscExecutableFileName + ";1\r\nVER = 1.00\r\n" + expectedLine + "\r\n", contents);
    }

    /// <summary>A disc layout without a video mode is refused.</summary>
    [Fact]
    public void BuildBootConfigContents_WithoutVideoMode_Throws() {
        Assert.Throws<ArgumentNullException>(() => Ps2DiscLayoutWriter.BuildBootConfigContents(null));
    }

    /// <summary>Writing a disc layout without a video mode is refused before the disc root is touched.</summary>
    [Fact]
    public void Write_WithoutVideoMode_ThrowsBeforeStagingTheDisc() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-disc-layout-tests", Guid.NewGuid().ToString("N"));
        Ps2BuildWorkspace workspace = new(
            repositoryRootPath: rootPath,
            stagingRootPath: Path.Combine(rootPath, "staging"),
            generatedCoreRootPath: Path.Combine(rootPath, "generated"),
            outputRootPath: Path.Combine(rootPath, "out"),
            artifactRootPath: Path.Combine(rootPath, "artifacts"),
            nativeExecutablePath: Path.Combine(rootPath, "native", "helengine_ps2.elf"));

        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new Ps2DiscLayoutWriter().Write(workspace, null));

        Assert.Equal("videoMode", exception.ParamName);
        Assert.False(Directory.Exists(workspace.DiscRootPath));
    }
}
