using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 native source inputs required by the runtime build pipeline.
/// </summary>
public sealed class Ps2NativeBuildInputsTests {
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
    /// Ensures the PS2 3D renderer resolves authored camera viewports into pixel bounds before projection and rasterization.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_resolves_camera_viewport_to_pixels_before_rendering() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("ResolvePixelViewport(camera, windowSize)", source, StringComparison.Ordinal);
        Assert.Contains("int2* windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4 viewport = camera->get_Viewport();", source, StringComparison.Ordinal);
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
    }

    /// <summary>
    /// Ensures the PS2 renderer can force one flat-color diagnostic mode that bypasses textures, material alpha state, lighting, and HDR glow.
    /// </summary>
    [Fact]
    public void Ps2_renderer3d_supports_flat_color_diagnostic_submission_mode() {
        string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

        Assert.Contains("constexpr bool EnableFlatColorDiagnostics = true;", source, StringComparison.Ordinal);
        Assert.Contains("ResolveDiagnosticProxyColor(proxy)", source, StringComparison.Ordinal);
        Assert.Contains("const bool useDiagnosticFlatColor = EnableFlatColorDiagnostics;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor) {", source, StringComparison.Ordinal);
        Assert.Contains("ApplyMaterialAlphaState(*material);", source, StringComparison.Ordinal);
        Assert.Contains("GSTEXTURE* texture = nullptr;", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !material->GetTextureRelativePath().empty()) {", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t diagnosticColor = ResolveDiagnosticProxyColor(proxy);", source, StringComparison.Ordinal);
        Assert.Contains("const std::uint64_t colorA = useDiagnosticFlatColor ? diagnosticColor : ResolveVertexColor(*material, normalA);", source, StringComparison.Ordinal);
        Assert.Contains("const bool useTexture = !useDiagnosticFlatColor", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && !ShouldDrawAlphaTestTriangle(", source, StringComparison.Ordinal);
        Assert.Contains("if (!useDiagnosticFlatColor && HdrEnabled && ShouldEmitHdrGlow(*material, clippedColorA, clippedColorB, clippedColorC)) {", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_prim_triangle_gouraud_3d(", source, StringComparison.Ordinal);
    }
}
