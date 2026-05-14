#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

#include <packet2.h>

#include "float3.hpp"
#include "float4.hpp"
#include "float4x4.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"

typedef struct gsGlobal GSGLOBAL;

namespace helengine::ps2 {
    class Ps2VuVifPacketBuilder final {
    public:
        ~Ps2VuVifPacketBuilder();
        void Reset();
        void AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, int textureWidth, int textureHeight);
        packet2_t* GetPacket() const;
        std::size_t GetPacketByteCount() const;
        const std::vector<std::uint8_t>& GetGifPacketBytes() const;
        std::uint32_t GetLastCompletedPhase() const;
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
        packet2_t* Packet = nullptr;
        std::vector<std::uint8_t> GifPacketBytes;
        std::uint32_t LastCompletedPhase = 0;
        std::size_t SubmittedTriangleCount = 0;
        ::float4 SubmittedScreenBounds = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleBoundsA = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleBoundsB = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexA0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexA1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexA2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexB0 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexB1 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
        ::float4 SubmittedTriangleVertexB2 = ::float4(0.0f, 0.0f, 0.0f, 0.0f);
    };
}
