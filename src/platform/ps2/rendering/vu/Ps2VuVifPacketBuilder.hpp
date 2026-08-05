#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

#include <packet2.h>

#include "float3.hpp"
#include "float4.hpp"
#include "float4x4.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatchSlice.hpp"
#include "platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp"

typedef struct gsGlobal GSGLOBAL;
typedef struct gsTexture GSTEXTURE;

namespace helengine::ps2 {
    class Ps2VuVifPacketBuilder final {
    public:
        ~Ps2VuVifPacketBuilder();
        void Reset();
        void AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, GSTEXTURE* texture, int textureWidth, int textureHeight);
        std::size_t AddOpaqueUntexturedBatches(
            const std::vector<const Ps2VuOpaqueBatch*>& batches,
            const std::vector<::float4x4>& worlds,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            float nearPlaneDistance,
            const ::float3& lightDirection,
            GSGLOBAL* gsGlobal,
            bool createVifPacket);
        void AddOpaqueTexturedVuBatches(
            const std::vector<Ps2VuOpaqueBatchSlice>& batches,
            std::size_t firstBatchIndex,
            std::size_t batchCount,
            const std::vector<::float4x4>& worlds,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            float nearPlaneDistance,
            const ::float3& lightDirection,
            GSGLOBAL* gsGlobal,
            const std::vector<GSTEXTURE*>& textures,
            const std::vector<int>& textureWidths,
            const std::vector<int>& textureHeights);
        static std::size_t GetMaximumTexturedVuSourceBatchCount();
        void AddOpaqueTexturedBatches(
            const std::vector<Ps2VuOpaqueBatchSlice>& batches,
            const std::vector<::float4x4>& worlds,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            float nearPlaneDistance,
            const ::float3& lightDirection,
            GSGLOBAL* gsGlobal,
            const std::vector<GSTEXTURE*>& textures,
            const std::vector<int>& textureWidths,
            const std::vector<int>& textureHeights,
            bool createVifPacket);
        packet2_t* GetPacket() const;
        packet2_t* ReleasePacket();
        std::size_t GetPacketByteCount() const;
        const std::vector<std::uint8_t>& GetGifPacketBytes() const;
        std::uint32_t GetLastCompletedPhase() const;
        double GetLastTriangleSetupMilliseconds() const;
        double GetLastPacketAssemblyMilliseconds() const;
        double GetLastTrianglePrepMilliseconds() const;
        double GetLastTriangleEmitMilliseconds() const;
        double GetLastTriangleLightingMilliseconds() const;
        double GetLastTrianglePayloadFillMilliseconds() const;
        double GetLastTexturedVuStateBuildMilliseconds() const;
        double GetLastTexturedVuCommandEncodeMilliseconds() const;
        /// <summary>
        /// Returns the number of original textured source triangles submitted through the unchanged fast microprogram in the current packet.
        /// </summary>
        std::size_t GetFastTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the number of original textured source triangles processed by the clipped route in the current packet.
        /// </summary>
        std::size_t GetClippedTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the number of original textured source triangles omitted after route classification or clipping in the current packet.
        /// </summary>
        std::size_t GetRejectedTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the number of generated clipped fan triangles submitted through the pretransformed microprogram in the current packet.
        /// </summary>
        std::size_t GetGeneratedClippedTexturedTriangleCount() const;
        /// <summary>
        /// Returns the number of pretransformed clipped batches emitted in the current packet.
        /// </summary>
        std::size_t GetClippedTexturedBatchCount() const;
        /// <summary>
        /// Returns the triangle count of the most recently emitted pretransformed clipped batch in the current packet.
        /// </summary>
        std::size_t GetLastEmittedClippedBatchTriangleCount() const;
        /// <summary>
        /// Returns the VU1 output buffer start qword used by the most recently emitted textured dispatch in the current packet.
        /// </summary>
        std::uint32_t GetLastDispatchedTexturedOutputStartQword() const;
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
        /// <summary>
        /// Returns the next alternating VU1 GIF output buffer start qword and records it for diagnostics readback.
        /// </summary>
        std::uint32_t AcquireNextTexturedOutputStartQword();

        packet2_t* Packet = nullptr;
        std::vector<std::uint8_t> GifPacketBytes;
        std::vector<std::uint64_t> DirectGifPacketWords;
        Ps2VuTexturedPacketCache TexturedPacketCache;
        std::uint32_t LastCompletedPhase = 0;
        double LastTriangleSetupMilliseconds = 0.0;
        double LastPacketAssemblyMilliseconds = 0.0;
        double LastTrianglePrepMilliseconds = 0.0;
        double LastTriangleEmitMilliseconds = 0.0;
        double LastTriangleLightingMilliseconds = 0.0;
        double LastTrianglePayloadFillMilliseconds = 0.0;
        double LastTexturedVuStateBuildMilliseconds = 0.0;
        double LastTexturedVuCommandEncodeMilliseconds = 0.0;
        /// <summary>
        /// Counts original textured source triangles submitted through the unchanged fast route in the current packet.
        /// </summary>
        std::size_t FastTexturedSourceTriangleCount = 0u;
        /// <summary>
        /// Counts original textured source triangles passed to the clipped route in the current packet.
        /// </summary>
        std::size_t ClippedTexturedSourceTriangleCount = 0u;
        /// <summary>
        /// Counts original textured source triangles omitted after route classification or clipping in the current packet.
        /// </summary>
        std::size_t RejectedTexturedSourceTriangleCount = 0u;
        /// <summary>
        /// Counts fan triangles generated by the clipped route in the current packet.
        /// </summary>
        std::size_t GeneratedClippedTexturedTriangleCount = 0u;
        /// <summary>
        /// Counts pretransformed clipped batch submissions emitted in the current packet.
        /// </summary>
        std::size_t ClippedTexturedBatchCount = 0u;
        /// <summary>
        /// Triangle count of the most recently emitted pretransformed clipped batch in the current packet.
        /// </summary>
        std::size_t LastEmittedClippedBatchTriangleCount = 0u;
        /// <summary>
        /// Alternates textured dispatches between the two VU1 GIF output buffers so a new dispatch never overwrites the previous dispatch's in-flight XGKICK data.
        /// </summary>
        bool UseSecondTexturedOutputBuffer = false;
        /// <summary>
        /// Holds the VU1 output buffer start qword used by the most recently emitted textured dispatch in the current packet.
        /// </summary>
        std::uint32_t LastDispatchedTexturedOutputStartQwordValue = 0u;
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
