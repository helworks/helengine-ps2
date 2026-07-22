#include "platform/ps2/Ps2BootHost.hpp"

#include <dma.h>
#include <dmaKit.h>
#include <debug.h>
#include <fcntl.h>
#include <libcdvd.h>
#include <malloc.h>
#include <packet2.h>
#include <packet2_utils.h>
#include <gsKit.h>
#include <cerrno>
#include <algorithm>
#include <ctime>
#include <cstring>
#include <cstdio>
#include <cmath>
#include <exception>
#include <limits>
#include <unordered_map>
#include <unistd.h>
#include <vector>

#include "Asset.hpp"
#include "BinaryReaderLE.hpp"
#include "helcpp_config.hpp"
#include "Core.hpp"
#include "CoreInitializationOptions.hpp"
#include "ContentManager.hpp"
#include "HostFileSystemContentStreamSource.hpp"
#include "CameraComponent.hpp"
#include "Entity.hpp"
#include "FPSComponent.hpp"
#include "FontAsset.hpp"
#include "FontChar.hpp"
#include "FontInfo.hpp"
#include "ICamera.hpp"
#include "IDrawable3D.hpp"
#include "IAudioBackend.hpp"
#include "IRuntimeDiagnosticsProvider.hpp"
#include "IRuntimeEntityDisposalDiagnosticsProvider.hpp"
#if HE_CPP_REQ_DEBUG
#if __has_include("IRuntimeSceneLoadTimingDiagnosticsProvider.hpp")
#define HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE 1
#include "IRuntimeSceneLoadTimingDiagnosticsProvider.hpp"
#else
#define HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE 0
#endif
#else
#define HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE 0
#endif
#include "IRuntimeSceneTransitionDiagnosticsProvider.hpp"
#include "IRuntimeUpdateStageDiagnosticsProvider.hpp"
#include "IRoundedRectDrawable2D.hpp"
#include "ISpriteDrawable2D.hpp"
#include "ITextDrawable2D.hpp"
#include "ModelAsset.hpp"
#include "ObjectManager.hpp"
#include "PlatformInfo.hpp"
#include "RenderCommand2DType.hpp"
#include "RenderCommandList2D.hpp"
#include "RenderCommandListBuilder2D.hpp"
#include "SceneAsset.hpp"
#include "SceneComponentAssetRecord.hpp"
#include "SceneEntityAsset.hpp"
#include "SceneManager.hpp"
#include "SpriteComponent.hpp"
#include "platform/ps2/audio/Ps2AudioBackend.hpp"
#include "platform/ps2/Ps2InputBackend.hpp"
#include "platform/ps2/rendering/Ps2RenderManager3D.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "TextureUtils.hpp"
#include "RenderManager2D.hpp"
#include "RenderManager3D.hpp"
#include "StandardPlatformAction.hpp"
#include "StandardPlatformActionBinding.hpp"
#include "StandardPlatformInputConfiguration.hpp"
#include "InputControlId.hpp"
#include "InputControlKind.hpp"
#include "InputDeviceKind.hpp"
#include "InputSystem.hpp"
#include "DebugComponent.hpp"
#include "PointerInteractionSystem.hpp"
#include "IPhysicsRuntime.hpp"
#include "PhysicsFixedStepScheduler.hpp"
#include "runtime/finally.hpp"
#include "runtime/native_list.hpp"
#include "runtime/native_cast.hpp"
#include "runtime/runtime_ps2_asset_path_manifest.hpp"
#include "runtime/runtime_scene_catalog_manifest.hpp"
#include "runtime/runtime_standard_platform_input_manifest.hpp"
#include "runtime/native_exceptions.hpp"
#include "RuntimeModel.hpp"
#include "RuntimeSceneLoadService.hpp"
#include "RuntimeSceneCatalog.hpp"
#include "RuntimeSceneCatalogEntry.hpp"
#include "SceneLoadMode.hpp"
#include "RuntimeTexture.hpp"
#if __has_include("Physics3DRuntimeComponentRegistration.hpp")
#define HE_PS2_HAS_PHYSICS3D_RUNTIME_REGISTRATION 1
#include "Physics3DRuntimeComponentRegistration.hpp"
#else
#define HE_PS2_HAS_PHYSICS3D_RUNTIME_REGISTRATION 0
#endif
#include "Ps2TextureAsset.hpp"
#include "system/io/file.hpp"
#include "system/io/memory-stream.hpp"
#include "system/io/path.hpp"
#include "TextLayoutUtils.hpp"
#include "TextureAsset.hpp"
#include "RuntimeMemoryDiagnosticsSnapshot.hpp"
#include "runtime/runtime_physics3d_scene_feature_manifest.hpp"

extern "C" {
    extern u32 Ps2OpaqueDraw3D_CodeStart __attribute__((section(".vudata")));
    extern u32 Ps2OpaqueDraw3D_CodeEnd __attribute__((section(".vudata")));
    extern u32 Ps2OpaqueTexturedDraw3D_CodeStart __attribute__((section(".vudata")));
    extern u32 Ps2OpaqueTexturedDraw3D_CodeEnd __attribute__((section(".vudata")));
}

namespace {
    enum class UpdatePhaseDiagnosticMode {
        Full,
        SkipAll,
        InputEarlyOnly,
        InputLateOnly,
        InputOnly,
        BackendCaptureOnly,
        AllManual,
        FrameStatsOnly,
        ObjectManagerOnly,
        PhysicsOnly,
        PointerOnly
    };

    constexpr const char* Ps2BootVersionStamp = "PS2CITYBOOT-5";
    constexpr int Ps2DefaultFramebufferWidth = 640;
    constexpr int Ps2DefaultFramebufferHeight = 448;
    constexpr const char* CubeModelDiagnosticPath = "cdrom0:\\COOKED\\ENGINE\\MODELS\\CUBE.HAS;1";
    constexpr const char* CubeMaterialEarlyDiagnosticPath = "cdrom0:\\COOKED\\ENGINE\\MAT\\CUBE00\\CUBE00.HAS;1";
    constexpr const char* CubeMaterialLateDiagnosticPath = "cdrom0:\\COOKED\\ENGINE\\MAT\\CUBE14\\CUBE14.HAS;1";
    constexpr const char* SceneLocalMaterialCookedDiagnosticPath = "cdrom0:\\COOKED\\MAD3CCDB\\REFF7C42\\CO5CB17F\\CUBE14.HEL;1";
    constexpr const char* SceneLocalMaterialRootDiagnosticPath = "cdrom0:\\MAD3CCDB\\REFF7C42\\CO5CB17F\\CUBE14.HEL;1";
    constexpr bool EnableCubeSpriteDiagnostics = false;
    constexpr bool EnableCubeTriangle2dDiagnostics = false;
    constexpr bool EnableCubeTriangle3dDiagnostics = false;
    constexpr bool EnableCubeRuntimeDiagnostics = false;
    constexpr bool EnableDraw2dExecutionDiagnostics = false;
    constexpr bool EnableRoundedRectDrawSkipDiagnostics = false;
    constexpr bool EnableGlyphQuadDrawSkipDiagnostics = false;
    constexpr bool EnableCubeRuntimeDiagnosticImmediateHalt = false;
    constexpr bool EnableCubeRuntimePhaseFrameProbe = false;
    constexpr double CubeRuntimeDiagnosticWatchSeconds = 5.0;
    constexpr const char* CubeRuntimeDiagnosticSceneId = "textured_cube_grid";
    constexpr bool EnableVerboseEntityDisposalDiagnostics = false;
    constexpr bool EnableVerboseUpdateStageDiagnostics = false;
    constexpr bool EnableFrameTimingDiagnostics = true;
    constexpr bool EnableFrameTimingDiagnosticHalt = false;
    constexpr bool EnableFirstUpdateStateHalt = false;
    constexpr bool EnablePackagedPhysics3DRegistration = true;
    constexpr bool EnablePhysicsWarmupTrace = true;
    constexpr UpdatePhaseDiagnosticMode ActiveUpdatePhaseDiagnosticMode = UpdatePhaseDiagnosticMode::Full;
    constexpr const char* StartupSceneDiagnosticOverrideId = "";
    constexpr bool EnableStartupSceneLoadTimingDiagnostic = true;
    constexpr bool EnableStartupScenePreRenderHalt = false;
    constexpr const char* BootLogHostFilePath = "host:ps2_bootlog.txt";
    constexpr const char* BootLogHostFallbackFilePath = "host0:ps2_bootlog.txt";
    constexpr int BootLogHistoryMaxEntries = 64;
    constexpr int FrameTimingSampleFrameCount = 60;
    constexpr double MemoryDiagnosticsSparseSampleDelaySeconds = 60.0;
    constexpr int MemoryDiagnosticsSparseSampleCount = 4;
    constexpr float CubeSpriteDiagnosticLeft = 211.843231f;
    constexpr float CubeSpriteDiagnosticTop = 115.843239f;
    constexpr float CubeSpriteDiagnosticRight = 428.156738f;
    constexpr float CubeSpriteDiagnosticBottom = 332.156738f;
    constexpr float CubeTriangle2dVertexA0X = 211.843231f;
    constexpr float CubeTriangle2dVertexA0Y = 332.156738f;
    constexpr float CubeTriangle2dVertexA1X = 211.843231f;
    constexpr float CubeTriangle2dVertexA1Y = 115.843239f;
    constexpr float CubeTriangle2dVertexA2X = 428.156738f;
    constexpr float CubeTriangle2dVertexA2Y = 332.156738f;
    constexpr float CubeTriangle2dVertexB0X = 428.156738f;
    constexpr float CubeTriangle2dVertexB0Y = 332.156738f;
    constexpr float CubeTriangle2dVertexB1X = 211.843231f;
    constexpr float CubeTriangle2dVertexB1Y = 115.843239f;
    constexpr float CubeTriangle2dVertexB2X = 428.156738f;
    constexpr float CubeTriangle2dVertexB2Y = 115.843239f;
    constexpr float CubeTriangle3dDiagnosticDepth = 1.0f;
    constexpr const char* FrameTimingOverlayBuildNumber = "B14";
    bool DebugConsoleReady = false;
    bool CubeDiagnosticsShown = false;
    bool CubeRuntimeDiagnosticsCompleted = false;
    bool CubeRuntimeDiagnosticWatchActive = false;
    std::clock_t CubeRuntimeDiagnosticWatchStartTicks = 0;
    std::clock_t MemoryDiagnosticsFirstSampleTicks = 0;
    std::clock_t MemoryDiagnosticsLastSampleTicks = 0;
    int PhysicsWarmupUpdateCount = 0;
    bool CubeFrameBoundaryCheckpointLogged = false;
    bool CubeFrameDrawReturnedLogged = false;
    bool CubeFrameGifWaitBeginLogged = false;
    bool CubeFrameGifWaitEndLogged = false;
    bool LoggedFirst2dCommandTrace = false;
    std::string Pending2dCommandTraceSceneId;
    bool Draw2dExecutionDiagnosticsCompleted = false;
    int32_t ActiveDraw2dDiagnosticCommandIndex = -1;
    double FrameTimingUpdateSeconds = 0.0;
    double FrameTimingDraw3dSeconds = 0.0;
    double FrameTimingGifWaitSeconds = 0.0;
    double FrameTimingDraw2dSeconds = 0.0;
    double FrameTimingDrawSeconds = 0.0;
    double FrameTimingPresentSeconds = 0.0;
    double FrameTimingProxySyncMilliseconds = 0.0;
    double FrameTimingFramePlanMilliseconds = 0.0;
    double FrameTimingVuBatchBuildMilliseconds = 0.0;
    double FrameTimingVuBatchDispatchCount = 0.0;
    double FrameTimingVuTrianglePrepMilliseconds = 0.0;
    double FrameTimingVuTriangleEmitMilliseconds = 0.0;
    double FrameTimingVuTriangleLightingMilliseconds = 0.0;
    double FrameTimingVuTrianglePayloadFillMilliseconds = 0.0;
    double FrameTimingVuPacketAssemblyMilliseconds = 0.0;
    double FrameTimingVuWaitMilliseconds = 0.0;
    double FrameTimingVuSubmitMilliseconds = 0.0;
    double FrameTimingVuPacketEncodeMilliseconds = 0.0;
    double FrameTimingGifDrainMilliseconds = 0.0;
    double FrameTimingLegacyOpaqueMilliseconds = 0.0;
    double FrameTimingLegacyOpaqueTriangleCount = 0.0;
    double FrameTimingVifPacketByteCount = 0.0;
    double FrameTimingCompatibleUntexturedGroupCount = 0.0;
    int FrameTimingFrameCount = 0;
    bool FrameTimingSampleCompleted = false;
    bool FrameTimingOverlayPending = false;
    bool FrameTimingOverlayPresented = false;
    bool MemoryDiagnosticsFirstSampleCaptured = false;
    int MemoryDiagnosticsCapturedSampleCount = 0;
    bool FirstFramePresentCheckpointLogged = false;
    std::string FrameTimingOverlayLine1;
    std::string FrameTimingOverlayLine2;
    std::string FrameTimingOverlayDetailLine;
    std::string FrameTimingOverlayAdditionalText;
    std::vector<std::string> BootLogHistory;
    bool BootLogHostFileStatusPrinted = false;
    GSGLOBAL* ActiveGsGlobal = nullptr;
    double CookedTextureOpenMilliseconds = 0.0;
    double CookedTextureBufferedReadMilliseconds = 0.0;
    double CookedTextureDeserializeMilliseconds = 0.0;
    double CookedTexturePopulateMilliseconds = 0.0;
    double CookedTextureRuntimeTextureCreateMilliseconds = 0.0;
    constexpr uint8_t Ps2BinaryEndiannessLittleEndian = 1;
    constexpr uint8_t Ps2BinaryHeaderCurrentVersion = 2;
    constexpr uint8_t Ps2BinaryHeaderLegacyVersion = 1;
    constexpr uint16_t Ps2BinaryHeaderFormatId = 2;
    constexpr uint16_t Ps2BinaryRecordKindAsset = 1;
    constexpr uint16_t Ps2BinaryValueKindTextureAsset = 2;
    constexpr uint8_t Ps2BinaryRuntimeAssetIdentityVersion = 1;
    constexpr uint8_t Ps2BinaryTextureStorageMetadataVersion = 2;

    const char* ResolveUpdatePhaseDiagnosticModeName(UpdatePhaseDiagnosticMode mode) {
        switch (mode) {
            case UpdatePhaseDiagnosticMode::Full:
                return "full";
            case UpdatePhaseDiagnosticMode::SkipAll:
                return "skip-all";
            case UpdatePhaseDiagnosticMode::InputEarlyOnly:
                return "input-early-only";
            case UpdatePhaseDiagnosticMode::InputLateOnly:
                return "input-late-only";
            case UpdatePhaseDiagnosticMode::InputOnly:
                return "input-only";
            case UpdatePhaseDiagnosticMode::BackendCaptureOnly:
                return "backend-capture-only";
            case UpdatePhaseDiagnosticMode::AllManual:
                return "all-manual";
            case UpdatePhaseDiagnosticMode::FrameStatsOnly:
                return "frame-stats-only";
            case UpdatePhaseDiagnosticMode::ObjectManagerOnly:
                return "object-manager-only";
            case UpdatePhaseDiagnosticMode::PhysicsOnly:
                return "physics-only";
            case UpdatePhaseDiagnosticMode::PointerOnly:
                return "pointer-only";
        }

        return "unknown";
    }

    std::string FormatMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks);
    double ResolveMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks);
    std::string FormatOverlayMilliseconds(double milliseconds);
    std::string FormatFloat4(const ::float4& value);
    void ApplyPlatformPerformanceOverlayRows(::Core* engineCore);
    void BootLog(const char* message);

    void BootLog(const std::string& message);

    void HostLogOnly(const char* message);

    void HostLogOnly(const std::string& message);

    void AppendBootLogToHostFile(const char* message);

    void PrintHostLogStatusLine(const std::string& message);

    void TrimBootLogHistory();

    void PresentBootLogHistoryToDebugConsole();

    class Ps2BootRuntimeDiagnosticsProvider final
        : public ::IRuntimeDiagnosticsProvider
        , public ::IRuntimeSceneTransitionDiagnosticsProvider
        , public ::IRuntimeEntityDisposalDiagnosticsProvider
        , public ::IRuntimeUpdateStageDiagnosticsProvider
#if HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE
        , public ::IRuntimeSceneLoadTimingDiagnosticsProvider
