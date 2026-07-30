#pragma once

#include <cstdint>
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
        int16_t LeftStickX = 0;
        int16_t LeftStickY = 0;
    };

    inline bool WasButtonJustPressed(bool current, bool previous) {
        return current && !previous;
    }

    inline bool ShouldToggleBootColor(const Ps2PadButtons& current, const Ps2PadButtons& previous) {
        return WasButtonJustPressed(current.Start, previous.Start);
    }

}
