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
    /// Requires the reserved pretransformed program to consume the Task 2 source contract without reviving VU1 clipping or changing active uploads.
    /// </summary>
    [Fact]
    public void Ps2OpaqueTexturedPretransformedDraw3D_UsesThePretransformedSevenQwordContractWithoutActiveUpload() {
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
        Assert.DoesNotContain("Ps2OpaqueTexturedPretransformedDraw3D_CodeStart", bootHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedPretransformedDraw3D.vsm", makefileSource, StringComparison.Ordinal);

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
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>The absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
