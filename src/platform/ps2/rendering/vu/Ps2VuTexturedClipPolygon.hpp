#pragma once

#include "platform/ps2/rendering/vu/Ps2VuTexturedClipVertex.hpp"

#include <array>
#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Stores a bounded textured clipping polygon in fixed inline storage for allocation-free clipping passes.
    /// </summary>
    class Ps2VuTexturedClipPolygon final {
    public:
        /// <summary>
        /// Defines the maximum vertex count produced by clipping one triangle against five planes.
        /// </summary>
        static constexpr std::size_t Capacity = 9u;

        /// <summary>
        /// Initializes an empty polygon with its fixed vertex storage ready for use.
        /// </summary>
        Ps2VuTexturedClipPolygon();

        /// <summary>
        /// Removes every logical vertex while retaining the fixed storage for the next clipping pass.
        /// </summary>
        void Clear();

        /// <summary>
        /// Adds one vertex to the polygon or throws when doing so would exceed the fixed capacity.
        /// </summary>
        /// <param name="vertex">The vertex to append to the polygon.</param>
        void Append(const Ps2VuTexturedClipVertex& vertex);

        /// <summary>
        /// Returns a vertex at a valid logical index.
        /// </summary>
        /// <param name="index">The zero-based index of the requested vertex.</param>
        /// <returns>The requested stored vertex.</returns>
        const Ps2VuTexturedClipVertex& GetVertex(std::size_t index) const;

        /// <summary>
        /// Returns the number of logical vertices currently stored by the polygon.
        /// </summary>
        /// <returns>The current vertex count.</returns>
        std::size_t GetVertexCount() const;

    private:
        /// <summary>
        /// Stores all potential clipping vertices inline without dynamic allocation.
        /// </summary>
        std::array<Ps2VuTexturedClipVertex, Capacity> Vertices;

        /// <summary>
        /// Tracks how many entries at the start of <see cref="Vertices"/> are valid polygon vertices.
        /// </summary>
        std::size_t VertexCount;
    };
}
