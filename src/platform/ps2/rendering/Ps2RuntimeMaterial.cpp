#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"

#include <stdexcept>

#include "Ps2MaterialAlphaMode.hpp"
#include "Ps2MaterialAsset.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "Ps2RenderClass.hpp"
namespace helengine::ps2 {
    Ps2RuntimeMaterial::Ps2RuntimeMaterial()
        : AlphaMode(::Ps2MaterialAlphaMode::Opaque),
          DoubleSided(false),
          ExpensiveModeAllowed(false),
          LightingMode(::Ps2MaterialLightingMode::Unlit),
          RenderClass(::Ps2RenderClass::Opaque),
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

    bool Ps2RuntimeMaterial::GetDoubleSided() const {
        return DoubleSided;
    }

    bool Ps2RuntimeMaterial::GetExpensiveModeAllowed() const {
        return ExpensiveModeAllowed;
    }

    bool Ps2RuntimeMaterial::UsesVertexColor() const {
        return UseVertexColor;
    }

    void Ps2RuntimeMaterial::LoadFromCooked(::Ps2MaterialAsset* materialAsset) {
        if (materialAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material data is required.");
        }

        this->set_Id(materialAsset->get_Id());
        LightingMode = materialAsset->LightingMode;
        AlphaMode = materialAsset->AlphaMode;
        RenderClass = materialAsset->RenderClass;
        TextureRelativePath = materialAsset->TextureRelativePath;
        DoubleSided = materialAsset->DoubleSided;
        UseVertexColor = materialAsset->UseVertexColor;
        ExpensiveModeAllowed = materialAsset->ExpensiveModeAllowed;
    }
}
