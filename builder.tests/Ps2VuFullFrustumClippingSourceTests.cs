using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Defines the final PS2 textured VU1 full-frustum clipping source contract before bounded polygon clipping is implemented.
/// </summary>
public sealed class Ps2VuFullFrustumClippingSourceTests {
    /// <summary>
    /// Ensures textured slice routing uses conservative classification without forcing every slice through diagnostic clipping behavior.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenRoutingTexturedSlices_UsesClassifierWithoutDiagnosticOverrides() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Matches(@"const\s+Ps2VuNearPlaneRoute\s+classifiedRoute\s*=\s*Ps2VuNearPlaneSliceClassifier::Classify\s*\(", source);
        Assert.Matches(@"const\s+Ps2VuNearPlaneRoute\s+route\s*=\s*ForceAllTexturedSlicesThroughClipProgramDiagnostics\s*\?\s*Ps2VuNearPlaneRoute::Clipped\s*:\s*classifiedRoute\s*;", source);
        Assert.Contains("constexpr bool ForceAllTexturedSlicesThroughClipProgramDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool DropClippedTexturedSlicesForDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool UseFastProgramForClippedSliceDiagnostics = false;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the clipping program seeds and clips a polygon across every homogeneous frustum plane before emitting its triangle fan.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenClippingFullFrustum_SeedsPlanesBeforeFanPerspectiveDivision() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        int seedPolygonIndex = GetLabelDeclarationIndex(source, "texturedClipSeedPolygon");
        int cameraPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneCameraW");
        int nearPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneNearZ");
        int leftPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneLeft");
        int rightPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneRight");
        int bottomPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneBottom");
        int topPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneTop");
        int fanIndex = GetLabelDeclarationIndex(source, "texturedClipEmitTriangleFanLoop");
        string fanEmitterBlock = GetLabelBlock(source, "texturedClipEmitTriangleFanLoop");
        System.Text.RegularExpressions.Match firstDivisionMatch = System.Text.RegularExpressions.Regex.Match(
            source,
            @"(?m)^(?!\s*;)[^\r\n]*\bdiv\b\s+Q\s*,",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.True(firstDivisionMatch.Success);
        Assert.Matches(@"(?m)^(?![ \t]*;)[^\r\n]*\bbal\b[ \t]+VI15[ \t]*,[ \t]*texturedClipEmitTriangle[ \t]*\r?$", fanEmitterBlock);
        Assert.True(cameraPlaneIndex > seedPolygonIndex);
        Assert.True(nearPlaneIndex > cameraPlaneIndex);
        Assert.True(leftPlaneIndex > nearPlaneIndex);
        Assert.True(rightPlaneIndex > leftPlaneIndex);
        Assert.True(bottomPlaneIndex > rightPlaneIndex);
        Assert.True(topPlaneIndex > bottomPlaneIndex);
        Assert.True(fanIndex > topPlaneIndex);
        Assert.True(firstDivisionMatch.Index > fanIndex);
        Assert.DoesNotContain("texturedClipHardwareRejectTriangle", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures crossing edges preserve position and raw UV interpolation, snap to each clipping boundary, validate fan triangles, and emit a dynamic GIF loop.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenEmittingClippedFan_PreservesAttributesAndBuildsDynamicGifLoop() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));

        GetLabelDeclarationIndex(source, "texturedClipIntersectEdge");
        Assert.Contains("sub.xyzw      VF29, VF29, VF28", source, StringComparison.Ordinal);
        Assert.Contains("mulq.xyzw     VF29, VF29, Q", source, StringComparison.Ordinal);
        Assert.Contains("add.xyzw      VF28, VF28, VF29", source, StringComparison.Ordinal);
        Assert.Contains("sub.xy        VF31, VF31, VF30", source, StringComparison.Ordinal);
        Assert.Contains("mulq.xy       VF31, VF31, Q", source, StringComparison.Ordinal);
        Assert.Contains("add.xy        VF30, VF30, VF31", source, StringComparison.Ordinal);
        GetLabelDeclarationIndex(source, "texturedClipSnapCameraW");
        GetLabelDeclarationIndex(source, "texturedClipSnapNearZ");
        GetLabelDeclarationIndex(source, "texturedClipSnapLeft");
        GetLabelDeclarationIndex(source, "texturedClipSnapRight");
        GetLabelDeclarationIndex(source, "texturedClipSnapBottom");
        GetLabelDeclarationIndex(source, "texturedClipSnapTop");
        GetLabelDeclarationIndex(source, "texturedClipValidateTriangleA");
        GetLabelDeclarationIndex(source, "texturedClipValidateTriangleB");
        GetLabelDeclarationIndex(source, "texturedClipValidateTriangleC");
        Assert.Contains("iadd VI07, VI06, VI06", source, StringComparison.Ordinal);
        Assert.Contains("iadd VI07, VI07, VI06", source, StringComparison.Ordinal);
        Assert.Contains("isw.x VI07, 7(VI04)", source, StringComparison.Ordinal);
        Assert.Contains("xgkick VI04", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Locates a standalone VU assembly label declaration so source ordering checks cannot be satisfied by comments or incidental text.
    /// </summary>
    /// <param name="source">Complete VU assembly source text.</param>
    /// <param name="labelName">Label identifier without its terminating colon.</param>
    /// <returns>Character index of the declared label.</returns>
    static int GetLabelDeclarationIndex(string source, string labelName) {
        System.Text.RegularExpressions.Match labelDeclarationMatch = System.Text.RegularExpressions.Regex.Match(
            source,
            $@"(?m)^[ \t]*{System.Text.RegularExpressions.Regex.Escape(labelName)}:[ \t]*\r?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.True(labelDeclarationMatch.Success, $"Expected standalone VU label declaration '{labelName}:'.");
        return labelDeclarationMatch.Index;
    }

    /// <summary>
    /// Extracts the VU assembly text after one standalone label and before the next standalone label declaration.
    /// </summary>
    /// <param name="source">Complete VU assembly source text.</param>
    /// <param name="labelName">Starting label identifier without its terminating colon.</param>
    /// <returns>Textual instruction block owned by the starting label.</returns>
    static string GetLabelBlock(string source, string labelName) {
        System.Text.RegularExpressions.Match labelBlockMatch = System.Text.RegularExpressions.Regex.Match(
            source,
            $@"(?m)^[ \t]*{System.Text.RegularExpressions.Regex.Escape(labelName)}:[ \t]*\r?$\r?\n(?<block>(?:(?!^[ \t]*[A-Za-z_][A-Za-z0-9_]*:[ \t]*\r?$)[\s\S])*)(?=^[ \t]*[A-Za-z_][A-Za-z0-9_]*:[ \t]*\r?$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.True(labelBlockMatch.Success, $"Expected VU label block for '{labelName}:'.");
        return labelBlockMatch.Groups["block"].Value;
    }
}
