#pragma once

#include <cstdint>

namespace helengine::ps2 {
    struct alignas(16) Ps2VuUntexturedBatchHeader final {
        std::uint32_t TriangleCount;
        std::uint32_t OutputGifQwordOffset;
        std::uint32_t ConstantsQwordOffset;
        std::uint32_t TriangleInputQwordOffset;
    };

    struct alignas(16) Ps2VuUntexturedBatchConstants final {
        float WorldViewProjectionMatrix[16];
        float GsScale[4];
        float GsOffset[4];
    };

    struct alignas(16) Ps2VuUntexturedTriangleInput final {
        float PositionA[4];
        float PositionB[4];
        float PositionC[4];
    };
}
