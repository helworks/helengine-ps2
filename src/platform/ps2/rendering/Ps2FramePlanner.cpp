#include "platform/ps2/rendering/Ps2FramePlanner.hpp"

#include "Ps2RenderClass.hpp"
#include "platform/ps2/rendering/Ps2FramePlan.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"

namespace helengine::ps2 {
    Ps2FramePlan Ps2FramePlanner::Build(const std::vector<Ps2RenderProxy>& proxies) const {
        Ps2FramePlan plan;
        for (const Ps2RenderProxy& proxy : proxies) {
            Ps2RuntimeMaterial* material = proxy.GetMaterial();
            if (material == nullptr) {
                continue;
            }

            if (material->GetRenderClass() != ::Ps2RenderClass::Opaque) {
                continue;
            }

            if (proxy.IsStatic()) {
                if (material->GetRenderClass() == ::Ps2RenderClass::Transparent) {
                    plan.AlphaWorld.push_back(&proxy);
                } else {
                    plan.OpaqueWorld.push_back(&proxy);
                }
            } else {
                if (material->GetRenderClass() == ::Ps2RenderClass::Transparent) {
                    plan.AlphaDynamic.push_back(&proxy);
                } else {
                    plan.OpaqueDynamic.push_back(&proxy);
                }
            }
        }

        return plan;
    }
}
