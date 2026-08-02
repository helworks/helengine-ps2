#pragma once

#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp"

#include <array>
#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Owns one VU1-memory-bounded sequence of pretransformed clipped textured triangle records.
    /// </summary>
    class Ps2VuClippedTexturedBatch final {
    public:
        /// <summary>
        /// Initializes an empty clipped batch with its fixed VU1-safe storage ready for use.
        /// </summary>
        Ps2VuClippedTexturedBatch();

        /// <summary>
        /// Clears only the logical record count so existing inline data can be reused without allocation or erasure.
        /// </summary>
        void Clear();

        /// <summary>
        /// Reports whether the full requested triangle count fits without exceeding this batch's fixed capacity.
        /// </summary>
        /// <param name="triangleCount">The number of complete triangle records proposed for appending.</param>
        /// <returns><c>true</c> when every requested record fits; otherwise, <c>false</c>.</returns>
        bool CanAppend(std::size_t triangleCount) const;

        /// <summary>
        /// Appends one complete triangle record or throws without mutation when no capacity remains.
        /// </summary>
        /// <param name="triangle">The pretransformed clipped triangle record to append.</param>
        void Append(const Ps2VuClippedTexturedTriangleSource& triangle);

        /// <summary>
        /// Appends every triangle from a generated fan atomically or throws without copying any fan entries.
        /// </summary>
        /// <param name="fan">The generated fan whose complete logical contents must fit.</param>
        void Append(const Ps2VuClippedTexturedTriangleFan& fan);

        /// <summary>
        /// Returns the first element of the fixed record storage for DMA submission.
        /// </summary>
        /// <returns>A pointer to the fixed triangle record array.</returns>
        const Ps2VuClippedTexturedTriangleSource* GetTriangles() const;

        /// <summary>
        /// Returns the number of logical triangle records in this batch.
        /// </summary>
        /// <returns>The current clipped triangle count.</returns>
        std::size_t GetTriangleCount() const;

    private:
        /// <summary>
        /// Stores every VU1-safe clipped source record inline without dynamic allocation.
        /// </summary>
        std::array<Ps2VuClippedTexturedTriangleSource, TexturedVuClippedTriangleCapacity> Triangles;

        /// <summary>
        /// Tracks the number of logical entries currently valid at the front of <see cref="Triangles"/>.
        /// </summary>
        std::size_t TriangleCount;
    };
}
