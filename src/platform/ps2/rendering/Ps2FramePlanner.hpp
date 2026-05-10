#pragma once

#include <vector>

namespace helengine::ps2 {
    class Ps2FramePlan;
    class Ps2RenderProxy;

    class Ps2FramePlanner {
    public:
        Ps2FramePlan Build(const std::vector<Ps2RenderProxy>& proxies) const;
    };
}
