#include "platform/ps2/rendering/Ps2FramePlanner.hpp"

#include <Ps2RenderClass.hpp>
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

            const ::Ps2RenderClass renderClass = material->GetRenderClass();
            if (renderClass != ::Ps2RenderClass::Opaque &&
                renderClass != ::Ps2RenderClass::AlphaTest &&
                renderClass != ::Ps2RenderClass::Transparent) {
                continue;
            }

            if (proxy.IsStatic()) {
                if (renderClass == ::Ps2RenderClass::Transparent) {
                    plan.AlphaWorld.push_back(&proxy);
                } else {
                    plan.OpaqueWorld.push_back(&proxy);
                }
            } else {
                if (renderClass == ::Ps2RenderClass::Transparent) {
                    plan.AlphaDynamic.push_back(&proxy);
                } else {
                    plan.OpaqueDynamic.push_back(&proxy);
                }
            }
        }

        return plan;
    }
}
