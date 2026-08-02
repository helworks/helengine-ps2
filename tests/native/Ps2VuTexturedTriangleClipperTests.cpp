#include "platform/ps2/rendering/vu/Ps2VuTexturedClipPolygon.hpp"
#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedBatch.hpp"
#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedTriangleClipper.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp"

#include <cmath>
#include <cstddef>
#include <iostream>
#include <limits>
#include <stdexcept>

namespace {
    /// <summary>
    /// Defines the positive distance from the camera to the clipping near plane used by native clipper tests.
    /// </summary>
    constexpr float NearPlaneDistance = 1.0f;

    /// <summary>
    /// Defines the maximum accepted floating-point difference for deterministic clipping assertions.
    /// </summary>
    constexpr float Tolerance = 0.00001f;

    /// <summary>
    /// Stops the current native test and reports the source line when a required condition is false.
    /// </summary>
    #define REQUIRE(condition) \
        do { \
            if (!(condition)) { \
                std::cerr << "Requirement failed at line " << __LINE__ << ": " << #condition << std::endl; \
                return false; \
            } \
        } while (false)

    /// <summary>
    /// Verifies that the fixed clipping polygon accepts exactly its inline capacity and rejects invalid access.
    /// </summary>
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

    /// <summary>
    /// Verifies that a triangle fully inside every clipping plane retains its original winding and components.
    /// </summary>
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

    /// <summary>
    /// Verifies that a triangle fully before the near plane produces an empty polygon.
    /// </summary>
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

    /// <summary>
    /// Verifies that one near-plane crossing interpolates view, clip, and raw texture values consistently.
    /// </summary>
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

    /// <summary>
    /// Verifies that clipping a triangle with two vertices before the near plane yields one bounded triangle.
    /// </summary>
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

    /// <summary>
    /// Verifies that every homogeneous side-plane crossing lies on its expected boundary.
    /// </summary>
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

    /// <summary>
    /// Verifies that a vertex already on a clipping plane is retained without a duplicate intersection.
    /// </summary>
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

    /// <summary>
    /// Verifies that non-finite vertex data and an invalid near plane fail with invalid-argument errors.
    /// </summary>
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

