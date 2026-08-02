#pragma once

#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Stores one pretransformed textured triangle for the clipped VU1 path in its deterministic seven-qword DMA layout.
    /// </summary>
    struct alignas(16) Ps2VuClippedTexturedTriangleSource final {
        /// <summary>
        /// Stores the first vertex's homogeneous clip-space XYZW position without perspective division.
        /// </summary>
        float ClipPositionA[4];

        /// <summary>
        /// Stores the second vertex's homogeneous clip-space XYZW position without perspective division.
        /// </summary>
        float ClipPositionB[4];

        /// <summary>
        /// Stores the third vertex's homogeneous clip-space XYZW position without perspective division.
        /// </summary>
        float ClipPositionC[4];

        /// <summary>
        /// Stores the first vertex's raw U and V values followed by deterministic zero padding.
        /// </summary>
        float TexCoordA[4];

        /// <summary>
        /// Stores the second vertex's raw U and V values followed by deterministic zero padding.
        /// </summary>
        float TexCoordB[4];

        /// <summary>
        /// Stores the third vertex's raw U and V values followed by deterministic zero padding.
        /// </summary>
        float TexCoordC[4];

        /// <summary>
        /// Stores all four packed components of the original source triangle's flat face normal.
        /// </summary>
        float FaceNormal[4];
    };

    static_assert(sizeof(Ps2VuClippedTexturedTriangleSource) == 7u * 16u);
}
