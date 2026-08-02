#include "platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp"

#include <stdexcept>

namespace helengine::ps2 {
    Ps2VuTexturedClipPolygon::Ps2VuTexturedClipPolygon()
        : VertexCount(0u) {
    }

    void Ps2VuTexturedClipPolygon::Clear() {
        VertexCount = 0u;
    }

    void Ps2VuTexturedClipPolygon::Append(const Ps2VuTexturedClipVertex& vertex) {
        if (VertexCount >= Capacity) {
            throw std::overflow_error("The textured clipping polygon exceeded its fixed capacity.");
        }

        Vertices[VertexCount] = vertex;
        ++VertexCount;
    }

    const Ps2VuTexturedClipVertex& Ps2VuTexturedClipPolygon::GetVertex(std::size_t index) const {
        if (index >= VertexCount) {
            throw std::out_of_range("The textured clipping polygon vertex index is outside the logical vertex range.");
        }

        return Vertices[index];
    }

    std::size_t Ps2VuTexturedClipPolygon::GetVertexCount() const {
        return VertexCount;
    }
}