    /// <summary>
    /// Verifies that a three-vertex polygon produces one homogeneous, raw-UV, flat-normal source record.
    /// </summary>
    bool TestTriangleFanBuildsOneTriangleFromThreeVertices() {
        using helengine::ps2::Ps2VuClippedTexturedTriangleFan;
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;

        Ps2VuTexturedClipPolygon polygon;
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 1.0f, 2.0f, 3.0f, 4.0f, 0.1f, 0.2f });
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 5.0f, 6.0f, 7.0f, 8.0f, 0.3f, 0.4f });
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 9.0f, 10.0f, 11.0f, 12.0f, 0.5f, 0.6f });
        const float sourceNormal[] = { 0.7f, 0.8f, 0.9f, 1.0f };
        Ps2VuClippedTexturedTriangleFan fan;

        fan.BuildFromClippedPolygon(polygon, sourceNormal);

        REQUIRE(fan.GetTriangleCount() == 1u);
        const auto& triangle = fan.GetTriangle(0u);
        REQUIRE(triangle.ClipPositionA[0] == 1.0f);
        REQUIRE(triangle.ClipPositionA[1] == 2.0f);
        REQUIRE(triangle.ClipPositionA[2] == 3.0f);
        REQUIRE(triangle.ClipPositionA[3] == 4.0f);
        REQUIRE(triangle.ClipPositionB[0] == 5.0f);
        REQUIRE(triangle.ClipPositionC[3] == 12.0f);
        REQUIRE(triangle.TexCoordA[0] == 0.1f);
        REQUIRE(triangle.TexCoordA[1] == 0.2f);
        REQUIRE(triangle.TexCoordA[2] == 0.0f);
        REQUIRE(triangle.TexCoordA[3] == 0.0f);
        REQUIRE(triangle.TexCoordB[0] == 0.3f);
        REQUIRE(triangle.TexCoordC[1] == 0.6f);
        REQUIRE(triangle.TexCoordC[2] == 0.0f);
        REQUIRE(triangle.TexCoordC[3] == 0.0f);
        REQUIRE(triangle.FaceNormal[0] == sourceNormal[0]);
        REQUIRE(triangle.FaceNormal[1] == sourceNormal[1]);
        REQUIRE(triangle.FaceNormal[2] == sourceNormal[2]);
        REQUIRE(triangle.FaceNormal[3] == sourceNormal[3]);
        return true;
    }

    /// <summary>
    /// Verifies that a quadrilateral produces the stable two-triangle fan order and preserves its flat normal.
    /// </summary>
    bool TestTriangleFanBuildsStableOrderFromFourVertices() {
        using helengine::ps2::Ps2VuClippedTexturedTriangleFan;
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;

        Ps2VuTexturedClipPolygon polygon;
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 10.0f, 11.0f, 12.0f, 13.0f, 0.1f, 0.2f });
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 20.0f, 21.0f, 22.0f, 23.0f, 0.3f, 0.4f });
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 30.0f, 31.0f, 32.0f, 33.0f, 0.5f, 0.6f });
        polygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 40.0f, 41.0f, 42.0f, 43.0f, 0.7f, 0.8f });
        const float sourceNormal[] = { -1.0f, 2.0f, -3.0f, 4.0f };
        Ps2VuClippedTexturedTriangleFan fan;

        fan.BuildFromClippedPolygon(polygon, sourceNormal);

        REQUIRE(fan.GetTriangleCount() == 2u);
        const auto& firstTriangle = fan.GetTriangle(0u);
        const auto& secondTriangle = fan.GetTriangle(1u);
        REQUIRE(firstTriangle.ClipPositionA[0] == 10.0f);
        REQUIRE(firstTriangle.ClipPositionB[0] == 20.0f);
        REQUIRE(firstTriangle.ClipPositionC[0] == 30.0f);
        REQUIRE(secondTriangle.ClipPositionA[0] == 10.0f);
        REQUIRE(secondTriangle.ClipPositionB[0] == 30.0f);
        REQUIRE(secondTriangle.ClipPositionC[0] == 40.0f);
        REQUIRE(secondTriangle.TexCoordA[0] == 0.1f);
        REQUIRE(secondTriangle.TexCoordB[0] == 0.5f);
        REQUIRE(secondTriangle.TexCoordC[1] == 0.8f);
        for (std::size_t index = 0u; index < 4u; ++index) {
            REQUIRE(firstTriangle.FaceNormal[index] == sourceNormal[index]);
            REQUIRE(secondTriangle.FaceNormal[index] == sourceNormal[index]);
        }

        try {
            fan.GetTriangle(fan.GetTriangleCount());
            REQUIRE(false);
        } catch (const std::out_of_range&) {
        }

        return true;
    }

    /// <summary>
    /// Verifies that a clipped batch preserves complete fan records and rejects capacity overflow without mutation.
    /// </summary>
    bool TestClippedBatchAppendsFansAtomicallyAtCapacity() {
        using helengine::ps2::Ps2VuClippedTexturedBatch;
        using helengine::ps2::Ps2VuClippedTexturedTriangleFan;
        using helengine::ps2::Ps2VuTexturedClipPolygon;
        using helengine::ps2::Ps2VuTexturedClipVertex;
        using helengine::ps2::TexturedVuClippedInputTriangleCapacity;
        using helengine::ps2::TexturedVuClippedOutputTriangleCapacity;
        using helengine::ps2::TexturedVuClippedTriangleCapacity;
        using helengine::ps2::TexturedVuDataMemoryQwordCount;
        using helengine::ps2::TexturedVuGifStateQwordCount;
        using helengine::ps2::TexturedVuOutputQwordsPerTriangle;
        using helengine::ps2::TexturedVuOutputStartQword;
        using helengine::ps2::TexturedVuSharedStateQwordCount;

        REQUIRE(TexturedVuClippedTriangleCapacity == 33u);
        REQUIRE(TexturedVuSharedStateQwordCount + (TexturedVuClippedInputTriangleCapacity * 7u) <= TexturedVuOutputStartQword);
        REQUIRE(TexturedVuOutputStartQword + TexturedVuGifStateQwordCount + (TexturedVuClippedOutputTriangleCapacity * TexturedVuOutputQwordsPerTriangle) <= TexturedVuDataMemoryQwordCount);

        Ps2VuTexturedClipPolygon trianglePolygon;
        trianglePolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 1.0f, 2.0f, 3.0f, 4.0f, 0.1f, 0.2f });
        trianglePolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 5.0f, 6.0f, 7.0f, 8.0f, 0.3f, 0.4f });
        trianglePolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 9.0f, 10.0f, 11.0f, 12.0f, 0.5f, 0.6f });
        Ps2VuTexturedClipPolygon quadPolygon;
        quadPolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 1.0f, 2.0f, 3.0f, 4.0f, 0.1f, 0.2f });
        quadPolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 5.0f, 6.0f, 7.0f, 8.0f, 0.3f, 0.4f });
        quadPolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 9.0f, 10.0f, 11.0f, 12.0f, 0.5f, 0.6f });
        quadPolygon.Append(Ps2VuTexturedClipVertex { 0.0f, 0.0f, -2.0f, 13.0f, 14.0f, 15.0f, 16.0f, 0.7f, 0.8f });
        const float sourceNormal[] = { 1.0f, 2.0f, 3.0f, 4.0f };
        Ps2VuClippedTexturedTriangleFan singleTriangleFan;
        Ps2VuClippedTexturedTriangleFan twoTriangleFan;
        singleTriangleFan.BuildFromClippedPolygon(trianglePolygon, sourceNormal);
        twoTriangleFan.BuildFromClippedPolygon(quadPolygon, sourceNormal);
        Ps2VuClippedTexturedBatch batch;

        REQUIRE(batch.GetTriangleCount() == 0u);
        REQUIRE(batch.CanAppend(TexturedVuClippedTriangleCapacity));
        REQUIRE(!batch.CanAppend(TexturedVuClippedTriangleCapacity + 1u));
        for (std::size_t index = 0u; index < (TexturedVuClippedTriangleCapacity - 1u); ++index) {
            batch.Append(singleTriangleFan);
        }

        const auto firstTriangle = batch.GetTriangles()[0u];
        REQUIRE(!batch.CanAppend(twoTriangleFan.GetTriangleCount()));
        try {
            batch.Append(twoTriangleFan);
            REQUIRE(false);
        } catch (const std::overflow_error&) {
        }

        REQUIRE(batch.GetTriangleCount() == TexturedVuClippedTriangleCapacity - 1u);
        REQUIRE(batch.GetTriangles()[0u].ClipPositionA[0] == firstTriangle.ClipPositionA[0]);
        REQUIRE(batch.GetTriangles()[0u].FaceNormal[3] == firstTriangle.FaceNormal[3]);
        batch.Append(singleTriangleFan);
        REQUIRE(batch.GetTriangleCount() == TexturedVuClippedTriangleCapacity);
        const auto finalTriangle = batch.GetTriangles()[TexturedVuClippedTriangleCapacity - 1u];
        try {
            batch.Append(singleTriangleFan.GetTriangle(0u));
            REQUIRE(false);
        } catch (const std::overflow_error&) {
        }

        REQUIRE(batch.GetTriangleCount() == TexturedVuClippedTriangleCapacity);
        REQUIRE(batch.GetTriangles()[TexturedVuClippedTriangleCapacity - 1u].ClipPositionC[3] == finalTriangle.ClipPositionC[3]);
        REQUIRE(batch.GetTriangles()[TexturedVuClippedTriangleCapacity - 1u].TexCoordC[1] == finalTriangle.TexCoordC[1]);
        batch.Clear();
        REQUIRE(batch.GetTriangleCount() == 0u);
        REQUIRE(batch.GetTriangles()[0u].ClipPositionA[0] == firstTriangle.ClipPositionA[0]);
        return true;
    }
}

/// <summary>
/// Runs the dependency-free native tests for clipping polygons, generated triangle fans, and bounded clipped batches.
/// </summary>
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

    if (!TestTriangleFanBuildsOneTriangleFromThreeVertices()) {
        return 1;
    }

    if (!TestTriangleFanBuildsStableOrderFromFourVertices()) {
        return 1;
    }

    if (!TestClippedBatchAppendsFansAtomicallyAtCapacity()) {
        return 1;
    }

    std::cout << "Ps2VuTexturedTriangleClipperTests passed." << std::endl;
    return 0;
}
