#pragma once

#include "platform/ps2/rendering/vu/Ps2VuProgramKind.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"

namespace helengine::ps2 {
    class Ps2VuProgramRegistry final {
    public:
        Ps2VuProgramKind ResolveOpaqueProgram(const Ps2VuOpaqueBatch& batch) const;
    };
}
