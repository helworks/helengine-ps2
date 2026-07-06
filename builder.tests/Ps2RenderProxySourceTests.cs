using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 render-proxy source stays aligned with the generated drawable material-array contract.
/// </summary>
public sealed class Ps2RenderProxySourceTests {
    /// <summary>
    /// Ensures the PS2 render proxy resolves the first runtime material from the drawable material array instead of calling the removed single-material virtual.
    /// </summary>
    [Fact]
    public void Ps2RenderProxy_WhenSynchronizingDrawable_UsesDrawableMaterialArrayContract() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "rendering", "Ps2RenderProxy.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected PS2 render proxy source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("Array<::RuntimeMaterial*>* materials = drawable->get_Materials();", source, StringComparison.Ordinal);
        Assert.Contains("::RuntimeMaterial* runtimeMaterial = materials != nullptr && materials->Length > 0", source, StringComparison.Ordinal);
        Assert.Contains("Material = he_cpp_try_cast<Ps2RuntimeMaterial>(runtimeMaterial);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("drawable->get_Material()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
