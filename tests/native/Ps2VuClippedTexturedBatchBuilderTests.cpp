#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedBatchBuilder.hpp"
#include "platform/ps2/rendering/vu/Ps2VuClippedTexturedTriangleFan.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp"
#include "platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.hpp"
#include "platform/ps2/rendering/vu/Ps2VuNearPlaneRoute.hpp"
#include "platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp"

#include "float4x4.hpp"

#include <cmath>
#include <cstddef>
#include <cstdio>
#include <iostream>
#include <vector>

namespace {
    using helengine::ps2::Ps2VuClippedTexturedBatchBuilder;
    using helengine::ps2::Ps2VuClippedTexturedTriangleFan;
    using helengine::ps2::Ps2VuTexturedPackedTriangleSource;
    using helengine::ps2::Ps2VuNearPlaneSliceClassifier;
    using helengine::ps2::Ps2VuNearPlaneRoute;
    using helengine::ps2::Ps2VuSourceSliceBounds;

    struct Vec3 {
        float X;
        float Y;
        float Z;
    };

    Ps2VuTexturedPackedTriangleSource MakeTriangle(Vec3 a, Vec3 b, Vec3 c) {
        Ps2VuTexturedPackedTriangleSource triangle {};
        triangle.PositionA[0] = a.X; triangle.PositionA[1] = a.Y; triangle.PositionA[2] = a.Z; triangle.PositionA[3] = 1.0f;
        triangle.PositionB[0] = b.X; triangle.PositionB[1] = b.Y; triangle.PositionB[2] = b.Z; triangle.PositionB[3] = 1.0f;
        triangle.PositionC[0] = c.X; triangle.PositionC[1] = c.Y; triangle.PositionC[2] = c.Z; triangle.PositionC[3] = 1.0f;
        triangle.FaceNormal[0] = 0.0f; triangle.FaceNormal[1] = 1.0f; triangle.FaceNormal[2] = 0.0f; triangle.FaceNormal[3] = 0.0f;
        return triangle;
    }

    // 12 triangles of a unit cube (corners at +-0.5), two per face.
    std::vector<Ps2VuTexturedPackedTriangleSource> UnitCubeTriangles() {
        std::vector<Ps2VuTexturedPackedTriangleSource> triangles;
        Vec3 c[8] = {
            {-0.5f, -0.5f, -0.5f}, {0.5f, -0.5f, -0.5f}, {0.5f, 0.5f, -0.5f}, {-0.5f, 0.5f, -0.5f},
            {-0.5f, -0.5f, 0.5f}, {0.5f, -0.5f, 0.5f}, {0.5f, 0.5f, 0.5f}, {-0.5f, 0.5f, 0.5f}
        };
        int faceIndices[6][4] = {
            {0, 1, 2, 3}, // back  (-Z)
            {5, 4, 7, 6}, // front (+Z)
            {4, 0, 3, 7}, // left  (-X)
            {1, 5, 6, 2}, // right (+X)
            {4, 5, 1, 0}, // bottom (-Y)
            {3, 2, 6, 7}  // top    (+Y)
        };
        for (int face = 0; face < 6; ++face) {
            triangles.push_back(MakeTriangle(c[faceIndices[face][0]], c[faceIndices[face][1]], c[faceIndices[face][2]]));
            triangles.push_back(MakeTriangle(c[faceIndices[face][0]], c[faceIndices[face][2]], c[faceIndices[face][3]]));
        }
        return triangles;
    }

    void ScaleTriangle(Ps2VuTexturedPackedTriangleSource& triangle, float sx, float sy, float sz) {
        triangle.PositionA[0] *= sx; triangle.PositionA[1] *= sy; triangle.PositionA[2] *= sz;
        triangle.PositionB[0] *= sx; triangle.PositionB[1] *= sy; triangle.PositionB[2] *= sz;
        triangle.PositionC[0] *= sx; triangle.PositionC[1] *= sy; triangle.PositionC[2] *= sz;
    }

