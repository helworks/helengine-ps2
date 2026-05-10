#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp"

#include "platform/ps2/rendering/Ps2FramePlan.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"

namespace helengine::ps2 {
    std::vector<Ps2VuOpaqueBatch> Ps2VuOpaqueBatchBuilder::Build(const Ps2FramePlan& plan) const {
        LastRejectedMissingMaterialCount = 0;
        LastRejectedMissingModelCount = 0;
        LastRejectedMissingPackedModelCount = 0;
        std::vector<Ps2VuOpaqueBatch> batches;
        batches.reserve(plan.OpaqueWorld.size() + plan.OpaqueDynamic.size());
        AppendProxyBatches(plan.OpaqueWorld, batches);
        AppendProxyBatches(plan.OpaqueDynamic, batches);
        return batches;
    }

    std::size_t Ps2VuOpaqueBatchBuilder::GetLastRejectedMissingMaterialCount() const {
        return LastRejectedMissingMaterialCount;
    }

    std::size_t Ps2VuOpaqueBatchBuilder::GetLastRejectedMissingModelCount() const {
        return LastRejectedMissingModelCount;
    }

    std::size_t Ps2VuOpaqueBatchBuilder::GetLastRejectedMissingPackedModelCount() const {
        return LastRejectedMissingPackedModelCount;
    }

    void Ps2VuOpaqueBatchBuilder::AppendProxyBatches(const std::vector<const Ps2RenderProxy*>& proxies, std::vector<Ps2VuOpaqueBatch>& batches) const {
        for (const Ps2RenderProxy* proxy : proxies) {
            if (proxy == nullptr) {
                continue;
            }

            Ps2RuntimeMaterial* runtimeMaterial = proxy->GetMaterial();
            Ps2RuntimeModel* runtimeModel = proxy->GetModel();
            if (runtimeMaterial == nullptr) {
                LastRejectedMissingMaterialCount += 1;
                continue;
            }

            if (runtimeModel == nullptr) {
                LastRejectedMissingModelCount += 1;
                continue;
            }

            const Ps2VuPackedModel* packedModel = runtimeModel->GetVuPackedModel();
            if (packedModel == nullptr) {
                LastRejectedMissingPackedModelCount += 1;
                continue;
            }

            Ps2VuOpaqueBatch batch {};
            batch.Proxy = proxy;
            batch.Model = packedModel;
            batch.Material = runtimeMaterial;
            batch.Textured = !runtimeMaterial->GetTextureRelativePath().empty();
            batches.push_back(batch);
        }
    }
}
