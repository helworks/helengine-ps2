#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedBatchBuilder.hpp"

#include "platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp"

#include <cmath>
#include <stdexcept>

namespace helengine::ps2 {
    void Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan(
        const Ps2VuTexturedPackedTriangleSource& sourceTriangle,
        const ::float4x4& worldView,
        const ::float4x4& projection,
        float nearPlaneDistance,
        Ps2VuClippedTexturedTriangleFan& outputFan) {
        const ::float3 viewPositionA = TransformPosition(sourceTriangle.PositionA, worldView);
        const ::float3 viewPositionB = TransformPosition(sourceTriangle.PositionB, worldView);
        const ::float3 viewPositionC = TransformPosition(sourceTriangle.PositionC, worldView);
        const ::float4 clipPositionA = ProjectPosition(viewPositionA, projection);
        const ::float4 clipPositionB = ProjectPosition(viewPositionB, projection);
        const ::float4 clipPositionC = ProjectPosition(viewPositionC, projection);
        const Ps2VuTexturedClipVertex vertexA {
            viewPositionA.X, viewPositionA.Y, viewPositionA.Z,
            clipPositionA.X, clipPositionA.Y, clipPositionA.Z, clipPositionA.W,
            sourceTriangle.TexCoordA[0], sourceTriangle.TexCoordA[1]
        };
        const Ps2VuTexturedClipVertex vertexB {
            viewPositionB.X, viewPositionB.Y, viewPositionB.Z,
            clipPositionB.X, clipPositionB.Y, clipPositionB.Z, clipPositionB.W,
            sourceTriangle.TexCoordB[0], sourceTriangle.TexCoordB[1]
        };
        const Ps2VuTexturedClipVertex vertexC {
            viewPositionC.X, viewPositionC.Y, viewPositionC.Z,
            clipPositionC.X, clipPositionC.Y, clipPositionC.Z, clipPositionC.W,
            sourceTriangle.TexCoordC[0], sourceTriangle.TexCoordC[1]
        };
        Ps2VuTexturedClipPolygon clippedPolygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, nearPlaneDistance, clippedPolygon);
        if (clippedPolygon.GetVertexCount() < 3u) {
            outputFan.Clear();
            return;
        }

        for (std::size_t index = 0u; index < clippedPolygon.GetVertexCount(); ++index) {
            const Ps2VuTexturedClipVertex& clippedVertex = clippedPolygon.GetVertex(index);
            if (!std::isfinite(clippedVertex.ClipW) || clippedVertex.ClipW <= 0.0001f) {
                throw std::runtime_error("The clipped textured triangle contains an invalid homogeneous clip W component.");
            }
        }

        outputFan.BuildFromClippedPolygon(clippedPolygon, sourceTriangle.FaceNormal);
    }

    ::float3 Ps2VuClippedTexturedBatchBuilder::TransformPosition(
        const float position[4],
        const ::float4x4& worldView) {
        return ::float3(
            (position[0] * worldView.M11) + (position[1] * worldView.M21) + (position[2] * worldView.M31) + (position[3] * worldView.M41),
            (position[0] * worldView.M12) + (position[1] * worldView.M22) + (position[2] * worldView.M32) + (position[3] * worldView.M42),
            (position[0] * worldView.M13) + (position[1] * worldView.M23) + (position[2] * worldView.M33) + (position[3] * worldView.M43));
    }

    ::float4 Ps2VuClippedTexturedBatchBuilder::ProjectPosition(
        const ::float3& viewPosition,
        const ::float4x4& projection) {
        return ::float4(
            (viewPosition.X * projection.M11) + (viewPosition.Y * projection.M21) + (viewPosition.Z * projection.M31) + projection.M41,
            (viewPosition.X * projection.M12) + (viewPosition.Y * projection.M22) + (viewPosition.Z * projection.M32) + projection.M42,
            (viewPosition.X * projection.M13) + (viewPosition.Y * projection.M23) + (viewPosition.Z * projection.M33) + projection.M43,
            (viewPosition.X * projection.M14) + (viewPosition.Y * projection.M24) + (viewPosition.Z * projection.M34) + projection.M44);
    }
}
