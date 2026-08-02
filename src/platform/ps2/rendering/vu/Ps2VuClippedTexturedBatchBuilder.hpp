#pragma once

#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp"

#include "float4x4.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Builds allocation-free clipped textured triangle fans from immutable local-space packed source records.
    /// </summary>
    class Ps2VuClippedTexturedBatchBuilder final {
    public:
        /// <summary>
        /// Transforms one local source triangle to view and homogeneous clip space, clips it, and generates its stable output fan.
        /// </summary>
        /// <param name="sourceTriangle">The immutable packed local source triangle containing positions, raw UVs, and a flat normal.</param>
        /// <param name="worldView">The matrix that transforms local positions into view space.</param>
        /// <param name="projection">The matrix that transforms view positions into homogeneous clip space.</param>
        /// <param name="nearPlaneDistance">The finite positive camera near-plane distance for clipping.</param>
        /// <param name="outputFan">The fixed fan that receives all generated clipped triangles.</param>
        static void BuildTriangleFan(
            const Ps2VuTexturedPackedTriangleSource& sourceTriangle,
            const ::float4x4& worldView,
            const ::float4x4& projection,
            float nearPlaneDistance,
            Ps2VuClippedTexturedTriangleFan& outputFan);

    private:
        /// <summary>
        /// Transforms one packed local homogeneous position into view-space XYZ without a perspective divide.
        /// </summary>
        /// <param name="position">The four packed local position components.</param>
        /// <param name="worldView">The local-to-view transform matrix.</param>
        /// <returns>The resulting view-space position.</returns>
        static ::float3 TransformPosition(const float position[4], const ::float4x4& worldView);

        /// <summary>
        /// Transforms one view-space position into a homogeneous clip-space XYZW vector without a perspective divide.
        /// </summary>
        /// <param name="viewPosition">The view-space position to project.</param>
        /// <param name="projection">The view-to-clip projection matrix.</param>
        /// <returns>The resulting homogeneous clip-space vector.</returns>
        static ::float4 ProjectPosition(const ::float3& viewPosition, const ::float4x4& projection);
    };
}
