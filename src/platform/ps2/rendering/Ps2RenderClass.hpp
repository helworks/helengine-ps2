#pragma once

#include <cstdint>

enum class Ps2RenderClass : std::int32_t {
    Opaque = 0,
    AlphaTest = 1,
    Transparent = 2
};
