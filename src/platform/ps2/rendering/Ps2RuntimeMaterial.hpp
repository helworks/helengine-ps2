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
        std::uint8_t GetBaseColorR() const;
        std::uint8_t GetBaseColorG() const;
        std::uint8_t GetBaseColorB() const;
        std::uint8_t GetBaseColorA() const;
        bool GetDoubleSided() const;
        bool GetCastShadows() const;
        bool GetExpensiveModeAllowed() const;
        float GetRoughness() const;
        float GetSpecularStrength() const;
        float GetEmissiveStrength() const;
        bool UsesVertexColor() const;
        void LoadFromCooked(::Ps2MaterialAsset* materialAsset);

    private:
        ::Ps2MaterialAlphaMode AlphaMode;
        bool DoubleSided;
        bool CastShadows;
        bool ExpensiveModeAllowed;
        ::Ps2MaterialLightingMode LightingMode;
        ::Ps2RenderClass RenderClass;
        std::uint8_t BaseColorR;
        std::uint8_t BaseColorG;
        std::uint8_t BaseColorB;
        std::uint8_t BaseColorA;
        float Roughness;
        float SpecularStrength;
        float EmissiveStrength;
        std::string TextureRelativePath;
        bool UseVertexColor;
    };
}
