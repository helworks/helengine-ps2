#pragma once

#include <vector>

#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"

namespace helengine::ps2 {
    class Ps2FramePlan;
    class Ps2RenderProxy;

    class Ps2VuOpaqueBatchBuilder final {
    public:
        std::vector<Ps2VuOpaqueBatch> Build(const Ps2FramePlan& plan) const;
        std::size_t GetLastRejectedMissingMaterialCount() const;
        std::size_t GetLastRejectedMissingModelCount() const;
        std::size_t GetLastRejectedMissingPackedModelCount() const;

    private:
        void AppendProxyBatches(const std::vector<const Ps2RenderProxy*>& proxies, std::vector<Ps2VuOpaqueBatch>& batches) const;

        mutable std::size_t LastRejectedMissingMaterialCount = 0;
        mutable std::size_t LastRejectedMissingModelCount = 0;
        mutable std::size_t LastRejectedMissingPackedModelCount = 0;
    };
}
