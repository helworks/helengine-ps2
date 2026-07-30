#pragma once

#include "float3.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Stores conservative local-space center and extents for one fixed-capacity textured VU1 source slice.
    /// </summary>
    struct Ps2VuSourceSliceBounds final {
        /// <summary>
        /// Gets the midpoint of the source slice's local-space axis-aligned bounds.
        /// </summary>
        ::float3 Center;

        /// <summary>
        /// Gets the positive half-size of the source slice's local-space axis-aligned bounds.
        /// </summary>
        ::float3 Extents;
    };
}