#endif
    {
    public:
        Ps2BootRuntimeDiagnosticsProvider()
            : CurrentLoadSceneId(),
              LastSceneStage(),
              LastSceneStageTicks(0),
              SceneLoadStartTicks(0),
              PhaseTimingsMilliseconds() {
        }

        ::RuntimeMemoryDiagnosticsSnapshot* CaptureSnapshot() override {
            return new ::RuntimeMemoryDiagnosticsSnapshot();
        }

        void ReportEntityDisposalStage(std::string stage, int32_t entityChildCount, int32_t componentCount, int32_t componentIndex) override {
            if (!EnableVerboseEntityDisposalDiagnostics) {
                return;
            }

            if (!String::StartsWith(stage, "Before", StringComparison::Ordinal) &&
                !String::StartsWith(stage, "After", StringComparison::Ordinal)) {
                return;
            }

            BootLog(
                std::string("entity disposal stage=")
                + stage
                + " childCount="
                + std::to_string(entityChildCount)
                + " componentCount="
                + std::to_string(componentCount)
                + " componentIndex="
                + std::to_string(componentIndex));
        }

        void ReportSceneTransitionStage(std::string stage, std::string sceneId, int32_t loadedSceneCount, int32_t pendingOperationCount) override {
            const std::clock_t nowTicks = std::clock();
            if (!String::Equals(sceneId, CurrentLoadSceneId, StringComparison::Ordinal)) {
                CurrentLoadSceneId = sceneId;
                LastSceneStage = String::Empty;
                LastSceneStageTicks = nowTicks;
                SceneLoadStartTicks = nowTicks;
            }

            const std::string sincePrevious = FormatMillisecondsFromClockTicks(LastSceneStageTicks, nowTicks);
            const std::string sinceStart = FormatMillisecondsFromClockTicks(SceneLoadStartTicks, nowTicks);
            BootLog(
                std::string("scene id=")
                + sceneId
                + " stage="
                + stage
                + " dt="
                + sincePrevious
                + " total="
                + sinceStart
                + " loaded="
                + std::to_string(loadedSceneCount)
                + " pending="
                + std::to_string(pendingOperationCount));
            if (String::Equals(stage, "LoadSceneImmediateEnd", StringComparison::Ordinal)
                && (sceneId.find("colored_cube_grid") != std::string::npos
                    || sceneId.find("textured_cube_grid") != std::string::npos)) {
                Pending2dCommandTraceSceneId = sceneId;
                LoggedFirst2dCommandTrace = false;
            }
            LastSceneStage = stage;
            LastSceneStageTicks = nowTicks;
        }

        void ReportUpdateStage(std::string stage) override {
            if (!EnableVerboseUpdateStageDiagnostics) {
                return;
            }
            if (String::Equals(LastUpdateStage, stage, StringComparison::Ordinal)) {
                return;
            }

            LastUpdateStage = stage;
            HostLogOnly(std::string("update stage=") + stage);
        }

#if HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE
        void ReportSceneLoadPhaseTiming(std::string phaseName, double elapsedMilliseconds) override {
            PhaseTimingsMilliseconds[phaseName] = elapsedMilliseconds;
            BootLog(
                std::string("scene timing ")
                + phaseName
                + " ms="
                + std::to_string(elapsedMilliseconds));
        }

        double GetPhaseTimingMilliseconds(const std::string& phaseName) const {
            const std::unordered_map<std::string, double>::const_iterator phaseTiming = PhaseTimingsMilliseconds.find(phaseName);
            if (phaseTiming == PhaseTimingsMilliseconds.end()) {
                return -1.0;
            }

            return phaseTiming->second;
        }
#endif

    private:
        std::string CurrentLoadSceneId;
        std::string LastSceneStage;
        std::string LastUpdateStage;
        std::clock_t LastSceneStageTicks;
        std::clock_t SceneLoadStartTicks;
        std::unordered_map<std::string, double> PhaseTimingsMilliseconds;
    };

    void BootLogTimingSummary(Ps2BootRuntimeDiagnosticsProvider* diagnosticsProvider, const char* label, const char* phaseName) {
#if HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE
        if (diagnosticsProvider == nullptr || label == nullptr || phaseName == nullptr) {
            return;
        }

        const double elapsedMilliseconds = diagnosticsProvider->GetPhaseTimingMilliseconds(phaseName);
        if (elapsedMilliseconds < 0.0) {
            return;
        }

        BootLog(std::string("sum ") + label + "=" + std::to_string(elapsedMilliseconds) + "ms");
#else
        (void)diagnosticsProvider;
        (void)label;
        (void)phaseName;
#endif
    }

    void BootLogTimingValue(const char* label, double elapsedMilliseconds) {
        if (label == nullptr || elapsedMilliseconds < 0.0) {
            return;
        }

        BootLog(std::string("sum ") + label + "=" + std::to_string(elapsedMilliseconds) + "ms");
    }

    bool IsPs2DiscRuntimePath(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0;
    }

    std::string ResolvePs2CookedAssetOpenPath(const std::string& path) {
        if (path.empty() || IsPs2DiscRuntimePath(path)) {
            return path;
        }

        const char* physicalPath = he_get_runtime_ps2_asset_physical_path(path.c_str());
        if (physicalPath == nullptr || physicalPath[0] == '\0') {
            return path;
        }

        return physicalPath;
    }

    bool HasPackagedPhysics3DScenes() {
        std::size_t sceneCount = 0;
        const HERuntimePhysics3DSceneFeatureEntry* sceneEntries = he_runtime_physics3d_scene_feature_entries(&sceneCount);
        if (sceneEntries == nullptr) {
            return false;
        }

        for (std::size_t index = 0; index < sceneCount; index++) {
            const HERuntimePhysics3DSceneFeatureEntry& entry = sceneEntries[index];
            if (entry.FeatureFlags != 0u) {
                return true;
            }
        }

        return false;
    }

    ::Array<uint8_t>* ReadPs2DiscFileBytes(const std::string& path, double& openMilliseconds, double& readMilliseconds) {
        openMilliseconds = 0.0;
        readMilliseconds = 0.0;
        const std::clock_t openStartTicks = std::clock();
        const int fileDescriptor = open(path.c_str(), O_RDONLY);
        const std::clock_t openEndTicks = std::clock();
        openMilliseconds = ResolveMillisecondsFromClockTicks(openStartTicks, openEndTicks);
        if (fileDescriptor < 0) {
            throw std::runtime_error(std::string("PS2 disc file open failed for path: ") + path);
        }

        [[maybe_unused]] auto fileDescriptorGuard = he_cpp_make_scope_exit([fileDescriptor]() {
            close(fileDescriptor);
        });

        const off_t fileLength = lseek(fileDescriptor, 0, SEEK_END);
        if (fileLength < 0) {
            throw std::runtime_error(std::string("PS2 disc file length read failed for path: ") + path);
        }

        if (lseek(fileDescriptor, 0, SEEK_SET) < 0) {
            throw std::runtime_error(std::string("PS2 disc file rewind failed for path: ") + path);
        }

        if (fileLength > static_cast<off_t>(std::numeric_limits<int32_t>::max())) {
            throw std::invalid_argument("PS2 cooked texture payload exceeds buffered runtime limits.");
        }

        ::Array<uint8_t>* bufferedBytes = new ::Array<uint8_t>(static_cast<int32_t>(fileLength));
        const std::clock_t readStartTicks = std::clock();
        std::size_t bufferedOffset = 0;
        const std::size_t totalLength = static_cast<std::size_t>(fileLength);
        while (bufferedOffset < totalLength) {
            const ssize_t bytesRead = read(
                fileDescriptor,
                bufferedBytes->Data + bufferedOffset,
                totalLength - bufferedOffset);
            if (bytesRead <= 0) {
                delete bufferedBytes;
                throw std::runtime_error(std::string("PS2 disc file read ended before buffering completed for path: ") + path);
            }

            bufferedOffset += static_cast<std::size_t>(bytesRead);
        }

        const std::clock_t readEndTicks = std::clock();
        readMilliseconds = ResolveMillisecondsFromClockTicks(readStartTicks, readEndTicks);
        return bufferedBytes;
    }

    std::string BuildLoadedSceneIdsDiagnostic(::SceneManager* sceneManager) {
        if (sceneManager == nullptr) {
            return "null";
        }

        List<std::string>* loadedSceneIds = sceneManager->GetLoadedSceneIds();
        if (loadedSceneIds == nullptr) {
            return "null";
        }

        std::string result;
        for (int32_t index = 0; index < loadedSceneIds->get_Count(); index++) {
            if (!result.empty()) {
                result += ",";
            }

            result += (*loadedSceneIds)[index];
        }

        delete loadedSceneIds;
        return result.empty() ? "none" : result;
    }

    bool ShouldCaptureCubeRuntimeDiagnostics(::SceneManager* sceneManager) {
        if (sceneManager == nullptr) {
            return false;
        }

        std::string loadedSceneIds = BuildLoadedSceneIdsDiagnostic(sceneManager);
        return loadedSceneIds == CubeRuntimeDiagnosticSceneId;
    }

    bool IsCubeRuntimeDiagnosticsSceneActive(::Core* engineCore) {
        if (engineCore == nullptr) {
            return false;
        }

        return ShouldCaptureCubeRuntimeDiagnostics(engineCore->get_SceneManager());
    }

    std::string BuildStartupSceneRootSummary(::SceneAsset* sceneAsset) {
        if (sceneAsset == nullptr) {
            return "scene=null";
        }

        Array<::SceneEntityAsset*>* rootEntities = sceneAsset->get_RootEntities();
        const int32_t rootCount = rootEntities != nullptr ? rootEntities->get_Length() : 0;
        std::string summary = "roots=" + std::to_string(rootCount);
        if (rootCount <= 0 || rootEntities == nullptr || (*rootEntities)[0] == nullptr) {
            return summary;
        }

        ::SceneEntityAsset* firstRoot = (*rootEntities)[0];
        Array<::SceneComponentAssetRecord*>* firstRootComponents = firstRoot->get_Components();
        const int32_t componentCount = firstRootComponents != nullptr ? firstRootComponents->get_Length() : 0;
        summary += " firstRootComponents=" + std::to_string(componentCount);
        if (componentCount <= 0 || firstRootComponents == nullptr) {
            return summary;
        }

        summary += " firstComponent=";
        ::SceneComponentAssetRecord* firstComponent = (*firstRootComponents)[0];
        if (firstComponent == nullptr) {
            summary += "null";
            return summary;
        }

        summary += firstComponent->get_ComponentTypeId();
        return summary;
    }

    void PresentBootLogHistoryToDebugConsole() {
        init_scr();
        scr_setbgcolor(0x101010);
        scr_clear();
        scr_setXY(0, 0);

        const int historyCount = static_cast<int>(BootLogHistory.size());
        const int startIndex = std::max(0, historyCount - 30);
        for (int index = startIndex; index < historyCount; index++) {
            scr_printf("[helengine-ps2] %s\n", BootLogHistory[static_cast<size_t>(index)].c_str());
        }
    }

    std::string BuildSceneLoadRuntimeDiagnostics(::Core* engineCore);

    void BootLogRuntimeException(const char* phase, const std::string& detail, ::Core* engineCore, bool includeSceneLoadDiagnostics);

    void BootLogRuntimeException(const char* phase, Exception* exception, ::Core* engineCore, bool includeSceneLoadDiagnostics);

    void BootLogRuntimeException(const char* phase, const std::exception& exception, ::Core* engineCore, bool includeSceneLoadDiagnostics);

    void BootLogUnknownRuntimeException(const char* phase, ::Core* engineCore, bool includeSceneLoadDiagnostics);

    std::string FormatFloat4(const ::float4& value);

    const char* ResolveRenderCommand2DTypeName(::RenderCommand2DType commandType) {
        if (commandType == ::RenderCommand2DType::ClipPush) {
            return "ClipPush";
        } else if (commandType == ::RenderCommand2DType::ClipPop) {
            return "ClipPop";
        } else if (commandType == ::RenderCommand2DType::TexturedQuad) {
            return "TexturedQuad";
        } else if (commandType == ::RenderCommand2DType::GlyphQuad) {
            return "GlyphQuad";
        } else if (commandType == ::RenderCommand2DType::RoundedRect) {
            return "RoundedRect";
        }

        return "Unknown";
    }

    double ResolveSecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks) {
        if (endTicks <= startTicks) {
            return 0.0;
        }

        return static_cast<double>(endTicks - startTicks) / static_cast<double>(CLOCKS_PER_SEC);
    }

    std::string FormatMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks) {
        const double elapsedMilliseconds = ResolveSecondsFromClockTicks(startTicks, endTicks) * 1000.0;
        char buffer[64];
        std::snprintf(buffer, sizeof(buffer), "%.3fms", elapsedMilliseconds);
        return std::string(buffer);
    }

    double ResolveMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks) {
        return ResolveSecondsFromClockTicks(startTicks, endTicks) * 1000.0;
    }

    std::string FormatOverlayMilliseconds(double milliseconds) {
        char buffer[32];
        std::snprintf(buffer, sizeof(buffer), "%.1f", milliseconds);
        return std::string(buffer);
    }

    void ApplyPlatformPerformanceOverlayRows(::Core* engineCore) {
        if (engineCore == nullptr) {
            return;
        }

        if (FrameTimingOverlayLine1.empty()
            && FrameTimingOverlayLine2.empty()
            && FrameTimingOverlayDetailLine.empty()
            && FrameTimingOverlayAdditionalText.empty()) {
            return;
        }

        engineCore->SetPerformanceOverlayTextRows(
            true,
            FrameTimingOverlayLine1,
            FrameTimingOverlayLine2,
            FrameTimingOverlayDetailLine,
            FrameTimingOverlayAdditionalText);
    }

    void RecordRenderManagerTimingSample(const helengine::ps2::Ps2RenderManager3D& renderManager3DBackend) {
        if (!EnableFrameTimingDiagnostics) {
            return;
        }

        const helengine::ps2::Ps2RenderPerformanceMetrics& metrics = renderManager3DBackend.GetLastPerformanceMetrics();
        FrameTimingProxySyncMilliseconds += metrics.ProxySyncMilliseconds;
        FrameTimingFramePlanMilliseconds += metrics.FramePlanMilliseconds;
        FrameTimingVuBatchBuildMilliseconds += metrics.VuBatchBuildMilliseconds;
        FrameTimingVuBatchDispatchCount += static_cast<double>(metrics.VifPacketCount);
        FrameTimingVuTrianglePrepMilliseconds += renderManager3DBackend.GetLastVuTrianglePrepMilliseconds();
        FrameTimingVuTriangleEmitMilliseconds += renderManager3DBackend.GetLastVuTriangleEmitMilliseconds();
        FrameTimingVuTriangleLightingMilliseconds += renderManager3DBackend.GetLastVuTriangleLightingMilliseconds();
        FrameTimingVuTrianglePayloadFillMilliseconds += renderManager3DBackend.GetLastVuTrianglePayloadFillMilliseconds();
        FrameTimingVuPacketAssemblyMilliseconds += renderManager3DBackend.GetLastVuPacketAssemblyMilliseconds();
        FrameTimingVuWaitMilliseconds += metrics.VifReuseWaitMilliseconds;
        FrameTimingVuSubmitMilliseconds += metrics.VifSubmitMilliseconds;
        FrameTimingVuPacketEncodeMilliseconds += metrics.PacketEncodeMilliseconds;
        FrameTimingGifDrainMilliseconds += metrics.GifDrainMilliseconds;
        FrameTimingLegacyOpaqueMilliseconds += metrics.LegacyOpaqueMilliseconds;
        FrameTimingLegacyOpaqueTriangleCount += static_cast<double>(metrics.LegacyOpaqueTriangleCount);
        FrameTimingVifPacketByteCount += static_cast<double>(metrics.VifPacketByteCount);
        FrameTimingCompatibleUntexturedGroupCount += static_cast<double>(metrics.CompatibleUntexturedGroupCount);
    }

    void RecordFrameTimingSample(
        const helengine::ps2::Ps2RenderManager3D& renderManager3DBackend,
        double updateSeconds,
        double draw3dSeconds,
        double gifWaitSeconds,
        double draw2dSeconds,
        double drawSeconds,
        double presentSeconds) {
        if (!EnableFrameTimingDiagnostics) {
            return;
        }

        FrameTimingUpdateSeconds += updateSeconds;
        FrameTimingDraw3dSeconds += draw3dSeconds;
        FrameTimingGifWaitSeconds += gifWaitSeconds;
        FrameTimingDraw2dSeconds += draw2dSeconds;
        FrameTimingDrawSeconds += drawSeconds;
        FrameTimingPresentSeconds += presentSeconds;
        FrameTimingFrameCount += 1;
        if (FrameTimingFrameCount < FrameTimingSampleFrameCount) {
            return;
        }

        const double sampledFrameCount = static_cast<double>(FrameTimingFrameCount);
        const double averageUpdateMilliseconds = (FrameTimingUpdateSeconds / sampledFrameCount) * 1000.0;
        const double averageDraw3dMilliseconds = (FrameTimingDraw3dSeconds / sampledFrameCount) * 1000.0;
        const double averageGifWaitMilliseconds = (FrameTimingGifWaitSeconds / sampledFrameCount) * 1000.0;
        const double averageDraw2dMilliseconds = (FrameTimingDraw2dSeconds / sampledFrameCount) * 1000.0;
        const double averageDrawMilliseconds = (FrameTimingDrawSeconds / sampledFrameCount) * 1000.0;
        const double averagePresentMilliseconds = (FrameTimingPresentSeconds / sampledFrameCount) * 1000.0;
        const double averageProxySyncMilliseconds = FrameTimingProxySyncMilliseconds / sampledFrameCount;
        const double averageFramePlanMilliseconds = FrameTimingFramePlanMilliseconds / sampledFrameCount;
        const double averageVuBatchBuildMilliseconds = FrameTimingVuBatchBuildMilliseconds / sampledFrameCount;
        const double averageVuBatchDispatchCount = FrameTimingVuBatchDispatchCount / sampledFrameCount;
        const double averageVuTrianglePrepMilliseconds = FrameTimingVuTrianglePrepMilliseconds / sampledFrameCount;
        const double averageVuTriangleEmitMilliseconds = FrameTimingVuTriangleEmitMilliseconds / sampledFrameCount;
        const double averageVuTriangleLightingMilliseconds = FrameTimingVuTriangleLightingMilliseconds / sampledFrameCount;
        const double averageVuTrianglePayloadFillMilliseconds = FrameTimingVuTrianglePayloadFillMilliseconds / sampledFrameCount;
        const double averageVuPacketAssemblyMilliseconds = FrameTimingVuPacketAssemblyMilliseconds / sampledFrameCount;
        const double averageVuWaitMilliseconds = FrameTimingVuWaitMilliseconds / sampledFrameCount;
        const double averageVuSubmitMilliseconds = FrameTimingVuSubmitMilliseconds / sampledFrameCount;
        const double averageVuPacketEncodeMilliseconds = FrameTimingVuPacketEncodeMilliseconds / sampledFrameCount;
        const double averageGifDrainMilliseconds = FrameTimingGifDrainMilliseconds / sampledFrameCount;
        const double averageLegacyOpaqueMilliseconds = FrameTimingLegacyOpaqueMilliseconds / sampledFrameCount;
        const double averageLegacyOpaqueTriangleCount = FrameTimingLegacyOpaqueTriangleCount / sampledFrameCount;
        const double averageVifPacketByteCount = FrameTimingVifPacketByteCount / sampledFrameCount;
        const double averageCompatibleUntexturedGroupCount = FrameTimingCompatibleUntexturedGroupCount / sampledFrameCount;
        const double averageSetMilliseconds = averageProxySyncMilliseconds
            + averageFramePlanMilliseconds
            + averageVuBatchBuildMilliseconds
            + averageVuTrianglePrepMilliseconds
            + averageVuTriangleEmitMilliseconds
            + averageVuTriangleLightingMilliseconds
            + averageVuTrianglePayloadFillMilliseconds
            + averageVuPacketAssemblyMilliseconds
            + averageVuPacketEncodeMilliseconds;
        const double totalSeconds = FrameTimingUpdateSeconds + FrameTimingDrawSeconds + FrameTimingPresentSeconds;
        const double averageFramesPerSecond = totalSeconds <= 0.0 ? 0.0 : sampledFrameCount / totalSeconds;
        BootLog(
            "frame timing avg updateMs="
            + std::to_string(averageUpdateMilliseconds)
            + " draw3dMs="
            + std::to_string(averageDraw3dMilliseconds)
            + " gifWaitMs="
            + std::to_string(averageGifWaitMilliseconds)
            + " draw2dMs="
            + std::to_string(averageDraw2dMilliseconds)
            + " drawMs="
            + std::to_string(averageDrawMilliseconds)
            + " presentMs="
            + std::to_string(averagePresentMilliseconds)
            + " setMs="
            + std::to_string(averageSetMilliseconds)
            + " syncMs="
            + std::to_string(averageProxySyncMilliseconds)
            + " planMs="
            + std::to_string(averageFramePlanMilliseconds)
            + " buildMs="
            + std::to_string(averageVuBatchBuildMilliseconds)
            + " encMs="
            + std::to_string(averageVuPacketEncodeMilliseconds)
            + " prepMs="
            + std::to_string(averageVuTrianglePrepMilliseconds)
            + " emitMs="
            + std::to_string(averageVuTriangleEmitMilliseconds)
            + " lgtMs="
            + std::to_string(averageVuTriangleLightingMilliseconds)
            + " tplMs="
            + std::to_string(averageVuTrianglePayloadFillMilliseconds)
            + " asmMs="
            + std::to_string(averageVuPacketAssemblyMilliseconds)
            + " wtMs="
            + std::to_string(averageVuWaitMilliseconds)
            + " subMs="
            + std::to_string(averageVuSubmitMilliseconds)
            + " gifMs="
            + std::to_string(averageGifDrainMilliseconds)
            + " legMs="
            + std::to_string(averageLegacyOpaqueMilliseconds)
            + " legTri="
            + std::to_string(averageLegacyOpaqueTriangleCount)
            + " pktBytes="
            + std::to_string(averageVifPacketByteCount)
            + " grp="
            + std::to_string(averageCompatibleUntexturedGroupCount)
            + " disp="
            + std::to_string(averageVuBatchDispatchCount)
            + " fps="
            + std::to_string(averageFramesPerSecond));
        FrameTimingOverlayLine1 =
            std::string(FrameTimingOverlayBuildNumber)
            + " FPS "
            + FormatOverlayMilliseconds(averageFramesPerSecond)
            + " Set "
            + FormatOverlayMilliseconds(averageSetMilliseconds)
            + " Drw "
            + FormatOverlayMilliseconds(averageDrawMilliseconds);
        FrameTimingOverlayLine2 =
            std::string("Drw ")
            + FormatOverlayMilliseconds(averageDrawMilliseconds)
            + " Sync "
            + FormatOverlayMilliseconds(averageProxySyncMilliseconds)
            + " Plan "
            + FormatOverlayMilliseconds(averageFramePlanMilliseconds)
            + " Bld "
            + FormatOverlayMilliseconds(averageVuBatchBuildMilliseconds);
        FrameTimingOverlayDetailLine =
            std::string("Enc ")
            + FormatOverlayMilliseconds(averageVuPacketEncodeMilliseconds)
            + " Leg "
            + FormatOverlayMilliseconds(averageLegacyOpaqueMilliseconds)
            + " Tri "
            + std::to_string(static_cast<int>(averageLegacyOpaqueTriangleCount))
            + " Pkt "
            + FormatOverlayMilliseconds(averageVuBatchDispatchCount);
        FrameTimingOverlayAdditionalText =
            std::string("Leg ")
            + FormatOverlayMilliseconds(averageLegacyOpaqueMilliseconds)
            + " Tri "
            + std::to_string(static_cast<int>(averageLegacyOpaqueTriangleCount))
            + " Pkt "
            + FormatOverlayMilliseconds(averageVuBatchDispatchCount)
            + " Bytes "
            + std::to_string(static_cast<int>(averageVifPacketByteCount))
            + " Grp "
            + FormatOverlayMilliseconds(averageCompatibleUntexturedGroupCount);
        FrameTimingOverlayPending = true;
        FrameTimingOverlayPresented = false;
        FrameTimingSampleCompleted = true;
        FrameTimingUpdateSeconds = 0.0;
        FrameTimingDraw3dSeconds = 0.0;
        FrameTimingGifWaitSeconds = 0.0;
        FrameTimingDraw2dSeconds = 0.0;
        FrameTimingDrawSeconds = 0.0;
        FrameTimingPresentSeconds = 0.0;
        FrameTimingProxySyncMilliseconds = 0.0;
        FrameTimingFramePlanMilliseconds = 0.0;
        FrameTimingVuBatchBuildMilliseconds = 0.0;
        FrameTimingVuBatchDispatchCount = 0.0;
        FrameTimingVuTrianglePrepMilliseconds = 0.0;
        FrameTimingVuTriangleEmitMilliseconds = 0.0;
        FrameTimingVuTriangleLightingMilliseconds = 0.0;
        FrameTimingVuTrianglePayloadFillMilliseconds = 0.0;
        FrameTimingVuPacketAssemblyMilliseconds = 0.0;
        FrameTimingVuWaitMilliseconds = 0.0;
        FrameTimingVuSubmitMilliseconds = 0.0;
        FrameTimingVuPacketEncodeMilliseconds = 0.0;
        FrameTimingGifDrainMilliseconds = 0.0;
        FrameTimingLegacyOpaqueMilliseconds = 0.0;
        FrameTimingLegacyOpaqueTriangleCount = 0.0;
        FrameTimingVifPacketByteCount = 0.0;
        FrameTimingCompatibleUntexturedGroupCount = 0.0;
        FrameTimingFrameCount = 0;
    }

    void PublishCubeRuntimeOverlayMetrics(const helengine::ps2::Ps2RenderManager3D& renderManager3DBackend) {
        FrameTimingOverlayLine1 =
            std::string("P:")
            + std::to_string(renderManager3DBackend.GetLastProxyCount())
            + " B:"
            + std::to_string(renderManager3DBackend.GetLastVuBatchDispatchCount())
            + " T:"
            + std::to_string(renderManager3DBackend.GetLastSubmittedTriangleCount())
            + " Ph:"
            + std::to_string(renderManager3DBackend.GetLastVuPacketPhase());
        const ::float4 screenBounds = renderManager3DBackend.GetLastSubmittedScreenBounds();
        FrameTimingOverlayLine2 =
            std::string("SB:")
            + std::to_string(static_cast<int>(screenBounds.X))
            + ","
            + std::to_string(static_cast<int>(screenBounds.Y))
            + ","
            + std::to_string(static_cast<int>(screenBounds.Z))
            + ","
            + std::to_string(static_cast<int>(screenBounds.W));
        FrameTimingOverlayPending = true;
        FrameTimingOverlayPresented = false;
    }

    void EnsureDebugConsole() {
        if (DebugConsoleReady) {
            return;
        }

        init_scr();
        scr_setbgcolor(0x101010);
        scr_clear();
        DebugConsoleReady = true;
    }

    void AppendBootLogToHostFile(const char* message) {
        if (message == nullptr) {
            return;
        }

        const std::string hostLogLine = std::string("[helengine-ps2] ") + message + "\n";
        const char* hostLogCandidates[] = { BootLogHostFilePath, BootLogHostFallbackFilePath };
        for (const char* candidatePath : hostLogCandidates) {
            int hostLogFileDescriptor = open(candidatePath, O_CREAT | O_WRONLY | O_APPEND, 0666);
            if (hostLogFileDescriptor < 0) {
                continue;
            }

            const int bytesWritten = static_cast<int>(write(hostLogFileDescriptor, hostLogLine.c_str(), hostLogLine.size()));
            close(hostLogFileDescriptor);
            if (!BootLogHostFileStatusPrinted) {
                const std::string diagnostic = bytesWritten < static_cast<int>(hostLogLine.size())
                    ? std::string("host log write failed bytes=") + std::to_string(bytesWritten)
                    : std::string("host log write ok path=") + candidatePath + std::string(" bytes=") + std::to_string(bytesWritten);
                PrintHostLogStatusLine(diagnostic);
                BootLogHostFileStatusPrinted = true;
            }

            return;
        }

        if (!BootLogHostFileStatusPrinted) {
            const std::string diagnostic = std::string("host log open failed errno=") + std::to_string(errno);
            PrintHostLogStatusLine(diagnostic);
            BootLogHostFileStatusPrinted = true;
        }
    }

    void PrintHostLogStatusLine(const std::string& message) {
        BootLogHistory.push_back(message);
        TrimBootLogHistory();
        std::printf("[helengine-ps2] %s\n", message.c_str());
        std::fflush(stdout);
    }

    void TrimBootLogHistory() {
        if (BootLogHistory.size() <= static_cast<std::size_t>(BootLogHistoryMaxEntries)) {
            return;
        }

        const std::size_t removeCount = BootLogHistory.size() - static_cast<std::size_t>(BootLogHistoryMaxEntries);
        const std::vector<std::string>::difference_type removeDistance = static_cast<std::vector<std::string>::difference_type>(removeCount);
        BootLogHistory.erase(BootLogHistory.begin(), BootLogHistory.begin() + removeDistance);
    }

    void HostLogOnly(const char* message) {
        if (message == nullptr) {
            return;
        }

        std::printf("[helengine-ps2] %s\n", message);
        std::fflush(stdout);
        AppendBootLogToHostFile(message);
    }

    void HostLogOnly(const std::string& message) {
        HostLogOnly(message.c_str());
    }

    void BootLog(const char* message) {
        BootLogHistory.push_back(message != nullptr ? message : "");
        TrimBootLogHistory();
        std::printf("[helengine-ps2] %s\n", message);
        std::fflush(stdout);
        AppendBootLogToHostFile(message);
    }

    void BootLog(const std::string& message) {
        BootLog(message.c_str());
    }

    std::string BuildSceneLoadRuntimeDiagnostics(::Core* engineCore) {
        if (engineCore == nullptr) {
            return std::string();
        }

        ::SceneManager* sceneManager = engineCore->get_SceneManager();
        auto* sceneLoadService = engineCore->get_SceneLoadService();
        if (sceneManager == nullptr && sceneLoadService == nullptr) {
            return std::string();
        }

        return std::string(" sceneTrace=")
            + (sceneManager != nullptr ? sceneManager->get_LastTraceStage() : std::string("null"))
            + " sceneLoadTrace="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTraceStage() : std::string("null"))
            + " rootIndex="
            + std::to_string(sceneLoadService != nullptr ? sceneLoadService->get_LastTraceRootEntityIndex() : -1)
            + " depth="
            + std::to_string(sceneLoadService != nullptr ? sceneLoadService->get_LastTraceEntityDepth() : -1)
            + " component="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTraceComponentTypeId() : std::string("null"))
            + " textStage="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTextLoadStage() : std::string("null"))
            + " textureStage="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTextureLoadStage() : std::string("null"))
            + " texturePath="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTextureRelativePath() : std::string("null"))
            + " fontPath="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastTextFontRelativePath() : std::string("null"))
            + " fontDeserialize="
            + (sceneLoadService != nullptr ? sceneLoadService->get_LastFontDeserializeStage() : std::string("null"));
    }

    void BootLogRuntimeException(const char* phase, const std::string& detail, ::Core* engineCore, bool includeSceneLoadDiagnostics) {
        std::string message = std::string(phase) + " exception: " + detail;
        if (includeSceneLoadDiagnostics) {
            message += BuildSceneLoadRuntimeDiagnostics(engineCore);
        }

        BootLog(message);
    }

    void BootLogRuntimeException(const char* phase, Exception* exception, ::Core* engineCore, bool includeSceneLoadDiagnostics) {
        BootLogRuntimeException(
            phase,
            exception != nullptr ? exception->what() : std::string("null"),
            engineCore,
            includeSceneLoadDiagnostics);
    }

    void BootLogRuntimeException(const char* phase, const std::exception& exception, ::Core* engineCore, bool includeSceneLoadDiagnostics) {
        BootLogRuntimeException(phase, exception.what(), engineCore, includeSceneLoadDiagnostics);
    }

    void BootLogUnknownRuntimeException(const char* phase, ::Core* engineCore, bool includeSceneLoadDiagnostics) {
        BootLogRuntimeException(phase, "unknown", engineCore, includeSceneLoadDiagnostics);
    }

    std::string FormatFloat4(const ::float4& value) {
        return "("
            + std::to_string(value.X)
            + ","
            + std::to_string(value.Y)
            + ","
            + std::to_string(value.Z)
            + ","
            + std::to_string(value.W)
            + ")";
    }

    std::string FormatByte4(const ::byte4& value) {
        return "("
            + std::to_string(static_cast<int>(value.X))
            + ","
            + std::to_string(static_cast<int>(value.Y))
            + ","
            + std::to_string(static_cast<int>(value.Z))
            + ","
            + std::to_string(static_cast<int>(value.W))
            + ")";
    }

    void BootLogDiscProbe(const char* label, const char* path) {
        bool exists = File::Exists(path);
        BootLog(std::string(label) + ": exists=" + (exists ? "true" : "false") + " path=" + path);
        std::FILE* directFile = std::fopen(path, "rb");
        BootLog(std::string(label) + ": fopen=" + (directFile != nullptr ? "true" : "false"));
        if (directFile != nullptr) {
            std::fclose(directFile);
        }
        if (!exists) {
            return;
        }

        try {
            FileStream* stream = File::OpenRead(path);
            if (stream == nullptr) {
                BootLog(std::string(label) + ": open returned null");
                return;
            }

            BootLog(std::string(label) + ": open length=" + std::to_string(stream->Length()));
            delete stream;
        } catch (Exception* exception) {
            BootLog(std::string(label) + ": open exception=" + (exception != nullptr ? exception->what() : "null"));
            delete exception;
        } catch (const std::exception& exception) {
            BootLog(std::string(label) + ": open std exception=" + exception.what());
        } catch (...) {
            BootLog(std::string(label) + ": open unknown exception");
        }
    }

    void DrawCubeSpriteDiagnosticsFrame(GSGLOBAL* gsGlobal) {
        if (gsGlobal == nullptr) {
            return;
        }

        gsKit_set_test(gsGlobal, GS_ATEST_OFF);
        gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
        gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        gsKit_prim_sprite(
            gsGlobal,
            CubeSpriteDiagnosticLeft,
            CubeSpriteDiagnosticTop,
            CubeSpriteDiagnosticRight,
            CubeSpriteDiagnosticBottom,
            0.0f,
            GS_SETREG_RGBAQ(0xD0, 0x30, 0x30, 0x80, 0x00));
    }

    void DrawCubeTriangle2dDiagnosticsFrame(GSGLOBAL* gsGlobal) {
        if (gsGlobal == nullptr) {
            return;
        }

        const u64 darkerRed = GS_SETREG_RGBAQ(0xA0, 0x20, 0x20, 0x80, 0x00);
        const u64 lighterRed = GS_SETREG_RGBAQ(0xD0, 0x50, 0x50, 0x80, 0x00);
        gsKit_set_test(gsGlobal, GS_ATEST_OFF);
        gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
        gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        gsKit_prim_triangle_gouraud(
            gsGlobal,
            CubeTriangle2dVertexA0X,
            CubeTriangle2dVertexA0Y,
            CubeTriangle2dVertexA1X,
            CubeTriangle2dVertexA1Y,
            CubeTriangle2dVertexA2X,
            CubeTriangle2dVertexA2Y,
            1.0f,
            darkerRed,
            darkerRed,
            darkerRed);
        gsKit_prim_triangle_gouraud(
            gsGlobal,
            CubeTriangle2dVertexB0X,
            CubeTriangle2dVertexB0Y,
            CubeTriangle2dVertexB1X,
            CubeTriangle2dVertexB1Y,
            CubeTriangle2dVertexB2X,
            CubeTriangle2dVertexB2Y,
            1.0f,
            lighterRed,
            lighterRed,
            lighterRed);
    }

    void DrawCubeTriangle3dDiagnosticsFrame(GSGLOBAL* gsGlobal) {
        if (gsGlobal == nullptr) {
            return;
        }

        const u64 darkerRed = GS_SETREG_RGBAQ(0xA0, 0x20, 0x20, 0x80, 0x00);
        const u64 lighterRed = GS_SETREG_RGBAQ(0xD0, 0x50, 0x50, 0x80, 0x00);
        gsKit_set_test(gsGlobal, GS_ATEST_OFF);
        gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
        gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        gsKit_prim_triangle_gouraud_3d(
            gsGlobal,
            CubeTriangle2dVertexA0X, CubeTriangle2dVertexA0Y, CubeTriangle3dDiagnosticDepth,
            CubeTriangle2dVertexA1X, CubeTriangle2dVertexA1Y, CubeTriangle3dDiagnosticDepth,
            CubeTriangle2dVertexA2X, CubeTriangle2dVertexA2Y, CubeTriangle3dDiagnosticDepth,
            darkerRed,
            darkerRed,
            darkerRed);
        gsKit_prim_triangle_gouraud_3d(
            gsGlobal,
            CubeTriangle2dVertexB0X, CubeTriangle2dVertexB0Y, CubeTriangle3dDiagnosticDepth,
            CubeTriangle2dVertexB1X, CubeTriangle2dVertexB1Y, CubeTriangle3dDiagnosticDepth,
            CubeTriangle2dVertexB2X, CubeTriangle2dVertexB2Y, CubeTriangle3dDiagnosticDepth,
            lighterRed,
            lighterRed,
            lighterRed);
    }

    [[noreturn]] void FatalHalt(const char* message) {
        BootLog(message);
        PresentBootLogHistoryToDebugConsole();
        if (ActiveGsGlobal != nullptr) {
            gsKit_sync_flip(ActiveGsGlobal);
        }
        while (true) {
        }
    }

    [[noreturn]] void FatalHalt(const std::string& message) {
        FatalHalt(message.c_str());
    }

    struct Ps2TextureRecord {
        GSTEXTURE Texture {};
        void* Pixels = nullptr;
        int ClutWidth = 0;
        int ClutHeight = 0;
        bool Uploaded = false;
        std::size_t CpuPixelBytes = 0;
        std::size_t CpuClutBytes = 0;
        std::size_t VramPixelBytes = 0;
        std::size_t VramClutBytes = 0;
    };

    struct Ps2MemoryDiagnosticsSnapshot {
        long HeapUsedBytes = -1;
        long HeapFreeBytes = -1;
        std::size_t RuntimeTextureCount = 0;
        std::size_t FontTextureCount = 0;
        std::size_t UploadedTextureCount = 0;
        std::size_t CpuTextureBytes = 0;
        std::size_t VramTextureBytes = 0;
        int LoadedSceneCount = 0;
        int EntityCount = 0;
        int Drawable2DCount = 0;
        int Drawable3DCount = 0;
        int CameraCount = 0;
    };

    std::string BuildMemoryDiagnosticsLogLine(const Ps2MemoryDiagnosticsSnapshot& current, const Ps2MemoryDiagnosticsSnapshot* previous);

    std::unordered_map<const ::RuntimeTexture*, Ps2TextureRecord> TextureRecords;
    std::unordered_map<const ::FontAsset*, Ps2TextureRecord> FontTextureRecords;
    Ps2MemoryDiagnosticsSnapshot LastMemoryDiagnosticsSnapshot;

    int ResolveGsPixelStorageMode(::Ps2TexturePixelStorageMode mode) {
        if (mode == ::Ps2TexturePixelStorageMode::PsmCt32) {
            return GS_PSM_CT32;
        } else if (mode == ::Ps2TexturePixelStorageMode::PsmT8) {
            return GS_PSM_T8;
        } else if (mode == ::Ps2TexturePixelStorageMode::PsmT4) {
            return GS_PSM_T4;
        }

        throw std::runtime_error("Unsupported PS2 texture pixel storage mode.");
    }

    int ResolveClutWidth(::Ps2TextureAsset* data) {
        if (data == nullptr || data->PaletteData == nullptr || data->PaletteData->Length <= 0) {
            return 0;
        }

        int paletteEntryCount = data->PaletteData->Length / 4;
        if (paletteEntryCount <= 16) {
            return 8;
        }

        return 16;
    }

    int ResolveClutHeight(::Ps2TextureAsset* data) {
        if (data == nullptr || data->PaletteData == nullptr || data->PaletteData->Length <= 0) {
            return 0;
        }

        int paletteEntryCount = data->PaletteData->Length / 4;
        if (paletteEntryCount <= 16) {
            return 2;
        }

        return 16;
    }

    std::size_t ResolveTextureRecordCpuBytes(const Ps2TextureRecord& record) {
        return record.CpuPixelBytes + record.CpuClutBytes;
    }

    std::size_t ResolveTextureRecordVramBytes(const Ps2TextureRecord& record) {
        return record.VramPixelBytes + record.VramClutBytes;
    }

    bool ShouldLogPhysicsWarmupUpdateCount(int updateCount) {
        switch (updateCount) {
            case 1:
            case 2:
            case 4:
            case 8:
            case 16:
            case 32:
            case 64:
            case 128:
            case 256:
            case 512:
            case 1024:
                return true;
            default:
                return false;
        }
    }

    Ps2MemoryDiagnosticsSnapshot CaptureMemoryDiagnosticsSnapshot(::Core* engineCore) {
        Ps2MemoryDiagnosticsSnapshot snapshot;

        const struct mallinfo heapInfo = mallinfo();
        snapshot.HeapUsedBytes = heapInfo.uordblks;
        snapshot.HeapFreeBytes = heapInfo.fordblks;
        snapshot.RuntimeTextureCount = TextureRecords.size();
        snapshot.FontTextureCount = FontTextureRecords.size();

        for (const auto& textureEntry : TextureRecords) {
            const Ps2TextureRecord& record = textureEntry.second;
            snapshot.CpuTextureBytes += ResolveTextureRecordCpuBytes(record);
            snapshot.VramTextureBytes += ResolveTextureRecordVramBytes(record);
            if (record.Uploaded) {
                snapshot.UploadedTextureCount += 1;
            }
        }

        for (const auto& fontTextureEntry : FontTextureRecords) {
            const Ps2TextureRecord& record = fontTextureEntry.second;
            snapshot.CpuTextureBytes += ResolveTextureRecordCpuBytes(record);
            snapshot.VramTextureBytes += ResolveTextureRecordVramBytes(record);
            if (record.Uploaded) {
                snapshot.UploadedTextureCount += 1;
            }
        }

        ::SceneManager* sceneManager = engineCore != nullptr ? engineCore->get_SceneManager() : nullptr;
        if (sceneManager != nullptr && sceneManager->get_LoadedScenes() != nullptr) {
            snapshot.LoadedSceneCount = sceneManager->get_LoadedScenes()->get_Count();
        }

        ::ObjectManager* objectManager = engineCore != nullptr ? engineCore->get_ObjectManager() : nullptr;
        if (objectManager != nullptr) {
            snapshot.EntityCount = objectManager->get_Entities() != nullptr ? objectManager->get_Entities()->get_Count() : 0;
            snapshot.Drawable2DCount = objectManager->get_Drawables2D() != nullptr ? objectManager->get_Drawables2D()->get_Count() : 0;
            snapshot.Drawable3DCount = objectManager->get_Drawables3D() != nullptr ? objectManager->get_Drawables3D()->get_Count() : 0;
            snapshot.CameraCount = objectManager->get_Cameras() != nullptr ? objectManager->get_Cameras()->get_Count() : 0;
        }

        return snapshot;
    }

    void HostLogLabeledMemoryDiagnostics(const char* label, ::Core* engineCore) {
        const Ps2MemoryDiagnosticsSnapshot snapshot = CaptureMemoryDiagnosticsSnapshot(engineCore);
        HostLogOnly(std::string("memory trace ") + label + " " + BuildMemoryDiagnosticsLogLine(snapshot, nullptr));
    }

    std::string FormatSignedDelta(long currentValue, long previousValue) {
        const long delta = currentValue - previousValue;
        if (delta > 0) {
            return "+" + std::to_string(delta);
        }

        return std::to_string(delta);
    }

    std::string FormatUnsignedDelta(std::size_t currentValue, std::size_t previousValue) {
        if (currentValue >= previousValue) {
            return "+" + std::to_string(currentValue - previousValue);
        }

        return "-" + std::to_string(previousValue - currentValue);
    }

    std::string BuildMemoryDiagnosticsLogLine(const Ps2MemoryDiagnosticsSnapshot& current, const Ps2MemoryDiagnosticsSnapshot* previous) {
        std::string message =
            "memory diag heapUsed="
            + std::to_string(current.HeapUsedBytes)
            + " heapFree="
            + std::to_string(current.HeapFreeBytes)
            + " rtTex="
            + std::to_string(current.RuntimeTextureCount)
            + " fontTex="
            + std::to_string(current.FontTextureCount)
            + " uploadedTex="
            + std::to_string(current.UploadedTextureCount)
            + " cpuTexBytes="
            + std::to_string(current.CpuTextureBytes)
            + " vramTexBytes="
            + std::to_string(current.VramTextureBytes)
            + " scenes="
            + std::to_string(current.LoadedSceneCount)
            + " ent="
            + std::to_string(current.EntityCount)
            + " d2="
            + std::to_string(current.Drawable2DCount)
            + " d3="
            + std::to_string(current.Drawable3DCount)
            + " cam="
            + std::to_string(current.CameraCount);

        if (previous != nullptr) {
            message +=
                " deltaHeapUsed="
                + FormatSignedDelta(current.HeapUsedBytes, previous->HeapUsedBytes)
                + " deltaHeapFree="
                + FormatSignedDelta(current.HeapFreeBytes, previous->HeapFreeBytes)
                + " deltaCpuTexBytes="
                + FormatUnsignedDelta(current.CpuTextureBytes, previous->CpuTextureBytes)
                + " deltaVramTexBytes="
                + FormatUnsignedDelta(current.VramTextureBytes, previous->VramTextureBytes)
                + " deltaUploadedTex="
                + FormatUnsignedDelta(current.UploadedTextureCount, previous->UploadedTextureCount);
        }

        return message;
    }

    void RecordMemoryDiagnosticsSample(::Core* engineCore, std::clock_t framePresentEndTicks) {
        if (!MemoryDiagnosticsFirstSampleCaptured) {
            LastMemoryDiagnosticsSnapshot = CaptureMemoryDiagnosticsSnapshot(engineCore);
            MemoryDiagnosticsFirstSampleTicks = framePresentEndTicks;
            MemoryDiagnosticsLastSampleTicks = framePresentEndTicks;
            MemoryDiagnosticsFirstSampleCaptured = true;
            MemoryDiagnosticsCapturedSampleCount = 1;
            HostLogOnly(BuildMemoryDiagnosticsLogLine(LastMemoryDiagnosticsSnapshot, nullptr));
            return;
        }

        if (MemoryDiagnosticsCapturedSampleCount >= MemoryDiagnosticsSparseSampleCount) {
            return;
        }

        const double secondsSinceLastSample = ResolveSecondsFromClockTicks(MemoryDiagnosticsLastSampleTicks, framePresentEndTicks);
        if (secondsSinceLastSample < MemoryDiagnosticsSparseSampleDelaySeconds) {
            return;
        }

        const Ps2MemoryDiagnosticsSnapshot currentSnapshot = CaptureMemoryDiagnosticsSnapshot(engineCore);
        HostLogOnly(BuildMemoryDiagnosticsLogLine(currentSnapshot, &LastMemoryDiagnosticsSnapshot));
        LastMemoryDiagnosticsSnapshot = currentSnapshot;
        MemoryDiagnosticsLastSampleTicks = framePresentEndTicks;
        MemoryDiagnosticsCapturedSampleCount += 1;
    }

    ::Ps2TexturePixelStorageMode ResolveLegacyTexturePixelStorageMode(::Ps2TextureFormat format) {
        if (format == ::Ps2TextureFormat::Rgba32) {
            return ::Ps2TexturePixelStorageMode::PsmCt32;
        } else if (format == ::Ps2TextureFormat::Indexed8) {
            return ::Ps2TexturePixelStorageMode::PsmT8;
        } else if (format == ::Ps2TextureFormat::Indexed4) {
            return ::Ps2TexturePixelStorageMode::PsmT4;
        }

        throw std::invalid_argument("Unsupported legacy PS2 texture format.");
    }

    uint64_t ReadPs2RuntimeAssetId(::BinaryReaderLE* reader) {
        if (reader == nullptr) {
            throw std::invalid_argument("PS2 texture asset reader was null.");
        }

        const uint64_t lower = reader->ReadUInt32();
        const uint64_t upper = reader->ReadUInt32();
        return lower | (upper << 32);
    }

    void ValidatePs2TextureAssetHeader(uint8_t endianness, uint8_t version, uint16_t formatId, uint16_t recordKind, uint16_t valueKind) {
        if (endianness != Ps2BinaryEndiannessLittleEndian) {
            throw std::invalid_argument("Unsupported PS2 texture asset binary endianness.");
        }
        if (version < Ps2BinaryHeaderLegacyVersion || version > Ps2BinaryHeaderCurrentVersion) {
            throw std::invalid_argument("Unsupported PS2 texture asset binary version.");
        }
        if (formatId != Ps2BinaryHeaderFormatId) {
            throw std::invalid_argument("Unsupported PS2 texture asset binary format id.");
        }
        if (recordKind != Ps2BinaryRecordKindAsset) {
            throw std::invalid_argument("Unsupported PS2 texture asset record kind.");
        }
        if (valueKind != Ps2BinaryValueKindTextureAsset) {
            throw std::invalid_argument("Unsupported PS2 texture asset value kind.");
        }
    }

    ::Ps2TextureAsset* DeserializePs2TextureAsset(::Stream* stream) {
        if (stream == nullptr) {
            throw std::invalid_argument("PS2 texture asset stream was null.");
        }

        ::BinaryReaderLE* reader = new ::BinaryReaderLE(stream, true);
        [[maybe_unused]] auto readerGuard = he_cpp_make_scope_exit([reader]() {
            if (reader != nullptr) {
                reader->Dispose();
                delete reader;
            }
        });

        if (reader->ReadByte() != static_cast<uint8_t>('H') ||
            reader->ReadByte() != static_cast<uint8_t>('E') ||
            reader->ReadByte() != static_cast<uint8_t>('L') ||
            reader->ReadByte() != static_cast<uint8_t>('E')) {
            throw std::invalid_argument("The PS2 texture asset payload did not start with the HELE header.");
        }

        const uint8_t endianness = reader->ReadByte();
        const uint8_t version = reader->ReadByte();
        const uint16_t formatId = reader->ReadUInt16();
        const uint16_t recordKind = reader->ReadUInt16();
        const uint16_t valueKind = reader->ReadUInt16();
        ValidatePs2TextureAssetHeader(endianness, version, formatId, recordKind, valueKind);

        ::Ps2TextureAsset* asset = new ::Ps2TextureAsset();
        asset->set_Id(reader->ReadString());
        asset->set_RuntimeAssetId(version >= Ps2BinaryRuntimeAssetIdentityVersion ? ReadPs2RuntimeAssetId(reader) : 0ul);
        asset->Width = reader->ReadUInt16();
        asset->Height = reader->ReadUInt16();
        asset->Format = static_cast<::Ps2TextureFormat>(reader->ReadByte());
        if (version >= Ps2BinaryTextureStorageMetadataVersion) {
            asset->PixelStorageMode = static_cast<::Ps2TexturePixelStorageMode>(reader->ReadByte());
            asset->ClutPixelStorageMode = static_cast<::Ps2TexturePixelStorageMode>(reader->ReadByte());
        } else {
            asset->PixelStorageMode = ResolveLegacyTexturePixelStorageMode(asset->Format);
            asset->ClutPixelStorageMode = ::Ps2TexturePixelStorageMode::PsmCt32;
        }

        asset->AlphaMode = static_cast<::Ps2TextureAlphaMode>(reader->ReadByte());
        asset->PixelData = reader->ReadByteArray();
        asset->PaletteData = reader->ReadByteArray();
        return asset;
    }

    void ReleaseOwnedByteArray(::Array<uint8_t>* bytes) {
        if (bytes != nullptr && bytes != ::Array<uint8_t>::Empty()) {
            delete bytes;
        }
    }

    void ReleasePs2TextureAsset(::Ps2TextureAsset* asset) {
        if (asset == nullptr) {
            return;
        }

        ReleaseOwnedByteArray(asset->PixelData);
        asset->PixelData = nullptr;
        ReleaseOwnedByteArray(asset->PaletteData);
        asset->PaletteData = nullptr;
        delete asset;
    }

    bool PopulateTextureRecordFromPs2TextureAsset(::Ps2TextureAsset* data, Ps2TextureRecord& record) {
        if (data == nullptr || data->PixelData == nullptr || data->PixelData->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
            return false;
        }

        record.Texture.Width = data->Width;
        record.Texture.Height = data->Height;
        record.Texture.PSM = ResolveGsPixelStorageMode(data->PixelStorageMode);
        record.Texture.ClutPSM = ResolveGsPixelStorageMode(data->ClutPixelStorageMode);
        record.Texture.Clut = nullptr;
        record.Texture.VramClut = 0;
        record.Texture.Filter = GS_FILTER_NEAREST;
        record.Texture.ClutStorageMode = GS_CLUT_STORAGE_CSM1;
        record.Texture.Mem = static_cast<u32*>(memalign(128, static_cast<size_t>(data->PixelData->Length)));
        if (record.Texture.Mem == nullptr) {
            return false;
        }

        std::memcpy(record.Texture.Mem, data->PixelData->Data, static_cast<size_t>(data->PixelData->Length));
        record.Pixels = record.Texture.Mem;
        record.CpuPixelBytes = static_cast<std::size_t>(data->PixelData->Length);
        if (data->PaletteData != nullptr && data->PaletteData->Length > 0) {
            record.Texture.Clut = static_cast<u32*>(memalign(128, static_cast<size_t>(data->PaletteData->Length)));
            if (record.Texture.Clut == nullptr) {
                return false;
            }

            std::memcpy(record.Texture.Clut, data->PaletteData->Data, static_cast<size_t>(data->PaletteData->Length));
            record.ClutWidth = ResolveClutWidth(data);
            record.ClutHeight = ResolveClutHeight(data);
            record.CpuClutBytes = static_cast<std::size_t>(data->PaletteData->Length);
        }

        return true;
    }

    Ps2TextureRecord* ResolveFontTextureRecord(::FontAsset* font) {
        if (font == nullptr) {
            return nullptr;
        }

        ::RuntimeTexture* runtimeTexture = font->get_Texture();
        if (runtimeTexture != nullptr) {
            auto textureIt = TextureRecords.find(runtimeTexture);
            if (textureIt != TextureRecords.end()) {
                return &textureIt->second;
            }
        }

        const std::string cookedAtlasTextureRelativePath = font->get_CookedAtlasTextureRelativePath();
        if (cookedAtlasTextureRelativePath.empty()) {
            return nullptr;
        }

        auto fontTextureIt = FontTextureRecords.find(font);
        if (fontTextureIt != FontTextureRecords.end()) {
            return &fontTextureIt->second;
        }

        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ContentManager() == nullptr) {
            return nullptr;
        }

        ::Stream* stream = core->get_ContentManager()->get_ContentStreamSource()->OpenRead(cookedAtlasTextureRelativePath);
        [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
            if (stream != nullptr) {
                stream->Dispose();
                delete stream;
            }
        });
        ::Ps2TextureAsset* textureAsset = DeserializePs2TextureAsset(stream);
        [[maybe_unused]] auto textureAssetGuard = he_cpp_make_scope_exit([textureAsset]() {
            ReleasePs2TextureAsset(textureAsset);
        });
        Ps2TextureRecord record;
        if (!PopulateTextureRecordFromPs2TextureAsset(textureAsset, record)) {
            return nullptr;
        }

        auto insertedRecord = FontTextureRecords.emplace(font, record);
        return &insertedRecord.first->second;
    }

    void ReleaseTextureRecord(Ps2TextureRecord& record) {
        if (record.Texture.Clut != nullptr) {
            free(record.Texture.Clut);
            record.Texture.Clut = nullptr;
        }

        if (record.Texture.Mem != nullptr) {
            free(record.Texture.Mem);
            record.Texture.Mem = nullptr;
        }

        record.Texture.Vram = 0;
        record.Texture.VramClut = 0;
        record.Pixels = nullptr;
        record.ClutWidth = 0;
        record.ClutHeight = 0;
        record.Uploaded = false;
        record.CpuPixelBytes = 0;
        record.CpuClutBytes = 0;
        record.VramPixelBytes = 0;
        record.VramClutBytes = 0;
    }

    void InvalidateUploadedTextureRecords() {
        for (auto& textureEntry : TextureRecords) {
            Ps2TextureRecord& record = textureEntry.second;
            record.Texture.Vram = 0;
            record.Texture.VramClut = 0;
            record.Uploaded = false;
            record.VramPixelBytes = 0;
            record.VramClutBytes = 0;
        }

        for (auto& fontTextureEntry : FontTextureRecords) {
            Ps2TextureRecord& record = fontTextureEntry.second;
            record.Texture.Vram = 0;
            record.Texture.VramClut = 0;
            record.Uploaded = false;
            record.VramPixelBytes = 0;
            record.VramClutBytes = 0;
        }
    }

    void UploadVuOpaqueMicroProgram() {
        const u32 untexturedPacketSize = packet2_utils_get_packet_size_for_program(&Ps2OpaqueDraw3D_CodeStart, &Ps2OpaqueDraw3D_CodeEnd) + 1;
        const u32 texturedPacketSize = packet2_utils_get_packet_size_for_program(&Ps2OpaqueTexturedDraw3D_CodeStart, &Ps2OpaqueTexturedDraw3D_CodeEnd) + 1;
        packet2_t* packet2 = packet2_create(untexturedPacketSize + texturedPacketSize, P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
        packet2_vif_add_micro_program(packet2, 0, &Ps2OpaqueDraw3D_CodeStart, &Ps2OpaqueDraw3D_CodeEnd);
        packet2_vif_add_micro_program(packet2, 64, &Ps2OpaqueTexturedDraw3D_CodeStart, &Ps2OpaqueTexturedDraw3D_CodeEnd);
        packet2_utils_vu_add_end_tag(packet2);
        dma_channel_send_packet2(packet2, DMA_CHANNEL_VIF1, 1);
        dma_channel_wait(DMA_CHANNEL_VIF1, 0);
        packet2_free(packet2);
    }

    void InitializeVuOpaqueDoubleBuffer() {
        packet2_t* packet2 = packet2_create(1, P2_TYPE_NORMAL, P2_MODE_CHAIN, 1);
        packet2_utils_vu_add_double_buffer(packet2, 8, 496);
        packet2_utils_vu_add_end_tag(packet2);
        dma_channel_send_packet2(packet2, DMA_CHANNEL_VIF1, 1);
        dma_channel_wait(DMA_CHANNEL_VIF1, 0);
        packet2_free(packet2);
    }

    ::RuntimeSceneCatalog* BuildRuntimeSceneCatalogFromManifest() {
        std::size_t count = 0;
        const HERuntimeSceneCatalogEntry* manifestEntries = he_runtime_scene_catalog_entries(&count);
        if (manifestEntries == nullptr || count == 0) {
            return nullptr;
        }

        Array<::RuntimeSceneCatalogEntry*>* entries = new Array<::RuntimeSceneCatalogEntry*>(static_cast<int32_t>(count));
        for (int32_t index = 0; index < static_cast<int32_t>(count); index++) {
            const HERuntimeSceneCatalogEntry& manifestEntry = manifestEntries[index];
            (*entries)[index] = new ::RuntimeSceneCatalogEntry(manifestEntry.SceneId, manifestEntry.CookedRelativePath);
        }

        return new ::RuntimeSceneCatalog(entries);
    }

    ::StandardPlatformInputConfiguration* BuildStandardPlatformInputConfigurationFromManifest() {
        std::size_t count = 0;
        const HERuntimeStandardPlatformActionEntry* manifestEntries = he_runtime_standard_platform_action_entries(&count);
        if (manifestEntries == nullptr || count == 0) {
            return ::StandardPlatformInputConfiguration::get_Empty();
        }

        List<::StandardPlatformActionBinding*>* bindings = new List<::StandardPlatformActionBinding*>(static_cast<int32_t>(count));
        for (int32_t index = 0; index < static_cast<int32_t>(count); index++) {
            const HERuntimeStandardPlatformActionEntry& manifestEntry = manifestEntries[index];
            bindings->Add(new ::StandardPlatformActionBinding(
                static_cast<::StandardPlatformAction>(manifestEntry.ActionId),
                ::InputControlId(
                    static_cast<::InputDeviceKind>(manifestEntry.DeviceKind),
                    static_cast<::InputControlKind>(manifestEntry.ControlKind),
                    manifestEntry.DeviceIndex,
                    manifestEntry.ControlIndex)));
        }

        return new ::StandardPlatformInputConfiguration(bindings);
    }

    std::string ResolveStartupSceneIdFromManifest() {
        const char* startupScenePhysicalPath = he_get_runtime_ps2_startup_scene_path();
        if (startupScenePhysicalPath == nullptr || startupScenePhysicalPath[0] == '\0') {
            throw std::runtime_error("The packaged PS2 export did not publish one startup scene path.");
        }

        std::size_t count = 0;
        const HERuntimeSceneCatalogEntry* manifestEntries = he_runtime_scene_catalog_entries(&count);
        if (manifestEntries == nullptr || count == 0) {
            throw std::runtime_error("The packaged PS2 export did not publish one runtime scene catalog.");
        }

        for (std::size_t index = 0; index < count; index++) {
            const HERuntimeSceneCatalogEntry& manifestEntry = manifestEntries[index];
            if (manifestEntry.SceneId == nullptr || manifestEntry.CookedRelativePath == nullptr) {
                continue;
            }

            if (std::strcmp(manifestEntry.CookedRelativePath, startupScenePhysicalPath) == 0) {
                return manifestEntry.SceneId;
            }
        }

        throw std::runtime_error("The startup scene path was not found in the runtime scene catalog manifest.");
    }

    bool EnsureTextureUploaded(Ps2TextureRecord& record) {
        if (record.Uploaded) {
            return true;
        }

        record.VramPixelBytes = static_cast<std::size_t>(gsKit_texture_size(record.Texture.Width, record.Texture.Height, record.Texture.PSM));
        record.Texture.Vram = gsKit_vram_alloc(
            ActiveGsGlobal,
            static_cast<int>(record.VramPixelBytes),
            GSKIT_ALLOC_USERBUFFER);
        if (record.Texture.Vram == GSKIT_ALLOC_ERROR) {
            record.VramPixelBytes = 0;
            BootLog(
                std::string("render2d upload failed texture vram width=")
                + std::to_string(record.Texture.Width)
                + " height="
                + std::to_string(record.Texture.Height)
                + " psm="
                + std::to_string(record.Texture.PSM));
            return false;
        }
        if (record.Texture.Clut != nullptr) {
            record.VramClutBytes = static_cast<std::size_t>(gsKit_texture_size(record.ClutWidth, record.ClutHeight, GS_PSM_CT32));
            record.Texture.VramClut = gsKit_vram_alloc(
                ActiveGsGlobal,
                static_cast<int>(record.VramClutBytes),
                GSKIT_ALLOC_USERBUFFER);
            if (record.Texture.VramClut == GSKIT_ALLOC_ERROR) {
                record.VramClutBytes = 0;
                BootLog(
                    std::string("render2d upload failed clut vram width=")
                    + std::to_string(record.ClutWidth)
                    + " height="
                    + std::to_string(record.ClutHeight)
                    + " textureWidth="
                    + std::to_string(record.Texture.Width)
                    + " textureHeight="
                    + std::to_string(record.Texture.Height));
                return false;
            }
        } else {
            record.VramClutBytes = 0;
        }

        gsKit_texture_upload(ActiveGsGlobal, &record.Texture);
        record.Uploaded = true;
        return true;
    }

    u64 ResolveSpriteRgba(const ::byte4& color) {
        const u8 alpha = static_cast<u8>(std::min(static_cast<int>(color.W), 0x80));
        return GS_SETREG_RGBAQ(color.X, color.Y, color.Z, alpha, 0x00);
    }

    u8 ResolveTexturedSpriteColorComponent(u8 component) {
        const double normalizedComponent = static_cast<double>(component) / 255.0;
        const double ps2Component = normalizedComponent * 128.0;
        return static_cast<u8>(std::clamp(std::lround(ps2Component), 0l, 128l));
    }

    u64 ResolveTexturedSpriteRgba(const ::byte4& color) {
        const u8 red = ResolveTexturedSpriteColorComponent(color.X);
        const u8 green = ResolveTexturedSpriteColorComponent(color.Y);
        const u8 blue = ResolveTexturedSpriteColorComponent(color.Z);
        const u8 alpha = ResolveTexturedSpriteColorComponent(color.W);
        return GS_SETREG_RGBAQ(red, green, blue, alpha, 0x00);
    }

    bool TryClipQuadToRect(const ::float4& clipRect, ::float4& bounds, ::float4& sourceRect) {
        const double boundsLeft = bounds.X;
        const double boundsTop = bounds.Y;
        const double boundsRight = static_cast<double>(bounds.X) + static_cast<double>(bounds.Z);
        const double boundsBottom = static_cast<double>(bounds.Y) + static_cast<double>(bounds.W);
        const double clipLeft = clipRect.X;
        const double clipTop = clipRect.Y;
        const double clipRight = static_cast<double>(clipRect.X) + static_cast<double>(clipRect.Z);
        const double clipBottom = static_cast<double>(clipRect.Y) + static_cast<double>(clipRect.W);
        const double clippedLeft = std::max(boundsLeft, clipLeft);
        const double clippedTop = std::max(boundsTop, clipTop);
        const double clippedRight = std::min(boundsRight, clipRight);
        const double clippedBottom = std::min(boundsBottom, clipBottom);
        const double boundsWidth = boundsRight - boundsLeft;
        const double boundsHeight = boundsBottom - boundsTop;
        if (boundsWidth <= 0.0 || boundsHeight <= 0.0 || clippedRight <= clippedLeft || clippedBottom <= clippedTop) {
            return false;
        }

        const double leftRatio = (clippedLeft - boundsLeft) / boundsWidth;
        const double topRatio = (clippedTop - boundsTop) / boundsHeight;
        const double rightRatio = (clippedRight - boundsLeft) / boundsWidth;
        const double bottomRatio = (clippedBottom - boundsTop) / boundsHeight;
        const double sourceLeft = static_cast<double>(sourceRect.X) + (static_cast<double>(sourceRect.Z) * leftRatio);
        const double sourceTop = static_cast<double>(sourceRect.Y) + (static_cast<double>(sourceRect.W) * topRatio);
        const double sourceRight = static_cast<double>(sourceRect.X) + (static_cast<double>(sourceRect.Z) * rightRatio);
        const double sourceBottom = static_cast<double>(sourceRect.Y) + (static_cast<double>(sourceRect.W) * bottomRatio);
        bounds = ::float4(
            static_cast<float>(clippedLeft),
            static_cast<float>(clippedTop),
            static_cast<float>(clippedRight - clippedLeft),
            static_cast<float>(clippedBottom - clippedTop));
        sourceRect = ::float4(
            static_cast<float>(sourceLeft),
            static_cast<float>(sourceTop),
            static_cast<float>(sourceRight - sourceLeft),
            static_cast<float>(sourceBottom - sourceTop));
        return true;
    }

    class Ps2RenderManager2D final : public RenderManager2D {
    public:
        Ps2RenderManager2D()
            : CommandListBuilder(new ::RenderCommandListBuilder2D()) {
        }

        ~Ps2RenderManager2D() override {
            delete CommandListBuilder;
        }

        RuntimeTexture* BuildTextureFromRaw(TextureAsset* data) override {
            BootLog(std::string("render2d build raw texture begin id=") + (data != nullptr ? data->get_Id() : std::string("null")));
            RuntimeTexture* texture = new RuntimeTexture();
            if (data != nullptr) {
                texture->set_Id(data->get_Id());
                texture->set_Width(static_cast<int32_t>(data->Width));
                texture->set_Height(static_cast<int32_t>(data->Height));
                if (data->Colors != nullptr && data->Colors->Length > 0) {
                    Ps2TextureRecord record;
                    record.Texture.Width = data->Width;
                    record.Texture.Height = data->Height;
                    record.Texture.PSM = GS_PSM_CT32;
                    record.Texture.Clut = nullptr;
                    record.Texture.VramClut = 0;
                    record.Texture.Filter = GS_FILTER_NEAREST;
                    record.Texture.Mem = static_cast<u32*>(memalign(128, static_cast<size_t>(data->Colors->Length)));
                    if (record.Texture.Mem != nullptr) {
                        std::memcpy(record.Texture.Mem, data->Colors->Data, static_cast<size_t>(data->Colors->Length));
                        record.Pixels = record.Texture.Mem;
                        record.CpuPixelBytes = static_cast<std::size_t>(data->Colors->Length);
                        TextureRecords.emplace(texture, record);
                    }
                }
            }

            BootLog(std::string("render2d build raw texture end id=") + (texture != nullptr ? texture->get_Id() : std::string("null")));
            return texture;
        }

        RuntimeTexture* BuildTextureFromCooked(std::string cookedAssetPath, IContentStreamSource* contentStreamSource) override {
            (void)contentStreamSource;
            if (cookedAssetPath.empty()) {
                throw std::invalid_argument("PS2 cooked texture path is required.");
            }

            BootLog(std::string("render2d build cooked texture begin path=") + cookedAssetPath);
            const std::string resolvedCookedAssetPath = ResolvePs2CookedAssetOpenPath(cookedAssetPath);
            if (resolvedCookedAssetPath != cookedAssetPath) {
                BootLog(
                    std::string("render2d build cooked texture resolved path=")
                    + resolvedCookedAssetPath
                    + " logical="
                    + cookedAssetPath);
            }
            const std::clock_t totalStartTicks = std::clock();
            ::Array<uint8_t>* bufferedBytes = nullptr;
            [[maybe_unused]] auto bufferedBytesGuard = he_cpp_make_scope_exit([&bufferedBytes]() {
                delete bufferedBytes;
            });
            std::clock_t deserializeStartTicks = 0;
            if (IsPs2DiscRuntimePath(resolvedCookedAssetPath)) {
                double openMilliseconds = 0.0;
                double readMilliseconds = 0.0;
                bufferedBytes = ReadPs2DiscFileBytes(resolvedCookedAssetPath, openMilliseconds, readMilliseconds);
                CookedTextureOpenMilliseconds += openMilliseconds;
                BootLog(
                    std::string("render2d build cooked texture open ms=")
                    + std::to_string(openMilliseconds)
                    + "ms path="
                    + resolvedCookedAssetPath);
                CookedTextureBufferedReadMilliseconds += readMilliseconds;
                BootLog(
                    std::string("render2d build cooked texture buffered read ms=")
                    + std::to_string(readMilliseconds)
                    + "ms path="
                    + resolvedCookedAssetPath);
                deserializeStartTicks = std::clock();
            } else {
                const std::clock_t openStartTicks = totalStartTicks;
                ::FileStream* stream = ::File::OpenRead(resolvedCookedAssetPath);
                [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
                    if (stream != nullptr) {
                        stream->Dispose();
                    }
                });
                const std::clock_t openEndTicks = std::clock();
                CookedTextureOpenMilliseconds += ResolveMillisecondsFromClockTicks(openStartTicks, openEndTicks);
                BootLog(
                    std::string("render2d build cooked texture open ms=")
                    + FormatMillisecondsFromClockTicks(openStartTicks, openEndTicks)
                    + " path="
                    + resolvedCookedAssetPath);
                ::Stream* readStream = stream;
                const std::clock_t bufferedReadStartTicks = openEndTicks;
                const std::size_t streamLength = readStream->Seek(0, ::SeekOrigin::End);
                readStream->Seek(0, ::SeekOrigin::Begin);
                if (streamLength > static_cast<std::size_t>(std::numeric_limits<int32_t>::max())) {
                    throw std::invalid_argument("PS2 cooked texture payload exceeds buffered runtime limits.");
                }

                bufferedBytes = new ::Array<uint8_t>(static_cast<int32_t>(streamLength));
                std::size_t bufferedOffset = 0;
                while (bufferedOffset < streamLength) {
                    const std::size_t bytesRead = readStream->Read(
                        bufferedBytes,
                        bufferedOffset,
                        streamLength - bufferedOffset);
                    if (bytesRead == 0) {
                        throw std::invalid_argument("PS2 cooked texture payload read ended before the full asset was buffered.");
                    }

                    bufferedOffset += bytesRead;
                }

                const std::clock_t bufferedReadEndTicks = std::clock();
                CookedTextureBufferedReadMilliseconds += ResolveMillisecondsFromClockTicks(bufferedReadStartTicks, bufferedReadEndTicks);
                BootLog(
                    std::string("render2d build cooked texture buffered read ms=")
                    + FormatMillisecondsFromClockTicks(bufferedReadStartTicks, bufferedReadEndTicks)
                    + " path="
                    + resolvedCookedAssetPath);
                deserializeStartTicks = bufferedReadEndTicks;
            }
            ::MemoryStream* bufferedStream = new ::MemoryStream(bufferedBytes, false);
            [[maybe_unused]] auto bufferedStreamGuard = he_cpp_make_scope_exit([bufferedStream]() {
                if (bufferedStream != nullptr) {
                    bufferedStream->Dispose();
                    delete bufferedStream;
                }
            });
            ::Ps2TextureAsset* textureAsset = DeserializePs2TextureAsset(bufferedStream);
            [[maybe_unused]] auto textureAssetGuard = he_cpp_make_scope_exit([textureAsset]() {
                ReleasePs2TextureAsset(textureAsset);
            });
            const std::clock_t deserializeEndTicks = std::clock();
            CookedTextureDeserializeMilliseconds += ResolveMillisecondsFromClockTicks(deserializeStartTicks, deserializeEndTicks);
            BootLog(
                std::string("render2d build cooked texture deserialize ms=")
                + FormatMillisecondsFromClockTicks(deserializeStartTicks, deserializeEndTicks)
                + " path="
                + resolvedCookedAssetPath);
            const std::clock_t populateStartTicks = deserializeEndTicks;
            Ps2TextureRecord record;
            if (!PopulateTextureRecordFromPs2TextureAsset(textureAsset, record)) {
                throw std::invalid_argument("PS2 cooked texture payload did not deserialize as a usable Ps2TextureAsset.");
            }
            const std::clock_t populateEndTicks = std::clock();
            CookedTexturePopulateMilliseconds += ResolveMillisecondsFromClockTicks(populateStartTicks, populateEndTicks);
            BootLog(
                std::string("render2d build cooked texture populate ms=")
                + FormatMillisecondsFromClockTicks(populateStartTicks, populateEndTicks)
                + " path="
                + resolvedCookedAssetPath);

            const std::clock_t runtimeTextureCreateStartTicks = populateEndTicks;
            RuntimeTexture* runtimeTexture = new RuntimeTexture();
            runtimeTexture->set_Id(textureAsset != nullptr ? textureAsset->get_Id() : std::string());
            runtimeTexture->set_Width(record.Texture.Width);
            runtimeTexture->set_Height(record.Texture.Height);
            TextureRecords.emplace(runtimeTexture, record);
            const std::clock_t runtimeTextureCreateEndTicks = std::clock();
            CookedTextureRuntimeTextureCreateMilliseconds += ResolveMillisecondsFromClockTicks(runtimeTextureCreateStartTicks, runtimeTextureCreateEndTicks);
            BootLog(
                std::string("render2d build cooked texture runtime texture create ms=")
                + FormatMillisecondsFromClockTicks(runtimeTextureCreateStartTicks, runtimeTextureCreateEndTicks)
                + " path="
                + resolvedCookedAssetPath);
            BootLog(
                std::string("render2d build cooked texture total ms=")
                + FormatMillisecondsFromClockTicks(totalStartTicks, runtimeTextureCreateEndTicks)
                + " path="
                + resolvedCookedAssetPath);
            BootLog(std::string("render2d build cooked texture end path=") + resolvedCookedAssetPath);
            return runtimeTexture;
        }

        void ReleaseTexture(RuntimeTexture* texture) override {
            if (texture == nullptr) {
                throw std::invalid_argument("PS2 runtime texture release requires one texture.");
            }

            auto textureIt = TextureRecords.find(texture);
            if (textureIt != TextureRecords.end()) {
                ReleaseTextureRecord(textureIt->second);
                TextureRecords.erase(textureIt);
            }

            RenderManager2D::ReleaseTexture(texture);
        }

        void ReleaseFont(FontAsset* font) override {
            if (font == nullptr) {
                throw std::invalid_argument("PS2 font release requires one font.");
            }

            auto fontTextureIt = FontTextureRecords.find(font);
            if (fontTextureIt != FontTextureRecords.end()) {
                ReleaseTextureRecord(fontTextureIt->second);
                FontTextureRecords.erase(fontTextureIt);
            }

            RenderManager2D::ReleaseFont(font);
        }

        void FlushReleasedTextures() override {
            if (ActiveGsGlobal == 0) {
                return;
            }

            gsKit_vram_clear(ActiveGsGlobal);
            InvalidateUploadedTextureRecords();
        }

        void Draw() override {
            if (ActiveGsGlobal == 0) {
                return;
            }

            ::Core* core = ::Core::get_Instance();
            if (core == nullptr || core->get_ObjectManager() == nullptr) {
                return;
            }

            List<::ICamera*>* cameras = core->get_ObjectManager()->get_Cameras();
            if (cameras == nullptr) {
                return;
            }

            std::vector<::float4> clipStack;
            for (int32_t cameraIndex = 0; cameraIndex < cameras->get_Count(); cameraIndex++) {
                ::ICamera* camera = (*cameras)[cameraIndex];
                if (camera == nullptr || camera->get_RenderQueue2D() == nullptr) {
                    continue;
                }

                ::RenderCommandList2D* commandList = CommandListBuilder->Build(camera->get_RenderQueue2D());
                if (commandList == nullptr) {
                    continue;
                }

                if (!LoggedFirst2dCommandTrace) {
                    BootLog(
                        std::string("draw2d command list begin count=")
                        + std::to_string(commandList->get_Count())
                        + " cameraIndex="
                        + std::to_string(cameraIndex)
                        + " sceneId="
                        + (Pending2dCommandTraceSceneId.empty() ? std::string("unknown") : Pending2dCommandTraceSceneId));
                    const int32_t commandTraceCount = std::min(commandList->get_Count(), static_cast<int32_t>(16));
                    for (int32_t commandIndex = 0; commandIndex < commandTraceCount; commandIndex++) {
                        const ::RenderCommand2DType tracedCommandType = commandList->GetCommandType(commandIndex);
                        std::string commandTrace = std::string("draw2d command ")
                            + std::to_string(commandIndex)
                            + " type="
                            + ResolveRenderCommand2DTypeName(tracedCommandType);
                        if (tracedCommandType == ::RenderCommand2DType::ClipPush) {
                            commandTrace += " clip=" + FormatFloat4(commandList->GetClipPushRect(commandList->GetClipPushPayloadIndex(commandIndex)));
                        } else if (tracedCommandType == ::RenderCommand2DType::TexturedQuad) {
                            const int32_t payloadIndex = commandList->GetTexturedQuadPayloadIndex(commandIndex);
                            commandTrace += " bounds=" + FormatFloat4(commandList->GetTexturedQuadBounds(payloadIndex));
                            commandTrace += " src=" + FormatFloat4(commandList->GetTexturedQuadSourceRect(payloadIndex));
                            commandTrace += " color=" + FormatByte4(commandList->GetTexturedQuadColor(payloadIndex));
                            ::RuntimeTexture* tracedTexture = commandList->GetTexturedQuadTexture(payloadIndex);
                            commandTrace += " texId=" + (tracedTexture != nullptr ? tracedTexture->get_Id() : std::string("null"));
                        } else if (tracedCommandType == ::RenderCommand2DType::GlyphQuad) {
                            const int32_t payloadIndex = commandList->GetGlyphQuadPayloadIndex(commandIndex);
                            commandTrace += " bounds=" + FormatFloat4(commandList->GetGlyphQuadBounds(payloadIndex));
                            commandTrace += " src=" + FormatFloat4(commandList->GetGlyphQuadSourceRect(payloadIndex));
                            commandTrace += " color=" + FormatByte4(commandList->GetGlyphQuadColor(payloadIndex));
                            ::RuntimeTexture* tracedTexture = commandList->GetGlyphQuadTexture(payloadIndex);
                            commandTrace += " texId=" + (tracedTexture != nullptr ? tracedTexture->get_Id() : std::string("null"));
                        } else if (tracedCommandType == ::RenderCommand2DType::RoundedRect) {
                            const int32_t payloadIndex = commandList->GetRoundedRectPayloadIndex(commandIndex);
                            commandTrace += " bounds=" + FormatFloat4(commandList->GetRoundedRectBounds(payloadIndex));
                        }

                        BootLog(commandTrace);
                    }
                    LoggedFirst2dCommandTrace = true;
                    Pending2dCommandTraceSceneId.clear();
                }

                clipStack.clear();
                for (int32_t commandIndex = 0; commandIndex < commandList->get_Count(); commandIndex++) {
                    const ::RenderCommand2DType commandType = commandList->GetCommandType(commandIndex);
                    if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                        ActiveDraw2dDiagnosticCommandIndex = commandIndex;
                        BootLog(
                            std::string("draw2d exec begin index=")
                            + std::to_string(commandIndex)
                            + " type="
                            + ResolveRenderCommand2DTypeName(commandType)
                            + " clipDepth="
                            + std::to_string(clipStack.size()));
                    }

                    if (commandType == ::RenderCommand2DType::ClipPush) {
                        clipStack.push_back(commandList->GetClipPushRect(commandList->GetClipPushPayloadIndex(commandIndex)));
                    } else if (commandType == ::RenderCommand2DType::ClipPop) {
                        if (!clipStack.empty()) {
                            clipStack.pop_back();
                        }
                    } else if (commandType == ::RenderCommand2DType::TexturedQuad) {
                        const int32_t payloadIndex = commandList->GetTexturedQuadPayloadIndex(commandIndex);
                        DrawTexturedQuad(
                            commandList->GetTexturedQuadTexture(payloadIndex),
                            commandList->GetTexturedQuadBounds(payloadIndex),
                            commandList->GetTexturedQuadSourceRect(payloadIndex),
                            commandList->GetTexturedQuadColor(payloadIndex),
                            clipStack.empty() ? nullptr : &clipStack.back());
                    } else if (commandType == ::RenderCommand2DType::GlyphQuad) {
                        if (EnableGlyphQuadDrawSkipDiagnostics) {
                            continue;
                        }
                        const int32_t payloadIndex = commandList->GetGlyphQuadPayloadIndex(commandIndex);
                        DrawTexturedQuad(
                            commandList->GetGlyphQuadTexture(payloadIndex),
                            commandList->GetGlyphQuadBounds(payloadIndex),
                            commandList->GetGlyphQuadSourceRect(payloadIndex),
                            commandList->GetGlyphQuadColor(payloadIndex),
                            clipStack.empty() ? nullptr : &clipStack.back());
                    } else if (commandType == ::RenderCommand2DType::RoundedRect) {
                        if (EnableRoundedRectDrawSkipDiagnostics) {
                            continue;
                        }
                        const int32_t payloadIndex = commandList->GetRoundedRectPayloadIndex(commandIndex);
                        DrawSolidQuad(
                            commandList->GetRoundedRectBounds(payloadIndex),
                            commandList->GetRoundedRectFillColor(payloadIndex),
                            clipStack.empty() ? nullptr : &clipStack.back());
                    }

                    if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                        BootLog(
                            std::string("draw2d exec end index=")
                            + std::to_string(commandIndex)
                            + " type="
                            + ResolveRenderCommand2DTypeName(commandType)
                            + " clipDepth="
                            + std::to_string(clipStack.size()));
                        ActiveDraw2dDiagnosticCommandIndex = -1;
                    }
                }
            }

            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                Draw2dExecutionDiagnosticsCompleted = true;
            }
        }

        void DrawSprite(ISpriteDrawable2D* sprite) override {
            if (ActiveGsGlobal == 0 || sprite == nullptr || sprite->get_Texture() == nullptr || sprite->get_Parent() == nullptr) {
                return;
            }

            const ::int2 size = sprite->get_Size();
            const float width = size.X > 0 ? static_cast<float>(size.X) : static_cast<float>(sprite->get_Texture()->get_Width());
            const float height = size.Y > 0 ? static_cast<float>(size.Y) : static_cast<float>(sprite->get_Texture()->get_Height());
            const ::float3 position = sprite->get_Parent()->get_Position();
            DrawTexturedQuad(
                sprite->get_Texture(),
                ::float4(position.X, position.Y, width, height),
                sprite->get_SourceRect(),
                sprite->get_Color(),
                nullptr);
        }

        void DrawText(ITextDrawable2D* text) override {
            if (ActiveGsGlobal == 0 || text == nullptr || text->get_Parent() == nullptr) {
                return;
            }

            ::FontAsset* font = text->get_Font();
            if (font == nullptr || font->get_FontInfo() == nullptr || font->get_Texture() == nullptr) {
                return;
            }

            std::string content = text->get_Text();
            const double fontScale = std::max(static_cast<double>(text->get_FontScale()), 0.0001);
            if (text->get_WrapText()) {
                const double wrappedWidth = static_cast<double>(text->get_Size().X) / fontScale;
                content = TextLayoutUtils::WrapText(content, font, std::max(static_cast<int32_t>(1), static_cast<int32_t>(std::round(wrappedWidth))));
            }

            const double lineHeight = std::max(static_cast<double>(font->get_LineHeight()) * fontScale, 1.0);
            const double baseX = std::round(text->get_Parent()->get_Position().X);
            const double baseY = std::round(text->get_Parent()->get_Position().Y);
            double offsetX = 0.0;
            double offsetY = 0.0;

            for (int32_t index = 0; index < static_cast<int32_t>(content.size()); index++) {
                const char character = content[static_cast<std::size_t>(index)];
                if (character == '\n') {
                    offsetY += lineHeight;
                    offsetX = 0.0;
                    continue;
                }

                if (character == '\r') {
                    continue;
                }

                if (character == ' ') {
                    offsetX += static_cast<double>(font->get_FontInfo()->get_SpaceWidth()) * fontScale;
                    continue;
                }

                ::FontChar glyph;
                if (font->get_Characters() == nullptr || !font->get_Characters()->TryGetValue(character, glyph)) {
                    continue;
                }

                const double glyphWidth = static_cast<double>(glyph.SourceRect.Z) * static_cast<double>(font->get_AtlasWidth()) * fontScale;
                const double glyphHeight = static_cast<double>(glyph.SourceRect.W) * static_cast<double>(font->get_AtlasHeight()) * fontScale;
                const double drawY = baseY + std::round(offsetY) + (static_cast<double>(glyph.OffsetY) * fontScale);
                DrawTexturedQuad(
                    font->get_Texture(),
                    ::float4(static_cast<float>(baseX + offsetX), static_cast<float>(drawY), static_cast<float>(glyphWidth), static_cast<float>(glyphHeight)),
                    glyph.SourceRect,
                    text->get_Color(),
                    nullptr);

                const double advanceWidth = glyph.AdvanceWidth > 0.0f
                    ? static_cast<double>(glyph.AdvanceWidth) * fontScale
                    : glyphWidth;
                offsetX += advanceWidth;
            }
        }

        void DrawRoundedRect(IRoundedRectDrawable2D* shape) override {
            if (ActiveGsGlobal == 0 || shape == nullptr || shape->get_Parent() == nullptr) {
                return;
            }

            const ::float3 position = shape->get_Parent()->get_Position();
            const ::int2 size = shape->get_Size();
            DrawSolidQuad(::float4(position.X, position.Y, static_cast<float>(size.X), static_cast<float>(size.Y)), shape->get_FillColor(), nullptr);
        }

    private:
        ::RenderCommandListBuilder2D* CommandListBuilder;

        void DrawSolidQuad(::float4 bounds, const ::byte4& color, const ::float4* clipRect) {
            if (ActiveGsGlobal == 0) {
                return;
            }

            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                BootLog(
                    std::string("draw2d solid begin index=")
                    + std::to_string(ActiveDraw2dDiagnosticCommandIndex)
                    + " bounds="
                    + FormatFloat4(bounds));
            }

            if (clipRect != nullptr) {
                ::float4 sourceRect(0.0f, 0.0f, 1.0f, 1.0f);
                if (!TryClipQuadToRect(*clipRect, bounds, sourceRect)) {
                    return;
                }
            }

            const u64 rgba = ResolveSpriteRgba(color);
            gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
            gsKit_prim_sprite(
                ActiveGsGlobal,
                bounds.X,
                bounds.Y,
                bounds.X + bounds.Z,
                bounds.Y + bounds.W,
                0,
                rgba);

            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                BootLog(
                    std::string("draw2d solid end index=")
                    + std::to_string(ActiveDraw2dDiagnosticCommandIndex)
                    + " bounds="
                    + FormatFloat4(bounds));
            }
        }

        void DrawTexturedQuad(::RuntimeTexture* runtimeTexture, ::float4 bounds, ::float4 sourceRect, const ::byte4& color, const ::float4* clipRect) {
            if (ActiveGsGlobal == 0 || runtimeTexture == nullptr) {
                return;
            }

            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                BootLog(
                    std::string("draw2d textured begin index=")
                    + std::to_string(ActiveDraw2dDiagnosticCommandIndex)
                    + " texture="
                    + runtimeTexture->get_Id()
                    + " bounds="
                    + FormatFloat4(bounds));
            }

            auto textureIt = TextureRecords.find(runtimeTexture);
            if (textureIt == TextureRecords.end()) {
                if (!Pending2dCommandTraceSceneId.empty() || LoggedFirst2dCommandTrace) {
                    BootLog(
                        std::string("draw2d textured missing texture record texture=")
                        + runtimeTexture->get_Id()
                        + " sceneId="
                        + (Pending2dCommandTraceSceneId.empty() ? std::string("unknown") : Pending2dCommandTraceSceneId));
                }
                return;
            }

            Ps2TextureRecord& record = textureIt->second;
            if (!EnsureTextureUploaded(record)) {
                BootLog(
                    std::string("draw2d textured upload failed texture=")
                    + runtimeTexture->get_Id()
                    + " width="
                    + std::to_string(record.Texture.Width)
                    + " height="
                    + std::to_string(record.Texture.Height));
                return;
            }

            if (clipRect != nullptr && !TryClipQuadToRect(*clipRect, bounds, sourceRect)) {
                return;
            }

            const double textureWidth = static_cast<double>(record.Texture.Width);
            const double textureHeight = static_cast<double>(record.Texture.Height);
            const double sourceX = static_cast<double>(sourceRect.X) * textureWidth;
            const double sourceY = static_cast<double>(sourceRect.Y) * textureHeight;
            const double sourceWidth = static_cast<double>(sourceRect.Z) * textureWidth;
            const double sourceHeight = static_cast<double>(sourceRect.W) * textureHeight;
            const u64 rgba = ResolveTexturedSpriteRgba(color);
            gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 1, 0, 1, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_ON;
            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                BootLog(
                    std::string("draw2d textured submit index=")
                    + std::to_string(ActiveDraw2dDiagnosticCommandIndex)
                    + " texture="
                    + runtimeTexture->get_Id());
            }
            gsKit_prim_sprite_texture(
                ActiveGsGlobal,
                &record.Texture,
                bounds.X,
                bounds.Y,
                static_cast<float>(sourceX),
                static_cast<float>(sourceY),
                bounds.X + bounds.Z,
                bounds.Y + bounds.W,
                static_cast<float>(sourceX + sourceWidth),
                static_cast<float>(sourceY + sourceHeight),
                0.0f,
                rgba);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
            if (EnableDraw2dExecutionDiagnostics && !Draw2dExecutionDiagnosticsCompleted) {
                BootLog(
                    std::string("draw2d textured end index=")
                    + std::to_string(ActiveDraw2dDiagnosticCommandIndex)
                    + " texture="
                    + runtimeTexture->get_Id()
                    + " bounds="
                    + FormatFloat4(bounds));
            }
        }
    };

    Ps2RenderManager2D RenderManager2DBackend;
    helengine::ps2::Ps2RenderManager3D RenderManager3DBackend;
}

