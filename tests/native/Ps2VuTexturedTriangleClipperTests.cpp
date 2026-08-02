#include "platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp"

#include <cmath>
#include <cstddef>
#include <iostream>
#include <limits>
#include <stdexcept>

namespace {
    constexpr float NearPlaneDistance = 1.0f;
    constexpr float Tolerance = 0.00001f;

    #define REQUIRE(condition) \
        do { \
            if (!(condition)) { \
                std::cerr << "Requirement failed at line " << __LINE__ << ": " << #condition << std::endl; \
                return false; \
            } \
        } while (false)

    bool TestPolygonCapacityAndBounds() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;

        REQUIRE(Ps2VuTexturedClipPolygon::Capacity == 9u);

        Ps2VuTexturedClipPolygon polygon;
        const Ps2VuTexturedClipVertex vertex {
            1.0f, 2.0f, -2.0f,
            0.0f, 0.0f, 0.0f, 1.0f,
            0.25f, 0.75f
        };

        for (std::size_t index = 0u; index < Ps2VuTexturedClipPolygon::Capacity; ++index) {
            polygon.Append(vertex);
        }

        REQUIRE(polygon.GetVertexCount() == Ps2VuTexturedClipPolygon::Capacity);

        try {
            polygon.Append(vertex);
            REQUIRE(false);
        } catch (const std::overflow_error&) {
        }

        try {
            polygon.GetVertex(Ps2VuTexturedClipPolygon::Capacity);
            REQUIRE(false);
        } catch (const std::out_of_range&) {
        }

        polygon.Clear();
        REQUIRE(polygon.GetVertexCount() == 0u);
        return true;
    }

    bool TestFullyInsideTriangleKeepsOrder() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex vertexA {
            -0.5f, -0.5f, -2.0f,
            -0.5f, -0.5f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            0.5f, -0.5f, -2.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 0.5f, -2.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.5f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 3u);
        REQUIRE(polygon.GetVertex(0u).TextureU == vertexA.TextureU);
        REQUIRE(polygon.GetVertex(1u).TextureU == vertexB.TextureU);
        REQUIRE(polygon.GetVertex(2u).TextureU == vertexC.TextureU);
        REQUIRE(polygon.GetVertex(0u).ViewX == vertexA.ViewX);
        REQUIRE(polygon.GetVertex(1u).ViewX == vertexB.ViewX);
        REQUIRE(polygon.GetVertex(2u).ViewX == vertexC.ViewX);
        return true;
    }

    bool TestFullyOutsideNearPlaneReturnsNoVertices() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex vertexA {
            -0.5f, -0.5f, 0.0f,
            -0.5f, -0.5f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            0.5f, -0.5f, 0.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 0.5f, 0.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.5f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 0u);
        return true;
    }

    bool TestOneNearPlaneVertexOutsideInterpolatesUvs() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex vertexA {
            0.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            1.0f, 0.0f, -2.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 1.0f, -2.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.0f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 4u);
        REQUIRE(std::abs(polygon.GetVertex(0u).TextureU - 0.0f) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).TextureV - 0.5f) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).TextureU - 0.5f) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).TextureV - 0.0f) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ViewZ + NearPlaneDistance) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ViewZ + NearPlaneDistance) <= Tolerance);
        REQUIRE(std::isfinite(polygon.GetVertex(0u).TextureU));
        REQUIRE(std::isfinite(polygon.GetVertex(0u).TextureV));
        REQUIRE(std::isfinite(polygon.GetVertex(1u).TextureU));
        REQUIRE(std::isfinite(polygon.GetVertex(1u).TextureV));
        return true;
    }

    bool TestTwoNearPlaneVerticesOutsideReturnsTriangle() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex vertexA {
            -0.5f, -0.5f, 0.0f,
            -0.5f, -0.5f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            0.5f, -0.5f, 0.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 0.5f, -2.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.5f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 3u);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(std::abs(polygon.GetVertex(index).ViewZ + NearPlaneDistance) <= NearPlaneDistance + Tolerance);
        }

        return true;
    }

    bool TestSidePlaneCrossings() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex leftOutside {
            -2.0f, 0.0f, -2.0f,
            -2.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex rightOutside {
            2.0f, 0.0f, -2.0f,
            2.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex bottomOutside {
            0.0f, -2.0f, -2.0f,
            0.0f, -2.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex topOutside {
            0.0f, 2.0f, -2.0f,
            0.0f, 2.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            0.5f, -0.5f, -2.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 0.5f, -2.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.5f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(leftOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipX + polygon.GetVertex(index).ClipW >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(rightOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipW - polygon.GetVertex(index).ClipX >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(bottomOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipY + polygon.GetVertex(index).ClipW >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(topOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipW - polygon.GetVertex(index).ClipY >= -Tolerance);
        }

        return true;
    }

    bool TestVertexOnPlaneDoesNotDuplicate() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex vertexA {
            -1.0f, 0.0f, -2.0f,
            -1.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexB {
            0.5f, -0.5f, -2.0f,
            0.5f, -0.5f, 0.0f, 1.0f,
            1.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex vertexC {
            0.0f, 0.5f, -2.0f,
            0.0f, 0.5f, 0.0f, 1.0f,
            0.5f, 1.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 3u);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipX + polygon.GetVertex(0u).ClipW) <= Tolerance);
        return true;
    }

    bool TestNonFiniteInputThrowsInvalidArgument() {
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::Ps2VuTexturedTriangleClipper;

        const Ps2VuTexturedClipVertex finiteVertex {
            0.0f, 0.0f, -2.0f,
            0.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex nanVertex {
            std::numeric_limits<float>::quiet_NaN(), 0.0f, -2.0f,
            0.0f, 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        const Ps2VuTexturedClipVertex infiniteVertex {
            0.0f, 0.0f, -2.0f,
            std::numeric_limits<float>::infinity(), 0.0f, 0.0f, 1.0f,
            0.0f, 0.0f
        };
        Ps2VuTexturedClipPolygon polygon;

        try {
            Ps2VuTexturedTriangleClipper::ClipTriangle(nanVertex, finiteVertex, finiteVertex, NearPlaneDistance, polygon);
            REQUIRE(false);
        } catch (const std::invalid_argument&) {
        }

        try {
            Ps2VuTexturedTriangleClipper::ClipTriangle(infiniteVertex, finiteVertex, finiteVertex, NearPlaneDistance, polygon);
            REQUIRE(false);
        } catch (const std::invalid_argument&) {
        }

        try {
            Ps2VuTexturedTriangleClipper::ClipTriangle(finiteVertex, finiteVertex, finiteVertex, 0.0f, polygon);
            REQUIRE(false);
        } catch (const std::invalid_argument&) {
        }

        return true;
    }
}

int main() {
    if (!TestPolygonCapacityAndBounds()) {
        return 1;
    }

    if (!TestFullyInsideTriangleKeepsOrder()) {
        return 1;
    }

    if (!TestFullyOutsideNearPlaneReturnsNoVertices()) {
        return 1;
    }

    if (!TestOneNearPlaneVertexOutsideInterpolatesUvs()) {
        return 1;
    }

    if (!TestTwoNearPlaneVerticesOutsideReturnsTriangle()) {
        return 1;
    }

    if (!TestSidePlaneCrossings()) {
        return 1;
    }

    if (!TestVertexOnPlaneDoesNotDuplicate()) {
        return 1;
    }

    if (!TestNonFiniteInputThrowsInvalidArgument()) {
        return 1;
    }

    std::cout << "Ps2VuTexturedTriangleClipperTests passed." << std::endl;
    return 0;
}
