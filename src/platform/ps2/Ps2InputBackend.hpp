#pragma once

#include <libpad.h>

#include "IInputBackend.hpp"
#include "InputFrameState.hpp"
#include "InputGamepadState.hpp"
#include "platform/ps2/Ps2PadInputMapper.hpp"

namespace helengine::ps2 {
    class Ps2InputBackend final : public IInputBackend {
    public:
        Ps2InputBackend();

        bool Initialize();

        InputFrameState CaptureFrame() override;

        bool ShouldShowGreenFrame() const;

    private:
        /// <summary>
        /// Converts the controller packet into shared button and analog-stick state while treating unavailable analog data as centered.
        /// </summary>
        static Ps2PadButtons DecodeButtons(const padButtonStatus& buttons, bool analogAvailable);

        /// <summary>
        /// Converts one unsigned PS2 stick axis into a centered signed axis and suppresses small calibration noise.
        /// </summary>
        static int16_t NormalizeAnalogAxis(unsigned char value);

        InputGamepadState CaptureGamepadState() const;

        void Refresh();

        Array<InputGamepadState>* PrimaryCachedGamepads;
        Array<InputGamepadState>* SecondaryCachedGamepads;
        bool UsePrimaryCachedGamepads;

        int Port;
        int Slot;
        bool IsPadAvailable;
        bool AnalogAvailable;
        bool ShowGreenFrame;
        Ps2PadButtons CurrentButtons;
        Ps2PadButtons PreviousButtons;
        alignas(64) char PadBuffer[256];
    };
}
