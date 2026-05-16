#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"

#include <cstdio>
#include <stdexcept>

#include "PlatformMaterialAsset.hpp"
#include "Ps2MaterialAlphaMode.hpp"
#include "Ps2MaterialAsset.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "Ps2RenderClass.hpp"
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
        const std::string materialId = materialAsset->get_Id();
        const std::string& textureRelativePath = ps2MaterialAsset != nullptr
            ? ps2MaterialAsset->TextureRelativePath
            : materialAsset->TextureRelativePath;
        const std::string loadStartLog =
            std::string("Ps2RuntimeMaterial::LoadFromCooked start idLen=")
            + std::to_string(materialId.length())
            + " textureLen="
            + std::to_string(textureRelativePath.length());
        std::printf("[helengine-ps2] %s\n", loadStartLog.c_str());
        std::fflush(stdout);

        this->set_Id(materialAsset->get_Id());
        std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked copied id\n");
        std::fflush(stdout);
        if (ps2MaterialAsset != nullptr) {
            LightingMode = ps2MaterialAsset->LightingMode;
            AlphaMode = ps2MaterialAsset->AlphaMode;
            RenderClass = ps2MaterialAsset->RenderClass;
            BaseColorR = ps2MaterialAsset->BaseColorR;
            BaseColorG = ps2MaterialAsset->BaseColorG;
            BaseColorB = ps2MaterialAsset->BaseColorB;
            BaseColorA = ps2MaterialAsset->BaseColorA;
            TextureRelativePath = ps2MaterialAsset->TextureRelativePath;
            DoubleSided = ps2MaterialAsset->DoubleSided;
            CastShadows = ps2MaterialAsset->CastShadows;
            UseVertexColor = ps2MaterialAsset->UseVertexColor;
            ExpensiveModeAllowed = ps2MaterialAsset->ExpensiveModeAllowed;
            Roughness = ps2MaterialAsset->Roughness;
            SpecularStrength = ps2MaterialAsset->SpecularStrength;
            EmissiveStrength = ps2MaterialAsset->EmissiveStrength;
        } else {
            BaseColorR = materialAsset->BaseColorR;
            BaseColorG = materialAsset->BaseColorG;
            BaseColorB = materialAsset->BaseColorB;
            BaseColorA = materialAsset->BaseColorA;
            LightingMode = materialAsset->Lit ? ::Ps2MaterialLightingMode::SimpleLit : ::Ps2MaterialLightingMode::Unlit;
            AlphaMode = ::Ps2MaterialAlphaMode::Opaque;
            RenderClass = ::Ps2RenderClass::Opaque;
            std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked copying texture path\n");
            std::fflush(stdout);
            TextureRelativePath = materialAsset->TextureRelativePath;
            std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked copied texture path\n");
            std::fflush(stdout);
            DoubleSided = materialAsset->DoubleSided;
            UseVertexColor = materialAsset->UseVertexColor;
            CastShadows = false;
            ExpensiveModeAllowed = false;
            Roughness = 0.5f;
            SpecularStrength = 0.5f;
            EmissiveStrength = 0.0f;
        }
        std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked completed\n");
        std::fflush(stdout);
    }
}