void HelenginePs2DebugLog(const std::string& message) {
    BootLog(message);
}

namespace helengine::ps2 {
    Ps2BootHost::Ps2BootHost()
        : EngineCore(0),
          EngineOptions(0),
          EngineRuntimeDiagnosticsProvider(0),
          EnginePlatformInfo(0),
          EngineAudioBackend(0),
          EngineInputBackend(0),
          EngineRenderManager2D(0),
          EngineRenderManager3D(0),
          GsGlobal(0),
          StartupSceneLoaded(false) {
    }

    int Ps2BootHost::Run() {
        try {
            if (!InitializeRuntime()) {
                FatalHalt("runtime init returned false");
            }

            if (!InitializeGraphics()) {
                FatalHalt("graphics init returned false");
            }

            BootLog(std::string("startup scene load begin ") + Ps2BootVersionStamp);
            StartupSceneLoaded = LoadPackagedStartupScene();
            BootLog(StartupSceneLoaded ? "startup scene load succeeded" : "startup scene load failed");

            if (!StartupSceneLoaded) {
                BootLog("startup scene load failed; halting");
                PresentBootLogHistoryToDebugConsole();
                if (GsGlobal != 0) {
                    gsKit_sync_flip(GsGlobal);
                }
                while (true) {
                }
            }

                        if (EnableStartupScenePreRenderHalt) {
                            BootLog("startup scene pre-render halt");
                            PresentBootLogHistoryToDebugConsole();
                            if (GsGlobal != 0) {
                                gsKit_sync_flip(GsGlobal);
                            }
                while (true) {
                }
            }

            PresentBootFrame();
            return 0;
        } catch (Exception* exception) {
            FatalHalt(std::string("run exception: ") + (exception != nullptr ? exception->what() : "null"));
        } catch (const std::exception& exception) {
            FatalHalt(std::string("run std exception: ") + exception.what());
        } catch (...) {
            FatalHalt("run unknown exception");
        }
    }

