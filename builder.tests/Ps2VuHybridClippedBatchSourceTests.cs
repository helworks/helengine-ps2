namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies that the clipped textured batch builder preserves the required allocation-free matrix-to-fan contract.
/// </summary>
public sealed class Ps2VuHybridClippedBatchSourceTests {
    /// <summary>
    /// Requires the builder to transform packed local vertices through view and homogeneous clip space before clipping and fan generation.
    /// </summary>
    [Fact]
    public void Ps2VuClippedTexturedBatchBuilder_TransformsClipsAndBuildsBoundedFansWithoutAllocation() {
        string repositoryRootPath = GetRepositoryRootPath();
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuClippedTexturedBatchBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("TransformPosition(sourceTriangle.PositionA, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("TransformPosition(sourceTriangle.PositionB, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("TransformPosition(sourceTriangle.PositionC, worldView)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionA, projection)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionB, projection)", source, StringComparison.Ordinal);
        Assert.Contains("ProjectPosition(viewPositionC, projection)", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuTexturedTriangleClipper::ClipTriangle", source, StringComparison.Ordinal);
        Assert.Contains("clippedPolygon.GetVertexCount() < 3u", source, StringComparison.Ordinal);
        Assert.Contains("!std::isfinite(clippedVertex.ClipW) || clippedVertex.ClipW <= 0.0001f", source, StringComparison.Ordinal);
        Assert.Contains("outputFan.BuildFromClippedPolygon(clippedPolygon, sourceTriangle.FaceNormal);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::vector", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new", source, StringComparison.Ordinal);
        Assert.DoesNotContain("malloc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("realloc", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires the active pretransformed program to consume the Task 2 source contract without reviving the retired VU1 clipping program.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedPretransformedDraw3D_UsesThePretransformedSevenQwordContractThroughTheActiveUpload() {
        string repositoryRootPath = GetRepositoryRootPath();
        string programPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedPretransformedDraw3D.vsm");
        string addressPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuMicroProgramAddresses.hpp");
        string bootHostPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp");
        string makefilePath = Path.Combine(repositoryRootPath, "Makefile");
        string programSource = File.ReadAllText(programPath);
        string addressSource = File.ReadAllText(addressPath);
        string bootHostSource = File.ReadAllText(bootHostPath);
        string makefileSource = File.ReadAllText(makefilePath);

        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeStart", programSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeEnd", programSource, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint16_t TexturedPretransformedMicroProgramAddress = 320u;", addressSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeStart", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D.vsm", makefileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedClipDraw3D_CodeStart", bootHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedClipDraw3D.vsm", makefileSource, StringComparison.Ordinal);

        Assert.Contains("lq VF08, 0(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF08, 1(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF08, 2(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF09, 3(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF09, 4(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF09, 5(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF10, 6(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI05, VI05, 0x00000007", programSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI05, VI02, 0x00000015", programSource, StringComparison.Ordinal);

        Assert.Contains("iaddiu VI03, VI00, 0x00000100", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF08, 13(VI02)", programSource, StringComparison.Ordinal);
        Assert.Contains("lq VF08, 20(VI02)", programSource, StringComparison.Ordinal);
        Assert.Contains("sq VF08, 0(VI03)", programSource, StringComparison.Ordinal);
        Assert.Contains("sq VF08, 7(VI03)", programSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI03, VI03, 0x00000008", programSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI03, VI03, 0x00000009", programSource, StringComparison.Ordinal);
        Assert.Contains("iadd VI07, VI06, VI06", programSource, StringComparison.Ordinal);
        Assert.Contains("iadd VI07, VI07, VI06", programSource, StringComparison.Ordinal);
        Assert.Contains("iadd VI07, VI07, VI09", programSource, StringComparison.Ordinal);
        Assert.Contains("isw.x VI07, 7(VI04)", programSource, StringComparison.Ordinal);

        Assert.Equal(3, programSource.Split("div           Q, VF00w, VF08w", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, programSource.Split("mulq.xy       VF09, VF09, Q", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, programSource.Split("addq.z        VF09, VF00, Q", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, programSource.Split("mulq.w        VF17, VF00, Q", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("addq.w        VF17, VF00, Q", programSource, StringComparison.Ordinal);
        Assert.Equal(3, programSource.Split("mulq.xyz      VF08, VF08, Q", StringSplitOptions.None).Length - 1);
        Assert.Contains("mulw.w        VF17, VF00, VF10w", programSource, StringComparison.Ordinal);
        Assert.Contains("mulx.xyz      VF17, VF07, VF16x", programSource, StringComparison.Ordinal);
        Assert.Contains("ftoi0         VF17, VF17", programSource, StringComparison.Ordinal);

        Assert.Contains("ibne VI08, VI00, texturedPretransformedTriangleAccepted", programSource, StringComparison.Ordinal);
        Assert.Contains("opmula.xyz", programSource, StringComparison.Ordinal);
        Assert.Contains("opmsub.xyz", programSource, StringComparison.Ordinal);
        Assert.Contains("ibgez VI07, texturedPretransformedTriangleAccepted", programSource, StringComparison.Ordinal);
        Assert.Contains("ibeq VI06, VI00, texturedPretransformedDrawComplete", programSource, StringComparison.Ordinal);
        Assert.Equal(1, programSource.Split("xgkick", StringSplitOptions.None).Length - 1);

        Assert.DoesNotContain("VF01", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VF02", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VF03", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("VF04", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("clipw", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("fcand", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Polygon", programSource, StringComparison.Ordinal);
        Assert.DoesNotContain("0x0000004", programSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires the exceptional clipped route to preclassify fixed outer slices before allocation while preserving the ordinary packet budget for safe work.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_PreclassifiesOuterRoutesAndUsesTheExceptionalBudgetOnlyForIntersectingSlices() {
        string repositoryRootPath = GetRepositoryRootPath();
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr std::uint16_t MaximumTexturedVuSourcePacketQwords = 2048u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint16_t MaximumTexturedVuExceptionalPacketQwords = 4096u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t MaximumTexturedVuSourceBatchCount", source, StringComparison.Ordinal);
        Assert.Contains("if (batchCount > MaximumTexturedVuSourceBatchCount)", source, StringComparison.Ordinal);
        Assert.Contains("std::array<Ps2VuNearPlaneRoute, MaximumTexturedVuSourceBatchCount> outerRoutes", source, StringComparison.Ordinal);
        Assert.Contains("std::array<::float4x4, MaximumTexturedVuSourceBatchCount> cachedWorldViews", source, StringComparison.Ordinal);
        Assert.Contains("std::array<::float4x4, MaximumTexturedVuSourceBatchCount> cachedWorldViewProjections", source, StringComparison.Ordinal);
        Assert.Contains("bool hasClippedOuterRoute = false;", source, StringComparison.Ordinal);
        Assert.Contains("MaximumTexturedVuExceptionalPacketQwords", source, StringComparison.Ordinal);
        Assert.Contains("Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan", source, StringComparison.Ordinal);
        Assert.Contains("TexturedPretransformedMicroProgramAddress", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires the hybrid packet builder to expose exact per-packet source and generated triangle aggregates without timing the refinement loop.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_ReportsResettableHybridSourceAndGeneratedTriangleAggregatesWithoutHotLoopTiming() {
        string repositoryRootPath = GetRepositoryRootPath();
        string headerPath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp");
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string header = File.ReadAllText(headerPath);
        string source = File.ReadAllText(sourcePath);

        Assert.Contains("GetFastTexturedSourceTriangleCount", header, StringComparison.Ordinal);
        Assert.Contains("GetClippedTexturedSourceTriangleCount", header, StringComparison.Ordinal);
        Assert.Contains("GetRejectedTexturedSourceTriangleCount", header, StringComparison.Ordinal);
        Assert.Contains("GetGeneratedClippedTexturedTriangleCount", header, StringComparison.Ordinal);
        Assert.Contains("GetClippedTexturedBatchCount", header, StringComparison.Ordinal);
        Assert.Contains("FastTexturedSourceTriangleCount = 0u;", source, StringComparison.Ordinal);
        Assert.Contains("ClippedTexturedSourceTriangleCount = 0u;", source, StringComparison.Ordinal);
        Assert.Contains("RejectedTexturedSourceTriangleCount = 0u;", source, StringComparison.Ordinal);
        Assert.Contains("GeneratedClippedTexturedTriangleCount = 0u;", source, StringComparison.Ordinal);
        Assert.Contains("ClippedTexturedBatchCount = 0u;", source, StringComparison.Ordinal);
        Assert.Contains("FastTexturedSourceTriangleCount += batchSlice.SourceTriangleCount;", source, StringComparison.Ordinal);
        Assert.Contains("ClippedTexturedSourceTriangleCount++;", source, StringComparison.Ordinal);
        Assert.Contains("RejectedTexturedSourceTriangleCount++;", source, StringComparison.Ordinal);
        Assert.Contains("GeneratedClippedTexturedTriangleCount += clippedFan.GetTriangleCount();", source, StringComparison.Ordinal);
        Assert.Contains("ClippedTexturedBatchCount++;", source, StringComparison.Ordinal);
        Assert.Contains("SubmittedTriangleCount += clippedBatch.GetTriangleCount();", source, StringComparison.Ordinal);

        int refinementStartIndex = source.IndexOf("for (std::size_t sourceTriangleOffset = 0u;", StringComparison.Ordinal);
        int refinementEndIndex = source.IndexOf("packet2_chain_open_end(packet.get(), 0, 0);", refinementStartIndex, StringComparison.Ordinal);
        Assert.True(refinementStartIndex >= 0, "Expected the textured source-triangle refinement loop.");
        Assert.True(refinementEndIndex > refinementStartIndex, "Expected the textured packet finalization after refinement.");
        string refinement = source.Substring(refinementStartIndex, refinementEndIndex - refinementStartIndex);
        Assert.DoesNotContain("std::clock", refinement, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires exact clipping to classify an original source triangle as clipped only after it produces a non-empty generated fan.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenExactClippingProducesNoFan_CountsTheSourceOnlyAsRejected() {
        string repositoryRootPath = GetRepositoryRootPath();
        string sourcePath = Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp");
        string source = File.ReadAllText(sourcePath);

        int fanBuildIndex = source.IndexOf("Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan(", StringComparison.Ordinal);
        int generatedCountIndex = source.IndexOf("GeneratedClippedTexturedTriangleCount += clippedFan.GetTriangleCount();", fanBuildIndex, StringComparison.Ordinal);
        int emptyFanBranchIndex = source.IndexOf("if (clippedFan.GetTriangleCount() == 0u) {", generatedCountIndex, StringComparison.Ordinal);
        int rejectedCountIndex = source.IndexOf("RejectedTexturedSourceTriangleCount++;", emptyFanBranchIndex, StringComparison.Ordinal);
        int clippedCountIndex = source.IndexOf("ClippedTexturedSourceTriangleCount++;", emptyFanBranchIndex, StringComparison.Ordinal);

        Assert.True(fanBuildIndex >= 0, "Expected exact fan generation in the refined textured route.");
        Assert.True(generatedCountIndex > fanBuildIndex, "Expected generated output accounting immediately after exact fan creation.");
        Assert.True(emptyFanBranchIndex > generatedCountIndex, "Expected generated accounting before the exact empty-fan classification.");
        Assert.True(rejectedCountIndex > emptyFanBranchIndex, "Expected an empty exact fan to count the source as rejected.");
        Assert.True(clippedCountIndex > rejectedCountIndex, "Expected clipped source accounting only after the empty-fan rejection branch.");
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>The absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
