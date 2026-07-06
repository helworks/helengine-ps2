namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the shared 2D command-list builder keeps the non-wrapped text path allocation-light.
/// </summary>
public sealed class RenderCommandListBuilder2DSourceTests {
    /// <summary>
    /// Ensures only wrapped text materializes a temporary string while the non-wrapped path iterates the authored text directly.
    /// </summary>
    [Fact]
    public void EmitText_WhenTextIsNotWrapped_DoesNotCopyTextIntoOneTemporaryContentString() {
        string sourcePath = Path.Combine(
            GetHelengineRepositoryRootPath(),
            "engine",
            "helengine.core",
            "managers",
            "rendering",
            "RenderCommandListBuilder2D.cs");
        Assert.True(File.Exists(sourcePath), $"Expected render command builder source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("string content = text.Text ?? string.Empty;", source, StringComparison.Ordinal);
        Assert.Contains("string wrappedContent = TextLayoutUtils.WrapText(text.Text ?? string.Empty, font, Math.Max(1, (int)Math.Round(text.Size.X / fontScale)));", source, StringComparison.Ordinal);
        Assert.Contains("for (int index = 0; index < text.Text.Length; index++) {", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the sibling helengine repository root so PS2-side source tests can inspect shared engine code.
    /// </summary>
    /// <returns>Absolute helengine repository root path.</returns>
    static string GetHelengineRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "helengine"));
    }
}
