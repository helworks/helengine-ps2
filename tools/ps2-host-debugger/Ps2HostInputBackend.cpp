#include "Ps2HostInputBackend.hpp"

#include "InputFrameState.hpp"

namespace helengine::ps2::host {
    Ps2HostInputBackend::Ps2HostInputBackend() {
    }

    InputFrameState Ps2HostInputBackend::CaptureFrame() {
        return ::InputFrameState();
    }
}
