#pragma once

#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleSource.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp"

#include <array>
#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Owns the fixed triangle fan generated from one clipped textured polygon without allocating memory.
    /// </summary>
    class Ps2VuClippedTexturedTriangleFan final {
    public:
        /// <summary>
        /// Defines the largest fan produced by the nine-vertex clipping polygon.
        /// </summary>
        static constexpr std::size_t Capacity = Ps2VuTexturedClipPolygon::Capacity - 2u;

        /// <summary>
        /// Initializes an empty generated fan with fixed inline record storage.
        /// </summary>
        Ps2VuClippedTexturedTriangleFan();

        /// <summary>
        /// Removes every generated triangle while retaining fixed storage for the next clipped polygon.
        /// </summary>
        void Clear();

        /// <summary>
        /// Converts a clipped polygon into stable (0, index, index + 1) triangles while copying the supplied flat normal unchanged.
        /// </summary>
        /// <param name="polygon">The fixed clipped polygon to triangulate.</param>
        /// <param name="sourceFaceNormal">The four packed normal components copied into every generated record.</param>
        void BuildFromClippedPolygon(const Ps2VuTexturedClipPolygon& polygon, const float sourceFaceNormal[4]);

        /// <summary>
        /// Returns one generated triangle at a valid logical index.
        /// </summary>
        /// <param name="index">The zero-based triangle index in fan order.</param>
        /// <returns>The requested pretransformed triangle source record.</returns>
        const Ps2VuClippedTexturedTriangleSource& GetTriangle(std::size_t index) const;

        /// <summary>
        /// Returns the number of valid generated triangle records.
        /// </summary>
        /// <returns>The logical fan triangle count.</returns>
        std::size_t GetTriangleCount() const;

    private:
        /// <summary>
        /// Stores every possible generated triangle inline for allocation-free fan construction.
        /// </summary>
        std::array<Ps2VuClippedTexturedTriangleSource, Capacity> Triangles;

        /// <summary>
        /// Tracks the number of logical entries currently present at the start of <see cref="Triangles"/>.
        /// </summary>
        std::size_t TriangleCount;
    };
}
