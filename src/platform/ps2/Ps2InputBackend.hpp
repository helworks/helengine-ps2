#pragma once

#include <libpad.h>

#include "IInputBackend.hpp"
#include "InputFrameState.hpp"
#include "InputGamepadState.hpp"
#include "KeyboardState.hpp"
#include "MouseState.hpp"
#include "platform/ps2/Ps2PadInputMapper.hpp"

namespace helengine::ps2 {
    class Ps2InputBackend final : public IInputBackend {
    public:
        Ps2InputBackend();

        bool Initialize();

        InputFrameState CaptureFrame() override;

        bool ShouldShowGreenFrame() const;

    private:
        static Ps2PadButtons DecodeButtons(const padButtonStatus& buttons);

        InputGamepadState CaptureGamepadState() const;

        KeyboardState CaptureKeyboardState() const;

        MouseState CaptureMouseState() const;

        void Refresh();

        int Port;
        int Slot;
        bool IsPadAvailable;
        bool ShowGreenFrame;
        Ps2PadButtons CurrentButtons;
        Ps2PadButtons PreviousButtons;
        alignas(64) char PadBuffer[256];
    };
}
