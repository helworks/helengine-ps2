#pragma once

#include "float4x4.hpp"
#include "platform/ps2/rendering/vu/Ps2VuNearPlaneRoute.hpp"
#include "platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Conservatively routes a local-space textured source slice against every unsafe homogeneous frustum plane.
    /// </summary>
    class Ps2VuNearPlaneSliceClassifier final {
    public:
        /// <summary>
        /// Classifies the complete bounds as safely visible, fully hidden, or intersecting the near, camera, or side planes.
        /// </summary>
        /// <param name="bounds">Conservative local-space center and extents for the source slice.</param>
        /// <param name="worldViewProjection">Transform from local space to homogeneous clip space.</param>
        /// <returns>The VU1 program route that can process the entire source slice safely.</returns>
        static Ps2VuNearPlaneRoute Classify(
            const Ps2VuSourceSliceBounds& bounds,
            const ::float4x4& worldViewProjection);
    };
}
