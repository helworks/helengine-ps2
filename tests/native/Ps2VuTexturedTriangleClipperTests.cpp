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
        REQUIRE(polygon.GetVertex(0u).ViewX == vertexA.ViewX);
        REQUIRE(polygon.GetVertex(0u).ViewY == vertexA.ViewY);
        REQUIRE(polygon.GetVertex(0u).ViewZ == vertexA.ViewZ);
        REQUIRE(polygon.GetVertex(0u).ClipX == vertexA.ClipX);
        REQUIRE(polygon.GetVertex(0u).ClipY == vertexA.ClipY);
        REQUIRE(polygon.GetVertex(0u).ClipZ == vertexA.ClipZ);
        REQUIRE(polygon.GetVertex(0u).ClipW == vertexA.ClipW);
        REQUIRE(polygon.GetVertex(0u).TextureU == vertexA.TextureU);
        REQUIRE(polygon.GetVertex(0u).TextureV == vertexA.TextureV);
        REQUIRE(polygon.GetVertex(1u).ViewX == vertexB.ViewX);
        REQUIRE(polygon.GetVertex(1u).ViewY == vertexB.ViewY);
        REQUIRE(polygon.GetVertex(1u).ViewZ == vertexB.ViewZ);
        REQUIRE(polygon.GetVertex(1u).ClipX == vertexB.ClipX);
        REQUIRE(polygon.GetVertex(1u).ClipY == vertexB.ClipY);
        REQUIRE(polygon.GetVertex(1u).ClipZ == vertexB.ClipZ);
        REQUIRE(polygon.GetVertex(1u).ClipW == vertexB.ClipW);
        REQUIRE(polygon.GetVertex(1u).TextureU == vertexB.TextureU);
        REQUIRE(polygon.GetVertex(1u).TextureV == vertexB.TextureV);
        REQUIRE(polygon.GetVertex(2u).ViewX == vertexC.ViewX);
        REQUIRE(polygon.GetVertex(2u).ViewY == vertexC.ViewY);
        REQUIRE(polygon.GetVertex(2u).ViewZ == vertexC.ViewZ);
        REQUIRE(polygon.GetVertex(2u).ClipX == vertexC.ClipX);
        REQUIRE(polygon.GetVertex(2u).ClipY == vertexC.ClipY);
        REQUIRE(polygon.GetVertex(2u).ClipZ == vertexC.ClipZ);
        REQUIRE(polygon.GetVertex(2u).ClipW == vertexC.ClipW);
        REQUIRE(polygon.GetVertex(2u).TextureU == vertexC.TextureU);
        REQUIRE(polygon.GetVertex(2u).TextureV == vertexC.TextureV);
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
            10.0f, 20.0f, 1.0f,
            0.2f, -0.4f, 0.8f, 2.0f,
            0.1f, 0.2f
        };
        const Ps2VuTexturedClipVertex vertexB {
            -2.0f, 4.0f, -3.0f,
            1.0f, -2.0f, 3.0f, 4.0f,
            0.7f, 0.9f
        };
        const Ps2VuTexturedClipVertex vertexC {
            6.0f, -8.0f, -3.0f,
            -1.0f, 2.0f, -4.0f, 5.0f,
            0.3f, 0.5f
        };
        Ps2VuTexturedClipPolygon polygon;

        Ps2VuTexturedTriangleClipper::ClipTriangle(vertexA, vertexB, vertexC, NearPlaneDistance, polygon);

        REQUIRE(polygon.GetVertexCount() == 4u);
        const float expectedAmount = 0.5f;
        const float firstAmount = (polygon.GetVertex(0u).ViewX - vertexC.ViewX) / (vertexA.ViewX - vertexC.ViewX);
        const float secondAmount = (polygon.GetVertex(1u).ViewX - vertexA.ViewX) / (vertexB.ViewX - vertexA.ViewX);
        REQUIRE(std::isfinite(firstAmount));
        REQUIRE(std::isfinite(secondAmount));
        REQUIRE(firstAmount >= 0.0f && firstAmount <= 1.0f);
        REQUIRE(secondAmount >= 0.0f && secondAmount <= 1.0f);
        REQUIRE(std::abs(firstAmount - expectedAmount) <= Tolerance);
        REQUIRE(std::abs(secondAmount - expectedAmount) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ViewX - (vertexC.ViewX + ((vertexA.ViewX - vertexC.ViewX) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ViewY - (vertexC.ViewY + ((vertexA.ViewY - vertexC.ViewY) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ViewZ - (vertexC.ViewZ + ((vertexA.ViewZ - vertexC.ViewZ) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipX - (vertexC.ClipX + ((vertexA.ClipX - vertexC.ClipX) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipY - (vertexC.ClipY + ((vertexA.ClipY - vertexC.ClipY) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipZ - (vertexC.ClipZ + ((vertexA.ClipZ - vertexC.ClipZ) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipW - (vertexC.ClipW + ((vertexA.ClipW - vertexC.ClipW) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).TextureU - (vertexC.TextureU + ((vertexA.TextureU - vertexC.TextureU) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(0u).TextureV - (vertexC.TextureV + ((vertexA.TextureV - vertexC.TextureV) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ViewX - (vertexA.ViewX + ((vertexB.ViewX - vertexA.ViewX) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ViewY - (vertexA.ViewY + ((vertexB.ViewY - vertexA.ViewY) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ViewZ - (vertexA.ViewZ + ((vertexB.ViewZ - vertexA.ViewZ) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipX - (vertexA.ClipX + ((vertexB.ClipX - vertexA.ClipX) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipY - (vertexA.ClipY + ((vertexB.ClipY - vertexA.ClipY) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipZ - (vertexA.ClipZ + ((vertexB.ClipZ - vertexA.ClipZ) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipW - (vertexA.ClipW + ((vertexB.ClipW - vertexA.ClipW) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).TextureU - (vertexA.TextureU + ((vertexB.TextureU - vertexA.TextureU) * expectedAmount))) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).TextureV - (vertexA.TextureV + ((vertexB.TextureV - vertexA.TextureV) * expectedAmount))) <= Tolerance);
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
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipX + polygon.GetVertex(0u).ClipW) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipX + polygon.GetVertex(1u).ClipW) <= Tolerance);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipX + polygon.GetVertex(index).ClipW >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(rightOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipW - polygon.GetVertex(0u).ClipX) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipW - polygon.GetVertex(1u).ClipX) <= Tolerance);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipW - polygon.GetVertex(index).ClipX >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(bottomOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipY + polygon.GetVertex(0u).ClipW) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipY + polygon.GetVertex(1u).ClipW) <= Tolerance);
        for (std::size_t index = 0u; index < polygon.GetVertexCount(); ++index) {
            REQUIRE(polygon.GetVertex(index).ClipY + polygon.GetVertex(index).ClipW >= -Tolerance);
        }

        Ps2VuTexturedTriangleClipper::ClipTriangle(topOutside, vertexB, vertexC, NearPlaneDistance, polygon);
        REQUIRE(polygon.GetVertexCount() == 4u);
        REQUIRE(std::abs(polygon.GetVertex(0u).ClipW - polygon.GetVertex(0u).ClipY) <= Tolerance);
        REQUIRE(std::abs(polygon.GetVertex(1u).ClipW - polygon.GetVertex(1u).ClipY) <= Tolerance);
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
