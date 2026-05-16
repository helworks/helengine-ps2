#include "platform/ps2/rendering/vu/Ps2VuOpaqueUntexturedSetupBuilder.hpp"

#include <algorithm>
#include <cmath>
#include <ctime>
#include <cstring>

#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"

namespace helengine::ps2 {
    namespace {
        constexpr bool EnableVuSingleTrianglePayloadDiagnostics = false;
        constexpr float LightingAmbientBias = 0.25f;
        constexpr float LightingDiffuseScale = 0.75f;
        constexpr float LightingPaletteScale = 15.0f;
        constexpr float MinimumClipW = 0.0001f;

        double ResolveMillisecondsFromClockTicks(std::clock_t startTicks, std::clock_t endTicks) {
            if (endTicks <= startTicks) {
                return 0.0;
            }

            return (static_cast<double>(endTicks - startTicks) / static_cast<double>(CLOCKS_PER_SEC)) * 1000.0;
        }

        ::float3 TransformPosition(const ::float4& position, const ::float4x4& matrix) {
            return ::float3(
                (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + (position.W * matrix.M41),
                (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + (position.W * matrix.M42),
                (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + (position.W * matrix.M43));
        }

        ::float3 NormalizeOrFallback(const ::float3& value, const ::float3& fallback) {
            const float lengthSquared = ::float3::Dot(value, value);
            if (lengthSquared <= 0.000001f) {
                return fallback;
            }

            return ::float3::Normalize(value);
        }

        void PopulateIdentityMatrix(float* matrix) {
            std::memset(matrix, 0, sizeof(float) * 16u);
            matrix[0] = 1.0f;
            matrix[5] = 1.0f;
            matrix[10] = 1.0f;
            matrix[15] = 1.0f;
        }

        void CopyMatrix(const ::float4x4& source, float* destination) {
            destination[0] = source.M11;
            destination[1] = source.M12;
            destination[2] = source.M13;
            destination[3] = source.M14;
            destination[4] = source.M21;
            destination[5] = source.M22;
            destination[6] = source.M23;
            destination[7] = source.M24;
            destination[8] = source.M31;
            destination[9] = source.M32;
            destination[10] = source.M33;
            destination[11] = source.M34;
            destination[12] = source.M41;
            destination[13] = source.M42;
            destination[14] = source.M43;
            destination[15] = source.M44;
        }

        void PopulateDiagnosticTriangleSetup(
            const ::float4& viewport,
            const ::float3& normalizedLightDirection,
            Ps2VuOpaqueUntexturedTriangleSetup& triangleSetup) {
            std::memset(&triangleSetup, 0, sizeof(Ps2VuOpaqueUntexturedTriangleSetup));
            PopulateIdentityMatrix(triangleSetup.WorldMatrix);
            PopulateIdentityMatrix(triangleSetup.ViewMatrix);
            PopulateIdentityMatrix(triangleSetup.ProjectionMatrix);
            PopulateIdentityMatrix(triangleSetup.WorldViewProjectionMatrix);
            triangleSetup.Viewport[0] = viewport.X;
            triangleSetup.Viewport[1] = viewport.Y;
            triangleSetup.Viewport[2] = viewport.Z;
            triangleSetup.Viewport[3] = viewport.W;
            triangleSetup.SourceTriangle.PositionA[0] = -0.5f;
            triangleSetup.SourceTriangle.PositionA[1] = -0.5f;
            triangleSetup.SourceTriangle.PositionA[2] = 0.5f;
            triangleSetup.SourceTriangle.PositionA[3] = 1.0f;
            triangleSetup.SourceTriangle.PositionB[0] = -0.5f;
            triangleSetup.SourceTriangle.PositionB[1] = 0.5f;
            triangleSetup.SourceTriangle.PositionB[2] = 0.5f;
            triangleSetup.SourceTriangle.PositionB[3] = 1.0f;
            triangleSetup.SourceTriangle.PositionC[0] = 0.5f;
            triangleSetup.SourceTriangle.PositionC[1] = -0.5f;
            triangleSetup.SourceTriangle.PositionC[2] = 0.5f;
            triangleSetup.SourceTriangle.PositionC[3] = 1.0f;
            triangleSetup.SourceTriangle.NormalA[2] = -1.0f;
            triangleSetup.SourceTriangle.NormalB[2] = -1.0f;
            triangleSetup.SourceTriangle.NormalC[2] = -1.0f;
            triangleSetup.FaceNormal[2] = -1.0f;
            triangleSetup.LightDirection[0] = normalizedLightDirection.X;
            triangleSetup.LightDirection[1] = normalizedLightDirection.Y;
            triangleSetup.LightDirection[2] = normalizedLightDirection.Z;
            triangleSetup.LightConstants[0] = LightingDiffuseScale;
            triangleSetup.LightConstants[1] = LightingAmbientBias;
            triangleSetup.LightConstants[2] = LightingPaletteScale;
            triangleSetup.GsScale[0] = viewport.Z * 0.5f;
            triangleSetup.GsScale[1] = viewport.W * -0.5f;
            triangleSetup.GsScale[2] = -4194304.0f;
            triangleSetup.GsOffset[0] = 2048.0f + viewport.X + (viewport.Z * 0.5f);
            triangleSetup.GsOffset[1] = 2048.0f + viewport.Y + (viewport.W * 0.5f);
            triangleSetup.GsOffset[2] = 4194304.0f;
        }
    }

    Ps2VuOpaqueUntexturedSetupBuilder::Ps2VuOpaqueUntexturedSetupBuilder()
        : TriangleSetups(),
          LastTriangleSetupMilliseconds(0.0),
          LastTrianglePrepMilliseconds(0.0),
          LastTriangleEmitMilliseconds(0.0),
          SubmittedTriangleCount(0u),
          SubmittedScreenBounds(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleBoundsA(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleBoundsB(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexA0(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexA1(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexA2(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexB0(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexB1(::float4(0.0f, 0.0f, 0.0f, 0.0f)),
          SubmittedTriangleVertexB2(::float4(0.0f, 0.0f, 0.0f, 0.0f)) {
    }

    void Ps2VuOpaqueUntexturedSetupBuilder::Reset() {
        TriangleSetups.clear();
        LastTriangleSetupMilliseconds = 0.0;
        LastTrianglePrepMilliseconds = 0.0;
        LastTriangleEmitMilliseconds = 0.0;
        SubmittedTriangleCount = 0u;
        SubmittedScreenBounds = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleBoundsA = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleBoundsB = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexA0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexA1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexA2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexB0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexB1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        SubmittedTriangleVertexB2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
    }

    void Ps2VuOpaqueUntexturedSetupBuilder::Build(
        const Ps2VuOpaqueBatch& batch,
        const ::float4x4& world,
        const ::float4x4& view,
        const ::float4x4& projection,
        const ::float4& viewport,
        const ::float3& lightDirection,
        float nearPlaneDistance,
        GSGLOBAL* gsGlobal) {
        Reset();
        if (batch.Model == nullptr || batch.Material == nullptr) {
            return;
        }

        const std::uint32_t triangleVertexCount = batch.Model->GetTriangleVertexCount();
        if (triangleVertexCount == 0u) {
            return;
        }

        (void)nearPlaneDistance;

        const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
        const std::clock_t triangleSetupStartTicks = std::clock();
        if (EnableVuSingleTrianglePayloadDiagnostics) {
            Ps2VuOpaqueUntexturedTriangleSetup triangleSetup {};
            PopulateDiagnosticTriangleSetup(viewport, normalizedLightDirection, triangleSetup);
            TriangleSetups.push_back(triangleSetup);
            SubmittedTriangleCount = 1u;
            SubmittedTriangleVertexA0 = ::float4(triangleSetup.SourceTriangle.PositionA[0], triangleSetup.SourceTriangle.PositionA[1], triangleSetup.SourceTriangle.PositionA[2], 1.0f);
            SubmittedTriangleVertexA1 = ::float4(triangleSetup.SourceTriangle.PositionB[0], triangleSetup.SourceTriangle.PositionB[1], triangleSetup.SourceTriangle.PositionB[2], 1.0f);
            SubmittedTriangleVertexA2 = ::float4(triangleSetup.SourceTriangle.PositionC[0], triangleSetup.SourceTriangle.PositionC[1], triangleSetup.SourceTriangle.PositionC[2], 1.0f);
        } else {
            const Ps2RuntimeModel* runtimeModel = batch.Proxy != nullptr ? batch.Proxy->GetModel() : nullptr;
            const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
            const std::vector<::float3>* runtimeNormals = runtimeModel != nullptr ? &runtimeModel->GetNormals() : nullptr;
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
                const std::clock_t trianglePrepStartTicks = std::clock();
                const std::size_t positionWordIndexA = static_cast<std::size_t>(vertexIndex + 0u) * 4u;
                const std::size_t positionWordIndexB = static_cast<std::size_t>(vertexIndex + 1u) * 4u;
                const std::size_t positionWordIndexC = static_cast<std::size_t>(vertexIndex + 2u) * 4u;
                const ::float3 packedNormalA(
                    packedNormalWords[positionWordIndexA + 0u],
                    packedNormalWords[positionWordIndexA + 1u],
                    packedNormalWords[positionWordIndexA + 2u]);
                const ::float3 packedNormalB(
                    packedNormalWords[positionWordIndexB + 0u],
                    packedNormalWords[positionWordIndexB + 1u],
                    packedNormalWords[positionWordIndexB + 2u]);
                const ::float3 packedNormalC(
                    packedNormalWords[positionWordIndexC + 0u],
                    packedNormalWords[positionWordIndexC + 1u],
                    packedNormalWords[positionWordIndexC + 2u]);
                const ::float3 faceNormal = NormalizeOrFallback(
                    ::float3(
                        packedNormalA.X + packedNormalB.X + packedNormalC.X,
                        packedNormalA.Y + packedNormalB.Y + packedNormalC.Y,
                        packedNormalA.Z + packedNormalB.Z + packedNormalC.Z),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 packedPositionA(
                    packedPositionWords[positionWordIndexA + 0u],
                    packedPositionWords[positionWordIndexA + 1u],
                    packedPositionWords[positionWordIndexA + 2u]);
                const ::float3 packedPositionB(
                    packedPositionWords[positionWordIndexB + 0u],
                    packedPositionWords[positionWordIndexB + 1u],
                    packedPositionWords[positionWordIndexB + 2u]);
                const ::float3 packedPositionC(
                    packedPositionWords[positionWordIndexC + 0u],
                    packedPositionWords[positionWordIndexC + 1u],
                    packedPositionWords[positionWordIndexC + 2u]);
                const std::uint16_t sourceIndexA = runtimeIndices != nullptr && vertexIndex < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 0u]
                    : static_cast<std::uint16_t>(vertexIndex + 0u);
                const std::uint16_t sourceIndexB = runtimeIndices != nullptr && (vertexIndex + 1u) < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 1u]
                    : static_cast<std::uint16_t>(vertexIndex + 1u);
                const std::uint16_t sourceIndexC = runtimeIndices != nullptr && (vertexIndex + 2u) < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 2u]
                    : static_cast<std::uint16_t>(vertexIndex + 2u);
                const ::float3 sourceNormalA = runtimeNormals != nullptr && sourceIndexA < runtimeNormals->size()
                    ? (*runtimeNormals)[sourceIndexA]
                    : packedNormalA;
                const ::float3 sourceNormalB = runtimeNormals != nullptr && sourceIndexB < runtimeNormals->size()
                    ? (*runtimeNormals)[sourceIndexB]
                    : packedNormalB;
                const ::float3 sourceNormalC = runtimeNormals != nullptr && sourceIndexC < runtimeNormals->size()
                    ? (*runtimeNormals)[sourceIndexC]
                    : packedNormalC;
                const ::float4 positionA(packedPositionA.X, packedPositionA.Y, packedPositionA.Z, 1.0f);
                const ::float4 positionB(packedPositionB.X, packedPositionB.Y, packedPositionB.Z, 1.0f);
                const ::float4 positionC(packedPositionC.X, packedPositionC.Y, packedPositionC.Z, 1.0f);
                const ::float3 worldPositionA = TransformPosition(positionA, world);
                const ::float3 worldPositionB = TransformPosition(positionB, world);
                const ::float3 worldPositionC = TransformPosition(positionC, world);
                const ::float4 worldPositionA4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);
                const ::float4 worldPositionB4(worldPositionB.X, worldPositionB.Y, worldPositionB.Z, 1.0f);
                const ::float4 worldPositionC4(worldPositionC.X, worldPositionC.Y, worldPositionC.Z, 1.0f);
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(::float4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f), world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const std::clock_t trianglePrepEndTicks = std::clock();
                LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);

                const std::clock_t triangleEmitStartTicks = std::clock();
                ::float4x4 worldCopy = world;
                ::float4x4 viewCopy = view;
                ::float4x4 projectionCopy = projection;
                ::float4x4 worldViewMatrix;
                ::float4x4 worldViewProjectionMatrix;
                ::float4x4::Multiply(worldCopy, viewCopy, worldViewMatrix);
                ::float4x4::Multiply(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);
                Ps2VuOpaqueUntexturedTriangleSetup triangleSetup {};
                CopyMatrix(world, triangleSetup.WorldMatrix);
                CopyMatrix(view, triangleSetup.ViewMatrix);
                CopyMatrix(projection, triangleSetup.ProjectionMatrix);
                triangleSetup.Viewport[0] = viewport.X;
                triangleSetup.Viewport[1] = viewport.Y;
                triangleSetup.Viewport[2] = viewport.Z;
                triangleSetup.Viewport[3] = viewport.W;
                triangleSetup.SourceTriangle.PositionA[0] = packedPositionA.X;
                triangleSetup.SourceTriangle.PositionA[1] = packedPositionA.Y;
                triangleSetup.SourceTriangle.PositionA[2] = packedPositionA.Z;
                triangleSetup.SourceTriangle.PositionA[3] = 1.0f;
                triangleSetup.SourceTriangle.PositionB[0] = packedPositionB.X;
                triangleSetup.SourceTriangle.PositionB[1] = packedPositionB.Y;
                triangleSetup.SourceTriangle.PositionB[2] = packedPositionB.Z;
                triangleSetup.SourceTriangle.PositionB[3] = 1.0f;
                triangleSetup.SourceTriangle.PositionC[0] = packedPositionC.X;
                triangleSetup.SourceTriangle.PositionC[1] = packedPositionC.Y;
                triangleSetup.SourceTriangle.PositionC[2] = packedPositionC.Z;
                triangleSetup.SourceTriangle.PositionC[3] = 1.0f;
                triangleSetup.SourceTriangle.NormalA[0] = sourceNormalA.X;
                triangleSetup.SourceTriangle.NormalA[1] = sourceNormalA.Y;
                triangleSetup.SourceTriangle.NormalA[2] = sourceNormalA.Z;
                triangleSetup.SourceTriangle.NormalA[3] = 0.0f;
                triangleSetup.SourceTriangle.NormalB[0] = sourceNormalB.X;
                triangleSetup.SourceTriangle.NormalB[1] = sourceNormalB.Y;
                triangleSetup.SourceTriangle.NormalB[2] = sourceNormalB.Z;
                triangleSetup.SourceTriangle.NormalB[3] = 0.0f;
                triangleSetup.SourceTriangle.NormalC[0] = sourceNormalC.X;
                triangleSetup.SourceTriangle.NormalC[1] = sourceNormalC.Y;
                triangleSetup.SourceTriangle.NormalC[2] = sourceNormalC.Z;
                triangleSetup.SourceTriangle.NormalC[3] = 0.0f;
                triangleSetup.FaceNormal[0] = worldFaceNormal.X;
                triangleSetup.FaceNormal[1] = worldFaceNormal.Y;
                triangleSetup.FaceNormal[2] = worldFaceNormal.Z;
                triangleSetup.FaceNormal[3] = 0.0f;
                triangleSetup.LightDirection[0] = normalizedLightDirection.X;
                triangleSetup.LightDirection[1] = normalizedLightDirection.Y;
                triangleSetup.LightDirection[2] = normalizedLightDirection.Z;
                triangleSetup.LightDirection[3] = 0.0f;
                triangleSetup.LightConstants[0] = LightingDiffuseScale;
                triangleSetup.LightConstants[1] = LightingAmbientBias;
                triangleSetup.LightConstants[2] = LightingPaletteScale;
                triangleSetup.LightConstants[3] = 0.0f;
                CopyMatrix(worldViewProjectionMatrix, triangleSetup.WorldViewProjectionMatrix);
                triangleSetup.GsScale[0] = viewport.Z * 0.5f;
                triangleSetup.GsScale[1] = viewport.W * -0.5f;
                triangleSetup.GsScale[2] = -4194304.0f;
                triangleSetup.GsScale[3] = 0.0f;
                triangleSetup.GsOffset[0] = 2048.0f + viewport.X + (viewport.Z * 0.5f);
                triangleSetup.GsOffset[1] = 2048.0f + viewport.Y + (viewport.W * 0.5f);
                triangleSetup.GsOffset[2] = 4194304.0f;
                triangleSetup.GsOffset[3] = 0.0f;
                TriangleSetups.push_back(triangleSetup);
                SubmittedTriangleCount++;
                if (SubmittedTriangleCount == 1u) {
                    SubmittedTriangleVertexA0 = ::float4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);
                    SubmittedTriangleVertexA1 = ::float4(worldPositionB.X, worldPositionB.Y, worldPositionB.Z, 1.0f);
                    SubmittedTriangleVertexA2 = ::float4(worldPositionC.X, worldPositionC.Y, worldPositionC.Z, 1.0f);
                } else if (SubmittedTriangleCount == 2u) {
                    SubmittedTriangleVertexB0 = ::float4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);
                    SubmittedTriangleVertexB1 = ::float4(worldPositionB.X, worldPositionB.Y, worldPositionB.Z, 1.0f);
                    SubmittedTriangleVertexB2 = ::float4(worldPositionC.X, worldPositionC.Y, worldPositionC.Z, 1.0f);
                }
                const std::clock_t triangleEmitEndTicks = std::clock();
                LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
            }
        }

        const std::clock_t triangleSetupEndTicks = std::clock();
        LastTriangleSetupMilliseconds = ResolveMillisecondsFromClockTicks(triangleSetupStartTicks, triangleSetupEndTicks);
    }

    const std::vector<Ps2VuOpaqueUntexturedTriangleSetup>& Ps2VuOpaqueUntexturedSetupBuilder::GetTriangleSetups() const {
        return TriangleSetups;
    }

    double Ps2VuOpaqueUntexturedSetupBuilder::GetLastTriangleSetupMilliseconds() const {
        return LastTriangleSetupMilliseconds;
    }

    double Ps2VuOpaqueUntexturedSetupBuilder::GetLastTrianglePrepMilliseconds() const {
        return LastTrianglePrepMilliseconds;
    }

    double Ps2VuOpaqueUntexturedSetupBuilder::GetLastTriangleEmitMilliseconds() const {
        return LastTriangleEmitMilliseconds;
    }

    std::size_t Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleCount() const {
        return SubmittedTriangleCount;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedScreenBounds() const {
        return SubmittedScreenBounds;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleBoundsA() const {
        return SubmittedTriangleBoundsA;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleBoundsB() const {
        return SubmittedTriangleBoundsB;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexA0() const {
        return SubmittedTriangleVertexA0;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexA1() const {
        return SubmittedTriangleVertexA1;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexA2() const {
        return SubmittedTriangleVertexA2;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexB0() const {
        return SubmittedTriangleVertexB0;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexB1() const {
        return SubmittedTriangleVertexB1;
    }

    ::float4 Ps2VuOpaqueUntexturedSetupBuilder::GetSubmittedTriangleVertexB2() const {
        return SubmittedTriangleVertexB2;
    }
}
