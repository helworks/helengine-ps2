#include "platform/ps2/rendering/Ps2RenderManager3D.hpp"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <malloc.h>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <vector>

#include <gsKit.h>

#include "ContentManager.hpp"
#include "AssetSerializer.hpp"
#include "CameraComponent.hpp"
#include "Core.hpp"
#include "Entity.hpp"
#include "IDrawable3D.hpp"
#include "ModelAsset.hpp"
#include "ObjectManager.hpp"
#include "TextureAsset.hpp"
#include "Ps2MaterialAsset.hpp"
#include "Ps2MaterialLightingMode.hpp"
#include "MaterialBlendMode.hpp"
#include "float2.hpp"
#include "float3.hpp"
#include "platform/ps2/rendering/Ps2FramePlan.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "runtime/finally.hpp"
#include "runtime/native_cast.hpp"
#include "system/io/file.hpp"
#include "system/io/path.hpp"
#include "float4.hpp"
#include "float4x4.hpp"

namespace helengine::ps2 {
    namespace {
        constexpr double LightingScale = 191.0;
        constexpr double LightingBias = 64.0;
        constexpr float CameraFieldOfViewRadians = 0.785398185f;
        constexpr float MinimumClipW = 0.0001f;
        const ::float3 DefaultForward(0.0f, 0.0f, -1.0f);
        const ::float3 DefaultUp(0.0f, 1.0f, 0.0f);

        std::unordered_map<std::string, GSTEXTURE*> TextureRecords;

        GSTEXTURE* BuildTextureFromAsset(GSGLOBAL* gsGlobal, ::TextureAsset* data) {
            if (gsGlobal == nullptr || data == nullptr || data->Colors == nullptr || data->Colors->Length <= 0 || data->Width <= 0 || data->Height <= 0) {
                return nullptr;
            }

            GSTEXTURE* texture = new GSTEXTURE();
            texture->Width = data->Width;
            texture->Height = data->Height;
            texture->PSM = GS_PSM_CT32;
            texture->Clut = nullptr;
            texture->VramClut = 0;
            texture->Filter = GS_FILTER_NEAREST;
            texture->Mem = static_cast<u32*>(memalign(128, static_cast<std::size_t>(data->Colors->Length)));
            if (texture->Mem == nullptr) {
                delete texture;
                return nullptr;
            }

            std::memcpy(texture->Mem, data->Colors->Data, static_cast<std::size_t>(data->Colors->Length));
            texture->Vram = gsKit_vram_alloc(
                gsGlobal,
                gsKit_texture_size(texture->Width, texture->Height, texture->PSM),
                GSKIT_ALLOC_USERBUFFER);
            if (texture->Vram == GSKIT_ALLOC_ERROR) {
                free(texture->Mem);
                delete texture;
                return nullptr;
            }

            return texture;
        }

        GSTEXTURE* ResolveTexture(GSGLOBAL* gsGlobal, const std::string& textureRelativePath) {
            if (textureRelativePath.empty()) {
                return nullptr;
            }

            auto textureIt = TextureRecords.find(textureRelativePath);
            if (textureIt != TextureRecords.end()) {
                return textureIt->second;
            }

            ::Core* core = ::Core::get_Instance();
            if (core == nullptr || core->get_ContentManager() == nullptr) {
                return nullptr;
            }

            const std::string fullPath = ::Path::GetFullPath(::Path::Combine(core->get_ContentManager()->get_RootDirectory(), textureRelativePath));
            ::FileStream* stream = ::File::OpenRead(fullPath);
            [[maybe_unused]] auto streamGuard = he_cpp_make_scope_exit([stream]() {
                if (stream != nullptr) {
                    stream->Dispose();
                }
            });
            ::Asset* asset = ::AssetSerializer::Deserialize(stream);
            ::TextureAsset* textureAsset = he_cpp_try_cast<::TextureAsset>(asset);
            GSTEXTURE* texture = BuildTextureFromAsset(gsGlobal, textureAsset);
            if (texture == nullptr) {
                return nullptr;
            }

            gsKit_texture_upload(gsGlobal, texture);
            TextureRecords.emplace(textureRelativePath, texture);
            return texture;
        }
    }

    Ps2RenderManager3D::Ps2RenderManager3D()
        : FramePlanner(),
          GsGlobal(nullptr),
          Proxies() {
    }

