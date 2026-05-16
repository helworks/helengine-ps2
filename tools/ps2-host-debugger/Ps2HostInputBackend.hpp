#pragma once

#include "IInputBackend.hpp"

class InputFrameState;

namespace helengine::ps2::host {
    /// <summary>
    /// Supplies one empty input frame for host-debug sessions.
    /// </summary>
    class Ps2HostInputBackend final : public ::IInputBackend {
    public:
        Ps2HostInputBackend();

        InputFrameState CaptureFrame() override;
    };
}
