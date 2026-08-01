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
        string limitsSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuTexturedSourceLimits.hpp"));
        string topPlaneBlock = GetLabelBlock(source, "texturedClipPlaneTop");
        string triangulateBlock = GetLabelBlock(source, "texturedClipTriangulatePolygon");
        string fanLoopBlock = GetLabelBlock(source, "texturedClipEmitTriangleFanLoop");
        string fanCompleteBlock = GetLabelBlock(source, "texturedClipEmitTriangleFanComplete");
        string restoreTriangleLoopTailBlock = GetLabelBlock(source, "texturedClipRestoreTriangleLoopTail");
        string triangleLoopTailBlock = GetLabelBlock(source, "texturedClipTriangleLoopTail");
        string validationBlock = GetSourceSection(source, "texturedClipEmitTriangle", "texturedClipEmitTriangleProject");
        string projectionBlock = GetSourceSection(source, "texturedClipEmitTriangleProject", "texturedClipEmitTriangleAccepted");
        string preAcceptanceBlock = GetSourceSection(source, "texturedClipEmitTriangle", "texturedClipEmitTriangleAccepted");
        string acceptedBlock = GetLabelBlock(source, "texturedClipEmitTriangleAccepted");
        string gifOutputBlock = GetSourceSection(source, "texturedClipRestoreGifTagEndFlag", "texturedClipIntersectEdge");

        Assert.Matches(@"(?m)^(?![ \t]*;)[^\r\n]*\bb\b[ \t]+texturedClipTriangulatePolygon[ \t]*\r?$", topPlaneBlock);
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipTriangulatePolygon") > GetLabelDeclarationIndex(source, "texturedClipPlaneTop"));
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipEmitTriangleFanLoop") > GetLabelDeclarationIndex(source, "texturedClipTriangulatePolygon"));
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipValidateTriangleA") > GetLabelDeclarationIndex(source, "texturedClipEmitTriangleFanLoop"));
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipValidateTriangleB") > GetLabelDeclarationIndex(source, "texturedClipValidateTriangleA"));
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipValidateTriangleC") > GetLabelDeclarationIndex(source, "texturedClipValidateTriangleB"));
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipEmitTriangleProject") > GetLabelDeclarationIndex(source, "texturedClipValidateTriangleC"));
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bdiv\b", validationBlock);

        AssertContainsVuInstruction(triangulateBlock, "lq VF18, 0(VI12)");
        AssertContainsVuInstruction(triangulateBlock, "lq VF21, 1(VI12)");
        AssertContainsVuInstruction(triangulateBlock, "ilw.x VI03, 0x0000004f(VI00)");
        AssertContainsVuInstruction(triangulateBlock, "iaddiu VI10, VI12, 0x00000002");
        AssertContainsVuInstruction(triangulateBlock, "isubiu VI14, VI13, 0x00000002");
        AssertContainsVuInstruction(fanLoopBlock, "lq VF19, 0(VI10)");
        AssertContainsVuInstruction(fanLoopBlock, "lq VF22, 1(VI10)");
        AssertContainsVuInstruction(fanLoopBlock, "lq VF20, 2(VI10)");
        AssertContainsVuInstruction(fanLoopBlock, "lq VF23, 3(VI10)");
        AssertContainsVuInstruction(fanLoopBlock, "bal VI15, texturedClipEmitTriangle");
        AssertContainsVuInstruction(fanLoopBlock, "iaddiu VI10, VI10, 0x00000002");
        AssertContainsVuInstruction(fanLoopBlock, "isubiu VI14, VI14, 0x00000001");
        AssertContainsVuInstruction(fanLoopBlock, "ibne VI14, VI00, texturedClipEmitTriangleFanLoop");
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bilw\.x\s+VI03\b", fanLoopBlock);
        Assert.True(GetLabelDeclarationIndex(source, "texturedClipEmitTriangleFanComplete") > GetLabelDeclarationIndex(source, "texturedClipEmitTriangleFanLoop"));
        AssertContainsVuInstruction(fanCompleteBlock, "ilw.x VI01, 0x0000004e(VI00)");
        AssertContainsVuInstruction(fanCompleteBlock, "b texturedClipTriangleLoopTail");
        Assert.True(
            GetVuInstructionIndex(fanCompleteBlock, "b texturedClipTriangleLoopTail")
            > GetVuInstructionIndex(fanCompleteBlock, "ilw.x VI01, 0x0000004e(VI00)"));
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bilw\.x\s+VI03\b", fanCompleteBlock);
        AssertContainsVuInstruction(restoreTriangleLoopTailBlock, "ilw.x VI03, 0x0000004f(VI00)");
        AssertContainsVuInstruction(triangleLoopTailBlock, "isubiu VI01, VI01, 0x00000001");

        AssertTriangleVertexValidation(GetLabelBlock(source, "texturedClipValidateTriangleA"), "VF18");
        AssertTriangleVertexValidation(GetLabelBlock(source, "texturedClipValidateTriangleB"), "VF19");
        AssertTriangleVertexValidation(GetLabelBlock(source, "texturedClipValidateTriangleC"), "VF20");
        AssertPerspectiveCorrectProjection(projectionBlock, "VF18", "VF21");
        AssertPerspectiveCorrectProjection(projectionBlock, "VF19", "VF22");
        AssertPerspectiveCorrectProjection(projectionBlock, "VF20", "VF23");

        AssertContainsVuInstruction(projectionBlock, "ibne VI08, VI00, texturedClipEmitTriangleAccepted");
        AssertContainsVuInstruction(projectionBlock, "opmula.xyz ACC, VF27xyz, VF28xyz");
        AssertContainsVuInstruction(projectionBlock, "opmsub.xyz VF29xyz, VF28xyz, VF27xyz");
        AssertContainsVuInstruction(projectionBlock, "ibgtz VI07, texturedClipEmitTriangleAccepted");
        AssertContainsVuInstruction(projectionBlock, "b texturedClipEmitTriangleReturn");
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\bibltz\s+VI07\b", projectionBlock);
        Assert.True(
            GetVuInstructionIndex(projectionBlock, "ibne VI08, VI00, texturedClipEmitTriangleAccepted")
            < GetVuInstructionIndex(projectionBlock, "opmula.xyz ACC, VF27xyz, VF28xyz"));
        Assert.True(
            GetVuInstructionIndex(projectionBlock, "ibgtz VI07, texturedClipEmitTriangleAccepted")
            < GetVuInstructionIndex(projectionBlock, "b texturedClipEmitTriangleReturn"));
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\biaddiu\s+VI03,\s*VI03", preAcceptanceBlock);
        AssertContainsVuInstruction(acceptedBlock, "iaddiu VI03, VI03, 0x00000009");
        AssertContainsVuInstruction(acceptedBlock, "iaddiu VI06, VI06, 0x00000001");

        AssertContainsVuInstruction(gifOutputBlock, "ibeq VI06, VI00, texturedClipNoOutput");
        AssertContainsVuInstruction(gifOutputBlock, "iaddiu VI09, VI00, 0x00007fff");
        AssertContainsVuInstruction(gifOutputBlock, "iaddiu VI09, VI09, 0x00000001");
        AssertContainsVuInstruction(gifOutputBlock, "iadd VI07, VI06, VI06");
        AssertContainsVuInstruction(gifOutputBlock, "iadd VI07, VI07, VI06");
        AssertContainsVuInstruction(gifOutputBlock, "iadd VI07, VI07, VI09");
        AssertContainsVuInstruction(gifOutputBlock, "isw.x VI07, 7(VI04)");
        AssertContainsVuInstruction(gifOutputBlock, "xgkick VI04");
        Assert.True(
            GetVuInstructionIndex(gifOutputBlock, "iaddiu VI09, VI00, 0x00007fff")
            < GetVuInstructionIndex(gifOutputBlock, "iaddiu VI09, VI09, 0x00000001"));
        Assert.True(
            GetVuInstructionIndex(gifOutputBlock, "iaddiu VI09, VI09, 0x00000001")
            < GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI06, VI06"));
        Assert.True(
            GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI06, VI06")
            < GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI07, VI06"));
        Assert.True(
            GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI07, VI06")
            < GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI07, VI09"));
        Assert.True(
            GetVuInstructionIndex(gifOutputBlock, "iadd VI07, VI07, VI09")
            < GetVuInstructionIndex(gifOutputBlock, "isw.x VI07, 7(VI04)"));
        uint maximumAcceptedVertexCount = GetUnsignedConstant(limitsSource, "TexturedVuClippedSourceTriangleCapacity")
            * (GetUnsignedConstant(limitsSource, "TexturedVuMaximumClipPolygonVertexCount") - 2u)
            * 3u;
        Assert.InRange(maximumAcceptedVertexCount, 0u, 0x7fffu);
        Assert.Contains("texturedClipNoOutput:", source, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gifOutputBlock, @"(?m)^(?![ \t]*;)[^\r\n]*\bxgkick\s+VI04\b").Cast<System.Text.RegularExpressions.Match>());
        AssertContainsVuInstruction(source, "iaddiu VI04, VI00, 0x00000100");
    }

    /// <summary>
    /// Verifies one fan vertex is checked against camera W, near Z, and every homogeneous side plane before projection.
    /// </summary>
    /// <param name="validationBlock">Validation instructions for one generated triangle vertex.</param>
    /// <param name="positionRegister">Homogeneous position register validated by the block.</param>
    static void AssertTriangleVertexValidation(string validationBlock, string positionRegister) {
        string cameraWInstruction = $"addw.x VF24, VF00, {positionRegister}w";
        string cameraEpsilonInstruction = "subi.x VF24, VF24, I";
        string leftBoundInstruction = $"addw.x VF25, {positionRegister}, {positionRegister}w";
        string foldInstruction = "minix.x VF24, VF24, VF25x";
        string rightSubtractInstruction = $"sub.x VF25, VF00, {positionRegister}";
        string rightBoundInstruction = $"addw.x VF25, VF25, {positionRegister}w";
        string bottomAddInstruction = $"addy.x VF25, VF00, {positionRegister}y";
        string bottomBoundInstruction = $"addw.x VF25, VF25, {positionRegister}w";
        string topSubtractInstruction = $"suby.x VF25, VF00, {positionRegister}y";
        string topBoundInstruction = $"addw.x VF25, VF25, {positionRegister}w";
        string nearBoundInstruction = $"subi.z VF25, {positionRegister}, I";
        string nearFoldInstruction = "miniz.x VF24, VF24, VF25z";
        int cameraWIndex = GetVuInstructionIndex(validationBlock, cameraWInstruction);
        int cameraEpsilonIndex = GetVuInstructionIndex(validationBlock, cameraEpsilonInstruction);
        int leftBoundIndex = GetVuInstructionIndex(validationBlock, leftBoundInstruction);
        int leftFoldIndex = GetVuInstructionIndex(validationBlock, foldInstruction);
        int rightSubtractIndex = GetVuInstructionIndex(validationBlock, rightSubtractInstruction);
        int rightBoundIndex = GetVuInstructionIndexAfter(validationBlock, rightBoundInstruction, rightSubtractIndex);
        int rightFoldIndex = GetVuInstructionIndexAfter(validationBlock, foldInstruction, rightBoundIndex);
        int bottomAddIndex = GetVuInstructionIndex(validationBlock, bottomAddInstruction);
        int bottomBoundIndex = GetVuInstructionIndexAfter(validationBlock, bottomBoundInstruction, bottomAddIndex);
        int bottomFoldIndex = GetVuInstructionIndexAfter(validationBlock, foldInstruction, bottomBoundIndex);
        int topSubtractIndex = GetVuInstructionIndex(validationBlock, topSubtractInstruction);
        int topBoundIndex = GetVuInstructionIndexAfter(validationBlock, topBoundInstruction, topSubtractIndex);
        int topFoldIndex = GetVuInstructionIndexAfter(validationBlock, foldInstruction, topBoundIndex);
        int nearBoundIndex = GetVuInstructionIndex(validationBlock, nearBoundInstruction);
        int nearFoldIndex = GetVuInstructionIndex(validationBlock, nearFoldInstruction);

        Assert.True(cameraWIndex < cameraEpsilonIndex);
        Assert.True(cameraEpsilonIndex < leftBoundIndex);
        Assert.True(leftBoundIndex < leftFoldIndex);
        Assert.True(leftFoldIndex < rightSubtractIndex);
        Assert.True(rightSubtractIndex < rightBoundIndex);
        Assert.True(rightBoundIndex < rightFoldIndex);
        Assert.True(rightFoldIndex < bottomAddIndex);
        Assert.True(bottomAddIndex < bottomBoundIndex);
        Assert.True(bottomBoundIndex < bottomFoldIndex);
        Assert.True(bottomFoldIndex < topSubtractIndex);
        Assert.True(topSubtractIndex < topBoundIndex);
        Assert.True(topBoundIndex < topFoldIndex);
        Assert.True(topFoldIndex < nearBoundIndex);
        Assert.True(nearBoundIndex < nearFoldIndex);

        int cameraEpsilonLineEndIndex = validationBlock.IndexOf('\n', cameraEpsilonIndex);
        Assert.True(cameraEpsilonLineEndIndex > cameraEpsilonIndex);
        string cameraAccumulatorBeforeFirstFold = validationBlock[(cameraEpsilonLineEndIndex + 1)..leftFoldIndex];
        Assert.DoesNotMatch(@"(?m)^(?![ \t]*;)[^\r\n]*\b[a-z]+(?:\.[xyzw]+)?\s+VF24\b", cameraAccumulatorBeforeFirstFold);
        AssertContainsVuInstruction(validationBlock, "fmand VI07, VI07");
        AssertContainsVuInstruction(validationBlock, "ibne VI07, VI00, texturedClipEmitTriangleReturn");
    }

    /// <summary>
    /// Reads an unsigned size constant from the shared VU source-limit contract so output bounds remain derived from capacity definitions.
    /// </summary>
    /// <param name="source">Complete shared VU source-limit header text.</param>
    /// <param name="constantName">Name of the unsigned constant to read.</param>
    /// <returns>The constant's unsigned value.</returns>
    static uint GetUnsignedConstant(string source, string constantName) {
        System.Text.RegularExpressions.Match constantMatch = System.Text.RegularExpressions.Regex.Match(
            source,
            $@"constexpr\s+std::size_t\s+{System.Text.RegularExpressions.Regex.Escape(constantName)}\s*=\s*(?<value>\d+)u\s*;",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        Assert.True(constantMatch.Success, $"Expected unsigned VU source-limit constant '{constantName}'.");
        return uint.Parse(constantMatch.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifies one projected vertex keeps raw UV coordinates until its guarded reciprocal-W projection sequence completes.
    /// </summary>
    /// <param name="projectionBlock">Projection instructions for one generated triangle.</param>
    /// <param name="positionRegister">Clipped homogeneous position input register.</param>
    /// <param name="uvRegister">Raw texture-coordinate input register.</param>
    static void AssertPerspectiveCorrectProjection(string projectionBlock, string positionRegister, string uvRegister) {
        string sequencePattern = $@"(?s)\bmulw\.xyzw\s+VF08,\s*{positionRegister},\s*VF00w\b.*?\bmulw\.xyzw\s+VF09,\s*{uvRegister},\s*VF00w\b.*?\bdiv\s+Q,\s*VF00w,\s*VF08w\b.*?\bmulq\.xy\s+VF09,\s*VF09,\s*Q\b\s+waitq\b.*?\baddq\.z\s+VF09,\s*VF00,\s*Q\b.*?\bmulq\.xyz\s+VF08,\s*VF08,\s*Q\b";

        Assert.Matches(sequencePattern, projectionBlock);
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

    /// <summary>
    /// Locates a lower VU instruction after a previous instruction so repeated folds can be asserted in execution order.
    /// </summary>
    /// <param name="source">VU assembly text containing the instruction sequence.</param>
    /// <param name="instruction">Opcode and operands separated by ordinary spaces.</param>
    /// <param name="previousInstructionIndex">Character index of the instruction that must precede the returned match.</param>
    /// <returns>Character index of the first matching instruction after the supplied predecessor.</returns>
    static int GetVuInstructionIndexAfter(string source, string instruction, int previousInstructionIndex) {
        Assert.InRange(previousInstructionIndex, 0, source.Length - 1);

        int searchStartIndex = previousInstructionIndex + 1;
        return searchStartIndex + GetVuInstructionIndex(source[searchStartIndex..], instruction);
    }
}
