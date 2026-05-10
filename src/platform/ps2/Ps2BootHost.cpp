#include "platform/ps2/Ps2BootHost.hpp"

#include <dmaKit.h>
#include <debug.h>
#include <malloc.h>
#include <gsKit.h>
#include <algorithm>
#include <cstring>
#include <cstdio>
#include <cmath>
#include <exception>
#include <unordered_map>
#include <vector>

#include "Asset.hpp"
#include "AssetSerializer.hpp"
#include "Core.hpp"
#include "CoreInitializationOptions.hpp"
#include "Entity.hpp"
#include "ModelAsset.hpp"
#include "SceneAsset.hpp"
#include "SpriteComponent.hpp"
#include "platform/ps2/Ps2InputBackend.hpp"
#include "platform/ps2/rendering/Ps2RenderManager3D.hpp"
#include "TextureUtils.hpp"
#include "RenderManager2D.hpp"
#include "RenderManager3D.hpp"
#include "runtime/runtime_graphics_renderer_manifest.hpp"
#include "runtime/runtime_ps2_asset_path_manifest.hpp"
#include "runtime/runtime_scene_catalog_manifest.hpp"
#include "runtime/native_exceptions.hpp"
#include "RuntimeModel.hpp"
#include "RuntimeSceneCatalog.hpp"
#include "RuntimeSceneCatalogEntry.hpp"
#include "RuntimeTexture.hpp"
#include "system/io/file.hpp"
#include "system/io/path.hpp"
#include "TextLayoutUtils.hpp"
#include "TextureAsset.hpp"

namespace {
    constexpr int Ps2DefaultFramebufferWidth = 640;
    constexpr int Ps2DefaultFramebufferHeight = 448;
    constexpr bool EnableCubeSpriteDiagnostics = false;
    constexpr bool EnableCubeTriangle2dDiagnostics = false;
    constexpr bool EnableCubeTriangle3dDiagnostics = false;
    constexpr bool EnableCubeRuntimeDiagnostics = false;
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
    bool DebugConsoleReady = false;
    bool CubeDiagnosticsShown = false;

    void EnsureDebugConsole() {
        if (DebugConsoleReady) {
            return;
        }

        init_scr();
        scr_setbgcolor(0x101010);
        scr_clear();
        DebugConsoleReady = true;
    }

    void BootLog(const char* message) {
        EnsureDebugConsole();
        scr_printf("[helengine-ps2] %s\n", message);
        std::printf("[helengine-ps2] %s\n", message);
        std::fflush(stdout);
    }

    void BootLog(const std::string& message) {
        BootLog(message.c_str());
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
        while (true) {
        }
    }

    [[noreturn]] void FatalHalt(const std::string& message) {
        FatalHalt(message.c_str());
    }

    GSGLOBAL* ActiveGsGlobal = nullptr;

    struct Ps2TextureRecord {
        GSTEXTURE Texture {};
        void* Pixels = nullptr;
        bool Uploaded = false;
    };

    std::unordered_map<const ::RuntimeTexture*, Ps2TextureRecord> TextureRecords;

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

    bool EnsureTextureUploaded(Ps2TextureRecord& record) {
        if (record.Uploaded) {
            return true;
        }

        record.Texture.Vram = gsKit_vram_alloc(
            ActiveGsGlobal,
            gsKit_texture_size(record.Texture.Width, record.Texture.Height, record.Texture.PSM),
            GSKIT_ALLOC_USERBUFFER);
        if (record.Texture.Vram == GSKIT_ALLOC_ERROR) {
            return false;
        }

        gsKit_texture_upload(ActiveGsGlobal, &record.Texture);
        record.Uploaded = true;
        return true;
    }

