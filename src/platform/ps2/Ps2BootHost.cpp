#include "platform/ps2/Ps2BootHost.hpp"

#include <libpad.h>

#include <dmaKit.h>
#include <debug.h>
#include <loadfile.h>
#include <gsKit.h>
#include <sifrpc.h>
#include <cstdio>

#include "Core.hpp"
#include "CoreInitializationOptions.hpp"
#include "InputManager.hpp"
#include "Keyboard.hpp"
#include "KeyboardState.hpp"
#include "ModelAsset.hpp"
#include "Mouse.hpp"
#include "MouseState.hpp"
#include "Ps2PadInputMapper.hpp"
#include "RenderManager2D.hpp"
#include "RenderManager3D.hpp"
#include "RuntimeModel.hpp"
#include "RuntimeTexture.hpp"
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

    using helengine::ps2::MapPadButtonsToKeys;
    using helengine::ps2::Ps2PadButtons;
    using helengine::ps2::ShouldToggleBootColor;

    class Ps2PadInputBridge final {
    public:
        bool Initialize() {
            BootLog("pad io init");
            SifInitRpc(0);

            int sioResult = SifLoadModule("rom0:SIO2MAN", 0, nullptr);
            BootLog(sioResult >= 0 ? "SIO2MAN loaded" : "SIO2MAN load failed");
            if (sioResult < 0) {
                return false;
            }

            int padResult = SifLoadModule("rom0:PADMAN", 0, nullptr);
            BootLog(padResult >= 0 ? "PADMAN loaded" : "PADMAN load failed");
            if (padResult < 0) {
                return false;
            }

            BootLog("pad init");
            padInit(0);
            Port = 0;
            Slot = 0;
            BootLog("pad port open");
            IsPadAvailable = padPortOpen(Port, Slot, PadBuffer) != 0;
            BootLog(IsPadAvailable ? "pad port open ok" : "pad port open failed");
            if (IsPadAvailable) {
                Refresh();
            }

            return IsPadAvailable;
        }

        void Refresh() {
            if (!IsPadAvailable) {
                PreviousButtons = CurrentButtons;
                CurrentButtons = {};
                return;
            }

            const int state = padGetState(Port, Slot);
            if (!HasLoggedPadSample) {
                BootLog("pad sample");
                HasLoggedPadSample = true;
            }
            if (state != PAD_STATE_STABLE && state != PAD_STATE_FINDCTP1) {
                return;
            }

            padButtonStatus buttons {};
            if (padRead(Port, Slot, &buttons) == 0) {
                return;
            }

            PreviousButtons = CurrentButtons;
            CurrentButtons = DecodeButtons(buttons);
            if (ShouldToggleBootColor(CurrentButtons, PreviousButtons)) {
                ShowGreenFrame = !ShowGreenFrame;
            }
        }

        KeyboardState BuildKeyboardState() const {
            List<::Keys> pressedKeys;
            std::vector<::Keys> mappedKeys = MapPadButtonsToKeys(CurrentButtons);
            for (const ::Keys key : mappedKeys) {
                pressedKeys.Add(key);
            }

            return KeyboardState(&pressedKeys, false, false);
        }

        MouseState BuildMouseState() const {
            return MouseState();
        }

        bool ShouldShowGreenFrame() const {
            return ShowGreenFrame;
        }

    private:
        static Ps2PadButtons DecodeButtons(const padButtonStatus& buttons) {
            const uint16_t padData = static_cast<uint16_t>(0xffff ^ buttons.btns);

            Ps2PadButtons snapshot;
            snapshot.Cross = (padData & PAD_CROSS) != 0;
            snapshot.Circle = (padData & PAD_CIRCLE) != 0;
            snapshot.Square = (padData & PAD_SQUARE) != 0;
            snapshot.Triangle = (padData & PAD_TRIANGLE) != 0;
            snapshot.DpadUp = (padData & PAD_UP) != 0;
            snapshot.DpadDown = (padData & PAD_DOWN) != 0;
            snapshot.DpadLeft = (padData & PAD_LEFT) != 0;
            snapshot.DpadRight = (padData & PAD_RIGHT) != 0;
            snapshot.L1 = (padData & PAD_L1) != 0;
            snapshot.L2 = (padData & PAD_L2) != 0;
            snapshot.L3 = (padData & PAD_L3) != 0;
            snapshot.R1 = (padData & PAD_R1) != 0;
            snapshot.R2 = (padData & PAD_R2) != 0;
            snapshot.R3 = (padData & PAD_R3) != 0;
            snapshot.Start = (padData & PAD_START) != 0;
            snapshot.Select = (padData & PAD_SELECT) != 0;
            return snapshot;
        }

        int Port = 0;
        int Slot = 0;
        bool IsPadAvailable = false;
        bool HasLoggedPadSample = false;
        bool ShowGreenFrame = false;
        Ps2PadButtons CurrentButtons {};
        Ps2PadButtons PreviousButtons {};
        alignas(64) char PadBuffer[256] {};
    };

    class Ps2Keyboard final : public Keyboard {
    public:
        explicit Ps2Keyboard(Ps2PadInputBridge& padInput)
            : PadInput(padInput)
            , IsActive(false) {
        }

        KeyboardState GetState() override {
            if (!IsActive) {
                return KeyboardState();
            }

            return PadInput.BuildKeyboardState();
        }

        void SetActive(bool isActive) override {
            IsActive = isActive;
        }

    private:
        Ps2PadInputBridge& PadInput;
        bool IsActive;
    };

    class Ps2Mouse final : public Mouse {
    public:
        explicit Ps2Mouse(Ps2PadInputBridge& padInput)
            : PadInput(padInput) {
        }

        MouseState GetState() override {
            return PadInput.BuildMouseState();
        }

        void SetPosition(int32_t x, int32_t y) override {
            (void)x;
            (void)y;
        }

    private:
        Ps2PadInputBridge& PadInput;
    };

    class Ps2InputManager final : public InputManager {
    public:
        Ps2InputManager(::Keyboard* keyboard, ::Mouse* mouse) {
            set_Keyboard(keyboard);
            set_Mouse(mouse);
        }
    };

    class Ps2RenderManager2D final : public RenderManager2D {
    public:
        RuntimeTexture* BuildTextureFromRaw(TextureAsset* data) override {
            RuntimeTexture* texture = new RuntimeTexture();
            if (data != nullptr) {
                texture->set_Id(data->get_Id());
                texture->set_Width(static_cast<int32_t>(data->Width));
                texture->set_Height(static_cast<int32_t>(data->Height));
            }

            return texture;
        }

        void DrawSprite(ISpriteDrawable2D* sprite) override {
            (void)sprite;
        }

        void DrawText(ITextDrawable2D* text) override {
            (void)text;
        }

        void DrawRoundedRect(IRoundedRectDrawable2D* shape) override {
            (void)shape;
        }
    };

    class Ps2RenderManager3D final : public RenderManager3D {
    public:
        RuntimeModel* BuildModelFromRaw(ModelAsset* data) override {
            RuntimeModel* model = new RuntimeModel();
            if (data != nullptr) {
                model->set_Id(data->get_Id());
            }

            return model;
        }

        void Draw() override {
        }
    };

    Ps2PadInputBridge PadInputBackend;
    Ps2Keyboard KeyboardBackend{PadInputBackend};
    Ps2Mouse MouseBackend{PadInputBackend};
    Ps2InputManager* InputManagerBackend = nullptr;
    Ps2RenderManager2D RenderManager2DBackend;
    Ps2RenderManager3D RenderManager3DBackend;
}