    // Subdivides one source triangle into a barycentric grid of `segments` per side, mimicking
    // real edge-length tessellation closely enough to stress the same classify/clip/cull pipeline.
    std::vector<Ps2VuTexturedPackedTriangleSource> SubdivideTriangle(const Ps2VuTexturedPackedTriangleSource& source, int segments) {
        std::vector<Ps2VuTexturedPackedTriangleSource> result;
        Vec3 a { source.PositionA[0], source.PositionA[1], source.PositionA[2] };
        Vec3 b { source.PositionB[0], source.PositionB[1], source.PositionB[2] };
        Vec3 c { source.PositionC[0], source.PositionC[1], source.PositionC[2] };
        auto Lerp3 = [](Vec3 p0, Vec3 p1, Vec3 p2, float u, float v) -> Vec3 {
            float w = 1.0f - u - v;
            return Vec3 {
                (p0.X * w) + (p1.X * u) + (p2.X * v),
                (p0.Y * w) + (p1.Y * u) + (p2.Y * v),
                (p0.Z * w) + (p1.Z * u) + (p2.Z * v)
            };
        };
        for (int row = 0; row < segments; ++row) {
            for (int col = 0; col < (segments - row); ++col) {
                float u0 = static_cast<float>(col) / segments;
                float v0 = static_cast<float>(row) / segments;
                float u1 = static_cast<float>(col + 1) / segments;
                float v1 = static_cast<float>(row) / segments;
                float u2 = static_cast<float>(col) / segments;
                float v2 = static_cast<float>(row + 1) / segments;
                Vec3 p0 = Lerp3(a, b, c, u0, v0);
                Vec3 p1 = Lerp3(a, b, c, u1, v1);
                Vec3 p2 = Lerp3(a, b, c, u2, v2);
                result.push_back(MakeTriangle(p0, p1, p2));
                if (col < (segments - row - 1)) {
                    float u3 = static_cast<float>(col + 1) / segments;
                    float v3 = static_cast<float>(row + 1) / segments;
                    Vec3 p3 = Lerp3(a, b, c, u3, v3);
                    result.push_back(MakeTriangle(p1, p3, p2));
                }
            }
        }
        return result;
    }