    ::RuntimeMaterial* Ps2RenderManager3D::BuildMaterialFromAsset(::Asset* materialAsset) {
        ::Ps2MaterialAsset* cookedAsset = he_cpp_try_cast<Ps2MaterialAsset>(materialAsset);
        if (cookedAsset == nullptr) {
            throw std::invalid_argument("PS2 cooked material asset is required.");
        }

        Ps2RuntimeMaterial* runtimeMaterial = new Ps2RuntimeMaterial();
        runtimeMaterial->LoadFromCooked(cookedAsset);
        return runtimeMaterial;
    }

    ::RuntimeModel* Ps2RenderManager3D::BuildModelFromRaw(::ModelAsset* data) {
        if (data == nullptr) {
            throw std::invalid_argument("PS2 raw model data is required.");
        }

        Ps2RuntimeModel* runtimeModel = new Ps2RuntimeModel();
        runtimeModel->LoadFromRaw(data);
        return runtimeModel;
    }

    void Ps2RenderManager3D::Draw() {
        if (GsGlobal == nullptr) {
            return;
        }

        ::CameraComponent* camera = GetActiveCamera();
        if (camera == nullptr || camera->get_Parent() == nullptr) {
            return;
        }

        ::float4 viewport = camera->get_Viewport();
        if (viewport.Z <= 0.0f || viewport.W <= 0.0f) {
            return;
        }

        ::float3 cameraPosition = camera->get_Parent()->get_Position();
        ::float4 cameraOrientation = camera->get_Parent()->get_Orientation();
        ::float3 cameraForward = ::float4::RotateVector(DefaultForward, cameraOrientation);
        ::float3 cameraUp = ::float4::RotateVector(DefaultUp, cameraOrientation);
        ::float3 cameraTarget = cameraPosition + cameraForward;
        ::float4x4 view;
        ::float4x4::CreateLookAt(cameraPosition, cameraTarget, cameraUp, view);

        ::float4x4 projection;
        ::float4x4::CreatePerspectiveFieldOfView(
            CameraFieldOfViewRadians,
            viewport.Z / viewport.W,
            camera->get_NearPlaneDistance(),
            camera->get_FarPlaneDistance(),
            projection);

        ::float4x4 viewProjection;
        ::float4x4::Multiply(view, projection, viewProjection);

        ApplyDepthState(GsGlobal->ZBuffering == GS_SETTING_ON);
        RebuildProxies();
        Ps2FramePlan plan = FramePlanner.Build(Proxies);

        SortAlphaProxies(plan.AlphaWorld, cameraPosition, cameraForward);
        SortAlphaProxies(plan.AlphaDynamic, cameraPosition, cameraForward);

        for (const Ps2RenderProxy* proxy : plan.OpaqueWorld) {
            if (proxy != nullptr) {
                DrawOpaqueProxy(*proxy, viewProjection, viewport);
            }
        }

        for (const Ps2RenderProxy* proxy : plan.OpaqueDynamic) {
            if (proxy != nullptr) {
                DrawOpaqueProxy(*proxy, viewProjection, viewport);
            }
        }

        ApplyAlphaBlendState(true);
        for (const Ps2RenderProxy* proxy : plan.AlphaWorld) {
            if (proxy != nullptr) {
                DrawAlphaProxy(*proxy, viewProjection, viewport);
            }
        }

        for (const Ps2RenderProxy* proxy : plan.AlphaDynamic) {
            if (proxy != nullptr) {
                DrawAlphaProxy(*proxy, viewProjection, viewport);
            }
        }
        ApplyAlphaBlendState(false);
    }

    void Ps2RenderManager3D::SetGsGlobal(GSGLOBAL* gsGlobal) {
        GsGlobal = gsGlobal;
    }

