#pragma once

#include <cstdint>
#include <vector>

#include "RenderManager3D.hpp"
#include "platform/ps2/rendering/Ps2FramePlanner.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"

typedef struct gsGlobal GSGLOBAL;
typedef struct gsTexture GSTEXTURE;

class CameraComponent;
class ModelAsset;
class Ps2MaterialAsset;
class RuntimeMaterial;
class RuntimeModel;
class float3;
class float4;
class float4x4;

namespace helengine::ps2 {
    class Ps2FramePlan;
    class Ps2RuntimeMaterial;
    class Ps2RuntimeModel;

    class Ps2RenderManager3D final : public ::RenderManager3D {
    public:
        Ps2RenderManager3D();

        ::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset) override;
        ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) override;
        void Draw() override;
        void SetHdrEnabled(bool enabled);
        void SetGsGlobal(GSGLOBAL* gsGlobal);

    private:
        void DrawOpaqueProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawAlphaProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawSoftwareDepthPass(
            const Ps2FramePlan& plan,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            float nearPlaneDistance,
            const ::float3& cameraPosition,
            const ::float3& cameraForward);
        void ApplyDepthState(bool enabled);
        void ApplyMaterialAlphaState(const Ps2RuntimeMaterial& material);
        ::CameraComponent* GetActiveCamera() const;
        bool ShouldDrawAlphaTestTriangle(
            const Ps2RuntimeMaterial& material,
            GSTEXTURE* texture,
            const ::float2& texCoordA,
            const ::float2& texCoordB,
            const ::float2& texCoordC,
            std::uint8_t alphaA,
            std::uint8_t alphaB,
            std::uint8_t alphaC) const;
        bool ShouldEmitHdrGlow(const Ps2RuntimeMaterial& material, std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const;
        bool IsGlowColorBright(std::uint64_t color) const;
        float ComputeHdrGlowStrength(std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const;
        std::uint64_t BoostHdrColor(std::uint64_t color, float glowStrength) const;
        void SortAlphaProxies(std::vector<const Ps2RenderProxy*>& proxies, const ::float3& cameraPosition, const ::float3& cameraForward);
        void RebuildProxies();
        bool ProjectWorldPosition(
            const ::float3& worldPosition,
            const ::float4x4& projection,
            const ::float4& viewport,
            float& screenX,
            float& screenY,
            float& screenZ) const;
        bool IsFrontFacingTriangle(
            float screenAX,
            float screenAY,
            float screenBX,
            float screenBY,
            float screenCX,
            float screenCY) const;
        std::uint8_t SampleTextureAlpha(GSTEXTURE* texture, const ::float2& texCoord) const;
        ::float3 TransformPosition(const ::float3& position, const ::float4x4& matrix) const;
        double ComputeProxyDepth(const Ps2RenderProxy& proxy, const ::float3& cameraPosition, const ::float3& cameraForward) const;
        std::uint64_t ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal);

        Ps2FramePlanner FramePlanner;
        bool HdrEnabled;
        GSGLOBAL* GsGlobal;
        std::vector<Ps2RenderProxy> Proxies;
    };
}
