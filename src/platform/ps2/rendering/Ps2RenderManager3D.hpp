#pragma once

#include <cstdint>
#include <vector>

#include "RenderManager3D.hpp"
#include "platform/ps2/rendering/Ps2FramePlanner.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"

typedef struct gsGlobal GSGLOBAL;

class Asset;
class CameraComponent;
class ModelAsset;
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

        ::RuntimeMaterial* BuildMaterialFromAsset(::Asset* materialAsset) override;
        ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) override;
        void Draw() override;
        void SetGsGlobal(GSGLOBAL* gsGlobal);

    private:
        void DrawOpaqueProxy(const Ps2RenderProxy& proxy, const ::float4x4& viewProjection, const ::float4& viewport);
        void DrawAlphaProxy(const Ps2RenderProxy& proxy, const ::float4x4& viewProjection, const ::float4& viewport);
        void ApplyDepthState(bool enabled);
        void ApplyAlphaBlendState(bool enabled);
        ::CameraComponent* GetActiveCamera() const;
        void SortAlphaProxies(std::vector<const Ps2RenderProxy*>& proxies, const ::float3& cameraPosition, const ::float3& cameraForward);
        void RebuildProxies();
        bool ProjectWorldPosition(
            const ::float3& worldPosition,
            const ::float4x4& viewProjection,
            const ::float4& viewport,
            float& screenX,
            float& screenY,
            float& screenZ) const;
        double ComputeProxyDepth(const Ps2RenderProxy& proxy, const ::float3& cameraPosition, const ::float3& cameraForward) const;
        std::uint64_t ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal);

        Ps2FramePlanner FramePlanner;
        GSGLOBAL* GsGlobal;
        std::vector<Ps2RenderProxy> Proxies;
    };
}
