using helengine.baseplatform.Definitions;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the PS2 video-mode boot record: exact round trip for every mode, rejection of corrupt records, and the
/// writer's refusal of old or unknown project graphics values.
/// </summary>
public sealed class Ps2VideoModeBootRecordTests : IDisposable {
    /// <summary>Workspace-owned staging root.</summary>
    readonly string StagingRoot = Path.Combine(@"C:\dev\helworks\builds\helengine-ps2\video-mode-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the staging root.</summary>
    public Ps2VideoModeBootRecordTests() {
        Directory.CreateDirectory(StagingRoot);
    }

    /// <summary>Deletes the staging root.</summary>
    public void Dispose() {
        Directory.Delete(StagingRoot, true);
    }

    /// <summary>Every PS2 mode encodes to 16 bytes and decodes back to its region, scan and size.</summary>
    [Fact]
    public void EncodeThenDecode_RoundTripsEveryMode() {
        foreach (PlatformVideoMode mode in Ps2VideoModes.CreateCapability().Modes) {
            byte[] bytes = Ps2VideoModeBootRecord.Encode(mode);
            Ps2VideoModeBootRecordContents contents = Ps2VideoModeBootRecord.Decode(bytes);

            Assert.Equal(Ps2VideoModeBootRecord.Size, bytes.Length);
            Assert.Equal(Ps2VideoModes.IsPal(mode), contents.IsPal);
            Assert.Equal(mode.IsProgressive, contents.IsProgressive);
            Assert.Equal(mode.Width, contents.Width);
            Assert.Equal(mode.Height, contents.Height);
        }
    }

    /// <summary>A corrupt record is rejected: wrong length, magic, version, flag or a size no mode has.</summary>
    [Theory]
    [InlineData(0, (byte)'X')]
    [InlineData(4, (byte)2)]
    [InlineData(6, (byte)2)]
    [InlineData(7, (byte)2)]
    [InlineData(8, (byte)0x81)]
    [InlineData(12, (byte)1)]
    public void Decode_RejectsCorruptRecord(int offset, byte value) {
        byte[] bytes = Ps2VideoModeBootRecord.Encode(Ps2VideoModes.CreateCapability().Resolve("ntsc-640x448i", "ps2"));
        bytes[offset] = value;

        Assert.Throws<InvalidDataException>(() => Ps2VideoModeBootRecord.Decode(bytes));
        Assert.Throws<InvalidDataException>(() => Ps2VideoModeBootRecord.Decode(new byte[15]));
    }

    /// <summary>
    /// A mode the PS2 would not show 4:3 is refused: the runtime outputs every mode with GS_ASPECT_4_3 and derives the
    /// window pixel aspect from that, so the record cannot describe another display aspect.
    /// </summary>
    [Fact]
    public void Encode_RejectsAModeThatIsNotShownFourByThree() {
        PlatformVideoMode wide = new PlatformVideoMode("ntsc-640x448i", 640, 448, false, 59.94d, 60d, 16, 9);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => Ps2VideoModeBootRecord.Encode(wide));

        Assert.Contains("4:3", exception.Message);
    }

    /// <summary>The record's staging path is the runtime path the native boot host opens.</summary>
    [Fact]
    public void LogicalPath_MapsToTheNativeRuntimePath() {
        Assert.Equal(Ps2VideoModeBootRecord.RuntimePath, Ps2DiscPathResolver.ResolveRuntimePath(Ps2VideoModeBootRecord.LogicalPath));
    }

    /// <summary>The writer stages the record for the selected mode.</summary>
    [Fact]
    public void Write_StagesTheSelectedMode() {
        PlatformVideoMode mode = Ps2VideoModeBootRecordWriter.Write(StagingRoot, Ps2PlatformDefinitionFactory.Create(), Ps2TestGraphicsSelection.ProfileId, Ps2TestGraphicsSelection.CreateOptionValues("ntsc-320x224p"));

        Assert.Equal("ntsc-320x224p", mode.Id);
        Ps2VideoModeBootRecordContents contents = Ps2VideoModeBootRecord.Decode(File.ReadAllBytes(Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath)));
        Assert.Equal(320, contents.Width);
        Assert.True(contents.IsProgressive);
    }

    /// <summary>An old project still holding default-width/default-height fails naming the field and the settings file.</summary>
    [Fact]
    public void Write_WhenOldSizeFieldsPresent_FailsNamingFieldAndFile() {
        Dictionary<string, string> values = Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x448i");
        values["default-width"] = "640";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => Ps2VideoModeBootRecordWriter.Write(StagingRoot, Ps2PlatformDefinitionFactory.Create(), Ps2TestGraphicsSelection.ProfileId, values));

        Assert.Contains("default-width", exception.Message);
        Assert.Contains("settings/platform.ps2.json", exception.Message);
    }

    /// <summary>A missing mode fails naming the field; an unknown one fails naming the value.</summary>
    [Fact]
    public void Write_WhenModeMissingOrUnknown_Fails() {
        Dictionary<string, string> missing = Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x448i");
        missing.Remove("video-mode");
        Dictionary<string, string> unknown = Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x480p");

        Assert.Contains("video-mode", Assert.Throws<InvalidOperationException>(() => Ps2VideoModeBootRecordWriter.Write(StagingRoot, Ps2PlatformDefinitionFactory.Create(), Ps2TestGraphicsSelection.ProfileId, missing)).Message);
        Assert.Contains("ntsc-640x480p", Assert.Throws<InvalidOperationException>(() => Ps2VideoModeBootRecordWriter.Write(StagingRoot, Ps2PlatformDefinitionFactory.Create(), Ps2TestGraphicsSelection.ProfileId, unknown)).Message);
    }
}
