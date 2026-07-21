#include "platform/ps2/Ps2InputBackend.hpp"

#include <cstdlib>
#include <loadfile.h>
#include <sifrpc.h>

#include "InputGamepadButton.hpp"
#include "runtime/array.hpp"

namespace helengine::ps2 {
    Ps2InputBackend::Ps2InputBackend()
        : PrimaryCachedGamepads(new Array<InputGamepadState>(1)),
          SecondaryCachedGamepads(new Array<InputGamepadState>(1)),
          UsePrimaryCachedGamepads(true),
          Port(0),
          Slot(0),
          IsPadAvailable(false),
          AnalogAvailable(false),
          ShowGreenFrame(false),
          CurrentButtons(),
          PreviousButtons(),
          PadBuffer() {
    }

    bool Ps2InputBackend::Initialize() {
        SifInitRpc(0);

        int sioResult = SifLoadModule("rom0:SIO2MAN", 0, nullptr);
        if (sioResult < 0) {
            return false;
        }

        int padResult = SifLoadModule("rom0:PADMAN", 0, nullptr);
        if (padResult < 0) {
            return false;
        }

        padInit(0);
        IsPadAvailable = padPortOpen(Port, Slot, PadBuffer) != 0;
        if (IsPadAvailable) {
            padSetMainMode(Port, Slot, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK);
            Refresh();
        }

        return IsPadAvailable;
    }

    InputFrameState Ps2InputBackend::CaptureFrame() {
        Refresh();

        InputFrameState frame;

        InputGamepadState gamepad = CaptureGamepadState();
        if (gamepad.get_Connected()) {
            Array<InputGamepadState>* gamepads = UsePrimaryCachedGamepads ? PrimaryCachedGamepads : SecondaryCachedGamepads;
            (*gamepads)[0] = gamepad;
            frame.set_Gamepads(gamepads);
            frame.set_GamepadCount(1);
            UsePrimaryCachedGamepads = !UsePrimaryCachedGamepads;
        } else {
            frame.set_Gamepads(Array<InputGamepadState>::Empty());
            frame.set_GamepadCount(0);
        }

        return frame;
    }

    bool Ps2InputBackend::ShouldShowGreenFrame() const {
        return ShowGreenFrame;
    }

    Ps2PadButtons Ps2InputBackend::DecodeButtons(const padButtonStatus& buttons, bool analogAvailable) {
        uint16_t padData = static_cast<uint16_t>(0xffff ^ buttons.btns);

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
        if (analogAvailable) {
            snapshot.LeftStickX = NormalizeAnalogAxis(buttons.ljoy_h);
            snapshot.LeftStickY = NormalizeAnalogAxis(buttons.ljoy_v);
        }
        return snapshot;
    }

    int16_t Ps2InputBackend::NormalizeAnalogAxis(unsigned char value) {
        constexpr int32_t AnalogCenter = 128;
        constexpr int32_t AnalogScale = 256;
        constexpr int32_t AnalogDeadzone = 12;
        int32_t centeredValue = static_cast<int32_t>(value) - AnalogCenter;
        if (std::abs(centeredValue) <= AnalogDeadzone) {
            return 0;
        }

        return static_cast<int16_t>(centeredValue * AnalogScale);
    }

    InputGamepadState Ps2InputBackend::CaptureGamepadState() const {
        InputGamepadState gamepad;
        gamepad.set_Connected(IsPadAvailable);
        if (!IsPadAvailable) {
            return gamepad;
        }

        gamepad.SetButtonDown(InputGamepadButton::South, CurrentButtons.Cross);
        gamepad.SetButtonDown(InputGamepadButton::East, CurrentButtons.Circle);
        gamepad.SetButtonDown(InputGamepadButton::West, CurrentButtons.Square);
        gamepad.SetButtonDown(InputGamepadButton::North, CurrentButtons.Triangle);
        gamepad.SetButtonDown(InputGamepadButton::LeftShoulder, CurrentButtons.L1);
        gamepad.SetButtonDown(InputGamepadButton::RightShoulder, CurrentButtons.R1);
        gamepad.SetButtonDown(InputGamepadButton::LeftStick, CurrentButtons.L3);
        gamepad.SetButtonDown(InputGamepadButton::RightStick, CurrentButtons.R3);
        gamepad.SetButtonDown(InputGamepadButton::LeftTrigger, CurrentButtons.L2);
        gamepad.SetButtonDown(InputGamepadButton::RightTrigger, CurrentButtons.R2);
        gamepad.SetButtonDown(InputGamepadButton::DPadUp, CurrentButtons.DpadUp);
        gamepad.SetButtonDown(InputGamepadButton::DPadDown, CurrentButtons.DpadDown);
        gamepad.SetButtonDown(InputGamepadButton::DPadLeft, CurrentButtons.DpadLeft);
        gamepad.SetButtonDown(InputGamepadButton::DPadRight, CurrentButtons.DpadRight);
        gamepad.SetButtonDown(InputGamepadButton::Start, CurrentButtons.Start);
        gamepad.SetButtonDown(InputGamepadButton::Select, CurrentButtons.Select);
        gamepad.set_LeftTrigger(CurrentButtons.L2 ? 32767 : 0);
        gamepad.set_RightTrigger(CurrentButtons.R2 ? 32767 : 0);
        gamepad.set_LeftStickX(CurrentButtons.LeftStickX);
        gamepad.set_LeftStickY(CurrentButtons.LeftStickY);
        return gamepad;
    }

    void Ps2InputBackend::Refresh() {
        if (!IsPadAvailable) {
            PreviousButtons = CurrentButtons;
            CurrentButtons = Ps2PadButtons();
            return;
        }

        int state = padGetState(Port, Slot);
        if (state != PAD_STATE_STABLE && state != PAD_STATE_FINDCTP1) {
            return;
        }

        padButtonStatus buttons {};
        if (padRead(Port, Slot, &buttons) == 0) {
            return;
        }

        int padType = padInfoMode(Port, Slot, PAD_MODECURID, 0);
        AnalogAvailable = padType == PAD_TYPE_DUALSHOCK || padType == PAD_TYPE_ANALOG;
        PreviousButtons = CurrentButtons;
        CurrentButtons = DecodeButtons(buttons, AnalogAvailable);
        if (ShouldToggleBootColor(CurrentButtons, PreviousButtons)) {
            ShowGreenFrame = !ShowGreenFrame;
        }
    }
}
