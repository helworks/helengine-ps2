using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 native source inputs required by the runtime build pipeline.
/// </summary>
public sealed class Ps2NativeBuildInputsTests {
    /// <summary>
    /// Ensures the textured VU1 program performs perspective-correct coordinate generation before kicking GIF output.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_transforms_vertices_with_perspective_q() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        if (microProgram.Contains("boundary diagnostic", StringComparison.Ordinal)) {
            return;
        }

        Assert.Contains("div           Q", microProgram, StringComparison.Ordinal);
        Assert.Contains("mulq.xy", microProgram, StringComparison.Ordinal);
        Assert.Contains("xgkick", microProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("NOP                                                        xgkick VI02", microProgram, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured VU1 program waits for the final matrix multiply-add before dividing by clip W.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_waits_for_clip_w_before_perspective_divide() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        if (microProgram.Contains("boundary diagnostic", StringComparison.Ordinal)) {
            return;
        }

        int matrixCompletionIndex = microProgram.IndexOf("maddw         VF08, VF04, VF08w", StringComparison.Ordinal);
        int perspectiveDivideIndex = microProgram.IndexOf("div           Q, VF00w, VF08w", matrixCompletionIndex, StringComparison.Ordinal);
        Assert.True(matrixCompletionIndex >= 0, "Expected the textured VU1 matrix completion instruction.");
        Assert.True(perspectiveDivideIndex > matrixCompletionIndex, "Expected the textured VU1 perspective divide after matrix completion.");

        string dependencyWindow = microProgram.Substring(matrixCompletionIndex, perspectiveDivideIndex - matrixCompletionIndex);
        int independentCycles = dependencyWindow
            .Split('\n')
            .Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP");
        Assert.Equal(4, independentCycles);
    }

    /// <summary>
    /// Ensures the textured VU1 program allows each projected-position result to settle before the next dependent operation.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_waits_between_projected_position_dependencies() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        if (microProgram.Contains("boundary diagnostic", StringComparison.Ordinal)) {
            return;
        }

        int perspectiveMultiplyIndex = microProgram.IndexOf("mulq.xyz      VF08, VF08, Q", StringComparison.Ordinal);
        int screenScaleIndex = microProgram.IndexOf("mul.xyz       VF08, VF08, VF05", perspectiveMultiplyIndex, StringComparison.Ordinal);
        int screenOffsetIndex = microProgram.IndexOf("add.xyz       VF08, VF08, VF06", screenScaleIndex, StringComparison.Ordinal);
        int depthConversionIndex = microProgram.IndexOf("ftoi0.z       VF08, VF08", screenOffsetIndex, StringComparison.Ordinal);
        int xyConversionIndex = microProgram.IndexOf("ftoi4.xy      VF08, VF08", depthConversionIndex, StringComparison.Ordinal);

        Assert.True(perspectiveMultiplyIndex >= 0 && screenScaleIndex > perspectiveMultiplyIndex);
        Assert.True(screenOffsetIndex > screenScaleIndex && depthConversionIndex > screenOffsetIndex);
        Assert.True(xyConversionIndex > depthConversionIndex);
        Assert.Equal(4, microProgram.Substring(perspectiveMultiplyIndex, screenScaleIndex - perspectiveMultiplyIndex).Split('\n').Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP"));
        Assert.Equal(4, microProgram.Substring(screenScaleIndex, screenOffsetIndex - screenScaleIndex).Split('\n').Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP"));
        Assert.Equal(4, microProgram.Substring(screenOffsetIndex, depthConversionIndex - screenOffsetIndex).Split('\n').Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP"));
    }

    /// <summary>
    /// Ensures every VU-produced XYZ2 vertex clears its ADC word before GIF submission so clip-space W cannot suppress or corrupt GS primitives.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_clears_xyz2_adc_words_before_gif_submission() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        if (microProgram.Contains("boundary diagnostic", StringComparison.Ordinal)) {
            return;
        }

        Assert.Contains("iaddiu VI06, VI00, 0x00000000", microProgram, StringComparison.Ordinal);
        Assert.Equal(1, microProgram.Split("isw.w VI06, 2(VI03)", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, microProgram.Split("isw.w VI06, 5(VI03)", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, microProgram.Split("isw.w VI06, 8(VI03)", StringSplitOptions.None).Length - 1);
        string triangleLoop = microProgram.Substring(microProgram.IndexOf("texturedTriangleLoop:", StringComparison.Ordinal));
        Assert.True(
            triangleLoop.IndexOf("sq VF08, 2(VI03)", StringComparison.Ordinal)
                < triangleLoop.IndexOf("isw.w VI06, 2(VI03)", StringComparison.Ordinal),
            "The first XYZ2 qword must be written before its ADC word is cleared.");
        Assert.True(
            triangleLoop.IndexOf("sq VF08, 5(VI03)", StringComparison.Ordinal)
                < triangleLoop.IndexOf("isw.w VI06, 5(VI03)", StringComparison.Ordinal),
            "The second XYZ2 qword must be written before its ADC word is cleared.");
        Assert.True(
            triangleLoop.IndexOf("sq VF08, 8(VI03)", StringComparison.Ordinal)
                < triangleLoop.IndexOf("isw.w VI06, 8(VI03)", StringComparison.Ordinal),
            "The third XYZ2 qword must be written before its ADC word is cleared.");
    }

    /// <summary>
    /// Ensures the textured VU1 program allows each source position load to complete before starting its matrix transformation.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_waits_for_source_position_load_before_transforming() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        if (microProgram.Contains("boundary diagnostic", StringComparison.Ordinal)) {
            return;
        }

        int positionLoadIndex = microProgram.IndexOf("lq VF08, 0(VI05)", StringComparison.Ordinal);
        int matrixStartIndex = microProgram.IndexOf("mulax         ACC, VF01, VF08x", positionLoadIndex, StringComparison.Ordinal);
        Assert.True(positionLoadIndex >= 0 && matrixStartIndex > positionLoadIndex);
        Assert.Equal(4, microProgram.Substring(positionLoadIndex, matrixStartIndex - positionLoadIndex).Split('\n').Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP"));
    }

    /// <summary>
    /// Ensures each VU matrix result is available before its homogeneous W component is used for perspective division.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_waits_for_matrix_result_before_perspective_division() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        int firstMatrixResultIndex = microProgram.IndexOf("maddw         VF08, VF04, VF08w", StringComparison.Ordinal);
        int firstPerspectiveDivisionIndex = microProgram.IndexOf("div           Q, VF00w, VF08w", firstMatrixResultIndex, StringComparison.Ordinal);
        Assert.True(firstMatrixResultIndex >= 0 && firstPerspectiveDivisionIndex > firstMatrixResultIndex);
        Assert.Equal(4, microProgram.Substring(firstMatrixResultIndex, firstPerspectiveDivisionIndex - firstMatrixResultIndex).Split('\n').Count(line => line.Trim().Replace(" ", string.Empty, StringComparison.Ordinal) == "NOPNOP"));
    }

    /// <summary>
    /// Ensures the B82 dynamic VU program is restored after the transport boundary diagnostics.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_restores_dynamic_triangle_generation_after_boundary_diagnostics() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string microProgram = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        Assert.DoesNotContain("boundary diagnostic", microProgram, StringComparison.Ordinal);
        Assert.Contains("texturedTriangleLoop:", microProgram, StringComparison.Ordinal);
        Assert.Contains("mulax         ACC, VF01, VF08x", microProgram, StringComparison.Ordinal);
        Assert.Contains("xgkick VI04", microProgram, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures B84 removes all fixed-packet storage before dynamic VU vertex generation is tested.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_packet_builder_removes_fixed_gif_diagnostic_storage_for_dynamic_generation() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.DoesNotContain("EnableTexturedVuFixedGifOutputDiagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2VuTexturedFixedGifTriangle", source, StringComparison.Ordinal);
        Assert.Contains("sourceTriangles.push_back(sourceTriangle);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the dynamic textured VU1 path receives local mesh source data instead of CPU-projected GIF registers.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_packet_builder_packs_local_source_triangles() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("void AddOpaqueTexturedVuBatches(", header, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuTexturedSourceTriangle", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack", source, StringComparison.Ordinal);
        int methodStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(", StringComparison.Ordinal);
        int methodEndIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", methodStartIndex, StringComparison.Ordinal);
        Assert.True(methodStartIndex >= 0, "Expected the dynamic textured VU1 packet builder.");
        Assert.True(methodEndIndex > methodStartIndex, "Expected the existing CPU textured fallback after the VU1 packet builder.");

        string vuFastPathBody = source.Substring(methodStartIndex, methodEndIndex - methodStartIndex);
        Assert.DoesNotContain("TryClassifyAndBuildTexturedVertexPositionRegister", vuFastPathBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the dynamic textured VU1 path submits every eligible bounded batch after correctness diagnostics are complete.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_submits_all_dynamic_textured_vu_batches() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.Contains("constexpr bool EnableTexturedVuSingleBatchDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedVuBatches.resize(dynamicTexturedVuBatchCount);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedVuWorlds.resize(dynamicTexturedVuBatchCount);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("texturedVuTextures.resize(dynamicTexturedVuBatchCount);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the dynamic textured VU1 path preserves every source triangle in each bounded batch.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_preserves_dynamic_textured_vu_batch_triangle_counts() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.DoesNotContain("texturedVuBatches.front().SourceTriangleCount = std::min(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicTexturedVuDiagnosticTriangleLimit,", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures color-only materials can use the renderer-owned white texture through the textured VU path without adding a cooked texture asset.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_uses_runtime_white_texture_batches_on_textured_vu_path_when_they_are_frustum_safe() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RenderManager3D.cpp"));

        Assert.Contains("const bool canUseTexturedVuFastPath = (batch.Textured || usesRuntimeWhiteTexture)", source, StringComparison.Ordinal);
        Assert.Contains("&& CanUseTexturedVuFastPath(batch, world, view, projection, nearPlaneDistance);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured VU program writes generated GIF data outside both VIF double-buffer input regions.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_writes_gif_output_outside_double_buffered_inputs() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));

        Assert.Contains("iaddiu VI03, VI00, 0x00000100", source, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI04, VI03, 0x00000000", source, StringComparison.Ordinal);
        Assert.DoesNotContain("iaddiu VI03, VI00, 0x00000200", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures every VU-textured vertex emits the packed GIF STQ, RGBA, and XYZ stream in the order required for perspective-correct texturing.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_emits_packed_stq_rgba_xyz_stream() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));
        string packetBuilderSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Equal(3, source.Split("addq.z        VF09, VF00, Q", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, source.Split("ftoi0         VF17, VF17", StringSplitOptions.None).Length - 1);
        Assert.Contains("sq VF09, 0(VI03)", source, StringComparison.Ordinal);
        Assert.Contains("sq VF17, 1(VI03)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("addq.w        VF10, VF00, Q", source, StringComparison.Ordinal);
        Assert.Contains("float MaterialLighting[4];", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("sharedState.MaterialLighting[0] = static_cast<float>(lightingConstants.BaseColorR) / 255.0f;", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("sharedState.StateTemplate[7].High = (static_cast<std::uint64_t>(GIF_REG_ST) << 0u)", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("| (static_cast<std::uint64_t>(GIF_REG_RGBAQ) << 4u)", packetBuilderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured VU program calculates its flat diffuse color from the uploaded normal, light, and material inputs.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_program_calculates_flat_diffuse_color() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string programSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "programs", "Ps2OpaqueTexturedDraw3D.vsm"));
        string packetBuilderSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        string packetBuilderHeaderSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        int dynamicVuPathStartIndex = packetBuilderSource.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(", StringComparison.Ordinal);
        int cpuPathStartIndex = packetBuilderSource.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", dynamicVuPathStartIndex, StringComparison.Ordinal);
        string dynamicVuPath = packetBuilderSource.Substring(dynamicVuPathStartIndex, cpuPathStartIndex - dynamicVuPathStartIndex);

        Assert.Contains("float FaceNormal[4];", packetBuilderSource, StringComparison.Ordinal);
        Assert.Contains("const ::float3& lightDirection", packetBuilderHeaderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::uint64_t triangleColor = ResolveTexturedVertexColor", dynamicVuPath, StringComparison.Ordinal);
        Assert.Contains("lq VF10, 6(VI05)", programSource, StringComparison.Ordinal);
        Assert.Contains("ersqrt", programSource, StringComparison.Ordinal);
        Assert.Contains("mulx.xyz", programSource, StringComparison.Ordinal);
        Assert.Contains("ftoi0         VF17, VF17", programSource, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI05, VI05, 0x00000007", programSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the dynamic textured VU path reuses immutable triangle sources instead of decoding packed buffers every frame.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_fast_path_uses_cached_immutable_triangle_sources() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        int dynamicVuPathStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(", StringComparison.Ordinal);
        int cpuPathStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", StringComparison.Ordinal);
        string dynamicVuPath = source.Substring(dynamicVuPathStartIndex, cpuPathStartIndex - dynamicVuPathStartIndex);

        Assert.Contains("const Ps2RuntimeModel* runtimeModel = batch->Proxy != nullptr ? batch->Proxy->GetModel() : nullptr;", dynamicVuPath, StringComparison.Ordinal);
        Assert.Contains("TexturedPacketCache.ResolveTriangleSources(*batch->Model, runtimeModel)", dynamicVuPath, StringComparison.Ordinal);
        Assert.DoesNotContain("const float* packedPositionWords", dynamicVuPath, StringComparison.Ordinal);
        Assert.DoesNotContain("const std::vector<std::uint16_t>* runtimeIndices", dynamicVuPath, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the textured VU source stream supplies VU1 with normal and material inputs for flat diffuse lighting.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_source_packet_contains_world_space_lighting_inputs() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("float FaceNormal[4];", source, StringComparison.Ordinal);
        Assert.Contains("float WorldNormalMatrix[16];", source, StringComparison.Ordinal);
        Assert.Contains("float WorldLightDirection[4];", source, StringComparison.Ordinal);
        Assert.Contains("float MaterialLighting[4];", source, StringComparison.Ordinal);
        Assert.Contains("sourceRecord.FaceNormal[0] = sourceTriangle.FaceNormal.X;", source, StringComparison.Ordinal);
        Assert.Contains("CopyMatrixWords(worlds[batchIndex], sharedState.WorldNormalMatrix);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the dynamic textured VU path accounts for CPU time spent constructing its per-triangle source payload separately from VIF packet assembly.
    /// </summary>
    [Fact]
    public void Ps2_dynamic_textured_vu_path_measures_source_payload_construction() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        int dynamicVuPathStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(", StringComparison.Ordinal);
        int cpuPathStartIndex = source.IndexOf("void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(", StringComparison.Ordinal);
        string dynamicVuPath = source.Substring(dynamicVuPathStartIndex, cpuPathStartIndex - dynamicVuPathStartIndex);

        Assert.Contains("const std::clock_t sourcePayloadFillStartTicks = std::clock();", dynamicVuPath, StringComparison.Ordinal);
        Assert.Contains("LastTrianglePayloadFillMilliseconds += ResolveMillisecondsFromClockTicks(sourcePayloadFillStartTicks, std::clock());", dynamicVuPath, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures unlit textured draws modulate the white fallback texture with the material's authored base color instead of a neutral diagnostic color.
    /// </summary>
    [Fact]
    public void Ps2_textured_packet_builder_uses_authored_base_color_for_unlit_materials() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));
        const string expectedColorRegister = "GS_SETREG_RGBAQ(lightingConstants.BaseColorR, lightingConstants.BaseColorG, lightingConstants.BaseColorB, lightingConstants.BaseColorA, 0x00)";

        Assert.Equal(2, source.Split(expectedColorRegister, StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Ensures the dynamic textured VU1 packet reserves the VIF chain commands in addition to its unpacked mesh data.
    /// </summary>
    [Fact]
    public void Ps2_textured_vu_packet_builder_reserves_per_submission_vif_overhead() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("constexpr std::size_t TexturedVuSourceBatchPayloadQwordCount", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t TexturedVuSourceBatchSubmissionOverheadQwordCount = 2u;", source, StringComparison.Ordinal);
        Assert.Contains(
            "batches.size() * (TexturedVuSourceBatchPayloadQwordCount + TexturedVuSourceBatchSubmissionOverheadQwordCount)",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 DualShock analog axes are normalized into the shared gamepad state used by menu navigation.
    /// </summary>
    [Fact]
    public void Ps2_input_backend_maps_dualshock_left_stick_axes() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string mapperHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2PadInputMapper.hpp"));
        string inputSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2InputBackend.cpp"));

        Assert.Contains("int16_t LeftStickX = 0;", mapperHeader, StringComparison.Ordinal);
        Assert.Contains("int16_t LeftStickY = 0;", mapperHeader, StringComparison.Ordinal);
        Assert.Contains("padSetMainMode(Port, Slot, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK);", inputSource, StringComparison.Ordinal);
        Assert.Contains("gamepad.set_LeftStickX(CurrentButtons.LeftStickX);", inputSource, StringComparison.Ordinal);
        Assert.Contains("gamepad.set_LeftStickY(CurrentButtons.LeftStickY);", inputSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 controller mapper remains gamepad-only and never imports desktop keyboard generated-core state.
    /// </summary>
    [Fact]
    public void Ps2_pad_input_mapper_has_no_desktop_keyboard_dependency() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string mapperHeader = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2PadInputMapper.hpp"));

        Assert.DoesNotContain("Keys.hpp", mapperHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPadButtonsToKeys", mapperHeader, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native runtime exposes one custom file-system bridge that maps rooted cooked logical paths onto the generated disc-layout manifest before delegating to file-stream reads.
    /// </summary>
    [Fact]
    public void Ps2_runtime_custom_file_system_resolves_rooted_cooked_paths_through_the_generated_manifest() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string makefile = File.ReadAllText(Path.Combine(repositoryRootPath, "Makefile"));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscFileSystem.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscFileSystem.cpp"));

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/Ps2DiscFileSystem.cpp", makefile, StringComparison.Ordinal);
        Assert.Contains("class Ps2DiscFileSystem final", header, StringComparison.Ordinal);
        Assert.Contains("static bool CanHandlePath(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("static bool Exists(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("static FileStream* OpenRead(const char* path);", header, StringComparison.Ordinal);
        Assert.Contains("#include \"runtime/runtime_ps2_asset_path_manifest.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("return path.rfind(\"/cooked/\", 0) == 0;", source, StringComparison.Ordinal);
        Assert.Contains("const char* physicalPath = he_get_runtime_ps2_asset_physical_path(logicalPath);", source, StringComparison.Ordinal);
        Assert.Contains("return new FileStream(resolvedPhysicalPath, FileMode::Open, FileAccess::Read, FileShare::Read);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the native boot host receives its mandatory content stream source from the PS2 disc runtime instead of a host-only generated class.
    /// </summary>
    [Fact]
    public void Ps2_boot_host_uses_the_native_disc_content_stream_source() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string makefile = File.ReadAllText(Path.Combine(repositoryRootPath, "Makefile"));
        string bootHostSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscContentStreamSource.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2DiscContentStreamSource.cpp"));

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/Ps2DiscContentStreamSource.cpp", makefile, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/Ps2DiscContentStreamSource.hpp\"", bootHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HostFileSystemContentStreamSource", bootHostSource, StringComparison.Ordinal);
        Assert.Contains("class Ps2DiscContentStreamSource final : public ::IContentStreamSource", header, StringComparison.Ordinal);
        Assert.Contains("::Stream* OpenRead(std::string assetPath) override;", header, StringComparison.Ordinal);
        Assert.Contains("#include \"system/io/file-stream.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("return Ps2DiscFileSystem::OpenRead(assetPath.c_str());", source, StringComparison.Ordinal);
        Assert.Contains("new helengine::ps2::Ps2DiscContentStreamSource()", bootHostSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 runtime model exposes embedded VU packed geometry loaded directly from the single-file PS2 cooked model asset payload.
    /// </summary>
    [Fact]
    public void Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "Ps2RuntimeModel.cpp"));

        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuPackedModel.hpp\"", header, StringComparison.Ordinal);
        Assert.Contains("void LoadFromCooked(::Ps2ModelAsset* modelAsset);", header, StringComparison.Ordinal);
        Assert.Contains("const Ps2VuPackedModel* GetVuPackedModel() const;", header, StringComparison.Ordinal);
        Assert.Contains("Ps2VuPackedModel* VuPackedModel;", header, StringComparison.Ordinal);
        Assert.Contains("VuPackedModel = new Ps2VuPackedModel();", source, StringComparison.Ordinal);
        Assert.Contains("modelAsset->PackedMeshBytes", source, StringComparison.Ordinal);
        Assert.Contains("VuPackedModel->LoadFromPackedBytes(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("            return;\r\n        }\r\n\r\n        if (modelAsset->Indices32 != nullptr && modelAsset->Indices32->Length > 0) {", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the packed VU runtime model parses the embedded triangle-stream header and exposes section counts and qword-aligned section pointers for packet assembly.
    /// </summary>
    [Fact]
    public void Ps2_vu_packed_model_parses_triangle_stream_header_and_exposes_section_accessors() {
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.hpp");
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.cpp");

        Assert.Contains("std::uint32_t GetTriangleVertexCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("const std::uint8_t* GetPositionBlockBytes() const;", header, StringComparison.Ordinal);
        Assert.Contains("const std::uint8_t* GetTexCoordBlockBytes() const;", header, StringComparison.Ordinal);
        Assert.Contains("TriangleVertexCount = ReadUInt32(4);", source, StringComparison.Ordinal);
        Assert.Contains("PositionBlockOffsetQwords = ReadUInt32(8);", source, StringComparison.Ordinal);
        Assert.Contains("TexCoordBlockOffsetQwords = ReadUInt32(16);", source, StringComparison.Ordinal);
        Assert.Contains("return PackedBytes.data() + (PositionBlockOffsetQwords * 16u);", source, StringComparison.Ordinal);
        Assert.Contains("return PackedBytes.data() + (TexCoordBlockOffsetQwords * 16u);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer header declares the VU opaque batch and packet infrastructure required by the fast path.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure() {
        string rendererHeader = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
        Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp\"", rendererHeader, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer routes opaque draws through a VU path while retaining the current CPU path behind an explicit internal fallback gate.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("RenderOpaqueWithVuPath(", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(", source, StringComparison.Ordinal);
        Assert.Contains("if (UseLegacyCpuOpaquePath)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the VU opaque batch builder emits per-proxy batches from the opaque frame-plan lists only when a packed VU model and runtime material are available.
    /// </summary>
    [Fact]
    public void Ps2_vu_opaque_batch_builder_emits_batches_for_opaque_proxies_with_packed_models() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueBatchBuilder.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuOpaqueBatchBuilder.cpp"));

        Assert.Contains("std::size_t GetLastRejectedMissingMaterialCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastRejectedMissingModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastRejectedMissingPackedModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingMaterialCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingModelCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("mutable std::size_t LastRejectedMissingPackedModelCount = 0;", header, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingMaterialCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingModelCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingPackedModelCount = 0;", source, StringComparison.Ordinal);
        Assert.Contains("AppendProxyBatches(plan.OpaqueWorld, batches);", source, StringComparison.Ordinal);
        Assert.Contains("AppendProxyBatches(plan.OpaqueDynamic, batches);", source, StringComparison.Ordinal);
        Assert.Contains("proxy->GetMaterial()", source, StringComparison.Ordinal);
        Assert.Contains("proxy->GetModel()", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingMaterialCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingModelCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastRejectedMissingPackedModelCount += 1;", source, StringComparison.Ordinal);
        Assert.Contains("runtimeModel->GetVuPackedModel()", source, StringComparison.Ordinal);
        Assert.Contains("batch.Proxy = proxy;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model = packedModel;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Material = runtimeMaterial;", source, StringComparison.Ordinal);
        Assert.Contains("batch.Textured = runtimeMaterial->HasTextureRelativePath();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("batch.Textured = !runtimeMaterial->GetTextureRelativePath().empty();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 VU packet builder assembles a real VIF chain packet from the packed triangle stream and a
    /// per-batch local-screen transform instead of remaining as a placeholder.
    /// </summary>
    [Fact]
    public void Ps2_vu_vif_packet_builder_assembles_local_screen_and_triangle_stream_packet_data() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string header = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.hpp"));
        string source = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "rendering", "vu", "Ps2VuVifPacketBuilder.cpp"));

        Assert.Contains("std::uint32_t GetLastCompletedPhase() const;", header, StringComparison.Ordinal);
        Assert.Contains("~Ps2VuVifPacketBuilder();", header, StringComparison.Ordinal);
        Assert.Contains("packet2_t* GetPacket() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetPacketByteCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("packet2_t* Packet = nullptr;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t LastCompletedPhase = 0;", header, StringComparison.Ordinal);
        Assert.Contains("const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, int textureWidth, int textureHeight", header, StringComparison.Ordinal);
        Assert.Contains("#include <packet2.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2_utils.h>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t EnableVuPacketPhaseDiagnostics = 0;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t VuPacketDiagnosticCutoffPhase = 11;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::uint32_t XtopGifPacketAddress = 0;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableVuFixedTriangleDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr bool EnableVuTwoTriangleBatchDiagnostic", source, StringComparison.Ordinal);
        Assert.DoesNotContain("constexpr std::uint32_t VuDiagnosticBatchTriangleCount = 2u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t TriangleGifPacketTemplateQwordCount = 11u;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t LitTrianglePayloadQwordCount = sizeof(Ps2VuLitTrianglePayload) / 16u;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 1;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 2;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 3;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 4;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 5;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 6;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 9;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 10;", source, StringComparison.Ordinal);
        Assert.Contains("LastCompletedPhase = 11;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {", source, StringComparison.Ordinal);
        Assert.Contains("packet2_create(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_open_unpack(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_close_unpack(", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model->GetTriangleVertexCount()", source, StringComparison.Ordinal);
        Assert.Contains("batch.Model->GetPositionBlockBytes()", source, StringComparison.Ordinal);
        Assert.Contains("struct alignas(16) Ps2VuLitTrianglePayload", source, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(payload.FaceNormal, triangleSetup.FaceNormal, sizeof(triangleSetup.FaceNormal));", source, StringComparison.Ordinal);
        Assert.Contains("TryBuildVertexPositionRegister(", source, StringComparison.Ordinal);
        Assert.Contains("GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);", source, StringComparison.Ordinal);
        Assert.Contains("std::memcpy(GifPacketBytes.data(), trianglePayloads.front().GifPacketTemplate, TriangleGifPacketTemplateByteCount);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildUntexturedTriangleGifPacketBytes(", source, StringComparison.Ordinal);
        Assert.Contains("GetTexCoordBlockBytes()", source, StringComparison.Ordinal);
        Assert.Contains("packet2_get_qw_count(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_mscal(packet.get(), UntexturedMicroProgramAddress, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            PacketBytes.resize(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n            std::memcpy(PacketBytes.data()", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer computes a combined view-projection matrix once per frame and a per-proxy world matrix
    /// before feeding opaque batches into the VU packet builder.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_computes_view_projection_and_world_matrices_for_vu_batches() {
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float4x4 BuildWorldMatrix(const Ps2RenderProxy& proxy) const;", header, StringComparison.Ordinal);
        Assert.Contains("const int2 windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
        Assert.Contains("::float4 viewport = ResolvePixelViewport(camera, windowSize);", source, StringComparison.Ordinal);
        Assert.Contains("::float4x4 world = BuildWorldMatrix(*batch.Proxy);", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.AddOpaqueBatch(", source, StringComparison.Ordinal);
        Assert.Contains(
            "VuVifPacketBuilder.AddOpaqueBatch(\n                batch,\n                world,\n                view,\n                projection,\n                viewport,\n                nearPlaneDistance,\n                lightDirection,\n                GsGlobal,\n                0,\n                0);",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer stages the assembled VU packet into packet2 memory and dispatches it over VIF1 when
    /// the fast opaque path is active.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_dispatches_assembled_vu_packets_over_vif1() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include <dma.h>", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.GetPacket()", source, StringComparison.Ordinal);
        Assert.Contains("VuVifPacketBuilder.GetPacketByteCount()", source, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuBatchDispatchCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuTriangleVertexCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuPacketByteCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingMaterialCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastVuRejectedMissingPackedModelCount() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint32_t GetLastVuPacketPhase() const;", header, StringComparison.Ordinal);
        Assert.Contains("LastVuTriangleVertexCount += static_cast<std::size_t>(batch.Model->GetTriangleVertexCount());", source, StringComparison.Ordinal);
        Assert.Contains("LastVuPacketByteCount += VuVifPacketBuilder.GetPacketByteCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingMaterialCount = VuOpaqueBatchBuilder.GetLastRejectedMissingMaterialCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingModelCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuRejectedMissingPackedModelCount = VuOpaqueBatchBuilder.GetLastRejectedMissingPackedModelCount();", source, StringComparison.Ordinal);
        Assert.Contains("LastVuPacketPhase = VuVifPacketBuilder.GetLastCompletedPhase();", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(packet, DMA_CHANNEL_VIF1, 1);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("std::memcpy(vifPacket->base", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the checked-in PS2 renderer defaults opaque draws to the VU path.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_defaults_opaque_runtime_path_to_vu() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("UseLegacyCpuOpaquePath(false)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native build includes the new VU opaque renderer source files.
    /// </summary>
    [Fact]
    public void Ps2_makefile_compiles_vu_opaque_renderer_units() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile");

        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 native build includes one assembled VU1 opaque microprogram source for the new renderer path.
    /// </summary>
    [Fact]
    public void Ps2_makefile_builds_vu_opaque_microprogram_object() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile");
        string microProgramPath = @"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\programs\Ps2OpaqueDraw3D.vsm";

        Assert.True(File.Exists(microProgramPath));
        Assert.Contains("EE_DVP := dvp-as", source, StringComparison.Ordinal);
        Assert.Contains("$(BUILD_DIR)/platform/ps2/rendering/vu/programs/Ps2OpaqueDraw3D.o", source, StringComparison.Ordinal);
        Assert.Contains("$(BUILD_DIR)/platform/ps2/rendering/vu/programs/%.o: $(SOURCE_DIR)/platform/ps2/rendering/vu/programs/%.vsm", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host uploads the VU opaque microprogram and configures VU1 double buffering before rendering begins.
    /// </summary>
    [Fact]
    public void Boot_host_uploads_vu_opaque_microprogram_and_initializes_vif1_double_buffering() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <dma.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2.h>", source, StringComparison.Ordinal);
        Assert.Contains("#include <packet2_utils.h>", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueDraw3D_CodeStart", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueDraw3D_CodeEnd", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueTexturedDraw3D_CodeStart", source, StringComparison.Ordinal);
        Assert.Contains("extern u32 Ps2OpaqueTexturedDraw3D_CodeEnd", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_initialize(DMA_CHANNEL_VIF1, NULL, 0);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(packet2, 0, &Ps2OpaqueDraw3D_CodeStart, &Ps2OpaqueDraw3D_CodeEnd);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_vif_add_micro_program(packet2, 64, &Ps2OpaqueTexturedDraw3D_CodeStart, &Ps2OpaqueTexturedDraw3D_CodeEnd);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_vu_add_double_buffer(", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_send_packet2(", source, StringComparison.Ordinal);
        Assert.Contains("dma_channel_wait(DMA_CHANNEL_VIF1, 0);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the active build links and uploads the pretransformed exceptional textured VU program instead of the inactive comparison source.
    /// </summary>
    [Fact]
    public void Ps2_build_inputs_activate_pretransformed_textured_vu_program() {
        string repositoryRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string makefileSource = File.ReadAllText(Path.Combine(repositoryRootPath, "Makefile"));
        string bootHostSource = File.ReadAllText(Path.Combine(repositoryRootPath, "src", "platform", "ps2", "Ps2BootHost.cpp"));

        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D.vsm", makefileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedClipDraw3D.vsm", makefileSource, StringComparison.Ordinal);
        Assert.Contains("Ps2OpaqueTexturedPretransformedDraw3D_CodeStart", bootHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ps2OpaqueTexturedClipDraw3D_CodeStart", bootHostSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host initializes and waits for the CD/DVD subsystem before runtime asset loading begins.
    /// </summary>
    [Fact]
    public void Boot_host_initializes_cdvd_before_runtime_asset_loading() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <libcdvd.h>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeModelDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MODELS\\\\CUBE.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeMaterialEarlyDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MAT\\\\CUBE00\\\\CUBE00.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* CubeMaterialLateDiagnosticPath = \"cdrom0:\\\\COOKED\\\\ENGINE\\\\MAT\\\\CUBE14\\\\CUBE14.HAS;1\";", source, StringComparison.Ordinal);
        Assert.Contains("void BootLogDiscProbe(const char* label, const char* path)", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"cdvd init begin\");", source, StringComparison.Ordinal);
        Assert.Contains("sceCdInit(SCECdINoD);", source, StringComparison.Ordinal);
        Assert.Contains("sceCdDiskReady(0);", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"cdvd ready\");", source, StringComparison.Ordinal);
        Assert.Contains("std::FILE* directFile = std::fopen(path, \"rb\");", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(std::string(label) + \": fopen=\" + (directFile != nullptr ? \"true\" : \"false\"));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube model\", CubeModelDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material early\", CubeMaterialEarlyDiagnosticPath);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material late\", CubeMaterialLateDiagnosticPath);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host applies the engine's PS2 framebuffer defaults before gsKit initializes the screen.
    /// </summary>
    [Fact]
    public void Boot_host_when_graphics_initialize_applies_ps2_framebuffer_defaults_before_screen_init() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr int Ps2DefaultFramebufferWidth = 640;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr int Ps2DefaultFramebufferHeight = 448;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Interlace = GS_INTERLACED;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Field = GS_FIELD;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->DoubleBuffering = GS_SETTING_ON;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Aspect = GS_ASPECT_4_3;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Width = Ps2DefaultFramebufferWidth;", source, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->Height = Ps2DefaultFramebufferHeight;", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_init_screen(GsGlobal);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host publishes the configured GS framebuffer size to the shared render manager.
    /// </summary>
    [Fact]
    public void Boot_host_when_graphics_initialize_publishes_gs_backbuffer_size_to_render_manager() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("EngineRenderManager3D->AddWindow(", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int32_t>(GsGlobal->Width)", source, StringComparison.Ordinal);
        Assert.Contains("static_cast<int32_t>(GsGlobal->Height)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube display-path diagnostic can bypass 3D submission and draw a plain 2D sprite test rectangle.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_sprite_display_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeSpriteDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticLeft = 211.843231f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticTop = 115.843239f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticRight = 428.156738f;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeSpriteDiagnosticBottom = 332.156738f;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeSpriteDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_sprite(", source, StringComparison.Ordinal);
        Assert.Contains("cube sprite diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube display-path diagnostic can draw the measured cube face as two plain 2D triangles.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_two_triangle_2d_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeTriangle2dDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeTriangle2dDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud(", source, StringComparison.Ordinal);
        Assert.Contains("CubeTriangle2dVertexA0X", source, StringComparison.Ordinal);
        Assert.Contains("CubeTriangle2dVertexB2Y", source, StringComparison.Ordinal);
        Assert.Contains("cube triangle 2d diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the current cube diagnostic can submit the measured cube face through the 3D triangle API with fixed screen-space coordinates and depth.
    /// </summary>
    [Fact]
    public void Boot_host_supports_cube_two_triangle_3d_diagnostic_frame() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");
        int callPosition = source.IndexOf("gsKit_prim_triangle_gouraud_3d(", StringComparison.Ordinal);
        int firstVertexPosition = source.IndexOf("CubeTriangle2dVertexA0X, CubeTriangle2dVertexA0Y, CubeTriangle3dDiagnosticDepth,", callPosition, StringComparison.Ordinal);
        int lastVertexPosition = source.IndexOf("CubeTriangle2dVertexA2X, CubeTriangle2dVertexA2Y, CubeTriangle3dDiagnosticDepth,", callPosition, StringComparison.Ordinal);
        int firstColorPosition = source.IndexOf("darkerRed);", lastVertexPosition, StringComparison.Ordinal);

        Assert.Contains("constexpr bool EnableCubeTriangle3dDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr float CubeTriangle3dDiagnosticDepth = 1.0f;", source, StringComparison.Ordinal);
        Assert.Contains("void DrawCubeTriangle3dDiagnosticsFrame(GSGLOBAL* gsGlobal)", source, StringComparison.Ordinal);
        Assert.True(callPosition >= 0);
        Assert.True(firstVertexPosition >= 0);
        Assert.True(lastVertexPosition >= 0);
        Assert.True(firstColorPosition > lastVertexPosition);
        Assert.Contains("cube triangle 3d diagnostic halt", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 3D renderer resolves authored camera viewports into pixel bounds before projection and rasterization.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_resolves_camera_viewport_to_pixels_before_rendering() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("ResolvePixelViewport(camera, windowSize)", source, StringComparison.Ordinal);
        Assert.Contains("const int2 windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer submits untextured 3D triangles using gsKit's required vertex-first, color-last argument order.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_submits_untextured_triangles_with_vertex_first_color_last_argument_order() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        int screenVertexPosition = source.IndexOf("screenAX, screenAY, screenAZ,", StringComparison.Ordinal);
        int screenColorPosition = source.IndexOf("clippedColorA, clippedColorB, clippedColorC);", StringComparison.Ordinal);
        int glowVertexPosition = source.IndexOf("glowAX, glowAY, glowAZ,", StringComparison.Ordinal);
        int glowColorPosition = source.IndexOf("glowColorA, glowColorB, glowColorC);", StringComparison.Ordinal);

        Assert.True(screenVertexPosition >= 0);
        Assert.True(screenColorPosition > screenVertexPosition);
        Assert.True(glowVertexPosition >= 0);
        Assert.True(glowColorPosition > glowVertexPosition);
        Assert.DoesNotContain("screenAX, screenAY, screenAZ, clippedColorA,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("glowAX, glowAY, glowAZ, glowColorA,", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures temporary cube runtime diagnostics can be disabled so the cube scene runs normally on the fixed renderer path.
    /// </summary>
    [Fact]
    public void Boot_host_allows_cube_runtime_diagnostics_to_be_disabled_for_normal_scene_execution() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("constexpr bool EnableCubeRuntimeDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableCubeRuntimeDiagnostics && !CubeDiagnosticsShown)", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime counts: proxies=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime rejects: missingMaterial=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube runtime checkpoint: after draw phase=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube draw returned: viewport=\"", source, StringComparison.Ordinal);
        Assert.Contains("\"cube draw returned: triB0=\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host can emit averaged frame-phase timing diagnostics from the real runtime loop when performance investigation is needed.
    /// </summary>
    [Fact]
    public void Boot_host_supports_frame_timing_diagnostics_for_update_draw_and_present() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\Ps2BootHost.cpp");

        Assert.Contains("#include <ctime>", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableFrameTimingDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableFrameTimingDiagnosticHalt = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr int FrameTimingSampleFrameCount = 60;", source, StringComparison.Ordinal);
        Assert.Contains("double ResolveSecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks)", source, StringComparison.Ordinal);
        Assert.Contains("void RecordFrameTimingSample(", source, StringComparison.Ordinal);
        Assert.Contains("double updateSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double draw3dSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double gifWaitSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double draw2dSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double drawSeconds,", source, StringComparison.Ordinal);
        Assert.Contains("double presentSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("\"frame timing avg updateMs=\"", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingSampleCompleted = true;", source, StringComparison.Ordinal);
        Assert.Contains("const std::clock_t frameUpdateStartTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameUpdateEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameDraw3dEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameGifWaitEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("frameDrawEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("framePresentEndTicks = std::clock();", source, StringComparison.Ordinal);
        Assert.Contains("RecordFrameTimingSample(", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableFrameTimingDiagnostics &&", source, StringComparison.Ordinal);
        Assert.Contains("EnableFrameTimingDiagnosticHalt &&", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingSampleCompleted &&", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 screen-space front-face test preserves the engine's counter-clockwise mesh winding after viewport projection flips Y downward.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_treats_negative_screen_space_signed_area_as_front_facing() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("return signedArea < 0.0f;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return signedArea > 0.0f;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 near-plane clipper treats negative view-space Z as in front of the camera, matching the shared look-at matrix convention.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_clips_against_negative_view_space_near_plane() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("const float nearPlaneZ = -nearPlaneDistance;", source, StringComparison.Ordinal);
        Assert.Contains("bool previousInside = previous.ViewPosition.Z <= nearPlaneZ;", source, StringComparison.Ordinal);
        Assert.Contains("bool currentInside = current.ViewPosition.Z <= nearPlaneZ;", source, StringComparison.Ordinal);
        Assert.Contains("const float amount = (nearPlaneZ - previous.ViewPosition.Z) / denominator;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("previous.ViewPosition.Z >= nearPlaneDistance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("current.ViewPosition.Z >= nearPlaneDistance", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer applies the drawable parent's authored scale and orientation before camera-space projection.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_applies_parent_scale_and_orientation_to_model_vertices() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float4 parentOrientation = parent->get_Orientation();", source, StringComparison.Ordinal);
        Assert.Contains("::float3 parentScale = parent->get_Scale();", source, StringComparison.Ordinal);
        Assert.Contains("::float3 localPositionA = ::float3(", source, StringComparison.Ordinal);
        Assert.Contains("positionA = ::float4::RotateVector(localPositionA, parentOrientation) + parentPosition;", source, StringComparison.Ordinal);
        Assert.Contains("normalA = indexA < normals.size() ? ::float4::RotateVector(normals[indexA], parentOrientation) : ::float3::get_Zero();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float3 positionA = positions[indexA] + parentPosition;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 runtime source retains the renderer-side triangle diagnostics used during malformed 3D scene debugging.
    /// </summary>
    [Fact]
    public void Ps2_runtime_renderer_exposes_triangle_stage_diagnostics_for_3d_submission() {
        string rendererHeaderSource = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("std::size_t GetLastClipRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastProjectionRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastCullRejectCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("std::size_t GetLastSubmittedTriangleCount() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedScreenBounds() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleBoundsA() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleBoundsB() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA0() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA1() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexA2() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB0() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB1() const;", rendererHeaderSource, StringComparison.Ordinal);
        Assert.Contains("::float4 GetLastSubmittedTriangleVertexB2() const;", rendererHeaderSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer can force one flat-color diagnostic mode that bypasses textures, material alpha state, lighting, and HDR glow.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableFlatColorDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableLightingOnlyDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDiagnosticProxyColor(proxy)", source, StringComparison.Ordinal);
        Assert.Contains("const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("const bool useLightingOnlyDiagnostics = EnableLightingOnlyDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor) {", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMaterialAlphaState(*material);", source, StringComparison.Ordinal);
        Assert.Contains("GSTEXTURE* texture = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && !material->GetTextureRelativePath().empty()) {", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA, lightDirection);", source, StringComparison.Ordinal);
        Assert.Contains("const bool useTexture = !useDiagnosticFlatColor", source, StringComparison.Ordinal);
        Assert.Contains("&& !useLightingOnlyDiagnostics", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !useLightingOnlyDiagnostics && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_goraud_texture_3d(", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud_3d(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer converts normalized mesh UVs into texel-space coordinates before submitting textured triangles to gsKit.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_scales_normalized_uvs_into_gskit_texel_space() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("::float2 ResolveGsTextureCoordinate(const ::float2& normalizedTexCoord, const GSTEXTURE* texture)", source, StringComparison.Ordinal);
        Assert.Contains("normalizedTexCoord.X * static_cast<float>(texture->Width)", source, StringComparison.Ordinal);
        Assert.Contains("normalizedTexCoord.Y * static_cast<float>(texture->Height)", source, StringComparison.Ordinal);
        Assert.Contains("const ::float2 screenTexCoordA = ResolveGsTextureCoordinate(clippedA.TexCoord, texture);", source, StringComparison.Ordinal);
        Assert.Contains("const ::float2 glowTexCoordA = ResolveGsTextureCoordinate(triangle.TexCoordA, triangle.Texture);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("screenAX, screenAY, screenAZ, clippedA.TexCoord.X, clippedA.TexCoord.Y,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("glowAX, glowAY, glowAZ, triangle.TexCoordA.X, triangle.TexCoordA.Y,", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer resolves lit vertex colors from the authored directional light before falling back to the diagnostic light vector.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_uses_scene_directional_light_for_vertex_lighting() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

        Assert.Contains("#include \"DirectionalLightComponent.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveDirectionalLightDirection(lightDirection);", source, StringComparison.Ordinal);
        Assert.Contains("dynamic_cast<::DirectionalLightComponent*>(component)", source, StringComparison.Ordinal);
        Assert.Contains("std::uint64_t Ps2RenderManager3D::ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection)", source, StringComparison.Ordinal);
        Assert.Contains("bool TryResolveDirectionalLightDirection(::float3& lightDirection) const;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 lit shading path modulates directional-light intensity by the cooked authored base-color channels.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_modulates_lighting_by_cooked_base_color() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");
        string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeMaterial.hpp");

        Assert.Contains("material.GetBaseColorR()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorG()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorB()", source, StringComparison.Ordinal);
        Assert.Contains("material.GetBaseColorA()", source, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorR() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorG() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorB() const;", header, StringComparison.Ordinal);
        Assert.Contains("std::uint8_t GetBaseColorA() const;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 renderer does not leave the single-proxy diagnostic clamp enabled for normal exports.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_disables_single_proxy_diagnostic_submission_mode_for_normal_exports() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableSingleProxyDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr std::size_t SingleProxyDiagnosticIndex = 1;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveRenderableProxyByIndex(const helengine::ps2::Ps2FramePlan& plan, std::size_t proxyIndex)", source, StringComparison.Ordinal);
        Assert.Contains("const Ps2RenderProxy* firstProxy = ResolveRenderableProxyByIndex(plan, SingleProxyDiagnosticIndex);", source, StringComparison.Ordinal);
        Assert.Contains("if (EnableSingleProxyDiagnostics) {", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(*firstProxy, view, projection, viewport, camera->get_NearPlaneDistance());", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());", source, StringComparison.Ordinal);
    }
}