    bool Ps2BootHost::InitializeRuntime() {
        BootLog(std::string("boot version: ") + Ps2BootVersionStamp);
        BootLog("runtime init");
        BootLog("cdvd init begin");
        sceCdInit(SCECdINoD);
        sceCdDiskReady(0);
        BootLog("cdvd ready");
        EngineOptions = new CoreInitializationOptions();
        EngineOptions->set_ContentStreamSource(new HostFileSystemContentStreamSource(ResolveApplicationDirectoryPath()));
        EngineOptions->set_UpdateOrderLayers(1);
        EngineOptions->set_RenderOrderLayers3D(1);
        EngineOptions->set_UpdateListInitialCapacity(4);
        EngineOptions->set_RenderList2DInitialCapacity(4);
        EngineOptions->set_RenderList3DInitialCapacity(4);
        EngineOptions->set_SceneCatalog(BuildRuntimeSceneCatalogFromManifest());
        EngineOptions->set_StandardPlatformInputConfiguration(BuildStandardPlatformInputConfigurationFromManifest());
        EngineCore = new Core(EngineOptions);
        EngineOptions = EngineCore->get_InitializationOptions();
        EngineRuntimeDiagnosticsProvider = new Ps2BootRuntimeDiagnosticsProvider();
        EngineOptions->set_RuntimeDiagnosticsProvider(EngineRuntimeDiagnosticsProvider);
#if HE_PS2_HAS_SCENE_LOAD_TIMING_DIAGNOSTICS_INTERFACE
        BootLog(
            he_cpp_try_cast<IRuntimeSceneLoadTimingDiagnosticsProvider>(EngineRuntimeDiagnosticsProvider) != nullptr
                ? "scene load timing diagnostics provider attached"
                : "scene load timing diagnostics provider missing");
#else
        BootLog("scene load timing diagnostics provider unavailable");
#endif

        BootLog("input bridge init");
        EngineInputBackend = new Ps2InputBackend();
        if (!EngineInputBackend->Initialize()) {
            BootLog("pad bridge init failed");
            return false;
        }
        BootLog("input bridge ready");

        BootLog("audio bridge init");
        EngineAudioBackend = new Ps2AudioBackend();
        BootLog("audio bridge ready");

        EngineRenderManager2D = &RenderManager2DBackend;
        EngineRenderManager3D = &RenderManager3DBackend;
        EnginePlatformInfo = new PlatformInfo("ps2", "1.0.0");
        BootLog("core initialize begin");
        EngineCore->Initialize(
            EngineRenderManager3D,
            EngineRenderManager2D,
            EngineInputBackend,
            EnginePlatformInfo,
            EngineOptions);
        EngineCore->SetAudioBackend(EngineAudioBackend);
        BootLog("core initialized");
#if HE_PS2_HAS_PHYSICS3D_RUNTIME_REGISTRATION
        if (EnablePackagedPhysics3DRegistration && HasPackagedPhysics3DScenes()) {
            if (EnablePhysicsWarmupTrace) {
                HostLogLabeledMemoryDiagnostics("before-physics-register", EngineCore);
            }
            BootLog("physics runtime register begin");
            Physics3DRuntimeComponentRegistration::Register(EngineCore);
            BootLog("physics runtime registered");
            if (EnablePhysicsWarmupTrace) {
                HostLogLabeledMemoryDiagnostics("after-physics-register", EngineCore);
            }
        }
#endif
        return true;
    }

