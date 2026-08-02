#pragma once

#include <cstdint>

namespace helengine::ps2 {
    /// <summary>
    /// Identifies the VU1 micro-memory entry point for untextured opaque draws.
    /// </summary>
    constexpr std::uint16_t UntexturedMicroProgramAddress = 0u;

    /// <summary>
    /// Identifies the unchanged fast textured opaque draw entry point used by fully visible source slices.
    /// </summary>
    constexpr std::uint16_t TexturedMicroProgramAddress = 64u;

    /// <summary>
    /// Identifies the textured pretransformed entry point used by host-generated clipped batches.
    /// </summary>
    constexpr std::uint16_t TexturedPretransformedMicroProgramAddress = 320u;
}