    void Ps2RenderManager3D::DrawOpaqueProxy(const Ps2RenderProxy& proxy, const ::float4x4& viewProjection, const ::float4& viewport) {
        const Ps2RuntimeModel* model = proxy.GetModel();
        const Ps2RuntimeMaterial* material = proxy.GetMaterial();
        ::IDrawable3D* drawable = proxy.GetDrawable();
        if (model == nullptr || material == nullptr || drawable == nullptr) {
            return;
        }

        const std::vector<::float3>& positions = model->GetPositions();
        const std::vector<::float3>& normals = model->GetNormals();
        const std::vector<::float2>& texCoords = model->GetTexCoords();
        const std::vector<std::uint16_t>& indices = model->GetIndices();
        if (positions.empty() || indices.size() < 3) {
            return;
        }

        ::float3 parentPosition = ::float3::get_Zero();
        ::Entity* parent = drawable->get_Parent();
        if (parent != nullptr) {
            parentPosition = parent->get_Position();
        }

        GSTEXTURE* texture = nullptr;
        if (!material->GetTextureRelativePath().empty()) {
            texture = ResolveTexture(GsGlobal, material->GetTextureRelativePath());
        }

        for (std::size_t index = 0; index + 2 < indices.size(); index += 3) {
            std::uint16_t indexA = indices[index + 0];
            std::uint16_t indexB = indices[index + 1];
            std::uint16_t indexC = indices[index + 2];
            if (indexA >= positions.size() || indexB >= positions.size() || indexC >= positions.size()) {
                continue;
            }

            ::float3 positionA = positions[indexA] + parentPosition;
            ::float3 positionB = positions[indexB] + parentPosition;
            ::float3 positionC = positions[indexC] + parentPosition;

            ::float3 normalA = indexA < normals.size() ? normals[indexA] : ::float3::get_Zero();
            ::float3 normalB = indexB < normals.size() ? normals[indexB] : ::float3::get_Zero();
            ::float3 normalC = indexC < normals.size() ? normals[indexC] : ::float3::get_Zero();

            const std::uint64_t colorA = ResolveVertexColor(*material, normalA);
            const std::uint64_t colorB = ResolveVertexColor(*material, normalB);
            const std::uint64_t colorC = ResolveVertexColor(*material, normalC);
            float screenAX;
            float screenAY;
            float screenAZ;
            float screenBX;
            float screenBY;
            float screenBZ;
            float screenCX;
            float screenCY;
            float screenCZ;
            if (!ProjectWorldPosition(positionA, viewProjection, viewport, screenAX, screenAY, screenAZ)
                || !ProjectWorldPosition(positionB, viewProjection, viewport, screenBX, screenBY, screenBZ)
                || !ProjectWorldPosition(positionC, viewProjection, viewport, screenCX, screenCY, screenCZ)) {
                continue;
            }

            if (texture != nullptr
                && indexA < texCoords.size()
                && indexB < texCoords.size()
                && indexC < texCoords.size()) {
                gsKit_prim_triangle_goraud_texture_3d(
                    GsGlobal,
                    texture,
                    screenAX, screenAY, screenAZ, texCoords[indexA].X, texCoords[indexA].Y,
                    screenBX, screenBY, screenBZ, texCoords[indexB].X, texCoords[indexB].Y,
                    screenCX, screenCY, screenCZ, texCoords[indexC].X, texCoords[indexC].Y,
                    colorA, colorB, colorC);
                continue;
            }

            gsKit_prim_triangle_gouraud_3d(
                GsGlobal,
                screenAX, screenAY, screenAZ, colorA,
                screenBX, screenBY, screenBZ, colorB,
                screenCX, screenCY, screenCZ, colorC);
        }
    }

    void Ps2RenderManager3D::DrawAlphaProxy(const Ps2RenderProxy& proxy, const ::float4x4& viewProjection, const ::float4& viewport) {
        DrawOpaqueProxy(proxy, viewProjection, viewport);
    }

    void Ps2RenderManager3D::ApplyDepthState(bool enabled) {
        if (GsGlobal == nullptr) {
            return;
        }

        if (enabled) {
            gsKit_set_test(GsGlobal, GS_ZTEST_ON);
        } else {
            gsKit_set_test(GsGlobal, GS_ZTEST_OFF);
        }
    }

    void Ps2RenderManager3D::SortAlphaProxies(std::vector<const Ps2RenderProxy*>& proxies, const ::float3& cameraPosition, const ::float3& cameraForward) {
        std::sort(proxies.begin(), proxies.end(), [this, &cameraPosition, &cameraForward](const Ps2RenderProxy* left, const Ps2RenderProxy* right) {
            if (left == nullptr) {
                return false;
            }

            if (right == nullptr) {
                return true;
            }

            return this->ComputeProxyDepth(*left, cameraPosition, cameraForward) > this->ComputeProxyDepth(*right, cameraPosition, cameraForward);
        });
    }

    void Ps2RenderManager3D::ApplyAlphaBlendState(bool enabled) {
        if (GsGlobal == nullptr) {
            return;
        }

        if (enabled) {
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(1, 2, 0, 0, 0), 1);
        } else {
            gsKit_set_primalpha(GsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
        }
    }

    ::CameraComponent* Ps2RenderManager3D::GetActiveCamera() const {
        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_Cameras() == nullptr) {
            return nullptr;
        }

        List<::ICamera*>* cameras = core->get_ObjectManager()->get_Cameras();
        for (int32_t index = 0; index < cameras->Count(); index++) {
            ::CameraComponent* camera = he_cpp_try_cast<::CameraComponent>((*cameras)[index]);
            if (camera != nullptr) {
                return camera;
            }
        }

