#pragma once

#include <vector>

#include "RenderManager3D.hpp"
#include "platform/ps2/rendering/Ps2FramePlanner.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp"

class MaterialAsset;
class ModelAsset;
class PlatformMaterialAsset;
class Ps2MaterialAsset;
class Ps2ModelAsset;
class RenderTarget;
class RuntimeMaterial;
class RuntimeModel;

namespace helengine::ps2::host {
    /// <summary>
    /// Reuses the PS2 runtime material and model paths while skipping hardware submission.
    /// </summary>
    class Ps2HostRenderManager3D final : public ::RenderManager3D {
    public:
        Ps2HostRenderManager3D();

        RuntimeMaterial* BuildMaterialFromCooked(PlatformMaterialAsset* materialAsset) override;
        RuntimeMaterial* BuildMaterialFromCooked(std::string cookedAssetPath, IContentStreamSource* contentStreamSource) override;
        RuntimeMaterial* BuildMaterialFromRawAsset(ContentManager* assetContentManager, std::string contentRootPath, std::string materialAssetPath) override;
        RuntimeModel* BuildModelFromCooked(std::string cookedAssetPath, IContentStreamSource* contentStreamSource) override;
        RuntimeModel* BuildModelFromRaw(ModelAsset* data) override;
        RenderTarget* CreateRenderTarget(int32_t width, int32_t height) override;
        void Draw() override;

        int32_t get_LastCameraCount() const;
        int32_t get_LastDrawable3DCount() const;
        int32_t get_LastDrawable2DCount() const;
        int32_t get_LastProxyCount() const;
        int32_t get_LastOpaqueWorldCount() const;
        int32_t get_LastOpaqueDynamicCount() const;
        int32_t get_LastAlphaWorldCount() const;
        int32_t get_LastAlphaDynamicCount() const;
        int32_t get_LastVuBatchCount() const;
        int32_t get_LastVuRejectedMissingMaterialCount() const;
        int32_t get_LastVuRejectedMissingModelCount() const;
        int32_t get_LastVuRejectedMissingPackedModelCount() const;
        double get_LastVuTriangleSetupMilliseconds() const;
        double get_LastVuTrianglePrepMilliseconds() const;
        double get_LastVuTriangleEmitMilliseconds() const;
        int32_t get_LastVuSubmittedTriangleCount() const;

    private:
        RuntimeMaterial* BuildMaterialFromCooked(Ps2MaterialAsset* materialAsset);
        RuntimeModel* BuildModelFromCooked(Ps2ModelAsset* modelAsset);

        helengine::ps2::Ps2FramePlanner FramePlanner;
        helengine::ps2::Ps2VuOpaqueBatchBuilder VuOpaqueBatchBuilder;
        helengine::ps2::Ps2VuOpaqueUntexturedSetupBuilder VuUntexturedSetupBuilder;
        std::vector<helengine::ps2::Ps2RenderProxy> Proxies;
        int32_t LastCameraCount;
        int32_t LastDrawable3DCount;
        int32_t LastDrawable2DCount;
        int32_t LastProxyCount;
        int32_t LastOpaqueWorldCount;
        int32_t LastOpaqueDynamicCount;
        int32_t LastAlphaWorldCount;
        int32_t LastAlphaDynamicCount;
        int32_t LastVuBatchCount;
        int32_t LastVuRejectedMissingMaterialCount;
        int32_t LastVuRejectedMissingModelCount;
        int32_t LastVuRejectedMissingPackedModelCount;
        double LastVuTriangleSetupMilliseconds;
        double LastVuTrianglePrepMilliseconds;
        double LastVuTriangleEmitMilliseconds;
        int32_t LastVuSubmittedTriangleCount;
    };
}
