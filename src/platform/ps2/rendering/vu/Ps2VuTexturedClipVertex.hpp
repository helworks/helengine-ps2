#pragma once

namespace helengine::ps2 {
    /// <summary>
    /// Stores one textured clipping vertex in view and homogeneous clip space without owning dynamic memory.
    /// </summary>
    struct Ps2VuTexturedClipVertex final {
        /// <summary>
        /// Gets or sets the view-space horizontal coordinate.
        /// </summary>
        float ViewX;

        /// <summary>
        /// Gets or sets the view-space vertical coordinate.
        /// </summary>
        float ViewY;

        /// <summary>
        /// Gets or sets the view-space depth coordinate.
        /// </summary>
        float ViewZ;

        /// <summary>
        /// Gets or sets the homogeneous clip-space horizontal coordinate.
        /// </summary>
        float ClipX;

        /// <summary>
        /// Gets or sets the homogeneous clip-space vertical coordinate.
        /// </summary>
        float ClipY;

        /// <summary>
        /// Gets or sets the homogeneous clip-space depth coordinate.
        /// </summary>
        float ClipZ;

        /// <summary>
        /// Gets or sets the homogeneous clip-space perspective coordinate.
        /// </summary>
        float ClipW;

        /// <summary>
        /// Gets or sets the unmodified horizontal texture coordinate.
        /// </summary>
        float TextureU;

        /// <summary>
        /// Gets or sets the unmodified vertical texture coordinate.
        /// </summary>
        float TextureV;
    };
}