    class Ps2RenderManager2D final : public RenderManager2D {
    public:
        RuntimeTexture* BuildTextureFromRaw(TextureAsset* data) override {
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
                        TextureRecords.emplace(texture, record);
                    }
                }
            }

            return texture;
        }

        void DrawSprite(ISpriteDrawable2D* sprite) override {
            if (ActiveGsGlobal == 0 || sprite == nullptr) {
                return;
            }

            ::Entity* parent = sprite->get_Parent();
            const ::float3 position = parent != nullptr ? parent->get_Position() : ::float3();
            ::int2* size = sprite->get_Size();
            const ::byte4 color = sprite->get_Color();
            const u8 alpha = static_cast<u8>(std::min(static_cast<int>(color.W), 0x80));
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, alpha, 0x00);

            ::RuntimeTexture* runtimeTexture = sprite->get_Texture();
            if (runtimeTexture != nullptr) {
                auto textureIt = TextureRecords.find(runtimeTexture);
                if (textureIt != TextureRecords.end()) {
                    Ps2TextureRecord& record = textureIt->second;
                    if (EnsureTextureUploaded(record)) {
                        gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
                        gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 1, 0, 1, 0), 0);
                        ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_ON;
                        gsKit_prim_sprite_texture(
                            ActiveGsGlobal,
                            &record.Texture,
                            position.X,
                            position.Y,
                            0.0f,
                            0.0f,
                            position.X + static_cast<float>(size != nullptr ? size->X : 0),
                            position.Y + static_cast<float>(size != nullptr ? size->Y : 0),
                            static_cast<float>(record.Texture.Width),
                            static_cast<float>(record.Texture.Height),
                            0.0f,
                            rgba);
                        gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
                        ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
                        return;
                    }
                }
            }

            gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
            gsKit_prim_sprite(
                ActiveGsGlobal,
                position.X,
                position.Y,
                position.X + static_cast<float>(size != nullptr ? size->X : 0),
                position.Y + static_cast<float>(size != nullptr ? size->Y : 0),
                0,
                rgba);
        }

        void DrawText(ITextDrawable2D* text) override {
            if (ActiveGsGlobal == 0 || text == nullptr) {
                return;
            }

            ::Entity* parent = text->get_Parent();
            if (parent == nullptr) {
                return;
            }

            ::FontAsset* font = text->get_Font();
            if (font == nullptr || font->get_FontInfo() == nullptr || font->get_Texture() == nullptr) {
                return;
            }

            auto textureIt = TextureRecords.find(font->get_Texture());
            if (textureIt == TextureRecords.end()) {
                return;
            }

            Ps2TextureRecord& record = textureIt->second;
            if (!EnsureTextureUploaded(record)) {
                return;
            }

            std::string content = text->get_Text();
            if (text->get_WrapText() && text->get_Size() != nullptr) {
                content = TextLayoutUtils::WrapText(content, font, text->get_Size()->X);
            }

            const ::float3 position = parent->get_Position();
            const ::byte4 color = text->get_Color();
            const u8 alpha = static_cast<u8>(std::min(static_cast<int>(color.W), 0x80));
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, alpha, 0x00);
            const double lineHeight = std::max(static_cast<double>(font->get_LineHeight()), 1.0);
            const double baseX = std::round(position.X);
            const double baseY = std::round(position.Y);
            double offsetX = 0.0;
            double offsetY = 0.0;

            gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 1, 0, 1, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_ON;

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
                    offsetX += font->get_FontInfo()->get_SpaceWidth();
                    continue;
                }

                ::FontChar glyph;
                if (font->get_Characters() == nullptr || !font->get_Characters()->TryGetValue(character, glyph)) {
                    continue;
                }

                const double pixelWidth = glyph.SourceRect.Z * static_cast<double>(record.Texture.Width);
                const double pixelHeight = glyph.SourceRect.W * static_cast<double>(record.Texture.Height);
                const double sourceX = glyph.SourceRect.X * static_cast<double>(record.Texture.Width);
                const double sourceY = glyph.SourceRect.Y * static_cast<double>(record.Texture.Height);
                const double drawX = baseX + offsetX;
                const double drawY = baseY + std::round(offsetY) + glyph.OffsetY;

                gsKit_prim_sprite_texture(
                    ActiveGsGlobal,
                    &record.Texture,
                    static_cast<float>(drawX),
                    static_cast<float>(drawY),
                    static_cast<float>(sourceX),
                    static_cast<float>(sourceY),
                    static_cast<float>(drawX + pixelWidth),
                    static_cast<float>(drawY + pixelHeight),
                    static_cast<float>(sourceX + pixelWidth),
                    static_cast<float>(sourceY + pixelHeight),
                    0.0f,
                    rgba);

                const double advance = glyph.AdvanceWidth > 0.0f ? glyph.AdvanceWidth : pixelWidth;
                offsetX += advance;
            }

            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        }

        void DrawRoundedRect(IRoundedRectDrawable2D* shape) override {
            if (ActiveGsGlobal == 0 || shape == nullptr) {
                return;
            }

            ::Entity* parent = shape->get_Parent();
            const ::float3 position = parent != nullptr ? parent->get_Position() : ::float3();
            ::int2* size = shape->get_Size();
            const ::byte4 color = shape->get_FillColor();
            const u8 alpha = static_cast<u8>(std::min(static_cast<int>(color.W), 0x80));
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, alpha, 0x00);

            gsKit_set_test(ActiveGsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(ActiveGsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            ActiveGsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
            gsKit_prim_sprite(
                ActiveGsGlobal,
                position.X,
                position.Y,
                position.X + static_cast<float>(size != nullptr ? size->X : 0),
                position.Y + static_cast<float>(size != nullptr ? size->Y : 0),
                0,
                rgba);
        }
    };

    Ps2RenderManager2D RenderManager2DBackend;
    helengine::ps2::Ps2RenderManager3D RenderManager3DBackend;
}