namespace helengine::ps2 {
    Ps2BootHost::Ps2BootHost()
        : EngineCore(0),
          EngineOptions(0),
          EngineInputManager(0),
          EngineRenderManager2D(0),
          EngineRenderManager3D(0),
          GsGlobal(0) {
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
        EngineOptions = new CoreInitializationOptions();
        EngineOptions->set_ContentRootPath(".");
        EngineOptions->set_UpdateOrderLayers(1);
        EngineOptions->set_RenderOrderLayers3D(1);
        EngineOptions->set_UpdateListInitialCapacity(4);
        EngineOptions->set_RenderList2DInitialCapacity(4);
        EngineOptions->set_RenderList3DInitialCapacity(4);

        BootLog("core creating");
        EngineCore = new Core(EngineOptions);
        BootLog("core created");

        BootLog("input bridge init");
        if (!PadInputBackend.Initialize()) {
            BootLog("pad bridge init failed");
            return false;
        }
        BootLog("input bridge ready");
        InputManagerBackend = new Ps2InputManager(&KeyboardBackend, &MouseBackend);
        EngineInputManager = InputManagerBackend;
        EngineInputManager->SetKeyboardActive(true);

        EngineRenderManager2D = &RenderManager2DBackend;
        EngineRenderManager3D = &RenderManager3DBackend;

        BootLog("core initialize begin");
        EngineCore->Initialize(
            EngineRenderManager3D,
            EngineRenderManager2D,
            EngineInputManager,
            EngineOptions);
        BootLog("core initialized");

        return true;
    }

    bool Ps2BootHost::InitializeGraphics() {
        BootLog("graphics init");
        BootLog("gsKit init global begin");
        GsGlobal = gsKit_init_global();

        if (GsGlobal == 0) {
            BootLog("gsKit_init_global failed");
            return false;
        }

        GsGlobal->Mode = GS_MODE_NTSC;
        GsGlobal->PSM = GS_PSM_CT32;
        GsGlobal->DoubleBuffering = GS_SETTING_OFF;
        GsGlobal->ZBuffering = GS_SETTING_OFF;

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

    void Ps2BootHost::PresentBootFrame() {
        if (GsGlobal == 0) {
            return;
        }

        BootLog("frame loop");
        while (true) {
            BootLog("frame begin");
            PadInputBackend.Refresh();

            if (EngineCore != 0) {
                EngineCore->Update();
                EngineCore->Draw();
            }

            const u64 clearColor = PadInputBackend.ShouldShowGreenFrame()
                ? GS_SETREG_RGBAQ(0x00, 0x80, 0x00, 0x00, 0x00)
                : GS_SETREG_RGBAQ(0x80, 0x00, 0x80, 0x00, 0x00);
            gsKit_clear(GsGlobal, clearColor);
            gsKit_queue_exec(GsGlobal);
            gsKit_sync_flip(GsGlobal);
            BootLog("frame present");
        }
    }
}
