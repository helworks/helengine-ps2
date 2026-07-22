#pragma once

#include <cstddef>

namespace helengine::ps2 {
    struct Ps2RenderPerformanceMetrics final {
        double ProxySyncMilliseconds = 0.0;
        double FramePlanMilliseconds = 0.0;
        double VuBatchBuildMilliseconds = 0.0;
        double PacketEncodeMilliseconds = 0.0;
        double VifReuseWaitMilliseconds = 0.0;
        double VifSubmitMilliseconds = 0.0;
        double GifDrainMilliseconds = 0.0;
        double LegacyOpaqueMilliseconds = 0.0;
        std::size_t SubmittedTriangleCount = 0u;
        std::size_t LegacyOpaqueTriangleCount = 0u;
        std::size_t VifPacketCount = 0u;
        std::size_t VifPacketByteCount = 0u;
        std::size_t CompatibleUntexturedGroupCount = 0u;
    };
}
