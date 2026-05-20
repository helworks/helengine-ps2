#pragma once

#include <cstdint>

#include "float4x4.hpp"

namespace helengine::ps2 {
    class Ps2RenderProxy;
    class Ps2RuntimeMaterial;
    class Ps2VuPackedModel;

    struct Ps2VuOpaqueBatch final {
        const Ps2RenderProxy* Proxy;
        const Ps2VuPackedModel* Model;
        const Ps2RuntimeMaterial* Material;
        bool Textured;
    };

    struct Ps2VuOpaqueBatchInstance final {
        Ps2VuOpaqueBatch Batch;
        ::float4x4 World;
    };
}
