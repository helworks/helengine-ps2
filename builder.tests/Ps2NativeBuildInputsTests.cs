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
        Assert.Contains("int2* windowSize = get_MainWindowSize();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("::float4 viewport = camera->get_Viewport();", source, StringComparison.Ordinal);
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
        Assert.Contains("+ \" updateables=\"", source, StringComparison.Ordinal);
        Assert.Contains("get_Updateables()", source, StringComparison.Ordinal);
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
        Assert.Contains("constexpr bool EnableLightingOnlyDiagnostics = true;", source, StringComparison.Ordinal);
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
        Assert.Contains("gsKit_prim_triangle_gouraud_3d(", source, StringComparison.Ordinal);
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
        Assert.Contains("DrawOpaqueProxy(*firstProxy, view, projection, viewport, camera->get_NearPlaneDistance());", source, StringComparison.Ordinal);
        Assert.Contains("DrawOpaqueProxy(*firstProxy, view, projection, viewport, nearPlaneDistance);", source, StringComparison.Ordinal);
    }
}
