using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 frame planner continues routing supported render classes into the expected frame buckets.
/// </summary>
public sealed class Ps2FramePlannerSourceTests {
    /// <summary>
    /// Ensures alpha-tested and transparent proxies are preserved in the frame plan instead of being discarded by an early non-opaque continue path.
    /// </summary>
    [Fact]
    public void Ps2FramePlanner_WhenEncounteringAlphaTestAndTransparentMaterials_RoutesThemIntoExpectedBuckets() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2FramePlanner.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected frame planner source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("const ::Ps2RenderClass renderClass = material->GetRenderClass();", source, StringComparison.Ordinal);
        Assert.Contains("renderClass != ::Ps2RenderClass::Opaque &&", source, StringComparison.Ordinal);
        Assert.Contains("renderClass != ::Ps2RenderClass::AlphaTest &&", source, StringComparison.Ordinal);
        Assert.Contains("renderClass != ::Ps2RenderClass::Transparent)", source, StringComparison.Ordinal);
        Assert.Contains("if (renderClass == ::Ps2RenderClass::Transparent) {", source, StringComparison.Ordinal);
        Assert.Contains("plan.AlphaWorld.push_back(&proxy);", source, StringComparison.Ordinal);
        Assert.Contains("plan.AlphaDynamic.push_back(&proxy);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (material->GetRenderClass() != ::Ps2RenderClass::Opaque) {", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the repository root path from the builder test output directory.
    /// </summary>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
