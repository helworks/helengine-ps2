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

    /// <summary>
    /// Pins the exact on-disc bytes (little-endian HVMR layout) of one NTSC interlaced and one PAL progressive mode. The
    /// native reader's test (Task B13) uses these same literal bytes, so a layout change on either side cannot pass
    /// unnoticed through a symmetric Encode/Decode round trip.
    /// </summary>
    [Theory]
    [InlineData("ntsc-640x448i", new byte[] { 0x48, 0x56, 0x4D, 0x52, 0x01, 0x00, 0x00, 0x00, 0x80, 0x02, 0xC0, 0x01, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData("pal-320x256p", new byte[] { 0x48, 0x56, 0x4D, 0x52, 0x01, 0x00, 0x01, 0x01, 0x40, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 })]
    public void Encode_ProducesTheGoldenBytes(string modeId, byte[] expected) {
        byte[] bytes = Ps2VideoModeBootRecord.Encode(Ps2VideoModes.CreateCapability().Resolve(modeId, "ps2"));

        Assert.Equal(expected, bytes);
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
    /// A mode whose declared display aspect differs from the one gsKit's integer magnification gives its width is refused
    /// naming the mode and both aspects: the record carries no aspect, and the native runtime derives it from the width the
    /// same way, so the record cannot describe another display aspect. Both a 640-wide mode declared other than 4:3 and a
    /// 320-wide mode declared 4:3 (instead of 3:2) are refused.
    /// </summary>
    [Theory]
    [InlineData("ntsc-640x448i", 640, 448, false, 16, 9, "16:9", "4:3")]
    [InlineData("ntsc-320x224p", 320, 224, true, 4, 3, "4:3", "3:2")]
    [InlineData("pal-256x256p", 256, 256, true, 4, 3, "4:3", "22:15")]
    public void Encode_RejectsAModeNotShownAtTheGsKitDerivedAspect(string id, int width, int height, bool progressive, int aspectWidth, int aspectHeight, string declared, string derived) {
        PlatformVideoMode mode = new PlatformVideoMode(id, width, height, progressive, 59.94d, 60d, aspectWidth, aspectHeight);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => Ps2VideoModeBootRecord.Encode(mode));

        Assert.Equal("mode", exception.ParamName);
        Assert.Contains($"'{id}'", exception.Message);
        Assert.Contains(declared, exception.Message);
        Assert.Contains(derived, exception.Message);
    }

    /// <summary>
    /// A declared aspect that is not reduced but equals the gsKit-derived one (6:4 for a 320-wide mode) is the same
    /// display aspect and is accepted, so the guard compares ratios rather than terms.
    /// </summary>
    [Fact]
    public void Encode_AcceptsAnEquivalentUnreducedAspect() {
        PlatformVideoMode mode = new PlatformVideoMode("ntsc-320x224p", 320, 224, true, 59.94d, 60d, 6, 4);

        Assert.Equal(Ps2VideoModeBootRecord.Size, Ps2VideoModeBootRecord.Encode(mode).Length);
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

    /// <summary>
    /// An unknown mode fails naming the settings file, the field, the value and every allowed mode id, so the project
    /// author can fix the file from the message alone.
    /// </summary>
    [Fact]
    public void Write_WhenModeUnknown_FailsNamingFileFieldValueAndAllowedIds() {
        Dictionary<string, string> unknown = Ps2TestGraphicsSelection.CreateOptionValues("ntsc-640x480p");

        string message = Assert.Throws<InvalidOperationException>(() => Ps2VideoModeBootRecordWriter.Write(StagingRoot, Ps2PlatformDefinitionFactory.Create(), Ps2TestGraphicsSelection.ProfileId, unknown)).Message;

        Assert.Contains("settings/platform.ps2.json", message);
        Assert.Contains("video-mode", message);
        Assert.Contains("ntsc-640x480p", message);
        foreach (string modeId in Ps2VideoModes.CreateCapability().GetModeIds()) {
            Assert.Contains(modeId, message);
        }
        Assert.False(File.Exists(Path.Combine(StagingRoot, Ps2VideoModeBootRecord.LogicalPath)));
    }
}
