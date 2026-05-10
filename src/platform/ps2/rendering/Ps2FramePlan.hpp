#pragma once

#include <vector>

namespace helengine::ps2 {
    class Ps2RenderProxy;

    class Ps2FramePlan {
    public:
        Ps2FramePlan();

        std::vector<const Ps2RenderProxy*> OpaqueDynamic;
        std::vector<const Ps2RenderProxy*> OpaqueWorld;
        std::vector<const Ps2RenderProxy*> AlphaDynamic;
        std::vector<const Ps2RenderProxy*> AlphaWorld;
    };
}
