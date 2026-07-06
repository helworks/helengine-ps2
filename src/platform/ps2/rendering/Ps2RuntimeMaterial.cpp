#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"

#include <cstdio>
#include <stdexcept>

#include "PlatformMaterialAsset.hpp"
#include <Ps2MaterialAlphaMode.hpp>
#include "Ps2MaterialAsset.hpp"
#include <Ps2MaterialLightingMode.hpp>
#include <Ps2RenderClass.hpp>
namespace helengine::ps2 {
    Ps2RuntimeMaterial::Ps2RuntimeMaterial()
        : AlphaMode(::Ps2MaterialAlphaMode::Opaque),
          DoubleSided(false),
          CastShadows(false),
          ExpensiveModeAllowed(false),
          LightingMode(::Ps2MaterialLightingMode::Unlit),
          RenderClass(::Ps2RenderClass::Opaque),
          BaseColorR(0xFF),
          BaseColorG(0xFF),
          BaseColorB(0xFF),
          BaseColorA(0xFF),
          Roughness(0.5f),
          SpecularStrength(0.5f),
          EmissiveStrength(0.0f),
          HasTextureRelativePathValue(false),
          TextureRelativePath(),
          UseVertexColor(false) {
    }

    ::Ps2MaterialAlphaMode Ps2RuntimeMaterial::GetAlphaMode() const {
        return AlphaMode;
    }

    ::Ps2MaterialLightingMode Ps2RuntimeMaterial::GetLightingMode() const {
        return LightingMode;
    }

    ::Ps2RenderClass Ps2RuntimeMaterial::GetRenderClass() const {
        return RenderClass;
    }

    bool Ps2RuntimeMaterial::HasTextureRelativePath() const {
        return HasTextureRelativePathValue;
    }

    const std::string& Ps2RuntimeMaterial::GetTextureRelativePath() const {
        return TextureRelativePath;
    }

    std::uint8_t Ps2RuntimeMaterial::GetBaseColorR() const {
        return BaseColorR;
    }

    std::uint8_t Ps2RuntimeMaterial::GetBaseColorG() const {
        return BaseColorG;
    }

    std::uint8_t Ps2RuntimeMaterial::GetBaseColorB() const {
        return BaseColorB;
    }

    std::uint8_t Ps2RuntimeMaterial::GetBaseColorA() const {
        return BaseColorA;
    }

    bool Ps2RuntimeMaterial::GetDoubleSided() const {
        return DoubleSided;
    }

    bool Ps2RuntimeMaterial::GetCastShadows() const {
        return CastShadows;
    }

    bool Ps2RuntimeMaterial::GetExpensiveModeAllowed() const {
        return ExpensiveModeAllowed;
    }

    float Ps2RuntimeMaterial::GetRoughness() const {
        return Roughness;
    }

    float Ps2RuntimeMaterial::GetSpecularStrength() const {
        return SpecularStrength;
    }

    float Ps2RuntimeMaterial::GetEmissiveStrength() const {
        return EmissiveStrength;
    }

    bool Ps2RuntimeMaterial::UsesVertexColor() const {
        return UseVertexColor;
    }

    void Ps2RuntimeMaterial::LoadFromCooked(::PlatformMaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material data is required.");
        }

        ::Ps2MaterialAsset* ps2MaterialAsset = dynamic_cast<::Ps2MaterialAsset*>(materialAsset);
        if (ps2MaterialAsset != nullptr) {
            LoadFromCooked(ps2MaterialAsset);
            return;
        }

        this->set_Id(materialAsset->get_Id());
        BaseColorR = materialAsset->BaseColorR;
        BaseColorG = materialAsset->BaseColorG;
        BaseColorB = materialAsset->BaseColorB;
        BaseColorA = materialAsset->BaseColorA;
        LightingMode = materialAsset->Lit ? ::Ps2MaterialLightingMode::SimpleLit : ::Ps2MaterialLightingMode::Unlit;
        AlphaMode = ::Ps2MaterialAlphaMode::Opaque;
        RenderClass = ::Ps2RenderClass::Opaque;
        HasTextureRelativePathValue = !materialAsset->TextureRelativePath.empty();
        TextureRelativePath = HasTextureRelativePathValue ? materialAsset->TextureRelativePath : std::string();
        DoubleSided = materialAsset->DoubleSided;
        UseVertexColor = materialAsset->UseVertexColor;
        CastShadows = false;
        ExpensiveModeAllowed = false;
        Roughness = 0.5f;
        SpecularStrength = 0.5f;
        EmissiveStrength = 0.0f;
    }

    void Ps2RuntimeMaterial::LoadFromCooked(::Ps2MaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material data is required.");
        }

        this->set_Id(materialAsset->get_Id());
        LightingMode = materialAsset->LightingMode;
        AlphaMode = materialAsset->AlphaMode;
        RenderClass = materialAsset->RenderClass;
        BaseColorR = materialAsset->BaseColorR;
        BaseColorG = materialAsset->BaseColorG;
        BaseColorB = materialAsset->BaseColorB;
        BaseColorA = materialAsset->BaseColorA;
        HasTextureRelativePathValue = !materialAsset->TextureRelativePath.empty();
        TextureRelativePath = HasTextureRelativePathValue ? materialAsset->TextureRelativePath : std::string();
        DoubleSided = materialAsset->DoubleSided;
        CastShadows = materialAsset->CastShadows;
        UseVertexColor = materialAsset->UseVertexColor;
        ExpensiveModeAllowed = materialAsset->ExpensiveModeAllowed;
        Roughness = materialAsset->Roughness;
        SpecularStrength = materialAsset->SpecularStrength;
        EmissiveStrength = materialAsset->EmissiveStrength;
    }
}
