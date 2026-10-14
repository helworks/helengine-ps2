using helengine.baseplatform.Definitions;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies that the platform identity the builder writes into the native PS2 build matches the identity the PS2 Play
/// capability hands to the desktop player, so the console and Play report the same <c>PlatformInfo</c>.
/// </summary>
public sealed class Ps2RuntimePlatformInfoHeaderWriterTests {
    /// <summary>
    /// Writes the generated header, reads back the string literal of each macro the native boot host passes to
    /// <c>PlatformInfo</c>, and compares them with the PS2 Play capability.
    /// </summary>
    [Fact]
    public void Write_EmitsThePlatformIdentityThePlayCapabilityDeclares() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-runtime-platform-info-tests", Guid.NewGuid().ToString("N"));
        try {
            string headerPath = new Ps2RuntimePlatformInfoHeaderWriter().Write(Path.Combine(rootPath, "generated-core"));
            string[] lines = File.ReadAllLines(headerPath);
            PlatformPlayerCapability capability = Ps2PlatformDefinitionFactory.Create().PlayerCapability;

            Assert.Equal(Path.Combine(rootPath, "generated-core", "runtime", Ps2RuntimePlatformInfoHeaderWriter.HeaderFileName), headerPath);
            Assert.Equal(capability.RuntimePlatformName, ReadMacroStringLiteral(lines, Ps2RuntimePlatformInfoHeaderWriter.PlatformNameMacro));
            Assert.Equal(capability.RuntimePlatformVersion, ReadMacroStringLiteral(lines, Ps2RuntimePlatformInfoHeaderWriter.PlatformVersionMacro));
        } finally {
            if (Directory.Exists(rootPath)) {
                Directory.Delete(rootPath, true);
            }
        }
    }

    /// <summary>
    /// Rewriting an unchanged header leaves the file untouched, so the native build does not recompile the boot host.
    /// </summary>
    [Fact]
    public void Write_WhenHeaderUnchanged_DoesNotRewriteIt() {
        string rootPath = Path.Combine(Path.GetTempPath(), "ps2-runtime-platform-info-tests", Guid.NewGuid().ToString("N"));
        try {
            Ps2RuntimePlatformInfoHeaderWriter writer = new();
            string headerPath = writer.Write(rootPath);
            DateTime earlier = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(headerPath, earlier);

            writer.Write(rootPath);

            Assert.Equal(earlier, File.GetLastWriteTimeUtc(headerPath));
        } finally {
            if (Directory.Exists(rootPath)) {
                Directory.Delete(rootPath, true);
            }
        }
    }

    /// <summary>
    /// Finds the single <c>#define</c> of <paramref name="macroName"/> and returns the contents of its string literal.
    /// </summary>
    /// <param name="lines">Generated header lines.</param>
    /// <param name="macroName">Macro to read.</param>
    /// <returns>The literal's contents without the surrounding quotes.</returns>
    static string ReadMacroStringLiteral(string[] lines, string macroName) {
        string prefix = "#define " + macroName + " \"";
        string line = Assert.Single(lines, candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
        Assert.EndsWith("\"", line);
        return line.Substring(prefix.Length, line.Length - prefix.Length - 1);
    }
}
