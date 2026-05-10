#pragma once

#include <cstdint>

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
}
