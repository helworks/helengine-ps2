#pragma once

#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"

typedef struct gsGlobal GSGLOBAL;

namespace helengine::ps2 {
    class Ps2VuGifStateEncoder final {
    public:
        void EncodeOpaqueState(const Ps2VuOpaqueBatch& batch, GSGLOBAL* gsGlobal) const;
    };
}
