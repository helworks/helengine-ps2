#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp"

#include <stdexcept>

namespace helengine::ps2 {
    Ps2VuClippedTexturedTriangleFan::Ps2VuClippedTexturedTriangleFan()
        : TriangleCount(0u) {
    }

    void Ps2VuClippedTexturedTriangleFan::Clear() {
        TriangleCount = 0u;
    }

    void Ps2VuClippedTexturedTriangleFan::BuildFromClippedPolygon(
        const Ps2VuTexturedClipPolygon& polygon,
        const float sourceFaceNormal[4]) {
        if (sourceFaceNormal == nullptr) {
            throw std::invalid_argument("The clipped textured triangle fan requires a source face normal.");
        }

        const std::size_t vertexCount = polygon.GetVertexCount();
        if (vertexCount < 3u) {
            Clear();
            return;
        }

        const std::size_t generatedTriangleCount = vertexCount - 2u;
        if (generatedTriangleCount > Capacity) {
            throw std::overflow_error("The clipped textured triangle fan exceeded its fixed capacity.");
        }

        Clear();
        const Ps2VuTexturedClipVertex& firstVertex = polygon.GetVertex(0u);
        for (std::size_t index = 1u; index < (vertexCount - 1u); ++index) {
            const Ps2VuTexturedClipVertex& secondVertex = polygon.GetVertex(index);
            const Ps2VuTexturedClipVertex& thirdVertex = polygon.GetVertex(index + 1u);
            Triangles[TriangleCount] = Ps2VuClippedTexturedTriangleSource {
                { firstVertex.ClipX, firstVertex.ClipY, firstVertex.ClipZ, firstVertex.ClipW },
                { secondVertex.ClipX, secondVertex.ClipY, secondVertex.ClipZ, secondVertex.ClipW },
                { thirdVertex.ClipX, thirdVertex.ClipY, thirdVertex.ClipZ, thirdVertex.ClipW },
                { firstVertex.TextureU, firstVertex.TextureV, 0.0f, 0.0f },
                { secondVertex.TextureU, secondVertex.TextureV, 0.0f, 0.0f },
                { thirdVertex.TextureU, thirdVertex.TextureV, 0.0f, 0.0f },
                { sourceFaceNormal[0], sourceFaceNormal[1], sourceFaceNormal[2], sourceFaceNormal[3] }
            };
            ++TriangleCount;
        }
    }

    const Ps2VuClippedTexturedTriangleSource& Ps2VuClippedTexturedTriangleFan::GetTriangle(std::size_t index) const {
        if (index >= TriangleCount) {
            throw std::out_of_range("The clipped textured triangle fan index is outside the logical triangle range.");
        }

        return Triangles[index];
    }

    std::size_t Ps2VuClippedTexturedTriangleFan::GetTriangleCount() const {
        return TriangleCount;
    }
}