    bool Ps2BootHost::InitializeGraphics() {
        BootLog("graphics init");
        BootLog("gsKit init global begin");
        GsGlobal = gsKit_init_global();
        ActiveGsGlobal = GsGlobal;

        if (GsGlobal == 0) {
            BootLog("gsKit_init_global failed");
            return false;
        }

        RenderManager3DBackend.SetGsGlobal(GsGlobal);

        GsGlobal->Mode = GS_MODE_NTSC;
        GsGlobal->Interlace = GS_INTERLACED;
        GsGlobal->Field = GS_FIELD;
        GsGlobal->Aspect = GS_ASPECT_4_3;
        GsGlobal->Width = Ps2DefaultFramebufferWidth;
        GsGlobal->Height = Ps2DefaultFramebufferHeight;
        GsGlobal->PSM = GS_PSM_CT32;
        GsGlobal->DoubleBuffering = GS_SETTING_ON;
        GsGlobal->ZBuffering = GS_SETTING_ON;
        RenderManager3DBackend.SetHdrEnabled(false);

        dmaKit_init(
            D_CTRL_RELE_OFF,
            D_CTRL_MFD_OFF,
            D_CTRL_STS_UNSPEC,
            D_CTRL_STD_OFF,
            D_CTRL_RCYC_8,
            1 << DMA_CHANNEL_GIF);
        dmaKit_chan_init(DMA_CHANNEL_GIF);
        dma_channel_initialize(DMA_CHANNEL_VIF1, NULL, 0);
        dma_channel_fast_waits(DMA_CHANNEL_VIF1);
        UploadVuOpaqueMicroProgram();
        InitializeVuOpaqueDoubleBuffer();

        gsKit_init_screen(GsGlobal);
        EngineRenderManager3D->AddWindow(
            0,
            static_cast<int32_t>(GsGlobal->Width),
            static_cast<int32_t>(GsGlobal->Height));
        gsKit_mode_switch(GsGlobal, GS_ONESHOT);
        BootLog("graphics ready");
        return true;
    }

