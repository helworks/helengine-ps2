#include "platform/ps2/Ps2BootHost.hpp"

#include <dmaKit.h>
#include <debug.h>
#include <malloc.h>
#include <gsKit.h>
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
#include "Ps2DebugTexture.hpp"
#include "TextureUtils.hpp"
#include "RenderManager2D.hpp"
#include "RenderManager3D.hpp"
#include "runtime/runtime_graphics_renderer_manifest.hpp"
#include "RuntimeModel.hpp"
#include "RuntimeTexture.hpp"
#include "runtime/runtime_startup_manifest.hpp"
#include "system/io/file.hpp"
#include "system/io/path.hpp"
#include "TextLayoutUtils.hpp"
#include "TextureAsset.hpp"

namespace {
    bool DebugConsoleReady = false;

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

    GSGLOBAL* ActiveGsGlobal = nullptr;

    struct Ps2TextureRecord {
        GSTEXTURE Texture {};
        void* Pixels = nullptr;
        bool Uploaded = false;
    };

    std::unordered_map<const ::RuntimeTexture*, Ps2TextureRecord> TextureRecords;

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

    ::TextureAsset* BuildBootTextureAsset() {
        const std::vector<std::uint8_t> pixels = helengine::ps2::BuildCheckerboardDebugTexture(8, 8);
        auto* colors = new Array<std::uint8_t>(static_cast<int32_t>(pixels.size()));
        if (colors != nullptr && colors->Data != nullptr && !pixels.empty()) {
            std::memcpy(colors->Data, pixels.data(), pixels.size());
        }

        auto* texture = new ::TextureAsset();
        texture->Colors = colors;
        texture->Width = 8;
        texture->Height = 8;
        return texture;
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
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, color.W, 0x00);

            ::RuntimeTexture* runtimeTexture = sprite->get_Texture();
            if (runtimeTexture != nullptr) {
                auto textureIt = TextureRecords.find(runtimeTexture);
                if (textureIt != TextureRecords.end()) {
                    Ps2TextureRecord& record = textureIt->second;
                    if (EnsureTextureUploaded(record)) {
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
                        return;
                    }
                }
            }

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
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, color.W, 0x00);
            const double lineHeight = std::max(static_cast<double>(font->get_LineHeight()), 1.0);
            const double baseX = std::round(position.X);
            const double baseY = std::round(position.Y);
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
        }

        void DrawRoundedRect(IRoundedRectDrawable2D* shape) override {
            if (ActiveGsGlobal == 0 || shape == nullptr) {
                return;
            }

            ::Entity* parent = shape->get_Parent();
            const ::float3 position = parent != nullptr ? parent->get_Position() : ::float3();
            ::int2* size = shape->get_Size();
            const ::byte4 color = shape->get_FillColor();
            const u64 rgba = GS_SETREG_RGBAQ(color.X, color.Y, color.Z, color.W, 0x00);

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
    ::Entity* BootEntity = nullptr;
    ::SpriteComponent* BootSprite = nullptr;
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
        if (!InitializeRuntime()) {
            return 1;
        }

        if (!InitializeGraphics()) {
            return 1;
        }

        PresentBootFrame();
        return 0;
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
            BootLog("2d fallback sprite init");
            BootEntity = new Entity();
            BootEntity->InitComponents();
            BootEntity->set_LayerMask(0b00000001);
            BootEntity->set_Position(::float3(192.0f, 96.0f, 0.0f));

            BootSprite = new SpriteComponent();
            BootSprite->set_RenderOrder2D(0);
            BootSprite->set_Size(new ::int2(256, 256));
            BootSprite->set_Texture(EngineRenderManager2D->BuildTextureFromRaw(BuildBootTextureAsset()));
            BootSprite->set_Color(::byte4(255, 255, 255, 255));
            BootEntity->AddComponent(BootSprite);
            BootLog("2d fallback sprite ready");
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
        gsKit_mode_switch(GsGlobal, GS_ONESHOT);
        BootLog("graphics ready");
        return true;
    }

    bool Ps2BootHost::LoadPackagedStartupScene() {
        try {
            const char* startupSceneRelativePath = he_get_runtime_startup_scene_relative_path();
            if (startupSceneRelativePath == nullptr || startupSceneRelativePath[0] == '\0') {
                BootLog("no startup scene configured");
                return false;
            }

            Asset* startupAsset = LoadPackagedAsset(startupSceneRelativePath);
            if (startupAsset == nullptr) {
                BootLog("startup scene asset load returned null");
                return false;
            }

            SceneAsset* startupScene = static_cast<SceneAsset*>(startupAsset);
            if (EngineCore != nullptr && EngineCore->get_SceneLoadService() != nullptr) {
                EngineCore->get_SceneLoadService()->Load(startupScene);
                BootLog(std::string("startup scene loaded: ") + startupSceneRelativePath);
                return true;
            }

            BootLog("startup scene load service missing");
            return false;
        } catch (const std::exception& exception) {
            BootLog(std::string("startup scene exception: ") + exception.what());
            return false;
        } catch (...) {
            BootLog("startup scene exception: unknown");
            return false;
        }
    }

    Asset* Ps2BootHost::LoadPackagedAsset(const std::string& relativePath) {
        std::string fullPath = Path::Combine(ResolveApplicationDirectoryPath(), relativePath);
        if (!File::Exists(fullPath)) {
            BootLog(std::string("packaged asset missing: ") + fullPath);
            return nullptr;
        }

        FileStream* stream = File::OpenRead(fullPath);
        return AssetSerializer::Deserialize(stream);
    }

    void Ps2BootHost::PresentBootFrame() {
        if (GsGlobal == 0) {
            return;
        }

        BootLog("frame loop");
        while (true) {
            BootLog("frame begin");
            if (EngineCore != 0) {
                EngineCore->Update();
            }

            const u64 clearColor = GS_SETREG_RGBAQ(0x10, 0x10, 0x10, 0x00, 0x00);
            gsKit_clear(GsGlobal, clearColor);

            if (EngineCore != nullptr) {
                EngineCore->Draw();
                if (EngineCore->get_ObjectManager() != nullptr &&
                    EngineCore->get_ObjectManager()->get_Drawables2D() != nullptr) {
                    auto* drawables2D = EngineCore->get_ObjectManager()->get_Drawables2D();
                    for (int32_t i = 0; i < drawables2D->Count(); i++) {
                        ::IDrawable2D* drawable = (*drawables2D)[i];
                        if (drawable != nullptr) {
                            drawable->Draw();
                        }
                    }
                }
            }

            const u64 inputIndicatorColor = EngineInputBackend != nullptr && EngineInputBackend->ShouldShowGreenFrame()
                ? GS_SETREG_RGBAQ(0x00, 0x90, 0x30, 0x00, 0x00)
                : GS_SETREG_RGBAQ(0x90, 0x00, 0x90, 0x00, 0x00);
            gsKit_prim_sprite(GsGlobal, 20.0f, 20.0f, 52.0f, 52.0f, 0, inputIndicatorColor);

            gsKit_queue_exec(GsGlobal);
            gsKit_sync_flip(GsGlobal);
            BootLog("frame present");
        }
    }

    std::string Ps2BootHost::ResolveApplicationDirectoryPath() const {
        return "cdrom0:\\";
    }
}
