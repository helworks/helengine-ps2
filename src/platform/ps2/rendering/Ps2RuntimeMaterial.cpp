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
        if (ps2MaterialAsset != nullptr) {
            LoadFromCooked(ps2MaterialAsset);
            return;
        }

        const std::string materialId = materialAsset->get_Id();
        const std::string& textureRelativePath = materialAsset->TextureRelativePath;
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
        std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked completed\n");
        std::fflush(stdout);
    }

    void Ps2RuntimeMaterial::LoadFromCooked(::Ps2MaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material data is required.");
        }

        const std::string materialId = materialAsset->get_Id();
        const std::string& textureRelativePath = materialAsset->TextureRelativePath;
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
        LightingMode = materialAsset->LightingMode;
        AlphaMode = materialAsset->AlphaMode;
        RenderClass = materialAsset->RenderClass;
        BaseColorR = materialAsset->BaseColorR;
        BaseColorG = materialAsset->BaseColorG;
        BaseColorB = materialAsset->BaseColorB;
        BaseColorA = materialAsset->BaseColorA;
        TextureRelativePath = materialAsset->TextureRelativePath;
        DoubleSided = materialAsset->DoubleSided;
        CastShadows = materialAsset->CastShadows;
        UseVertexColor = materialAsset->UseVertexColor;
        ExpensiveModeAllowed = materialAsset->ExpensiveModeAllowed;
        Roughness = materialAsset->Roughness;
        SpecularStrength = materialAsset->SpecularStrength;
        EmissiveStrength = materialAsset->EmissiveStrength;
        std::printf("[helengine-ps2] Ps2RuntimeMaterial::LoadFromCooked completed\n");
        std::fflush(stdout);
    }
}
