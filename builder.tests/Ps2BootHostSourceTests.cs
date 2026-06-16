using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the checked-in PS2 boot host source preserves the intended startup-scene boot flow.
/// </summary>
public sealed class Ps2BootHostSourceTests {
    /// <summary>
    /// Ensures the boot host continues into startup-scene loading after compact disc probes instead of stopping at a temporary diagnostic halt.
    /// </summary>
    [Fact]
    public void BootHost_AfterCompactDiscProbes_ContinuesToStartupSceneLoading() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int runMethodIndex = source.IndexOf("int Ps2BootHost::Run()", StringComparison.Ordinal);
        int initializeGraphicsIndex = source.IndexOf("if (!InitializeGraphics())", runMethodIndex, StringComparison.Ordinal);
        int startupSceneLoadIndex = source.IndexOf("BootLog(std::string(\"startup scene load begin \") + Ps2BootVersionStamp);", runMethodIndex, StringComparison.Ordinal);

        Assert.Contains("BootLog(std::string(\"startup scene load begin \") + Ps2BootVersionStamp);", source, StringComparison.Ordinal);
        Assert.Contains("StartupSceneLoaded = LoadPackagedStartupScene();", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(StartupSceneLoaded ? \"startup scene load succeeded\" : \"startup scene load failed\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup scene probe halt\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(\"startup diagnostic halt after non-null\");", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"startup scene invoking scene manager load\");", source, StringComparison.Ordinal);
        Assert.True(runMethodIndex >= 0, "Expected PS2 boot host run entry point.");
        Assert.True(initializeGraphicsIndex > runMethodIndex, "Expected PS2 boot host to initialize graphics inside Run().");
        Assert.True(startupSceneLoadIndex > initializeGraphicsIndex, "Expected startup scene loading to begin after graphics initialization is invoked.");
    }

    /// <summary>
    /// Ensures the PS2 boot host records startup-scene load timing while leaving the normal boot-frame presentation path active.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenStartupSceneTimingDiagnosticIsEnabled_LogsLoadDurationAndLeavesPresentBootFrameActive() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int runMethodIndex = source.IndexOf("int Ps2BootHost::Run()", StringComparison.Ordinal);
        int presentBootFrameIndex = source.IndexOf("PresentBootFrame();", runMethodIndex, StringComparison.Ordinal);
        int loadPackagedStartupSceneIndex = source.IndexOf("bool Ps2BootHost::LoadPackagedStartupScene()", StringComparison.Ordinal);
        int loadDurationIndex = source.IndexOf("BootLog(", loadPackagedStartupSceneIndex, StringComparison.Ordinal);

