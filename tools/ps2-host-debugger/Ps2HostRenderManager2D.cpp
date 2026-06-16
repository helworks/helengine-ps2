#include "Ps2HostRenderManager2D.hpp"

#include <iostream>
#include <stdexcept>

#include "Entity.hpp"
#include "FontAsset.hpp"
#include "ISpriteDrawable2D.hpp"
#include "ITextDrawable2D.hpp"
#include "IRoundedRectDrawable2D.hpp"
#include "Asset.hpp"
#include "Ps2AssetSerializer.hpp"
#include "Ps2TextureAsset.hpp"
#include "RuntimeTexture.hpp"
#include "TextureAsset.hpp"
#include "runtime/finally.hpp"
#include "runtime/native_cast.hpp"
#include "system/io/file-stream.hpp"
#include "system/io/file.hpp"

namespace helengine::ps2::host {
    Ps2HostRenderManager2D::Ps2HostRenderManager2D()
        : LoggedSpriteTrace(false),
          LoggedTextTrace(false),
          LoggedRoundedRectTrace(false) {
    }

    RuntimeTexture* Ps2HostRenderManager2D::BuildTextureFromCooked(std::string cookedAssetPath) {
        if (cookedAssetPath.empty()) {
            throw std::invalid_argument("One PS2 cooked texture path is required.");
        }

        ::FileStream* stream = ::File::OpenRead(cookedAssetPath);
        [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
            if (stream != nullptr) {
                stream->Dispose();
            }
        });
        ::Asset* asset = ::Ps2AssetSerializer::Deserialize(stream);
        ::Ps2TextureAsset* textureAsset = he_cpp_try_cast<::Ps2TextureAsset>(asset);
        if (textureAsset == nullptr) {
            throw std::invalid_argument("One PS2 cooked texture payload must deserialize as Ps2TextureAsset.");
        }

        auto* runtimeTexture = new ::RuntimeTexture();
        runtimeTexture->set_Id(textureAsset->get_Id());
        runtimeTexture->set_Width(textureAsset->Width);
        runtimeTexture->set_Height(textureAsset->Height);
        return runtimeTexture;
    }

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
        if (LoggedRoundedRectTrace || shape == nullptr || shape->get_Parent() == nullptr) {
            return;
        }

        LoggedRoundedRectTrace = true;
        const ::float3 position = shape->get_Parent()->get_Position();
        const ::int2 size = shape->get_Size();
        std::cout
            << "[ps2-host-debug] first rounded-rect pos=("
            << position.X << "," << position.Y << "," << position.Z
            << ") size=("
            << size.X << "," << size.Y
            << ") fill=("
            << static_cast<int>(shape->get_FillColor().X) << ","
            << static_cast<int>(shape->get_FillColor().Y) << ","
            << static_cast<int>(shape->get_FillColor().Z) << ","
            << static_cast<int>(shape->get_FillColor().W)
            << ")"
            << std::endl;
    }

    void Ps2HostRenderManager2D::DrawSprite(ISpriteDrawable2D* sprite) {
        if (LoggedSpriteTrace || sprite == nullptr || sprite->get_Parent() == nullptr) {
            return;
        }

        LoggedSpriteTrace = true;
        const ::float3 position = sprite->get_Parent()->get_Position();
        const ::int2 size = sprite->get_Size();
        ::RuntimeTexture* texture = sprite->get_Texture();
        const ::float4 sourceRect = sprite->get_SourceRect();
        std::cout
            << "[ps2-host-debug] first sprite pos=("
            << position.X << "," << position.Y << "," << position.Z
            << ") size=("
            << size.X << "," << size.Y
            << ") source=("
            << sourceRect.X << "," << sourceRect.Y << "," << sourceRect.Z << "," << sourceRect.W
            << ") texture=("
            << (texture != nullptr ? texture->get_Width() : 0) << "x" << (texture != nullptr ? texture->get_Height() : 0)
            << ") color=("
            << static_cast<int>(sprite->get_Color().X) << ","
            << static_cast<int>(sprite->get_Color().Y) << ","
            << static_cast<int>(sprite->get_Color().Z) << ","
            << static_cast<int>(sprite->get_Color().W)
            << ")"
            << std::endl;
    }

    void Ps2HostRenderManager2D::DrawText(ITextDrawable2D* text) {
        if (LoggedTextTrace || text == nullptr || text->get_Parent() == nullptr) {
            return;
        }

        LoggedTextTrace = true;
        const ::float3 position = text->get_Parent()->get_Position();
        const ::int2 size = text->get_Size();
        ::FontAsset* font = text->get_Font();
        std::cout
            << "[ps2-host-debug] first text pos=("
            << position.X << "," << position.Y << "," << position.Z
            << ") size=("
            << size.X << "," << size.Y
            << ") fontScale=" << text->get_FontScale()
            << " wrap=" << (text->get_WrapText() ? "true" : "false")
            << " fontTexture=("
            << (font != nullptr && font->get_Texture() != nullptr ? font->get_Texture()->get_Width() : 0)
            << "x"
            << (font != nullptr && font->get_Texture() != nullptr ? font->get_Texture()->get_Height() : 0)
            << ") text='" << text->get_Text() << "'"
            << std::endl;
    }
}
