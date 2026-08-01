using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Defines the final PS2 textured VU1 full-frustum clipping source contract before bounded polygon clipping is implemented.
/// </summary>
public sealed class Ps2VuFullFrustumClippingSourceTests {
    /// <summary>
    /// Ensures full-frustum clipping reserves enough shared VU1 scratch and derived output memory for the largest possible polygon fan.
    /// </summary>
    [Fact]
    public void Ps2VuTexturedSourceLimits_WhenClippingFullFrustum_ProvesScratchAndOutputCapacity() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));

        Assert.Contains("TexturedVuMaximumClipPolygonVertexCount = 9u", source, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClipScratchQwordsPerVertex = 2u", source, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClipScratchBufferAQword = 0x50u", source, StringComparison.Ordinal);
        Assert.Contains("TexturedVuClipScratchBufferBQword = 0x64u", source, StringComparison.Ordinal);
        Assert.Matches(@"static_assert\s*\(\s*TexturedVuClipScratchBufferAQword\s*\+\s*TexturedVuClipScratchBufferQwordCount\s*<=\s*TexturedVuClipScratchBufferBQword\s*\);", source);
        Assert.Contains("TexturedVuClipScratchEndQword <= TexturedVuOutputStartQword", source, StringComparison.Ordinal);
        Assert.Matches(@"constexpr\s+std::size_t\s+TexturedVuMaximumOutputTrianglesPerClippedSource\s*=\s*TexturedVuMaximumClipPolygonVertexCount\s*-\s*2u\s*;", source);
        Assert.Matches(@"constexpr\s+std::size_t\s+TexturedVuMaximumClippedTriangleCount\s*=\s*TexturedVuClippedSourceTriangleCapacity\s*\*\s*TexturedVuMaximumOutputTrianglesPerClippedSource\s*;", source);
        Assert.Matches(@"constexpr\s+std::size_t\s+TexturedVuMaximumOutputEndQword\s*=\s*TexturedVuOutputStartQword\s*\+\s*TexturedVuGifStateQwordCount\s*\+\s*\(\s*TexturedVuMaximumClippedTriangleCount\s*\*\s*TexturedVuOutputQwordsPerTriangle\s*\)\s*;", source);
        Assert.Contains("TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount", source, StringComparison.Ordinal);
    }

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
    /// Ensures Task 3 seeds a bounded polygon and clips it across every homogeneous frustum plane without relying on whole-triangle rejection.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenClippingFullFrustum_SeedsAndClipsTheBoundedPolygon() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        int seedPolygonIndex = GetLabelDeclarationIndex(source, "texturedClipSeedPolygon");
        int runPolygonPlanesIndex = GetLabelDeclarationIndex(source, "texturedClipRunPolygonPlanes");
        int cameraPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneCameraW");
        int nearPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneNearZ");
        int leftPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneLeft");
        int rightPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneRight");
        int bottomPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneBottom");
        int topPlaneIndex = GetLabelDeclarationIndex(source, "texturedClipPlaneTop");

        AssertContainsVuInstruction(source, "iaddiu VI12, VI00, 0x00000050");
        AssertContainsVuInstruction(source, "iaddiu VI13, VI00, 0x00000003");
        AssertContainsVuInstruction(source, "sq VF18, 0(VI12)");
        AssertContainsVuInstruction(source, "sq VF21, 1(VI12)");
        AssertContainsVuInstruction(source, "sq VF19, 2(VI12)");
        AssertContainsVuInstruction(source, "sq VF22, 3(VI12)");
        AssertContainsVuInstruction(source, "sq VF20, 4(VI12)");
        AssertContainsVuInstruction(source, "sq VF23, 5(VI12)");
        Assert.True(runPolygonPlanesIndex > seedPolygonIndex);
        Assert.True(cameraPlaneIndex > seedPolygonIndex);
        Assert.True(nearPlaneIndex > cameraPlaneIndex);
        Assert.True(leftPlaneIndex > nearPlaneIndex);
        Assert.True(rightPlaneIndex > leftPlaneIndex);
        Assert.True(bottomPlaneIndex > rightPlaneIndex);
        Assert.True(topPlaneIndex > bottomPlaneIndex);
        AssertContainsVuInstruction(source, "iaddiu VI04, VI00, 0x00000064");
        AssertContainsVuInstruction(source, "iaddiu VI03, VI00, 0x00000009");
        Assert.Contains("texturedClipPolygonOverflow:", source, StringComparison.Ordinal);
        Assert.Contains("clipw.xyz", source, StringComparison.Ordinal);
        Assert.Contains("fcand", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedClipEmitPolygon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedClipHardwareRejectTriangle", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures Task 3 crossing edges preserve position and raw UV interpolation, reject unstable denominators, clamp their interpolation, and snap to one active boundary.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenClippingCrossingEdges_PreservesAttributesAndSnapsBoundaries() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        string intersectionBlock = GetLabelBlock(source, "texturedClipIntersectEdge");
        string cameraSnapBlock = GetLabelBlock(source, "texturedClipSnapCameraW");

        GetLabelDeclarationIndex(source, "texturedClipIntersectEdge");
        AssertContainsVuInstruction(intersectionBlock, "sub.x VF27, VF24, VF25");
        AssertContainsVuInstruction(intersectionBlock, "abs.x VF26, VF27");
        AssertContainsVuInstruction(intersectionBlock, "mulw.xyzw VF28, VF18, VF00w");
        AssertContainsVuInstruction(intersectionBlock, "mulw.xyzw VF29, VF19, VF00w");
        AssertContainsVuInstruction(intersectionBlock, "mulw.xyzw VF30, VF21, VF00w");
        AssertContainsVuInstruction(intersectionBlock, "mulw.xyzw VF31, VF22, VF00w");
        Assert.True(
            GetVuInstructionIndex(intersectionBlock, "mulw.xyzw VF31, VF22, VF00w")
            < GetVuInstructionIndex(intersectionBlock, "sub.xyzw VF29, VF29, VF28"));
        AssertContainsVuInstruction(intersectionBlock, "mulq.w VF26, VF00, Q");
        AssertContainsVuInstruction(intersectionBlock, "maxw.x VF26, VF00, VF26w");
        AssertContainsVuInstruction(intersectionBlock, "miniw.x VF26, VF26, VF00w");
        Assert.DoesNotMatch(@"(?m)^[ \t]*maxw\.w[ \t]+VF26,[ \t]+VF00,[ \t]+VF26w\b", intersectionBlock);
        AssertContainsVuInstruction(intersectionBlock, "sub.xyzw VF29, VF29, VF28");
        AssertContainsVuInstruction(intersectionBlock, "mulx.xyzw VF29, VF29, VF26x");
        AssertContainsVuInstruction(intersectionBlock, "add.xyzw VF28, VF28, VF29");
        AssertContainsVuInstruction(intersectionBlock, "sub.xy VF31, VF31, VF30");
        AssertContainsVuInstruction(intersectionBlock, "mulx.xy VF31, VF31, VF26x");
        AssertContainsVuInstruction(intersectionBlock, "add.xy VF30, VF30, VF31");
        AssertContainsVuInstruction(cameraSnapBlock, "subw.w VF28, VF00, VF00w");
        AssertContainsVuInstruction(cameraSnapBlock, "addi.w VF28, VF28, I");
        Assert.DoesNotMatch(@"(?m)^[ \t]*addi\.w[ \t]+VF28,[ \t]+VF00,[ \t]+I\b", cameraSnapBlock);
        GetLabelDeclarationIndex(source, "texturedClipSnapCameraW");
        GetLabelDeclarationIndex(source, "texturedClipSnapNearZ");
        GetLabelDeclarationIndex(source, "texturedClipSnapLeft");
        GetLabelDeclarationIndex(source, "texturedClipSnapRight");
        GetLabelDeclarationIndex(source, "texturedClipSnapBottom");
        GetLabelDeclarationIndex(source, "texturedClipSnapTop");
    }

    /// <summary>
    /// Ensures camera-W clipping derives both edge distances from homogeneous W before subtracting epsilon.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenClippingCameraW_UsesHomogeneousWMinusEpsilonForBothEdgeEndpoints() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        string previousDistanceBlock = GetLabelBlock(source, "texturedClipDistanceCameraW");
        string currentDistanceBlock = GetLabelBlock(source, "texturedClipCurrentDistanceCameraW");

        AssertContainsVuInstruction(previousDistanceBlock, "addw.x VF24, VF00, VF18w");
        AssertContainsVuInstruction(previousDistanceBlock, "subi.x VF24, VF24, I");
        Assert.DoesNotMatch(@"(?m)^[ \t]*mulw\.x[ \t]+VF24,[ \t]+VF18,[ \t]+VF00w\b", previousDistanceBlock);
        AssertContainsVuInstruction(currentDistanceBlock, "addw.x VF25, VF00, VF19w");
        AssertContainsVuInstruction(currentDistanceBlock, "subi.x VF25, VF25, I");
        Assert.DoesNotMatch(@"(?m)^[ \t]*mulw\.x[ \t]+VF25,[ \t]+VF19,[ \t]+VF00w\b", currentDistanceBlock);
    }

    /// <summary>
    /// Ensures clipping retains the material flag while keeping plane-local control outside the eight clipped source records.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenRunningPolygonPlanes_PreservesMaterialAndInputMemory() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        string clippingBlock = GetSourceSection(source, "texturedClipPolygonAgainstPlane", "texturedClipEmitTriangle");

        AssertContainsVuInstruction(source, "iaddiu VI05, VI02, 0x00000015");
        AssertContainsVuInstruction(source, "iaddiu VI05, VI05, 0x00000007");
        Assert.Contains("Source triangle input occupies qwords 0x15..0x4c", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\b(?:isw|ilw)\.[xyzw][^\r\n]*\b0x0000004c\(VI00\)", clippingBlock);
        AssertContainsVuInstruction(clippingBlock, "iaddiu VI04, VI00, 0x00000050");
        AssertContainsVuInstruction(clippingBlock, "iaddiu VI04, VI00, 0x00000064");
        AssertContainsVuInstruction(clippingBlock, "iadd VI10, VI04, VI00");
        AssertContainsVuInstruction(clippingBlock, "iadd VI12, VI04, VI00");
        AssertContainsVuInstruction(clippingBlock, "iaddiu VI04, VI00, 0x00000100");
        AssertContainsVuInstruction(clippingBlock, "isw.y VI01, 0x0000004e(VI00)");
        AssertContainsVuInstruction(clippingBlock, "ilw.y VI01, 0x0000004e(VI00)");
        AssertContainsVuInstruction(source, "ilw.y VI08, 7(VI02)");
        AssertContainsVuInstruction(source, "ibne VI08, VI00, texturedClipEmitTriangleAccepted");
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bVI08\b", clippingBlock);
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bfcand[ \t]+VI08\b", source);
    }

    /// <summary>
    /// Keeps Task 4 fan projection and dynamic GIF emission as a separate contract from Task 3 polygon clipping.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedClipDraw3D_WhenEmittingClippedFan_ValidatesAndBuildsDynamicGifLoop() {
        string repositoryRootPath = GetRepositoryRootPath();
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedClipDraw3D.vsm"));
        string topPlaneBlock = GetLabelBlock(source, "texturedClipPlaneTop");

        Assert.Matches(@"(?m)^(?![ \t]*;)[^\r\n]*\bb\b[ \t]+texturedClipTriangulatePolygon[ \t]*\r?$", topPlaneBlock);
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

    /// <summary>
    /// Extracts a VU source section bounded by two standalone labels.
    /// </summary>
    /// <param name="source">Complete VU assembly source text.</param>
    /// <param name="startLabelName">First label included by the returned section.</param>
    /// <param name="endLabelName">First label excluded from the returned section.</param>
    /// <returns>Text between the two named labels.</returns>
    static string GetSourceSection(string source, string startLabelName, string endLabelName) {
        int sectionStartIndex = GetLabelDeclarationIndex(source, startLabelName);
        int sectionEndIndex = GetLabelDeclarationIndex(source, endLabelName);

        Assert.True(sectionEndIndex > sectionStartIndex, $"Expected '{endLabelName}:' after '{startLabelName}:'.");
        return source[sectionStartIndex..sectionEndIndex];
    }

    /// <summary>
    /// Matches a lower VU instruction with whitespace-tolerant token boundaries while preserving its exact opcode and operands.
    /// </summary>
    /// <param name="source">VU assembly text containing the instruction.</param>
    /// <param name="instruction">Opcode and operands separated by ordinary spaces.</param>
    static void AssertContainsVuInstruction(string source, string instruction) {
        GetVuInstructionIndex(source, instruction);
    }

    /// <summary>
    /// Locates a lower VU instruction with whitespace-tolerant token boundaries while preserving its exact opcode and operands.
    /// </summary>
    /// <param name="source">VU assembly text containing the instruction.</param>
    /// <param name="instruction">Opcode and operands separated by ordinary spaces.</param>
    /// <returns>Character index of the matched instruction line.</returns>
    static int GetVuInstructionIndex(string source, string instruction) {
        string[] instructionTokens = instruction.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string instructionPattern = string.Join("[ \\t]+", instructionTokens.Select(System.Text.RegularExpressions.Regex.Escape));
        System.Text.RegularExpressions.Match instructionMatch = System.Text.RegularExpressions.Regex.Match(
            source,
            $@"(?m)^[^\r\n]*\b{instructionPattern}(?:[ \t]|$)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.True(instructionMatch.Success, $"Expected VU instruction '{instruction}'.");
        return instructionMatch.Index;
    }
}
