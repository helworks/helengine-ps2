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
    /// Identifies the isolated textured near-plane clipping entry point used only by intersecting source slices.
    /// </summary>
    constexpr std::uint16_t TexturedClipMicroProgramAddress = 320u;
}
