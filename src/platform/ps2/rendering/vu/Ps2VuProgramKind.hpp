#pragma once

#include <cstdint>

namespace helengine::ps2 {
    enum class Ps2VuProgramKind : std::uint8_t {
        OpaqueUntextured = 0,
        OpaqueTextured = 1
    };
}
