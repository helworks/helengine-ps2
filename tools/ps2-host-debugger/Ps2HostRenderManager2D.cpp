#include "Ps2HostRenderManager2D.hpp"

#include "RuntimeTexture.hpp"
#include "TextureAsset.hpp"

namespace helengine::ps2::host {
    RuntimeTexture* Ps2HostRenderManager2D::BuildTextureFromRaw(TextureAsset* data) {
        auto* runtimeTexture = new ::RuntimeTexture();
        if (data != nullptr) {
            runtimeTexture->set_Id(data->get_Id());
            runtimeTexture->set_Width(data->Width);
            runtimeTexture->set_Height(data->Height);
        }

        return runtimeTexture;
    }

    void Ps2HostRenderManager2D::DrawRoundedRect(IRoundedRectDrawable2D* shape) {
    }

    void Ps2HostRenderManager2D::DrawSprite(ISpriteDrawable2D* sprite) {
    }

    void Ps2HostRenderManager2D::DrawText(ITextDrawable2D* text) {
    }
}