    bool Ps2BootHost::LoadPackagedStartupScene() {
        try {
            const char* startupScenePhysicalPath = he_get_runtime_ps2_startup_scene_path();
            if (startupScenePhysicalPath == nullptr || startupScenePhysicalPath[0] == '\0') {
                BootLog("no startup scene configured");
                return false;
            }

            BootLog(std::string("startup scene path: ") + startupScenePhysicalPath);
            BootLogDiscProbe("startup-scene-cdrom", startupScenePhysicalPath);
            std::string startupSceneDiscTokenPath = startupScenePhysicalPath;
            if (startupSceneDiscTokenPath.rfind("cdrom0:", 0) == 0 && startupSceneDiscTokenPath.length() > 7) {
                startupSceneDiscTokenPath = startupSceneDiscTokenPath.substr(7);
                BootLogDiscProbe("startup-scene-token", startupSceneDiscTokenPath.c_str());
            }
            BootLogDiscProbe("startup-probe-elf", "cdrom0:\\HELENGIN.ELF;1");
            BootLogDiscProbe("startup-probe-cnf", "cdrom0:\\SYSTEM.CNF;1");
            std::string menuSceneProbePath = ResolvePs2CookedAssetOpenPath("cooked/scenes/demodiscmainmenu.hasset");
            if (!menuSceneProbePath.empty()) {
                BootLogDiscProbe("startup-probe-menu-scene", menuSceneProbePath.c_str());
            }
            std::string cubesSceneProbePath = ResolvePs2CookedAssetOpenPath("cooked/scenes/rendering/textured_cube_grid.hasset");
            if (!cubesSceneProbePath.empty()) {
                BootLogDiscProbe("startup-probe-cubes-scene", cubesSceneProbePath.c_str());
            }
            std::string startupSceneId = ResolveStartupSceneIdFromManifest();
            if (StartupSceneDiagnosticOverrideId != nullptr && StartupSceneDiagnosticOverrideId[0] != '\0') {
                startupSceneId = StartupSceneDiagnosticOverrideId;
                BootLog(std::string("startup scene override id: ") + startupSceneId);
            }
            BootLog(std::string("startup scene id: ") + startupSceneId);
            if (EngineCore != nullptr && EngineCore->get_SceneManager() != nullptr) {
                BootLog("startup scene invoking scene manager load");
                const std::clock_t startupSceneLoadStartTicks = std::clock();
                EngineCore->get_SceneManager()->LoadScene(startupSceneId, ::SceneLoadMode::Single);
                const std::clock_t startupSceneLoadEndTicks = std::clock();
                BootLog("startup scene load returned from scene manager");
                if (EnableStartupSceneLoadTimingDiagnostic) {
                    if (EnableStartupScenePreRenderHalt) {
                        BootLogHistory.clear();
                    }

                    BootLog(
                        std::string("load ")
                        + startupSceneId
                        + "="
                        + FormatMillisecondsFromClockTicks(startupSceneLoadStartTicks, startupSceneLoadEndTicks));
                    Ps2BootRuntimeDiagnosticsProvider* diagnosticsProvider = he_cpp_try_cast<Ps2BootRuntimeDiagnosticsProvider*>(EngineRuntimeDiagnosticsProvider);
                    BootLogTimingSummary(diagnosticsProvider, "scene", "SceneManager.SceneContentLoad");
                    BootLogTimingSummary(diagnosticsProvider, "run", "RuntimeSceneLoadService.Load");
                    BootLogTimingSummary(diagnosticsProvider, "comp", "RuntimeSceneLoadService.ComponentDeserialize");
                    BootLogTimingSummary(diagnosticsProvider, "fcnt", "RuntimeSceneLoadService.AssetResolveFontContentLoad");
                    BootLogTimingSummary(diagnosticsProvider, "fatl", "RuntimeSceneLoadService.AssetResolveFontAtlasAttach");
                    BootLogTimingSummary(diagnosticsProvider, "txb", "RuntimeSceneLoadService.AssetResolveTextureBuild");
                    BootLogTimingValue("txo", CookedTextureOpenMilliseconds);
                    BootLogTimingValue("txr", CookedTextureBufferedReadMilliseconds);
                    BootLogTimingValue("txd", CookedTextureDeserializeMilliseconds);
                    BootLogTimingValue("txp", CookedTexturePopulateMilliseconds);
                    BootLogTimingValue("txc", CookedTextureRuntimeTextureCreateMilliseconds);
                }
                BootLog(std::string("startup scene loaded: ") + startupScenePhysicalPath);
                return true;
            }

            BootLog("startup scene manager missing");
            return false;
        } catch (Exception* exception) {
            BootLogRuntimeException("startup scene", exception, EngineCore, true);
            delete exception;
            return false;
        } catch (const std::exception& exception) {
            BootLogRuntimeException("startup scene", exception, EngineCore, true);
            return false;
        } catch (...) {
            BootLogUnknownRuntimeException("startup scene", EngineCore, true);
            return false;
        }
    }