namespace helengine::ps2 {
    Ps2BootHost::Ps2BootHost()
        : EngineCore(0),
          EngineOptions(0),
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
        BootLog("runtime init");
        EngineCore = new Core();
        EngineOptions = EngineCore->get_InitializationOptions();
        EngineOptions->set_ContentRootPath(ResolveApplicationDirectoryPath());
        EngineOptions->set_UpdateOrderLayers(1);
        EngineOptions->set_RenderOrderLayers3D(1);
        EngineOptions->set_UpdateListInitialCapacity(4);
        EngineOptions->set_RenderList2DInitialCapacity(4);
        EngineOptions->set_RenderList3DInitialCapacity(4);
        EngineOptions->set_RuntimeSceneCatalog(BuildRuntimeSceneCatalogFromManifest());

        BootLog("input bridge init");
        EngineInputBackend = new Ps2InputBackend();
        if (!EngineInputBackend->Initialize()) {
            BootLog("pad bridge init failed");
            return false;
        }
        BootLog("input bridge ready");

        EngineRenderManager2D = &RenderManager2DBackend;
        EngineRenderManager3D = &RenderManager3DBackend;

        BootLog("core initialize begin");
        EngineCore->Initialize(
            EngineRenderManager3D,
            EngineRenderManager2D,
            EngineInputBackend,
            EngineOptions);
        BootLog("core initialized");

        BootLog("startup scene load begin");
        StartupSceneLoaded = LoadPackagedStartupScene();
        BootLog(StartupSceneLoaded ? "startup scene load succeeded" : "startup scene load failed");

        if (!StartupSceneLoaded) {
            BootLog("startup scene load failed; halting");
            while (true) {
            }
        }

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
        GsGlobal->DoubleBuffering = GS_SETTING_OFF;
        const HERuntimeGraphicsRendererManifest* graphicsRendererManifest = he_get_runtime_graphics_renderer_manifest();
        if (graphicsRendererManifest == 0) {
            BootLog("graphics renderer manifest missing");
            return false;
        }

