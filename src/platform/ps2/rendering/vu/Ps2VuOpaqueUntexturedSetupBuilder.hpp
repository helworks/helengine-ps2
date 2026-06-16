#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

#include "float3.hpp"
#include "float4.hpp"
#include "float4x4.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"

typedef struct gsGlobal GSGLOBAL;

namespace helengine::ps2 {
    struct alignas(16) Ps2VuOpaqueSourceTriangle final {
        float PositionA[4];
        float PositionB[4];
        float PositionC[4];
    };

    struct alignas(16) Ps2VuOpaqueUntexturedTriangleSetup final {
        Ps2VuOpaqueSourceTriangle SourceTriangle;
        float FaceNormal[4];
        float LightDirection[4];
        float WorldViewProjectionMatrix[16];
        float GsScale[4];
        float GsOffset[4];
    };

    class Ps2VuOpaqueUntexturedSetupBuilder final {
    public:
        Ps2VuOpaqueUntexturedSetupBuilder();

        void Reset();
        void Build(
            const Ps2VuOpaqueBatch& batch,
            const ::float4x4& world,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            const ::float3& lightDirection,
            float nearPlaneDistance,
            GSGLOBAL* gsGlobal);

        const std::vector<Ps2VuOpaqueUntexturedTriangleSetup>& GetTriangleSetups() const;
        double GetLastTriangleSetupMilliseconds() const;
        double GetLastTrianglePrepMilliseconds() const;
        double GetLastTriangleEmitMilliseconds() const;
        std::size_t GetSubmittedTriangleCount() const;
        ::float4 GetSubmittedScreenBounds() const;
        ::float4 GetSubmittedTriangleBoundsA() const;
        ::float4 GetSubmittedTriangleBoundsB() const;
        ::float4 GetSubmittedTriangleVertexA0() const;
        ::float4 GetSubmittedTriangleVertexA1() const;
        ::float4 GetSubmittedTriangleVertexA2() const;
        ::float4 GetSubmittedTriangleVertexB0() const;
        ::float4 GetSubmittedTriangleVertexB1() const;
        ::float4 GetSubmittedTriangleVertexB2() const;

    private:
        std::vector<Ps2VuOpaqueUntexturedTriangleSetup> TriangleSetups;
        double LastTriangleSetupMilliseconds;
        double LastTrianglePrepMilliseconds;
        double LastTriangleEmitMilliseconds;
        std::size_t SubmittedTriangleCount;
        ::float4 SubmittedScreenBounds;
        ::float4 SubmittedTriangleBoundsA;
        ::float4 SubmittedTriangleBoundsB;
        ::float4 SubmittedTriangleVertexA0;
        ::float4 SubmittedTriangleVertexA1;
        ::float4 SubmittedTriangleVertexA2;
        ::float4 SubmittedTriangleVertexB0;
        ::float4 SubmittedTriangleVertexB1;
        ::float4 SubmittedTriangleVertexB2;
    };
}
