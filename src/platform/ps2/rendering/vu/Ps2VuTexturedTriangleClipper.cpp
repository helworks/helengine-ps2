#include "platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp"

#include <algorithm>
#include <cmath>
#include <stdexcept>

namespace helengine::ps2 {
    namespace {
        constexpr float CrossingDenominatorMinimum = 0.0000001f;
    }

    void Ps2VuTexturedTriangleClipper::ClipTriangle(
        const Ps2VuTexturedClipVertex& vertexA,
        const Ps2VuTexturedClipVertex& vertexB,
        const Ps2VuTexturedClipVertex& vertexC,
        float nearPlaneDistance,
        Ps2VuTexturedClipPolygon& outputPolygon) {
        if (!std::isfinite(nearPlaneDistance) || nearPlaneDistance <= 0.0f) {
            throw std::invalid_argument("The textured clipping near plane distance must be finite and positive.");
        }

        if (!IsFinite(vertexA) || !IsFinite(vertexB) || !IsFinite(vertexC)) {
            throw std::invalid_argument("The textured clipping triangle contains a non-finite vertex component.");
        }

        Ps2VuTexturedClipPolygon firstPolygon;
        Ps2VuTexturedClipPolygon secondPolygon;
        firstPolygon.Append(vertexA);
        firstPolygon.Append(vertexB);
        firstPolygon.Append(vertexC);

        ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Near, nearPlaneDistance);
        ClipAgainstPlane(secondPolygon, firstPolygon, Plane::Left, nearPlaneDistance);
        ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Right, nearPlaneDistance);
        ClipAgainstPlane(secondPolygon, firstPolygon, Plane::Bottom, nearPlaneDistance);
        ClipAgainstPlane(firstPolygon, secondPolygon, Plane::Top, nearPlaneDistance);

        outputPolygon.Clear();
        for (std::size_t index = 0u; index < secondPolygon.GetVertexCount(); ++index) {
            outputPolygon.Append(secondPolygon.GetVertex(index));
        }
    }

    void Ps2VuTexturedTriangleClipper::ClipAgainstPlane(
        const Ps2VuTexturedClipPolygon& inputPolygon,
        Ps2VuTexturedClipPolygon& outputPolygon,
        Plane plane,
        float nearPlaneDistance) {
        outputPolygon.Clear();

        if (inputPolygon.GetVertexCount() == 0u) {
            return;
        }

        Ps2VuTexturedClipVertex previousVertex = inputPolygon.GetVertex(inputPolygon.GetVertexCount() - 1u);
        float previousDistance = GetPlaneDistance(previousVertex, plane, nearPlaneDistance);
        if (!std::isfinite(previousDistance)) {
            throw std::runtime_error("The textured clipping plane distance became non-finite.");
        }

        bool previousInside = previousDistance >= 0.0f;
        for (std::size_t index = 0u; index < inputPolygon.GetVertexCount(); ++index) {
            const Ps2VuTexturedClipVertex& currentVertex = inputPolygon.GetVertex(index);
            const float currentDistance = GetPlaneDistance(currentVertex, plane, nearPlaneDistance);
            if (!std::isfinite(currentDistance)) {
                throw std::runtime_error("The textured clipping plane distance became non-finite.");
            }

            const bool currentInside = currentDistance >= 0.0f;
            if (previousInside != currentInside) {
                const float denominator = previousDistance - currentDistance;
                if (!std::isfinite(denominator) || std::abs(denominator) <= CrossingDenominatorMinimum) {
                    throw std::runtime_error("The textured clipping edge crossing denominator violated its invariant.");
                }

                const float amount = std::clamp(
                    previousDistance / denominator,
                    0.0f,
                    1.0f);
                if (!std::isfinite(amount)) {
                    throw std::runtime_error("The textured clipping edge interpolation amount became non-finite.");
                }

                outputPolygon.Append(Interpolate(previousVertex, currentVertex, amount));
            }

            if (currentInside) {
                outputPolygon.Append(currentVertex);
            }

            previousVertex = currentVertex;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }
    }

    float Ps2VuTexturedTriangleClipper::GetPlaneDistance(
        const Ps2VuTexturedClipVertex& vertex,
        Plane plane,
        float nearPlaneDistance) {
        switch (plane) {
            case Plane::Near:
                return -nearPlaneDistance - vertex.ViewZ;
            case Plane::Left:
                return vertex.ClipX + vertex.ClipW;
            case Plane::Right:
                return vertex.ClipW - vertex.ClipX;
            case Plane::Bottom:
                return vertex.ClipY + vertex.ClipW;
            case Plane::Top:
                return vertex.ClipW - vertex.ClipY;
        }

        throw std::runtime_error("The textured clipping plane is invalid.");
    }

    Ps2VuTexturedClipVertex Ps2VuTexturedTriangleClipper::Interpolate(
        const Ps2VuTexturedClipVertex& previousVertex,
        const Ps2VuTexturedClipVertex& currentVertex,
        float amount) {
        const Ps2VuTexturedClipVertex vertex {
            previousVertex.ViewX + ((currentVertex.ViewX - previousVertex.ViewX) * amount),
            previousVertex.ViewY + ((currentVertex.ViewY - previousVertex.ViewY) * amount),
            previousVertex.ViewZ + ((currentVertex.ViewZ - previousVertex.ViewZ) * amount),
            previousVertex.ClipX + ((currentVertex.ClipX - previousVertex.ClipX) * amount),
            previousVertex.ClipY + ((currentVertex.ClipY - previousVertex.ClipY) * amount),
            previousVertex.ClipZ + ((currentVertex.ClipZ - previousVertex.ClipZ) * amount),
            previousVertex.ClipW + ((currentVertex.ClipW - previousVertex.ClipW) * amount),
            previousVertex.TextureU + ((currentVertex.TextureU - previousVertex.TextureU) * amount),
            previousVertex.TextureV + ((currentVertex.TextureV - previousVertex.TextureV) * amount)
        };

        if (!IsFinite(vertex)) {
            throw std::runtime_error("The textured clipping edge interpolation produced a non-finite vertex component.");
        }

        return vertex;
    }

    bool Ps2VuTexturedTriangleClipper::IsFinite(const Ps2VuTexturedClipVertex& vertex) {
        return std::isfinite(vertex.ViewX)
            && std::isfinite(vertex.ViewY)
            && std::isfinite(vertex.ViewZ)
            && std::isfinite(vertex.ClipX)
            && std::isfinite(vertex.ClipY)
            && std::isfinite(vertex.ClipZ)
            && std::isfinite(vertex.ClipW)
            && std::isfinite(vertex.TextureU)
            && std::isfinite(vertex.TextureV);
    }
}
