#pragma once

#include <cstdint>

enum class Ps2MaterialAlphaMode : std::int32_t {
    Opaque = 0,
    AlphaTest = 1,
    AlphaBlend = 2,
    Additive = 3
};