    void Ps2BootHost::PresentBootFrame() {
        if (GsGlobal == 0) {
            return;
        }

        bool loggedFirstUpdateComplete = false;
        bool loggedFirstDrawComplete = false;
        while (true) {
            try {
                const std::clock_t frameUpdateStartTicks = std::clock();
                std::clock_t frameUpdateEndTicks = frameUpdateStartTicks;
                std::clock_t frameDraw3dEndTicks = frameUpdateStartTicks;
                std::clock_t frameGifWaitEndTicks = frameUpdateStartTicks;
                std::clock_t frameDrawEndTicks = frameUpdateStartTicks;
                std::clock_t framePresentEndTicks = frameUpdateStartTicks;
                if (EngineCore != 0) {
                    try {
                        if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::Full) {
                            EngineCore->Update();
                            if (EnablePhysicsWarmupTrace && EnablePackagedPhysics3DRegistration && EngineCore->get_PhysicsRuntime() != nullptr) {
                                PhysicsWarmupUpdateCount += 1;
                                if (ShouldLogPhysicsWarmupUpdateCount(PhysicsWarmupUpdateCount)) {
                                    HostLogLabeledMemoryDiagnostics((std::string("after-update-") + std::to_string(PhysicsWarmupUpdateCount)).c_str(), EngineCore);
                                }
                            }
                            if (!loggedFirstUpdateComplete) {
                                if (EnableFirstUpdateStateHalt) {
                                    BootLogHistory.clear();
                                    ::ObjectManager* objectManager = EngineCore->get_ObjectManager();
                                    ::SceneManager* sceneManager = EngineCore->get_SceneManager();
                                    BootLog(
                                        "U1 ls="
                                        + std::to_string(sceneManager != nullptr && sceneManager->get_LoadedScenes() != nullptr ? sceneManager->get_LoadedScenes()->get_Count() : 0)
                                        + " ids="
                                        + BuildLoadedSceneIdsDiagnostic(sceneManager)
                                        + " ent="
                                        + std::to_string(objectManager != nullptr && objectManager->get_Entities() != nullptr ? objectManager->get_Entities()->get_Count() : 0)
                                        + " d2="
                                        + std::to_string(objectManager != nullptr && objectManager->get_Drawables2D() != nullptr ? objectManager->get_Drawables2D()->get_Count() : 0)
                                        + " d3="
                                        + std::to_string(objectManager != nullptr && objectManager->get_Drawables3D() != nullptr ? objectManager->get_Drawables3D()->get_Count() : 0)
                                        + " cam="
                                        + std::to_string(objectManager != nullptr && objectManager->get_Cameras() != nullptr ? objectManager->get_Cameras()->get_Count() : 0)
                                        + " st="
                                        + (sceneManager != nullptr ? sceneManager->get_LastTraceStage() : std::string("null"))
                                        + " sl="
                                        + (EngineCore->get_SceneLoadService() != nullptr ? EngineCore->get_SceneLoadService()->get_LastTraceStage() : std::string("null")));
                                    PresentBootLogHistoryToDebugConsole();
                                    gsKit_sync_flip(GsGlobal);
                                    while (true) {
                                    }
                                }
                                loggedFirstUpdateComplete = true;
                            }
                        } else {
                            if (!loggedFirstUpdateComplete) {
                                HostLogOnly(std::string("frame update diagnostic mode=") + ResolveUpdatePhaseDiagnosticModeName(ActiveUpdatePhaseDiagnosticMode));
                                loggedFirstUpdateComplete = true;
                            }

                            if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::InputEarlyOnly) {
                                ::InputSystem* inputSystem = EngineCore->get_Input();
                                if (inputSystem != nullptr) {
                                    inputSystem->EarlyUpdate();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::InputLateOnly) {
                                ::InputSystem* inputSystem = EngineCore->get_Input();
                                if (inputSystem != nullptr) {
                                    inputSystem->Update();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::InputOnly) {
                                ::InputSystem* inputSystem = EngineCore->get_Input();
                                if (inputSystem != nullptr) {
                                    inputSystem->EarlyUpdate();
                                    inputSystem->Update();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::AllManual) {
                                ::InputSystem* inputSystem = EngineCore->get_Input();
                                if (inputSystem != nullptr) {
                                    inputSystem->EarlyUpdate();
                                }

                                ::FPSComponent::RecordUpdateFrame();
                                ::DebugComponent::RecordUpdateFrame();

                                ::ObjectManager* objectManager = EngineCore->get_ObjectManager();
                                if (objectManager != nullptr) {
                                    objectManager->Update();
                                }

                                ::IPhysicsRuntime* physicsRuntime = EngineCore->get_PhysicsRuntime();
                                ::PhysicsFixedStepScheduler* physicsScheduler = EngineCore->get_PhysicsScheduler();
                                if (physicsRuntime != nullptr && physicsScheduler != nullptr) {
                                    physicsScheduler->AddElapsedSeconds(1.0 / 60.0);
                                    while (physicsScheduler->TryConsumeStep()) {
                                        physicsRuntime->Step(physicsScheduler->StepSeconds);
                                    }
                                }

                                if (inputSystem != nullptr) {
                                    inputSystem->Update();
                                }

                                ::PointerInteractionSystem* pointerInteractionSystem = EngineCore->get_PointerInteractionSystem();
                                if (pointerInteractionSystem != nullptr) {
                                    pointerInteractionSystem->Update();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::BackendCaptureOnly) {
                                if (EngineInputBackend != nullptr) {
                                    EngineInputBackend->CaptureFrame();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::FrameStatsOnly) {
                                ::FPSComponent::RecordUpdateFrame();
                                ::DebugComponent::RecordUpdateFrame();
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::ObjectManagerOnly) {
                                ::ObjectManager* objectManager = EngineCore->get_ObjectManager();
                                if (objectManager != nullptr) {
                                    objectManager->Update();
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::PhysicsOnly) {
                                if (EngineCore != nullptr) {
                                    ::IPhysicsRuntime* physicsRuntime = EngineCore->get_PhysicsRuntime();
                                    ::PhysicsFixedStepScheduler* physicsScheduler = EngineCore->get_PhysicsScheduler();
                                    if (physicsRuntime != nullptr && physicsScheduler != nullptr) {
                                        physicsScheduler->AddElapsedSeconds(1.0 / 60.0);
                                        while (physicsScheduler->TryConsumeStep()) {
                                            physicsRuntime->Step(physicsScheduler->StepSeconds);
                                        }
                                    }
                                }
                            } else if (ActiveUpdatePhaseDiagnosticMode == UpdatePhaseDiagnosticMode::PointerOnly) {
                                ::PointerInteractionSystem* pointerInteractionSystem = EngineCore->get_PointerInteractionSystem();
                                if (pointerInteractionSystem != nullptr) {
                                    pointerInteractionSystem->Update();
                                }
                            }
                        }
                    } catch (Exception* exception) {
                        BootLogRuntimeException("frame update", exception, EngineCore, false);
                        delete exception;
                        while (true) {
                        }
                    } catch (const std::exception& exception) {
                        BootLogRuntimeException("frame update", exception, EngineCore, false);
                        while (true) {
                        }
                    } catch (...) {
                        BootLogUnknownRuntimeException("frame update", EngineCore, false);
                        while (true) {
                        }
                    }
                }
                frameUpdateEndTicks = std::clock();

                const u64 clearColor = GS_SETREG_RGBAQ(0x10, 0x10, 0x10, 0x00, 0x00);
                gsKit_clear(GsGlobal, clearColor);
                gsKit_set_test(GsGlobal, GS_ATEST_OFF);
                gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
                GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
                gsKit_queue_exec(GsGlobal);

                if (EnableCubeTriangle3dDiagnostics) {
                    DrawCubeTriangle3dDiagnosticsFrame(GsGlobal);
                    gsKit_queue_exec(GsGlobal);
                    gsKit_sync_flip(GsGlobal);
                    BootLog("cube triangle 3d diagnostic halt");
                    while (true) {
                    }
                }

                if (EnableCubeTriangle2dDiagnostics) {
                    DrawCubeTriangle2dDiagnosticsFrame(GsGlobal);
                    gsKit_queue_exec(GsGlobal);
                    gsKit_sync_flip(GsGlobal);
                    BootLog("cube triangle 2d diagnostic halt");
                    while (true) {
                    }
                }

                if (EnableCubeSpriteDiagnostics) {
                    DrawCubeSpriteDiagnosticsFrame(GsGlobal);
                    gsKit_queue_exec(GsGlobal);
                    gsKit_sync_flip(GsGlobal);
                    BootLog("cube sprite diagnostic halt");
                    while (true) {
                    }
                }

                if (EngineCore != nullptr) {
                    try {
                        if (EnableCubeRuntimeDiagnostics
                            && !CubeFrameBoundaryCheckpointLogged
                            && IsCubeRuntimeDiagnosticsSceneActive(EngineCore)) {
                            CubeFrameBoundaryCheckpointLogged = true;
                            BootLog("cube frame checkpoint: before EngineCore->Draw");
                        }
                        EngineCore->Draw();
                        if (EnableCubeRuntimeDiagnostics
                            && !CubeFrameDrawReturnedLogged
                            && IsCubeRuntimeDiagnosticsSceneActive(EngineCore)) {
                            CubeFrameDrawReturnedLogged = true;
                            BootLog("cube frame checkpoint: after EngineCore->Draw");
                        }
                        if (!loggedFirstDrawComplete) {
                            BootLog("first draw completed");
                            loggedFirstDrawComplete = true;
                        }
                    } catch (Exception* exception) {
                        BootLogRuntimeException("frame draw3d", exception, EngineCore, true);
                        delete exception;
                        while (true) {
                        }
                    } catch (const std::exception& exception) {
                        BootLogRuntimeException("frame draw3d", exception, EngineCore, true);
                        while (true) {
                        }
                    } catch (...) {
                        BootLogUnknownRuntimeException("frame draw3d", EngineCore, true);
                        while (true) {
                        }
                    }
                    frameDraw3dEndTicks = std::clock();

                    // VU opaque rendering emits GIF work asynchronously through VIF1. Wait for the
                    // GIF channel to drain before 2D overlays and the frame submit reuse GS state.
                    if (EnableCubeRuntimeDiagnostics
                        && !CubeFrameGifWaitBeginLogged
                        && IsCubeRuntimeDiagnosticsSceneActive(EngineCore)) {
                        CubeFrameGifWaitBeginLogged = true;
                        BootLog("cube frame checkpoint: before dma_channel_wait(DMA_CHANNEL_GIF)");
                    }
                    dma_channel_wait(DMA_CHANNEL_GIF, 0);
                    if (EnableCubeRuntimeDiagnostics
                        && !CubeFrameGifWaitEndLogged
                        && IsCubeRuntimeDiagnosticsSceneActive(EngineCore)) {
                        CubeFrameGifWaitEndLogged = true;
                        BootLog("cube frame checkpoint: after dma_channel_wait(DMA_CHANNEL_GIF)");
                    }
                    frameGifWaitEndTicks = std::clock();
                    RenderManager3DBackend.SetLastGifDrainMilliseconds(
                        ResolveMillisecondsFromClockTicks(frameDraw3dEndTicks, frameGifWaitEndTicks));

                    if (EnableCubeRuntimeDiagnostics
                        && !CubeDiagnosticsShown
                        && !CubeRuntimeDiagnosticsCompleted
                        && ShouldCaptureCubeRuntimeDiagnostics(EngineCore->get_SceneManager())
                        && RenderManager3DBackend.GetLastProxyCount() > 0u) {
                        CubeDiagnosticsShown = true;
                        scr_clear();
                        BootLog(
                            "cube runtime counts: proxies="
                            + std::to_string(RenderManager3DBackend.GetLastProxyCount())
                            + " opaqueWorld="
                            + std::to_string(RenderManager3DBackend.GetLastOpaqueWorldCount())
                            + " opaqueDynamic="
                            + std::to_string(RenderManager3DBackend.GetLastOpaqueDynamicCount())
                            + " alphaWorld="
                            + std::to_string(RenderManager3DBackend.GetLastAlphaWorldCount())
                            + " alphaDynamic="
                            + std::to_string(RenderManager3DBackend.GetLastAlphaDynamicCount()));
                        BootLog(
                            "cube runtime rejects: missingMaterial="
                            + std::to_string(RenderManager3DBackend.GetLastVuRejectedMissingMaterialCount())
                            + " missingModel="
                            + std::to_string(RenderManager3DBackend.GetLastVuRejectedMissingModelCount())
                            + " missingPackedModel="
                            + std::to_string(RenderManager3DBackend.GetLastVuRejectedMissingPackedModelCount())
                            + " clip="
                            + std::to_string(RenderManager3DBackend.GetLastClipRejectCount())
                            + " projection="
                            + std::to_string(RenderManager3DBackend.GetLastProjectionRejectCount())
                            + " cull="
                            + std::to_string(RenderManager3DBackend.GetLastCullRejectCount()));
                        BootLog(
                            "cube runtime checkpoint: after draw phase="
                            + std::to_string(RenderManager3DBackend.GetLastVuPacketPhase())
                            + " packetBytes="
                            + std::to_string(RenderManager3DBackend.GetLastVuPacketByteCount())
                            + " submitted="
                            + std::to_string(RenderManager3DBackend.GetLastSubmittedTriangleCount()));
                        BootLog(
                            "cube draw returned: viewport="
                            + FormatFloat4(RenderManager3DBackend.GetLastResolvedViewport())
                            + " screenBounds="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedScreenBounds()));
                        BootLog(
                            "cube draw returned: screenBounds="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedScreenBounds())
                            + " triA="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleBoundsA())
                            + " triB="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleBoundsB()));
                        BootLog(
                            "cube draw returned: triA0="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexA0())
                            + " triA1="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexA1())
                            + " triA2="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexA2()));
                        BootLog(
                            "cube draw returned: triB0="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexB0())
                            + " triB1="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexB1())
                            + " triB2="
                            + FormatFloat4(RenderManager3DBackend.GetLastSubmittedTriangleVertexB2()));
                        if (EnableCubeRuntimeDiagnosticImmediateHalt) {
                            BootLog("cube runtime checkpoint: diagnostic halt after draw");
                            while (true) {
                            }
                        }
                    }
                    if (EnableCubeRuntimeDiagnostics
                        && ShouldCaptureCubeRuntimeDiagnostics(EngineCore->get_SceneManager())) {
                        PublishCubeRuntimeOverlayMetrics(RenderManager3DBackend);
                    }

                    if (EngineCore->get_ObjectManager() != nullptr &&
                        EngineCore->get_ObjectManager()->get_Drawables2D() != nullptr) {
                        if (FrameTimingOverlayPending) {
                            auto* drawables2D = EngineCore->get_ObjectManager()->get_Drawables2D();
                            for (int32_t i = 0; i < drawables2D->Count(); i++) {
                                ::IDrawable2D* drawable = (*drawables2D)[i];
                                ::ITextDrawable2D* textDrawable = dynamic_cast<::ITextDrawable2D*>(drawable);
                                if (textDrawable == nullptr) {
                                    continue;
                                }

                                std::string currentText = textDrawable->get_Text();
                                if (currentText.rfind("Update FPS:", 0) == 0
                                    || currentText.rfind("Upd", 0) == 0
                                    || currentText.rfind("FPS", 0) == 0) {
                                    textDrawable->set_Text(FrameTimingOverlayLine1);
                                } else if (currentText.rfind("Render FPS:", 0) == 0
                                    || currentText.rfind("Rdr", 0) == 0
                                    || currentText.rfind("Drw", 0) == 0
                                    || currentText.rfind("Enc", 0) == 0) {
                                    textDrawable->set_Text(FrameTimingOverlayDetailLine);
                                }
                            }
                        }

                        const int32_t previousZBuffering = GsGlobal->ZBuffering;
                        GsGlobal->ZBuffering = GS_SETTING_OFF;
                        gsKit_set_test(GsGlobal, GS_ZTEST_OFF);

                        try {
                            EngineRenderManager2D->Draw();
                            GsGlobal->ZBuffering = previousZBuffering;
                            if (GsGlobal->ZBuffering == GS_SETTING_ON) {
                                gsKit_set_test(GsGlobal, GS_ZTEST_ON);
                            } else {
                                gsKit_set_test(GsGlobal, GS_ZTEST_OFF);
                            }
                        } catch (Exception* exception) {
                            BootLogRuntimeException("frame draw2d", exception, EngineCore, false);
                            delete exception;
                            while (true) {
                            }
                        } catch (const std::exception& exception) {
                            BootLogRuntimeException("frame draw2d", exception, EngineCore, false);
                            while (true) {
                            }
                        } catch (...) {
                            BootLogUnknownRuntimeException("frame draw2d", EngineCore, false);
                            while (true) {
                            }
                        }

                        if (!FirstFramePresentCheckpointLogged) {
                            BootLog("P0 after draw2d");
                        }
                    }
                }
                if (frameGifWaitEndTicks == frameUpdateStartTicks) {
                    frameGifWaitEndTicks = std::clock();
                }
                frameDrawEndTicks = std::clock();

                try {
                    if (!FirstFramePresentCheckpointLogged) {
                        BootLog("P1 before queue_exec");
                    }
                    gsKit_queue_exec(GsGlobal);
                    if (!FirstFramePresentCheckpointLogged) {
                        BootLog("P2 after queue_exec");
                    }
                    gsKit_sync_flip(GsGlobal);
                    if (!FirstFramePresentCheckpointLogged) {
                        BootLog("P3 after sync_flip");
                        FirstFramePresentCheckpointLogged = true;
                    }
                } catch (Exception* exception) {
                    BootLogRuntimeException("frame present", exception, EngineCore, false);
                    delete exception;
                    while (true) {
                    }
                } catch (const std::exception& exception) {
                    BootLogRuntimeException("frame present", exception, EngineCore, false);
                    while (true) {
                    }
                } catch (...) {
                    BootLogUnknownRuntimeException("frame present", EngineCore, false);
                    while (true) {
                    }
                }
                framePresentEndTicks = std::clock();
                if (FrameTimingOverlayPending) {
                    FrameTimingOverlayPresented = true;
                }
                RecordRenderManagerTimingSample(RenderManager3DBackend);
                RecordFrameTimingSample(
                    RenderManager3DBackend,
                    ResolveSecondsFromClockTicks(frameUpdateStartTicks, frameUpdateEndTicks),
                    ResolveSecondsFromClockTicks(frameUpdateEndTicks, frameDraw3dEndTicks),
                    ResolveSecondsFromClockTicks(frameDraw3dEndTicks, frameGifWaitEndTicks),
                    ResolveSecondsFromClockTicks(frameGifWaitEndTicks, frameDrawEndTicks),
                    ResolveSecondsFromClockTicks(frameUpdateEndTicks, frameDrawEndTicks),
                    ResolveSecondsFromClockTicks(frameDrawEndTicks, framePresentEndTicks));
                RecordMemoryDiagnosticsSample(EngineCore, framePresentEndTicks);
                ApplyPlatformPerformanceOverlayRows(EngineCore);
                if (EnableCubeRuntimeDiagnostics && CubeDiagnosticsShown && !CubeRuntimeDiagnosticsCompleted) {
                    if (!CubeRuntimeDiagnosticWatchActive) {
                        CubeRuntimeDiagnosticWatchActive = true;
                        CubeRuntimeDiagnosticWatchStartTicks = framePresentEndTicks;
                        BootLog("cube frame presented; watching for 5 seconds");
                    } else if (ResolveSecondsFromClockTicks(CubeRuntimeDiagnosticWatchStartTicks, framePresentEndTicks) >= CubeRuntimeDiagnosticWatchSeconds) {
                        CubeRuntimeDiagnosticsCompleted = true;
                        BootLog("cube frame presented; watch window complete");
                    }
                }
                if (EnableFrameTimingDiagnostics &&
                    EnableFrameTimingDiagnosticHalt &&
                    FrameTimingSampleCompleted &&
                    FrameTimingOverlayPresented) {
                    FrameTimingOverlayPending = false;
                    while (true) {
                    }
                }
            } catch (Exception* exception) {
                BootLogRuntimeException("frame", exception, EngineCore, false);
                delete exception;
                while (true) {
                }
            } catch (const std::exception& exception) {
                BootLogRuntimeException("frame", exception, EngineCore, false);
                while (true) {
                }
            } catch (...) {
                BootLogUnknownRuntimeException("frame", EngineCore, false);
                while (true) {
                }
            }
        }
    }

    std::string Ps2BootHost::ResolveApplicationDirectoryPath() const {
        return "cdrom0:\\";
    }
}