        if (graphicsRendererManifest->Ps2DepthHandlerMode == HERuntimePs2DepthHandlerMode::Hardware) {
            GsGlobal->ZBuffering = GS_SETTING_ON;
        } else {
            GsGlobal->ZBuffering = GS_SETTING_OFF;
        }

        RenderManager3DBackend.SetHdrEnabled(graphicsRendererManifest->HdrEnabled == true);

        dmaKit_init(
            D_CTRL_RELE_OFF,
            D_CTRL_MFD_OFF,
            D_CTRL_STS_UNSPEC,
            D_CTRL_STD_OFF,
            D_CTRL_RCYC_8,
            1 << DMA_CHANNEL_GIF);
        dmaKit_chan_init(DMA_CHANNEL_GIF);

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

            Asset* startupAsset = LoadPackagedPhysicalAsset(startupScenePhysicalPath);
            if (startupAsset == nullptr) {
                BootLog("startup scene asset load returned null");
                return false;
            }

            SceneAsset* startupScene = static_cast<SceneAsset*>(startupAsset);
            if (EngineCore != nullptr && EngineCore->get_SceneLoadService() != nullptr) {
                EngineCore->get_SceneLoadService()->Load(startupScene);
                BootLog(std::string("startup scene loaded: ") + startupScenePhysicalPath);
                return true;
            }

