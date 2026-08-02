#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedBatch.hpp"

#include <stdexcept>

namespace helengine::ps2 {
    Ps2VuClippedTexturedBatch::Ps2VuClippedTexturedBatch()
        : TriangleCount(0u) {
    }

    void Ps2VuClippedTexturedBatch::Clear() {
        TriangleCount = 0u;
    }

    bool Ps2VuClippedTexturedBatch::CanAppend(std::size_t triangleCount) const {
        return triangleCount <= TexturedVuClippedTriangleCapacity
            && TriangleCount <= (TexturedVuClippedTriangleCapacity - triangleCount);
    }

    void Ps2VuClippedTexturedBatch::Append(const Ps2VuClippedTexturedTriangleSource& triangle) {
        if (!CanAppend(1u)) {
            throw std::overflow_error("The clipped textured batch has no capacity for another triangle.");
        }

        Triangles[TriangleCount] = triangle;
        ++TriangleCount;
    }

    void Ps2VuClippedTexturedBatch::Append(const Ps2VuClippedTexturedTriangleFan& fan) {
        const std::size_t fanTriangleCount = fan.GetTriangleCount();
        if (!CanAppend(fanTriangleCount)) {
            throw std::overflow_error("The clipped textured batch cannot append the complete generated fan.");
        }

        for (std::size_t index = 0u; index < fanTriangleCount; ++index) {
            Triangles[TriangleCount] = fan.GetTriangle(index);
            ++TriangleCount;
        }
    }

    const Ps2VuClippedTexturedTriangleSource* Ps2VuClippedTexturedBatch::GetTriangles() const {
        return Triangles.data();
    }

    std::size_t Ps2VuClippedTexturedBatch::GetTriangleCount() const {
        return TriangleCount;
    }
}
