#pragma once

#include <cstdint>
#include <vector>

#include "Keys.hpp"

namespace helengine::ps2 {
    struct Ps2PadButtons {
        bool Cross = false;
        bool Circle = false;
        bool Square = false;
        bool Triangle = false;
        bool DpadUp = false;
        bool DpadDown = false;
        bool DpadLeft = false;
        bool DpadRight = false;
        bool L1 = false;
        bool L2 = false;
        bool L3 = false;
        bool R1 = false;
        bool R2 = false;
        bool R3 = false;
        bool Start = false;
        bool Select = false;
    };

    inline bool WasButtonJustPressed(bool current, bool previous) {
        return current && !previous;
    }

    inline bool ShouldToggleBootColor(const Ps2PadButtons& current, const Ps2PadButtons& previous) {
        return WasButtonJustPressed(current.Start, previous.Start);
    }

    inline std::vector<::Keys> MapPadButtonsToKeys(const Ps2PadButtons& buttons) {
        std::vector<::Keys> keys;
        if (buttons.Cross) {
            keys.push_back(::Keys::Space);
        }
        if (buttons.Circle) {
            keys.push_back(::Keys::Enter);
        }
        if (buttons.Square) {
            keys.push_back(::Keys::Escape);
        }
        if (buttons.Triangle) {
            keys.push_back(::Keys::Tab);
        }
        if (buttons.DpadUp) {
            keys.push_back(::Keys::Up);
        }
        if (buttons.DpadDown) {
            keys.push_back(::Keys::Down);
        }
        if (buttons.DpadLeft) {
            keys.push_back(::Keys::Left);
        }
        if (buttons.DpadRight) {
            keys.push_back(::Keys::Right);
        }
        if (buttons.L1) {
            keys.push_back(::Keys::LeftShift);
        }
        if (buttons.L2) {
            keys.push_back(::Keys::LeftControl);
        }
        if (buttons.L3) {
            keys.push_back(::Keys::LeftWindows);
        }
        if (buttons.R1) {
            keys.push_back(::Keys::RightShift);
        }
        if (buttons.R2) {
            keys.push_back(::Keys::RightControl);
        }
        if (buttons.R3) {
            keys.push_back(::Keys::RightWindows);
        }
        if (buttons.Start) {
            keys.push_back(::Keys::Enter);
        }
        if (buttons.Select) {
            keys.push_back(::Keys::Escape);
        }

        return keys;
    }
}
