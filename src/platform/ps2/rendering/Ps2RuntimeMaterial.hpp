#pragma once

#include "Ps2MaterialAlphaMode.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "Ps2RenderClass.hpp"
#include "RuntimeMaterial.hpp"

class Ps2MaterialAsset;

namespace helengine::ps2 {
    class Ps2RuntimeMaterial final : public ::RuntimeMaterial {
    public:
        Ps2RuntimeMaterial();

        ::Ps2MaterialAlphaMode GetAlphaMode() const;
        ::Ps2MaterialLightingMode GetLightingMode() const;
        ::Ps2RenderClass GetRenderClass() const;
        const std::string& GetTextureRelativePath() const;
        bool GetDoubleSided() const;
        bool GetExpensiveModeAllowed() const;
        bool UsesVertexColor() const;
        void LoadFromCooked(::Ps2MaterialAsset* materialAsset);

    private:
        ::Ps2MaterialAlphaMode AlphaMode;
        bool DoubleSided;
        bool ExpensiveModeAllowed;
        ::Ps2MaterialLightingMode LightingMode;
        ::Ps2RenderClass RenderClass;
        std::string TextureRelativePath;
        bool UseVertexColor;
    };
}
