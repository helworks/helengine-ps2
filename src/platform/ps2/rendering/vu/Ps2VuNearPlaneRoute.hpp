#pragma once

namespace helengine::ps2 {
    /// <summary>
    /// Selects the safe textured VU1 submission path for one conservatively classified source slice.
    /// </summary>
    enum class Ps2VuNearPlaneRoute {
        Fast,
        Clipped,
        Rejected
    };
}
