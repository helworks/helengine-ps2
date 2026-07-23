#pragma once

#include <cstddef>
#include <stdexcept>

#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"
#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

namespace helengine::ps2 {
    struct Ps2VuOpaqueBatchSlice final {
        const Ps2VuOpaqueBatch* Batch = nullptr;
        std::size_t FirstSourceTriangle = 0u;
        std::size_t SourceTriangleCount = 0u;

        static Ps2VuOpaqueBatchSlice Create(
            const Ps2VuOpaqueBatch& batch,
            std::size_t firstSourceTriangle,
            std::size_t sourceTriangleCount) {
            if (batch.Model == nullptr) {
                throw std::invalid_argument("PS2 VU opaque batch slices require a packed model.");
            }
            if (sourceTriangleCount == 0u) {
                throw std::invalid_argument("PS2 VU opaque batch slices require at least one source triangle.");
            }

            const std::size_t availableSourceTriangleCount = static_cast<std::size_t>(batch.Model->GetTriangleVertexCount()) / 3u;
            if (firstSourceTriangle >= availableSourceTriangleCount
                || sourceTriangleCount > (availableSourceTriangleCount - firstSourceTriangle)) {
                throw std::out_of_range("PS2 VU opaque batch slice exceeds the packed model source-triangle range.");
            }

            return Ps2VuOpaqueBatchSlice { &batch, firstSourceTriangle, sourceTriangleCount };
        }
    };
}