        return nullptr;
    }

    bool Ps2RenderManager3D::ProjectWorldPosition(
        const ::float3& worldPosition,
        const ::float4x4& viewProjection,
        const ::float4& viewport,
        float& screenX,
        float& screenY,
        float& screenZ) const {
        const float clipX = (worldPosition.X * viewProjection.M11)
            + (worldPosition.Y * viewProjection.M21)
            + (worldPosition.Z * viewProjection.M31)
            + viewProjection.M41;
        const float clipY = (worldPosition.X * viewProjection.M12)
            + (worldPosition.Y * viewProjection.M22)
            + (worldPosition.Z * viewProjection.M32)
            + viewProjection.M42;
        const float clipZ = (worldPosition.X * viewProjection.M13)
            + (worldPosition.Y * viewProjection.M23)
            + (worldPosition.Z * viewProjection.M33)
            + viewProjection.M43;
        const float clipW = (worldPosition.X * viewProjection.M14)
            + (worldPosition.Y * viewProjection.M24)
            + (worldPosition.Z * viewProjection.M34)
            + viewProjection.M44;

        if (clipW <= MinimumClipW) {
            return false;
        }

        const float inverseClipW = 1.0f / clipW;
        const float normalizedX = clipX * inverseClipW;
        const float normalizedY = clipY * inverseClipW;
        const float normalizedZ = clipZ * inverseClipW;

        screenX = viewport.X + ((normalizedX + 1.0f) * 0.5f * viewport.Z);
        screenY = viewport.Y + ((1.0f - normalizedY) * 0.5f * viewport.W);
        screenZ = (normalizedZ + 1.0f) * 0.5f;
        return true;
    }

    double Ps2RenderManager3D::ComputeProxyDepth(const Ps2RenderProxy& proxy, const ::float3& cameraPosition, const ::float3& cameraForward) const {
        ::IDrawable3D* drawable = proxy.GetDrawable();
        if (drawable == nullptr || drawable->get_Parent() == nullptr) {
            return 0.0;
        }

        const ::float3 proxyPosition = drawable->get_Parent()->get_Position();
        const ::float3 delta = proxyPosition - cameraPosition;
        return static_cast<double>(delta.X) * static_cast<double>(cameraForward.X)
            + static_cast<double>(delta.Y) * static_cast<double>(cameraForward.Y)
            + static_cast<double>(delta.Z) * static_cast<double>(cameraForward.Z);
    }

    void Ps2RenderManager3D::RebuildProxies() {
        Proxies.clear();

        ::Core* core = ::Core::get_Instance();
        if (core == nullptr || core->get_ObjectManager() == nullptr || core->get_ObjectManager()->get_Drawables3D() == nullptr) {
            return;
        }

        List<::IDrawable3D*>* drawables = core->get_ObjectManager()->get_Drawables3D();
        Proxies.reserve(static_cast<std::size_t>(drawables->Count()));
        for (int32_t index = 0; index < drawables->Count(); index++) {
            ::IDrawable3D* drawable = (*drawables)[index];
            Ps2RenderProxy proxy;
            proxy.Synchronize(drawable);
            if (proxy.GetModel() == nullptr || proxy.GetMaterial() == nullptr) {
                continue;
            }

            Proxies.push_back(proxy);
        }
    }

    std::uint64_t Ps2RenderManager3D::ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal) {
        if (material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit) {
            return GS_SETREG_RGBAQ(0xC0, 0xC0, 0xC0, 0x80, 0x00);
        }

        const double normalLengthSquared = static_cast<double>(normal.X) * static_cast<double>(normal.X)
            + static_cast<double>(normal.Y) * static_cast<double>(normal.Y)
            + static_cast<double>(normal.Z) * static_cast<double>(normal.Z);
        if (normalLengthSquared <= 0.000001) {
            return GS_SETREG_RGBAQ(0x40, 0x40, 0x40, 0x80, 0x00);
        }

        ::float3 normalizedNormal = ::float3::Normalize(normal);
        const double lightX = 0.0;
        const double lightY = -0.70710678;
        const double lightZ = -0.70710678;
        double ndotl = static_cast<double>(normalizedNormal.X) * lightX
            + static_cast<double>(normalizedNormal.Y) * lightY
            + static_cast<double>(normalizedNormal.Z) * lightZ;
        ndotl = std::max(0.0, ndotl);

        const std::uint8_t intensity = static_cast<std::uint8_t>(LightingBias + static_cast<int>(ndotl * LightingScale));
        return GS_SETREG_RGBAQ(intensity, intensity, intensity, 0x80, 0x00);
    }
}
