#pragma once

#include "RenderManager2D.hpp"

class IRoundedRectDrawable2D;
class ISpriteDrawable2D;
class ITextDrawable2D;
class RuntimeTexture;
class TextureAsset;

namespace helengine::ps2::host {
    /// <summary>
    /// Supplies one minimal 2D runtime surface for host-debug load-only runs.
    /// </summary>
    class Ps2HostRenderManager2D final : public ::RenderManager2D {
    public:
        Ps2HostRenderManager2D();

        RuntimeTexture* BuildTextureFromCooked(std::string cookedAssetPath) override;
        RuntimeTexture* BuildTextureFromRaw(TextureAsset* data) override;
        void DrawRoundedRect(IRoundedRectDrawable2D* shape) override;
        void DrawSprite(ISpriteDrawable2D* sprite) override;
        void DrawText(ITextDrawable2D* text) override;

    private:
        bool LoggedSpriteTrace;
        bool LoggedTextTrace;
        bool LoggedRoundedRectTrace;
    };
}