    Ps2VuSourceSliceBounds ComputeTriangleBounds(const Ps2VuTexturedPackedTriangleSource& triangle) {
        float minX = std::min({triangle.PositionA[0], triangle.PositionB[0], triangle.PositionC[0]});
        float maxX = std::max({triangle.PositionA[0], triangle.PositionB[0], triangle.PositionC[0]});
        float minY = std::min({triangle.PositionA[1], triangle.PositionB[1], triangle.PositionC[1]});
        float maxY = std::max({triangle.PositionA[1], triangle.PositionB[1], triangle.PositionC[1]});
        float minZ = std::min({triangle.PositionA[2], triangle.PositionB[2], triangle.PositionC[2]});
        float maxZ = std::max({triangle.PositionA[2], triangle.PositionB[2], triangle.PositionC[2]});
        Ps2VuSourceSliceBounds bounds;
        bounds.Center = ::float3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
        bounds.Extents = ::float3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f);
        return bounds;
    }

    const char* RouteName(Ps2VuNearPlaneRoute route) {
        switch (route) {
            case Ps2VuNearPlaneRoute::Fast: return "Fast";
            case Ps2VuNearPlaneRoute::Clipped: return "Clipped";
            case Ps2VuNearPlaneRoute::Rejected: return "Rejected";
        }
        return "Unknown";
    }

    // Runs one subdivided source triangle through the exact same classify -> clip -> VU backface-cull
    // pipeline the real PS2 renderer uses, and reports whether/why any resulting geometry is dropped.
    void DiagnoseTriangle(
        const char* label,
        std::size_t subIndex,
        const Ps2VuTexturedPackedTriangleSource& sourceTriangle,
        const ::float4x4& worldView,
        const ::float4x4& worldViewProjection,
        const ::float4x4& projection,
        float nearPlaneDistance,
        const ::float4& viewportScale,
        const ::float4& viewportOffset) {
        Ps2VuSourceSliceBounds bounds = ComputeTriangleBounds(sourceTriangle);
        Ps2VuNearPlaneRoute route = Ps2VuNearPlaneSliceClassifier::Classify(bounds, worldViewProjection);

        if (route == Ps2VuNearPlaneRoute::Rejected) {
            constexpr float GsScreenCoordinateBias = 2048.0f;
            const float* positions[3] = { sourceTriangle.PositionA, sourceTriangle.PositionB, sourceTriangle.PositionC };
            float screenX[3];
            float screenY[3];
            bool anyBehindCamera = false;
            for (int v = 0; v < 3; ++v) {
                float clipX = (positions[v][0] * worldViewProjection.M11) + (positions[v][1] * worldViewProjection.M21) + (positions[v][2] * worldViewProjection.M31) + worldViewProjection.M41;
                float clipY = (positions[v][0] * worldViewProjection.M12) + (positions[v][1] * worldViewProjection.M22) + (positions[v][2] * worldViewProjection.M32) + worldViewProjection.M42;
                float clipW = (positions[v][0] * worldViewProjection.M14) + (positions[v][1] * worldViewProjection.M24) + (positions[v][2] * worldViewProjection.M34) + worldViewProjection.M44;
                if (clipW <= 0.0001f) {
                    anyBehindCamera = true;
                    screenX[v] = -1.0f;
                    screenY[v] = -1.0f;
                    continue;
                }
                float ndcX = clipX / clipW;
                float ndcY = clipY / clipW;
                screenX[v] = (ndcX * viewportScale.X) + viewportOffset.X - GsScreenCoordinateBias;
                screenY[v] = (ndcY * viewportScale.Y) + viewportOffset.Y - GsScreenCoordinateBias;
            }
            bool wouldOverlapScreen = !anyBehindCamera
                && std::max({screenX[0], screenX[1], screenX[2]}) >= 0.0f
                && std::min({screenX[0], screenX[1], screenX[2]}) <= 640.0f
                && std::max({screenY[0], screenY[1], screenY[2]}) >= 0.0f
                && std::min({screenY[0], screenY[1], screenY[2]}) <= 448.0f;
            std::printf("%s sub=%zu route=Rejected(classify) centerLocal=(%.2f,%.2f,%.2f) behindCamera=%s wouldOverlapScreen=%s pixelX=(%.1f,%.1f,%.1f) pixelY=(%.1f,%.1f,%.1f)\n",
                label, subIndex, bounds.Center.X, bounds.Center.Y, bounds.Center.Z,
                anyBehindCamera ? "YES" : "no", wouldOverlapScreen ? "YES***" : "no",
                screenX[0], screenX[1], screenX[2], screenY[0], screenY[1], screenY[2]);
            return;
        }
        if (route == Ps2VuNearPlaneRoute::Fast) {
            constexpr float GsScreenCoordinateBias = 2048.0f;
            const float* positions[3] = { sourceTriangle.PositionA, sourceTriangle.PositionB, sourceTriangle.PositionC };
            float screenX[3];
            float screenY[3];
            float screenZ[3];
            for (int v = 0; v < 3; ++v) {
                float clipX = (positions[v][0] * worldViewProjection.M11) + (positions[v][1] * worldViewProjection.M21) + (positions[v][2] * worldViewProjection.M31) + worldViewProjection.M41;
                float clipY = (positions[v][0] * worldViewProjection.M12) + (positions[v][1] * worldViewProjection.M22) + (positions[v][2] * worldViewProjection.M32) + worldViewProjection.M42;
                float clipZ = (positions[v][0] * worldViewProjection.M13) + (positions[v][1] * worldViewProjection.M23) + (positions[v][2] * worldViewProjection.M33) + worldViewProjection.M43;
                float clipW = (positions[v][0] * worldViewProjection.M14) + (positions[v][1] * worldViewProjection.M24) + (positions[v][2] * worldViewProjection.M34) + worldViewProjection.M44;
                float ndcX = clipX / clipW;
                float ndcY = clipY / clipW;
                float ndcZ = clipZ / clipW;
                screenX[v] = (ndcX * viewportScale.X) + viewportOffset.X - GsScreenCoordinateBias;
                screenY[v] = (ndcY * viewportScale.Y) + viewportOffset.Y - GsScreenCoordinateBias;
                screenZ[v] = (ndcZ * viewportScale.Z) + viewportOffset.Z;
            }
            std::printf("%s sub=%zu route=Fast pixelX=(%.1f,%.1f,%.1f) pixelY=(%.1f,%.1f,%.1f) screenZ=(%.0f,%.0f,%.0f)\n",
                label, subIndex, screenX[0], screenX[1], screenX[2], screenY[0], screenY[1], screenY[2], screenZ[0], screenZ[1], screenZ[2]);
            return;
        }

        Ps2VuClippedTexturedTriangleFan fan;
        Ps2VuClippedTexturedBatchBuilder::BuildTriangleFan(sourceTriangle, worldView, projection, nearPlaneDistance, fan);
        if (fan.GetTriangleCount() == 0u) {
            constexpr float GsScreenCoordinateBias = 2048.0f;
            const float* positions[3] = { sourceTriangle.PositionA, sourceTriangle.PositionB, sourceTriangle.PositionC };
            float screenX[3];
            float screenY[3];
            bool anyBehindCamera = false;
            for (int v = 0; v < 3; ++v) {
                float clipX = (positions[v][0] * worldViewProjection.M11) + (positions[v][1] * worldViewProjection.M21) + (positions[v][2] * worldViewProjection.M31) + worldViewProjection.M41;
                float clipY = (positions[v][0] * worldViewProjection.M12) + (positions[v][1] * worldViewProjection.M22) + (positions[v][2] * worldViewProjection.M32) + worldViewProjection.M42;
                float clipW = (positions[v][0] * worldViewProjection.M14) + (positions[v][1] * worldViewProjection.M24) + (positions[v][2] * worldViewProjection.M34) + worldViewProjection.M44;
                if (clipW <= 0.0001f) {
                    anyBehindCamera = true;
                    screenX[v] = -1.0f;
                    screenY[v] = -1.0f;
                    continue;
                }
                float ndcX = clipX / clipW;
                float ndcY = clipY / clipW;
                screenX[v] = (ndcX * viewportScale.X) + viewportOffset.X - GsScreenCoordinateBias;
                screenY[v] = (ndcY * viewportScale.Y) + viewportOffset.Y - GsScreenCoordinateBias;
            }
            bool wouldOverlapScreen = !anyBehindCamera
                && std::max({screenX[0], screenX[1], screenX[2]}) >= 0.0f
                && std::min({screenX[0], screenX[1], screenX[2]}) <= 640.0f
                && std::max({screenY[0], screenY[1], screenY[2]}) >= 0.0f
                && std::min({screenY[0], screenY[1], screenY[2]}) <= 448.0f;
            std::printf("%s sub=%zu route=Clipped fan=0(exact-clip-empty) centerLocal=(%.2f,%.2f,%.2f) behindCamera=%s wouldOverlapScreen=%s pixelX=(%.1f,%.1f,%.1f) pixelY=(%.1f,%.1f,%.1f)\n",
                label, subIndex, bounds.Center.X, bounds.Center.Y, bounds.Center.Z,
                anyBehindCamera ? "YES" : "no", wouldOverlapScreen ? "YES***" : "no",
                screenX[0], screenX[1], screenX[2], screenY[0], screenY[1], screenY[2]);
            return;
        }

        for (std::size_t t = 0u; t < fan.GetTriangleCount(); ++t) {
            const auto& triangle = fan.GetTriangle(t);
            const float* positions[3] = { triangle.ClipPositionA, triangle.ClipPositionB, triangle.ClipPositionC };
            float ndcX[3];
            float ndcY[3];
            float screenX[3];
            float screenY[3];
            float screenZ[3];
            constexpr float GsScreenCoordinateBias = 2048.0f;
            for (int v = 0; v < 3; ++v) {
                float clipW = positions[v][3];
                ndcX[v] = positions[v][0] / clipW;
                ndcY[v] = positions[v][1] / clipW;
                screenX[v] = (ndcX[v] * viewportScale.X) + viewportOffset.X - GsScreenCoordinateBias;
                screenY[v] = (ndcY[v] * viewportScale.Y) + viewportOffset.Y - GsScreenCoordinateBias;
                screenZ[v] = (positions[v][2] / clipW * viewportScale.Z) + viewportOffset.Z;
            }

            // Exactly mirrors the VU program: VF18/19/20 = ndcXY of vertices 0/1/2 (viewport-scale
            // NOT yet applied), VF21=V1-V0, VF22=V2-V0, crossZ = VF21.x*VF22.y - VF21.y*VF22.x.
            float edge1X = ndcX[1] - ndcX[0];
            float edge1Y = ndcY[1] - ndcY[0];
            float edge2X = ndcX[2] - ndcX[0];
            float edge2Y = ndcY[2] - ndcY[0];
            float crossZ = (edge1X * edge2Y) - (edge1Y * edge2X);
            bool vuAccepts = crossZ >= 0.0f;

            std::printf(
                "%s sub=%zu route=Clipped fan=%zu crossZ=%.8f vuAccepts=%s pixelX=(%.1f,%.1f,%.1f) pixelY=(%.1f,%.1f,%.1f) screenZ=(%.0f,%.0f,%.0f)\n",
                label, subIndex, t, crossZ, vuAccepts ? "YES" : "NO_DROPPED",
                screenX[0], screenX[1], screenX[2], screenY[0], screenY[1], screenY[2],
                screenZ[0], screenZ[1], screenZ[2]);
        }
    }
}