        Assert.Contains("constexpr bool EnableStartupSceneLoadTimingDiagnostic = true;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr bool EnableStartupScenePreRenderHalt = false;", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* BootLogHostFilePath = \"host:ps2_bootlog.txt\";", source, StringComparison.Ordinal);
        Assert.Contains("constexpr const char* BootLogHostFallbackFilePath = \"host0:ps2_bootlog.txt\";", source, StringComparison.Ordinal);
        Assert.Contains("std::vector<std::string> BootLogHistory;", source, StringComparison.Ordinal);
        Assert.Contains("void PresentBootLogHistoryToDebugConsole()", source, StringComparison.Ordinal);
        Assert.Contains("BootLogHistory.push_back(message != nullptr ? message : \"\");", source, StringComparison.Ordinal);
        Assert.Contains("PresentBootLogHistoryToDebugConsole();", source, StringComparison.Ordinal);
        Assert.Contains("EngineOptions->set_RuntimeDiagnosticsProvider(EngineRuntimeDiagnosticsProvider);", source, StringComparison.Ordinal);
        Assert.Contains("class Ps2BootRuntimeDiagnosticsProvider final", source, StringComparison.Ordinal);
        Assert.Contains("#include <cerrno>", source, StringComparison.Ordinal);
        Assert.Contains("void AppendBootLogToHostFile(const char* message)", source, StringComparison.Ordinal);
        Assert.Contains("void PrintHostLogStatusLine(const std::string& message)", source, StringComparison.Ordinal);
        Assert.Contains("BootLogHistory.push_back(message);", source, StringComparison.Ordinal);
        Assert.Contains("const char* hostLogCandidates[] = { BootLogHostFilePath, BootLogHostFallbackFilePath };", source, StringComparison.Ordinal);
        Assert.Contains("int hostLogFileDescriptor = open(candidatePath, O_CREAT | O_WRONLY | O_APPEND, 0666);", source, StringComparison.Ordinal);
        Assert.Contains("const int bytesWritten = static_cast<int>(write(hostLogFileDescriptor, hostLogLine.c_str(), hostLogLine.size()));", source, StringComparison.Ordinal);
        Assert.Contains("close(hostLogFileDescriptor);", source, StringComparison.Ordinal);
        Assert.Contains("host log open failed errno=", source, StringComparison.Ordinal);
        Assert.Contains("host log write failed bytes=", source, StringComparison.Ordinal);
        Assert.Contains("host log write ok path=", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(", source, StringComparison.Ordinal);
        Assert.Contains("std::string(\"load \")", source, StringComparison.Ordinal);
        Assert.Contains("std::string(\"sum \")", source, StringComparison.Ordinal);
        Assert.Contains("BootLogTimingSummary(diagnosticsProvider, \"txb\", \"RuntimeSceneLoadService.AssetResolveTextureBuild\");", source, StringComparison.Ordinal);
        Assert.Contains("BootLogTimingValue(\"txo\", CookedTextureOpenMilliseconds);", source, StringComparison.Ordinal);
        Assert.Contains("BootLogTimingValue(\"txp\", CookedTexturePopulateMilliseconds);", source, StringComparison.Ordinal);
        Assert.Contains("FormatMillisecondsFromClockTicks(startupSceneLoadStartTicks, startupSceneLoadEndTicks)", source, StringComparison.Ordinal);
        Assert.Contains("gsKit_sync_flip(GsGlobal);", source, StringComparison.Ordinal);
        Assert.True(runMethodIndex >= 0, "Expected PS2 boot host run entry point.");
        Assert.True(presentBootFrameIndex > runMethodIndex, "Expected PresentBootFrame to remain active in the normal boot path.");
        Assert.True(loadPackagedStartupSceneIndex >= 0, "Expected packaged startup-scene load helper.");
        Assert.True(loadDurationIndex > loadPackagedStartupSceneIndex, "Expected startup-scene duration logging inside the load helper.");
    }

    /// <summary>
    /// Ensures normal runtime initialization no longer emits unconditional compact-disc probe spam and startup-scene runtime exceptions route through the shared boot-log helper.
    /// </summary>
    [Fact]
    public void BootHost_RuntimeExceptionLogging_UsesSharedHelperAndSkipsDefaultDiscProbeSpam() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("void BootLogRuntimeException(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube model\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material early\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe cube material late\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe scene local material cooked\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLogDiscProbe(\"disc probe scene local material root\"", source, StringComparison.Ordinal);
        Assert.Contains("BootLogRuntimeException(\"startup scene\"", source, StringComparison.Ordinal);
        Assert.Contains("+ \" textureStage=\"", source, StringComparison.Ordinal);
        Assert.Contains("+ \" texturePath=\"", source, StringComparison.Ordinal);
        Assert.Contains("BootLogRuntimeException(\"frame draw3d\", exception, EngineCore, true);", source, StringComparison.Ordinal);
        Assert.Contains("BootLogUnknownRuntimeException(\"frame draw3d\", EngineCore, true);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the temporary cube runtime diagnostics only trigger for the dedicated cube-test scene instead of any non-menu scene.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenCubeRuntimeDiagnosticsAreEnabled_TargetsOnlyCubeTestScene() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr const char* CubeRuntimeDiagnosticSceneId = \"cube_test\";", source, StringComparison.Ordinal);
        Assert.Contains("return loadedSceneIds == CubeRuntimeDiagnosticSceneId;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("loadedSceneIds.find(\"DemoDiscMainMenu\")", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host resolves packaged font atlases through the PS2-native texture runtime path when one font omits embedded raw atlas bytes.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingPackagedFontAtlases_UsesPs2TextureAssetRuntimePath() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("font->get_CookedAtlasTextureRelativePath()", source, StringComparison.Ordinal);
        Assert.Contains("#include \"BinaryReaderLE.hpp\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#include \"Ps2AssetSerializer.hpp\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("#include \"AssetSerializer.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("DeserializePs2TextureAsset(stream)", source, StringComparison.Ordinal);
        Assert.Contains("DeserializePs2TextureAsset(bufferedStream)", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2TextureAsset* textureAsset = DeserializePs2TextureAsset(stream);", source, StringComparison.Ordinal);
        Assert.Contains("::Ps2TextureAsset* textureAsset = DeserializePs2TextureAsset(bufferedStream);", source, StringComparison.Ordinal);
        Assert.Contains("FontTextureRecords", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host consumes cooked PS2 texture payload PSM and CLUT metadata instead of hardcoding direct-color uploads for every payload.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingPs2Textures_UsesPayloadDefinedPsmAndClutState() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int populateMethodIndex = source.IndexOf("bool PopulateTextureRecordFromPs2TextureAsset(::Ps2TextureAsset* data, Ps2TextureRecord& record)", StringComparison.Ordinal);
        Assert.True(populateMethodIndex >= 0, "Expected cooked PS2 texture population helper.");
        string populateMethod = source.Substring(populateMethodIndex, Math.Min(2200, source.Length - populateMethodIndex));

        Assert.Contains("ResolveGsPixelStorageMode(data->PixelStorageMode)", populateMethod, StringComparison.Ordinal);
        Assert.Contains("if (data->PaletteData != nullptr && data->PaletteData->Length > 0)", populateMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("if (data->Format != ::Ps2TextureFormat::Rgba32)", populateMethod, StringComparison.Ordinal);

        int uploadMethodIndex = source.IndexOf("bool EnsureTextureUploaded(Ps2TextureRecord& record)", StringComparison.Ordinal);
        Assert.True(uploadMethodIndex >= 0, "Expected cooked PS2 texture upload helper.");
        string uploadMethod = source.Substring(uploadMethodIndex, Math.Min(1400, source.Length - uploadMethodIndex));
        Assert.Contains("record.Texture.VramClut = gsKit_vram_alloc(", uploadMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host transfers CLUT pixel-storage metadata into the gsKit texture record for indexed runtime textures.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingIndexedPs2Textures_InitializesClutPixelStorageState() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int populateMethodIndex = source.IndexOf("bool PopulateTextureRecordFromPs2TextureAsset(::Ps2TextureAsset* data, Ps2TextureRecord& record)", StringComparison.Ordinal);
        Assert.True(populateMethodIndex >= 0, "Expected cooked PS2 texture population helper.");
        string populateMethod = source.Substring(populateMethodIndex, Math.Min(2200, source.Length - populateMethodIndex));

        Assert.Contains("record.Texture.ClutPSM = ResolveGsPixelStorageMode(data->ClutPixelStorageMode);", populateMethod, StringComparison.Ordinal);
        Assert.Contains("record.Texture.ClutStorageMode = GS_CLUT_STORAGE_CSM1;", populateMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host consumes the shared 2D render command list instead of bypassing source rect, font scale, and clip semantics through direct drawable callbacks.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDrawing2D_UsesSharedRenderCommandListPlaybackInsteadOfDirectDrawableCallbacks() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"RenderCommandListBuilder2D.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("RenderCommandListBuilder2D", source, StringComparison.Ordinal);
        Assert.Contains("RenderCommand2DType::TexturedQuad", source, StringComparison.Ordinal);
        Assert.Contains("RenderCommand2DType::GlyphQuad", source, StringComparison.Ordinal);
        Assert.Contains("RenderCommand2DType::ClipPush", source, StringComparison.Ordinal);
        Assert.Contains("EngineRenderManager2D->Draw();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("drawable->Draw();", source, StringComparison.Ordinal);
        Assert.Contains("u64 ResolveTexturedSpriteRgba(const ::byte4& color)", source, StringComparison.Ordinal);
        Assert.Contains("const u64 rgba = ResolveTexturedSpriteRgba(color);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host can emit first-frame 2D execution diagnostics that identify the exact command and submit boundary when native GS playback hangs.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDrawing2D_CanLogPerCommandExecutionBoundariesForNativeHangDiagnosis() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableDraw2dExecutionDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("int32_t ActiveDraw2dDiagnosticCommandIndex = -1;", source, StringComparison.Ordinal);
        Assert.Contains("draw2d exec begin index=", source, StringComparison.Ordinal);
        Assert.Contains("draw2d exec end index=", source, StringComparison.Ordinal);
        Assert.Contains("ActiveDraw2dDiagnosticCommandIndex = commandIndex;", source, StringComparison.Ordinal);
        Assert.Contains("ActiveDraw2dDiagnosticCommandIndex = -1;", source, StringComparison.Ordinal);
        Assert.Contains("draw2d textured begin index=", source, StringComparison.Ordinal);
        Assert.Contains("draw2d textured end index=", source, StringComparison.Ordinal);
        Assert.Contains("draw2d solid begin index=", source, StringComparison.Ordinal);
        Assert.Contains("draw2d solid end index=", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host disables depth testing around the 2D overlay pass so HUD text and sprites cannot be occluded by previously submitted 3D geometry.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDrawing2D_DisablesDepthTestingForOverlayPass() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int draw2dCallIndex = source.IndexOf("EngineRenderManager2D->Draw();", StringComparison.Ordinal);
        Assert.True(draw2dCallIndex >= 0, "Expected PS2 boot host 2D draw call.");

        int sliceStart = Math.Max(0, draw2dCallIndex - 700);
        int sliceLength = Math.Min(1800, source.Length - sliceStart);
        string draw2dSlice = source.Substring(sliceStart, sliceLength);

        Assert.Contains("const int32_t previousZBuffering = GsGlobal->ZBuffering;", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->ZBuffering = GS_SETTING_OFF;", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_test(GsGlobal, GS_ZTEST_OFF);", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("EngineRenderManager2D->Draw();", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("GsGlobal->ZBuffering = previousZBuffering;", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("if (GsGlobal->ZBuffering == GS_SETTING_ON) {", draw2dSlice, StringComparison.Ordinal);
        Assert.Contains("gsKit_set_test(GsGlobal, GS_ZTEST_ON);", draw2dSlice, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host leaves the normal FPS overlay active by default instead of hijacking those text lines with temporary cube-test diagnostics.
    /// </summary>
    [Fact]
    public void Ps2BootHost_DefaultBuild_DoesNotEnableVisibleCubeRuntimeOverlayMetrics() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableCubeRuntimeDiagnostics = false;", source, StringComparison.Ordinal);
        Assert.Contains("void PublishCubeRuntimeOverlayMetrics(const helengine::ps2::Ps2RenderManager3D& renderManager3DBackend)", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingOverlayLine1 =", source, StringComparison.Ordinal);
        Assert.Contains("FrameTimingOverlayLine2 =", source, StringComparison.Ordinal);
        Assert.Contains("\"P:\"", source, StringComparison.Ordinal);
        Assert.Contains("\" B:\"", source, StringComparison.Ordinal);
        Assert.Contains("\" T:\"", source, StringComparison.Ordinal);
        Assert.Contains("\"SB:\"", source, StringComparison.Ordinal);
        Assert.Contains("\" Ph:\"", source, StringComparison.Ordinal);
        Assert.Contains("PublishCubeRuntimeOverlayMetrics(RenderManager3DBackend);", source, StringComparison.Ordinal);
    }

    /// Ensures the PS2 boot host can present one visible cube-test phase color before the first VU draw, after draw return, and after GIF wait so runtime freezes can be localized without host-file logging.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenCubeRuntimeDiagnosticsAreEnabled_DoesNotEnableVisiblePhaseProbeFramesByDefault() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("constexpr bool EnableCubeRuntimePhaseFrameProbe = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" F:", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" St:", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures PS2 startup scene boot uses the shared scene-manager path so runtime asset tracking and font caching stay aligned with normal scene loads.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenLoadingStartupScene_UsesSceneManagerInsteadOfDirectSceneLoadService() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("ResolveStartupSceneIdFromManifest", source, StringComparison.Ordinal);
        Assert.Contains("EngineCore->get_SceneManager()->LoadScene(startupSceneId, ::SceneLoadMode::Single);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EngineCore->get_SceneLoadService()->Load(startupScene);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures PS2 debug boot diagnostics consume the shared engine-owned scene-load timing contract instead of defining PS2-specific timing boundaries.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenDebugSceneLoadTimingDiagnosticsAreEnabled_ImplementsSharedTimingSink() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int diagnosticsProviderCastIndex = source.IndexOf("he_cpp_try_cast<IRuntimeSceneLoadTimingDiagnosticsProvider>(EngineRuntimeDiagnosticsProvider)", StringComparison.Ordinal);
        int diagnosticsProviderIncludeIndex = source.IndexOf("#include \"IRuntimeSceneLoadTimingDiagnosticsProvider.hpp\"", StringComparison.Ordinal);
        int diagnosticsProviderIncludeGuardStartIndex = source.LastIndexOf("#if __has_include(\"IRuntimeSceneLoadTimingDiagnosticsProvider.hpp\")", diagnosticsProviderIncludeIndex, StringComparison.Ordinal);
        int diagnosticsProviderIncludeGuardEndIndex = source.IndexOf("#endif", diagnosticsProviderIncludeIndex, StringComparison.Ordinal);

        Assert.Contains("IRuntimeSceneLoadTimingDiagnosticsProvider", source, StringComparison.Ordinal);
        Assert.Contains("ReportSceneLoadPhaseTiming", source, StringComparison.Ordinal);
        Assert.Contains("#define HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE 1", source, StringComparison.Ordinal);
        Assert.Contains("#define HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE 0", source, StringComparison.Ordinal);
        Assert.Contains("BootLog(\"scene load timing diagnostics provider unavailable\");", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneManager.LoadSceneRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeSceneLoadService.RootEntityLoadLoop", source, StringComparison.Ordinal);
        Assert.True(diagnosticsProviderCastIndex >= 0, "Expected PS2 boot host to probe the debug-only scene-load timing diagnostics provider.");
        Assert.True(diagnosticsProviderIncludeIndex >= 0, "Expected PS2 boot host to include the scene-load timing diagnostics interface.");
        Assert.True(diagnosticsProviderIncludeGuardStartIndex >= 0 && diagnosticsProviderIncludeGuardStartIndex < diagnosticsProviderIncludeIndex, "Expected PS2 boot host to guard the scene-load timing diagnostics include behind a header-availability check.");
        Assert.True(diagnosticsProviderIncludeGuardEndIndex > diagnosticsProviderIncludeIndex, "Expected PS2 boot host to close the HE_CPP_REQ_DEBUG guard after the scene-load timing diagnostics include.");

        int diagnosticsProviderBlockStart = Math.Max(0, diagnosticsProviderCastIndex - 200);
        string diagnosticsProviderBlock = source.Substring(diagnosticsProviderBlockStart, Math.Min(520, source.Length - diagnosticsProviderBlockStart));
        Assert.Contains("#if HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE", diagnosticsProviderBlock, StringComparison.Ordinal);
        Assert.Contains("#else", diagnosticsProviderBlock, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host stays aligned with the root generated-core startup contract that still requires PlatformInfo and SceneCatalog.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenInitializingRootGeneratedCore_UsesPlatformInfoAndSceneCatalogContract() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        string headerPath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.hpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");
        Assert.True(File.Exists(headerPath), $"Expected boot host header at '{headerPath}'.");

        string source = File.ReadAllText(sourcePath);
        string header = File.ReadAllText(headerPath);

        Assert.Contains("#include \"PlatformInfo.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("EnginePlatformInfo = new PlatformInfo(\"ps2\", \"1.0.0\");", source, StringComparison.Ordinal);
        Assert.Contains("EngineOptions->set_SceneCatalog(BuildRuntimeSceneCatalogFromManifest());", source, StringComparison.Ordinal);
        Assert.Contains("EnginePlatformInfo,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("set_RuntimeSceneCatalog", source, StringComparison.Ordinal);
        Assert.Contains("class PlatformInfo;", header, StringComparison.Ordinal);
        Assert.Contains("::PlatformInfo* EnginePlatformInfo;", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host feeds the generated standard-platform-input manifest into core initialization so Accept and Return bindings work on console builds.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenInitializingRootGeneratedCore_UsesStandardPlatformInputManifestContract() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"runtime/runtime_standard_platform_input_manifest.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("::StandardPlatformInputConfiguration* BuildStandardPlatformInputConfigurationFromManifest()", source, StringComparison.Ordinal);
        Assert.Contains("const HERuntimeStandardPlatformActionEntry* manifestEntries = he_runtime_standard_platform_action_entries(&count);", source, StringComparison.Ordinal);
        Assert.Contains("List<::StandardPlatformActionBinding*>* bindings = new List<::StandardPlatformActionBinding*>", source, StringComparison.Ordinal);
        Assert.Contains("bindings->Add(new ::StandardPlatformActionBinding(", source, StringComparison.Ordinal);
        Assert.Contains("EngineOptions->set_StandardPlatformInputConfiguration(BuildStandardPlatformInputConfigurationFromManifest());", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host writes update-stage transitions to the host log only so runtime crash breadcrumbs remain available without reintroducing screen spam.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenReportingUpdateStages_WritesHostLogOnlyForDistinctStageTransitions() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);
        int reportUpdateStageIndex = source.IndexOf("void ReportUpdateStage(std::string stage) override", StringComparison.Ordinal);
        Assert.True(reportUpdateStageIndex >= 0, "Expected PS2 boot host update-stage diagnostics hook.");
        string reportUpdateStageMethod = source.Substring(reportUpdateStageIndex, Math.Min(420, source.Length - reportUpdateStageIndex));

        Assert.Contains("String::Equals(LastUpdateStage, stage, StringComparison::Ordinal)", reportUpdateStageMethod, StringComparison.Ordinal);
        Assert.Contains("LastUpdateStage = stage;", reportUpdateStageMethod, StringComparison.Ordinal);
        Assert.Contains("HostLogOnly(std::string(\"update stage=\") + stage);", reportUpdateStageMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("BootLog(std::string(\"update stage=\") + stage);", reportUpdateStageMethod, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the PS2 boot host includes the full generated-core type definitions required by the current native boot path.
    /// </summary>
    [Fact]
    public void Ps2BootHost_WhenUsingFontSceneObjectAndCameraMembers_IncludesFullGeneratedCoreHeaders() {
        string sourcePath = Path.Combine(GetRepositoryRootPath(), "src", "platform", "ps2", "Ps2BootHost.cpp");
        Assert.True(File.Exists(sourcePath), $"Expected boot host source at '{sourcePath}'.");

        string source = File.ReadAllText(sourcePath);

        Assert.Contains("#include \"FontAsset.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"FontChar.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"ContentManager.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"ObjectManager.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"RuntimeSceneLoadService.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"SceneManager.hpp\"", source, StringComparison.Ordinal);
        Assert.Contains("#include \"ICamera.hpp\"", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the PS2 repository root from the executing test binary directory.
    /// </summary>
    /// <returns>Absolute PS2 repository root path.</returns>
    static string GetRepositoryRootPath() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }
}