            BootLog("startup scene load service missing");
            return false;
        } catch (Exception* exception) {
            BootLog(std::string("startup scene exception: ") + (exception != nullptr ? exception->what() : "null"));
            delete exception;
            return false;
        } catch (const std::exception& exception) {
            BootLog(std::string("startup scene exception: ") + exception.what());
            return false;
        } catch (...) {
            BootLog("startup scene exception: unknown");
            return false;
        }
    }

    Asset* Ps2BootHost::LoadPackagedPhysicalAsset(const std::string& physicalPath) {
        if (!File::Exists(physicalPath)) {
            BootLog(std::string("packaged asset missing: ") + physicalPath);
            return nullptr;
        }

        FileStream* stream = File::OpenRead(physicalPath);
        return AssetSerializer::Deserialize(stream);
    }

    void Ps2BootHost::PresentBootFrame() {
        if (GsGlobal == 0) {
            return;
        }

        while (true) {
            try {
                if (EngineCore != 0) {
                    try {
                        EngineCore->Update();
                    } catch (Exception* exception) {
                        BootLog(std::string("frame update exception: ") + (exception != nullptr ? exception->what() : "null"));
                        delete exception;
                        while (true) {
                        }
                    } catch (const std::exception& exception) {
                        BootLog(std::string("frame update exception: ") + exception.what());
                        while (true) {
                        }
                    } catch (...) {
                        BootLog("frame update exception: unknown");
                        while (true) {
                        }
                    }
                }

                const u64 clearColor = GS_SETREG_RGBAQ(0x10, 0x10, 0x10, 0x00, 0x00);
                gsKit_clear(GsGlobal, clearColor);
                gsKit_set_test(GsGlobal, GS_ATEST_OFF);
                gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
                GsGlobal->PrimAlphaEnable = GS_SETTING_OFF;

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
                        EngineCore->Draw();
                    } catch (Exception* exception) {
                        BootLog(std::string("frame draw3d exception: ") + (exception != nullptr ? exception->what() : "null"));
                        delete exception;
                        while (true) {
                        }
                    } catch (const std::exception& exception) {
                        BootLog(std::string("frame draw3d exception: ") + exception.what());
                        while (true) {
                        }
                    } catch (...) {
                        BootLog("frame draw3d exception: unknown");
                        while (true) {
                        }
                    }

                    if (EnableCubeRuntimeDiagnostics && !CubeDiagnosticsShown) {
                        CubeDiagnosticsShown = true;
                        std::size_t drawableCount3D = 0;
                        std::size_t updateableCount = 0;
                        if (EngineCore->get_ObjectManager() != nullptr
                            && EngineCore->get_ObjectManager()->get_Drawables3D() != nullptr) {
                            drawableCount3D = static_cast<std::size_t>(EngineCore->get_ObjectManager()->get_Drawables3D()->Count());
                        }
                        if (EngineCore->get_ObjectManager() != nullptr
                            && EngineCore->get_ObjectManager()->get_Updateables() != nullptr) {
                            updateableCount = static_cast<std::size_t>(EngineCore->get_ObjectManager()->get_Updateables()->Count());
                        }

                        const ::float4 resolvedViewport = RenderManager3DBackend.GetLastResolvedViewport();
                        const ::float4 submittedBounds = RenderManager3DBackend.GetLastSubmittedScreenBounds();
                        const ::float4 triangleBoundsA = RenderManager3DBackend.GetLastSubmittedTriangleBoundsA();
                        const ::float4 triangleBoundsB = RenderManager3DBackend.GetLastSubmittedTriangleBoundsB();
                        const ::float4 triangleVertexA0 = RenderManager3DBackend.GetLastSubmittedTriangleVertexA0();
                        const ::float4 triangleVertexA1 = RenderManager3DBackend.GetLastSubmittedTriangleVertexA1();
                        const ::float4 triangleVertexA2 = RenderManager3DBackend.GetLastSubmittedTriangleVertexA2();
                        const ::float4 triangleVertexB0 = RenderManager3DBackend.GetLastSubmittedTriangleVertexB0();
                        const ::float4 triangleVertexB1 = RenderManager3DBackend.GetLastSubmittedTriangleVertexB1();
                        const ::float4 triangleVertexB2 = RenderManager3DBackend.GetLastSubmittedTriangleVertexB2();
                        BootLog(
                            "cube diagnostics: drawables3d="
                            + std::to_string(drawableCount3D)
                            + " updateables="
                            + std::to_string(updateableCount)
                            + " proxies="
                            + std::to_string(RenderManager3DBackend.GetLastProxyCount())
                            + " submitted="
                            + std::to_string(RenderManager3DBackend.GetLastSubmittedTriangleCount())
                            + " cullRejects="
                            + std::to_string(RenderManager3DBackend.GetLastCullRejectCount())
                            + " clipRejects="
                            + std::to_string(RenderManager3DBackend.GetLastClipRejectCount())
                            + " projectionRejects="
                            + std::to_string(RenderManager3DBackend.GetLastProjectionRejectCount())
                            + " zbuffer="
                            + std::to_string(static_cast<int>(GsGlobal->ZBuffering)));
                        BootLog(
                            "cube diagnostics viewport="
                            + std::to_string(resolvedViewport.X)
                            + ","
                            + std::to_string(resolvedViewport.Y)
                            + ","
                            + std::to_string(resolvedViewport.Z)
                            + ","
                            + std::to_string(resolvedViewport.W)
                            + " bounds="
                            + std::to_string(submittedBounds.X)
                            + ","
                            + std::to_string(submittedBounds.Y)
                            + ","
                            + std::to_string(submittedBounds.Z)
                            + ","
                            + std::to_string(submittedBounds.W));
                        BootLog(
                            "cube diagnostics triA="
                            + std::to_string(triangleBoundsA.X)
                            + ","
                            + std::to_string(triangleBoundsA.Y)
                            + ","
                            + std::to_string(triangleBoundsA.Z)
                            + ","
                            + std::to_string(triangleBoundsA.W)
                            + " triB="
                            + std::to_string(triangleBoundsB.X)
                            + ","
                            + std::to_string(triangleBoundsB.Y)
                            + ","
                            + std::to_string(triangleBoundsB.Z)
                            + ","
                            + std::to_string(triangleBoundsB.W));
                        BootLog(
                            "cube diagnostics vA0="
                            + std::to_string(triangleVertexA0.X)
                            + ","
                            + std::to_string(triangleVertexA0.Y)
                            + ","
                            + std::to_string(triangleVertexA0.Z)
                            + " vA1="
                            + std::to_string(triangleVertexA1.X)
                            + ","
                            + std::to_string(triangleVertexA1.Y)
                            + ","
                            + std::to_string(triangleVertexA1.Z)
                            + " vA2="
                            + std::to_string(triangleVertexA2.X)
                            + ","
                            + std::to_string(triangleVertexA2.Y)
                            + ","
                            + std::to_string(triangleVertexA2.Z));
                        BootLog(
                            "cube diagnostics vB0="
                            + std::to_string(triangleVertexB0.X)
                            + ","
                            + std::to_string(triangleVertexB0.Y)
                            + ","
                            + std::to_string(triangleVertexB0.Z)
                            + " vB1="
                            + std::to_string(triangleVertexB1.X)
                            + ","
                            + std::to_string(triangleVertexB1.Y)
                            + ","
                            + std::to_string(triangleVertexB1.Z)
                            + " vB2="
                            + std::to_string(triangleVertexB2.X)
                            + ","
                            + std::to_string(triangleVertexB2.Y)
                            + ","
                            + std::to_string(triangleVertexB2.Z));
                        while (true) {
                        }
                    }

                    if (EngineCore->get_ObjectManager() != nullptr &&
                        EngineCore->get_ObjectManager()->get_Drawables2D() != nullptr) {
                        auto* drawables2D = EngineCore->get_ObjectManager()->get_Drawables2D();
                        std::vector<::IDrawable2D*> orderedDrawables;
                        orderedDrawables.reserve(static_cast<std::size_t>(drawables2D->Count()));
                        for (int32_t i = 0; i < drawables2D->Count(); i++) {
                            orderedDrawables.push_back((*drawables2D)[i]);
                        }

                        std::stable_sort(
                            orderedDrawables.begin(),
                            orderedDrawables.end(),
                            [](::IDrawable2D* left, ::IDrawable2D* right) {
                                const std::uint32_t leftOrder = left != nullptr ? left->get_RenderOrder2D() : 0;
                                const std::uint32_t rightOrder = right != nullptr ? right->get_RenderOrder2D() : 0;
                                return leftOrder < rightOrder;
                            });

                        for (std::size_t i = 0; i < orderedDrawables.size(); i++) {
                            ::IDrawable2D* drawable = orderedDrawables[i];
                            if (drawable != nullptr) {
                                try {
                                    drawable->Draw();
                                } catch (Exception* exception) {
                                    BootLog(std::string("frame draw2d exception: ") + (exception != nullptr ? exception->what() : "null"));
                                    delete exception;
                                    while (true) {
                                    }
                                } catch (const std::exception& exception) {
                                    BootLog(std::string("frame draw2d exception: ") + exception.what());
                                    while (true) {
                                    }
                                } catch (...) {
                                    BootLog("frame draw2d exception: unknown");
                                    while (true) {
                                    }
                                }
                            }
                        }
                    }
                }

                try {
                    gsKit_queue_exec(GsGlobal);
                    gsKit_sync_flip(GsGlobal);
                } catch (Exception* exception) {
                    BootLog(std::string("frame present exception: ") + (exception != nullptr ? exception->what() : "null"));
                    delete exception;
                    while (true) {
                    }
                } catch (const std::exception& exception) {
                    BootLog(std::string("frame present exception: ") + exception.what());
                    while (true) {
                    }
                } catch (...) {
                    BootLog("frame present exception: unknown");
                    while (true) {
                    }
                }
            } catch (Exception* exception) {
                BootLog(std::string("frame exception: ") + (exception != nullptr ? exception->what() : "null"));
                delete exception;
                while (true) {
                }
            } catch (const std::exception& exception) {
                BootLog(std::string("frame exception: ") + exception.what());
                while (true) {
                }
            } catch (...) {
                BootLog("frame exception: unknown");
                while (true) {
                }
            }
        }
    }

    std::string Ps2BootHost::ResolveApplicationDirectoryPath() const {
        return "cdrom0:\\";
    }
}