int main() {
    // Exact view matrix read back at the live "F2 C20 G38 R2 N9 S40 E20" bug reading (untessellated probe).
    ::float4x4 view(
        1.000000f, 0.000000f, 0.000000f, 0.000000f,
        -0.000000f, 0.919145f, 0.393919f, 0.000000f,
        0.000000f, -0.393919f, 0.919145f, 0.000000f,
        -0.000000f, 0.000000f, -4.570795f, 1.000000f);
    ::float4x4 projection {};
    ::float4x4::CreatePerspectiveFieldOfView__out4(0.785398185f, 640.0f / 448.0f, 0.1f, 64.0f, projection);

    ::float4 viewportScale(320.0f, -224.0f, -4194304.0f, 0.0f);
    ::float4 viewportOffset(2048.0f + 320.0f, 2048.0f + 224.0f, 4194304.0f, 0.0f);

    ::float4x4 world = ::float4x4::get_Identity();
    ::float4x4 worldView {};
    ::float4x4::Multiply__ref0_ref1_out2(world, view, worldView);
    ::float4x4 worldViewProjection {};
    ::float4x4::Multiply__ref0_ref1_out2(worldView, projection, worldViewProjection);

    std::vector<Ps2VuTexturedPackedTriangleSource> wideBoxTriangles = UnitCubeTriangles();
    for (auto& triangle : wideBoxTriangles) {
        ScaleTriangle(triangle, 5.0f, 1.0f, 5.0f);
    }
    std::vector<Ps2VuTexturedPackedTriangleSource> tallBoxTriangles = UnitCubeTriangles();
    for (auto& triangle : tallBoxTriangles) {
        ScaleTriangle(triangle, 1.0f, 5.0f, 1.0f);
    }

    // No subdivision (segments=1): exactly the plain 12-triangle box now reproducing the bug.
    for (std::size_t i = 0u; i < wideBoxTriangles.size(); ++i) {
        DiagnoseTriangle("WIDE", i, wideBoxTriangles[i], worldView, worldViewProjection, projection, 0.1f, viewportScale, viewportOffset);
    }
    for (std::size_t i = 0u; i < tallBoxTriangles.size(); ++i) {
        DiagnoseTriangle("TALL", i, tallBoxTriangles[i], worldView, worldViewProjection, projection, 0.1f, viewportScale, viewportOffset);
    }

    return 0;
}
