#include "platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp"

#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>

#include <draw3d.h>
#include <draw.h>
#include <draw_buffers.h>
#include <draw_primitives.h>
#include <draw_sampling.h>
#include <gsKit.h>
#include <packet2.h>
#include <packet2_utils.h>

#include "float3.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

namespace helengine::ps2 {
    namespace {
        constexpr std::uint32_t XtopGifPacketAddress = 0;
        constexpr std::uint32_t MinimumVifPacketOverheadQwords = 32;
        constexpr std::uint32_t EnableVuPacketPhaseDiagnostics = 1;
        constexpr std::uint32_t VuPacketDiagnosticCutoffPhase = 11;
        constexpr bool EnableVuFixedTriangleDiagnostics = false;
        constexpr float MinimumClipW = 0.0001f;
        constexpr float MinimumNearPlaneEpsilon = 0.00001f;
        constexpr float FixedTriangleAX = 211.843231f;
        constexpr float FixedTriangleAY = 332.156738f;
        constexpr float FixedTriangleBX = 211.843231f;
        constexpr float FixedTriangleBY = 115.843239f;
        constexpr float FixedTriangleCX = 428.156738f;
        constexpr float FixedTriangleCY = 332.156738f;
        constexpr float FixedTriangleZ = 0.990000f;

        std::uint32_t BuildVifCode(std::uint16_t immediate, std::uint8_t number, std::uint8_t command, bool irq) {
            return static_cast<std::uint32_t>(immediate)
                | (static_cast<std::uint32_t>(number) << 16u)
                | (static_cast<std::uint32_t>(command) << 24u)
                | (irq ? 0x80000000u : 0u);
        }

        packet2_t* CreatePacketOrThrow(std::uint16_t qwords, Packet2Mode mode) {
            packet2_t* packet = packet2_create(qwords, P2_TYPE_NORMAL, mode, 1);
            if (packet == nullptr) {
                throw std::runtime_error("Failed to allocate PS2 VU packet.");
            }

            return packet;
        }

        std::uint32_t ResolveMaximumDepth([[maybe_unused]] GSGLOBAL* gsGlobal) {
            return 1u << 23;
        }

        ::float3 TransformPosition(const ::float4& position, const ::float4x4& matrix) {
            return ::float3(
                (position.X * matrix.M11) + (position.Y * matrix.M21) + (position.Z * matrix.M31) + (position.W * matrix.M41),
                (position.X * matrix.M12) + (position.Y * matrix.M22) + (position.Z * matrix.M32) + (position.W * matrix.M42),
                (position.X * matrix.M13) + (position.Y * matrix.M23) + (position.Z * matrix.M33) + (position.W * matrix.M43));
        }

        ::float3 InterpolateViewPosition(const ::float3& start, const ::float3& end, float amount) {
            return ::float3(
                start.X + ((end.X - start.X) * amount),
                start.Y + ((end.Y - start.Y) * amount),
                start.Z + ((end.Z - start.Z) * amount));
        }

        void ClipTriangleAgainstNearPlane(const ::float3& first, const ::float3& second, const ::float3& third, float nearPlaneDistance, std::vector<::float3>& clippedVertices) {
            clippedVertices.clear();

            const float nearPlaneZ = -nearPlaneDistance;
            ::float3 previous = third;
            bool previousInside = previous.Z <= nearPlaneZ;
            const ::float3 vertices[3] = { first, second, third };

            for (const ::float3& current : vertices) {
                const bool currentInside = current.Z <= nearPlaneZ;
                if (currentInside != previousInside) {
                    const float denominator = current.Z - previous.Z;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = (nearPlaneZ - previous.Z) / denominator;
                        clippedVertices.push_back(InterpolateViewPosition(previous, current, amount));
                    }
                }

                if (currentInside) {
                    clippedVertices.push_back(current);
                }

                previous = current;
                previousInside = currentInside;
            }
        }

        bool IsFrontFacingTriangle(float screenAX, float screenAY, float screenBX, float screenBY, float screenCX, float screenCY) {
            const float edgeABX = screenBX - screenAX;
            const float edgeABY = screenBY - screenAY;
            const float edgeACX = screenCX - screenAX;
            const float edgeACY = screenCY - screenAY;
            const float signedArea = (edgeABX * edgeACY) - (edgeABY * edgeACX);
            return signedArea < 0.0f;
        }

        bool TryBuildVertexPositionRegister(
            const ::float3& viewPosition,
            const ::float4x4& projection,
            const ::float4& viewport,
            GSGLOBAL* gsGlobal,
            float& screenX,
            float& screenY,
            float& screenZ,
            std::uint64_t& positionRegister) {
            const float clipX = (viewPosition.X * projection.M11)
                + (viewPosition.Y * projection.M21)
                + (viewPosition.Z * projection.M31)
                + projection.M41;
            const float clipY = (viewPosition.X * projection.M12)
                + (viewPosition.Y * projection.M22)
                + (viewPosition.Z * projection.M32)
                + projection.M42;
            const float clipZ = (viewPosition.X * projection.M13)
                + (viewPosition.Y * projection.M23)
                + (viewPosition.Z * projection.M33)
                + projection.M43;
            const float clipW = (viewPosition.X * projection.M14)
                + (viewPosition.Y * projection.M24)
                + (viewPosition.Z * projection.M34)
                + projection.M44;
            if (clipW <= MinimumClipW) {
                return false;
            }

            const float inverseClipW = 1.0f / clipW;
            const float normalizedX = clipX * inverseClipW;
            const float normalizedY = clipY * inverseClipW;
            const float normalizedZ = clipZ * inverseClipW;
            screenX = viewport.X + ((normalizedX + 1.0f) * 0.5f * viewport.Z);
            screenY = viewport.Y + ((1.0f - normalizedY) * 0.5f * viewport.W);
            const std::int32_t gsX = static_cast<std::int32_t>((2048.0f + screenX) * 16.0f);
            const std::int32_t gsY = static_cast<std::int32_t>((2048.0f + screenY) * 16.0f);
            screenZ = (normalizedZ + 1.0f) * 0.5f;
            const std::uint32_t gsZ = static_cast<std::uint32_t>(screenZ * static_cast<float>(ResolveMaximumDepth(gsGlobal)));
            positionRegister = GS_SETREG_XYZ2(gsX, gsY, gsZ);
            return true;
        }

        std::uint64_t BuildFixedTrianglePositionRegister(float screenX, float screenY, float screenZ, GSGLOBAL* gsGlobal) {
            const std::int32_t gsX = static_cast<std::int32_t>((2048.0f + screenX) * 16.0f);
            const std::int32_t gsY = static_cast<std::int32_t>((2048.0f + screenY) * 16.0f);
            const std::uint32_t gsZ = static_cast<std::uint32_t>(screenZ * static_cast<float>(ResolveMaximumDepth(gsGlobal)));
            return GS_SETREG_XYZ2(gsX, gsY, gsZ);
        }
    }

    Ps2VuVifPacketBuilder::~Ps2VuVifPacketBuilder() {
        Reset();
    }

    void Ps2VuVifPacketBuilder::Reset() {
        if (Packet != nullptr) {
            packet2_free(Packet);
            Packet = nullptr;
        }

        GifPacketBytes.clear();
        LastCompletedPhase = 0;
        SubmittedTriangleCount = 0;
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

    void Ps2VuVifPacketBuilder::AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, GSGLOBAL* gsGlobal) {
        if (batch.Model == nullptr || batch.Material == nullptr) {
            return;
        }

        std::uint32_t triangleVertexCount = batch.Model->GetTriangleVertexCount();
        if (triangleVertexCount == 0) {
            return;
        }

        LastCompletedPhase = 1;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        std::vector<std::uint64_t> projectedVertices;
        projectedVertices.reserve(static_cast<std::size_t>(triangleVertexCount) * 2u);
        if (EnableVuFixedTriangleDiagnostics) {
            projectedVertices.push_back(BuildFixedTrianglePositionRegister(FixedTriangleAX, FixedTriangleAY, FixedTriangleZ, gsGlobal));
            projectedVertices.push_back(BuildFixedTrianglePositionRegister(FixedTriangleBX, FixedTriangleBY, FixedTriangleZ, gsGlobal));
            projectedVertices.push_back(BuildFixedTrianglePositionRegister(FixedTriangleCX, FixedTriangleCY, FixedTriangleZ, gsGlobal));
            SubmittedTriangleCount = 1u;
            SubmittedScreenBounds = ::float4(FixedTriangleAX, FixedTriangleBY, FixedTriangleCX, FixedTriangleAY);
            SubmittedTriangleBoundsA = SubmittedScreenBounds;
            SubmittedTriangleVertexA0 = ::float4(FixedTriangleAX, FixedTriangleAY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA1 = ::float4(FixedTriangleBX, FixedTriangleBY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA2 = ::float4(FixedTriangleCX, FixedTriangleCY, FixedTriangleZ, 0.0f);
        } else {
            if (batch.Proxy == nullptr || batch.Proxy->GetModel() == nullptr) {
                return;
            }

            const Ps2RuntimeModel* runtimeModel = batch.Proxy->GetModel();
            const std::vector<::float3>& positions = runtimeModel->GetPositions();
            const std::vector<std::uint16_t>& indices = runtimeModel->GetIndices();
            if (positions.empty()) {
                return;
            }

            const bool useIndices = !indices.empty();
            const std::uint32_t sourceVertexCount = useIndices
                ? static_cast<std::uint32_t>(indices.size())
                : static_cast<std::uint32_t>(positions.size());
            std::vector<::float3> clippedVertices;
            clippedVertices.reserve(4u);
            for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < sourceVertexCount; vertexIndex += 3u) {
                const std::uint32_t indexA = useIndices ? static_cast<std::uint32_t>(indices[vertexIndex + 0u]) : vertexIndex + 0u;
                const std::uint32_t indexB = useIndices ? static_cast<std::uint32_t>(indices[vertexIndex + 1u]) : vertexIndex + 1u;
                const std::uint32_t indexC = useIndices ? static_cast<std::uint32_t>(indices[vertexIndex + 2u]) : vertexIndex + 2u;
                if (indexA >= positions.size() || indexB >= positions.size() || indexC >= positions.size()) {
                    continue;
                }

                const ::float4 positionA(positions[indexA].X, positions[indexA].Y, positions[indexA].Z, 1.0f);
                const ::float4 positionB(positions[indexB].X, positions[indexB].Y, positions[indexB].Z, 1.0f);
                const ::float4 positionC(positions[indexC].X, positions[indexC].Y, positions[indexC].Z, 1.0f);
                const ::float3 worldPositionA = TransformPosition(positionA, world);
                const ::float3 worldPositionB = TransformPosition(positionB, world);
                const ::float3 worldPositionC = TransformPosition(positionC, world);
                const ::float4 worldPositionA4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);
                const ::float4 worldPositionB4(worldPositionB.X, worldPositionB.Y, worldPositionB.Z, 1.0f);
                const ::float4 worldPositionC4(worldPositionC.X, worldPositionC.Y, worldPositionC.Z, 1.0f);
                const ::float3 viewPositionA = TransformPosition(worldPositionA4, view);
                const ::float3 viewPositionB = TransformPosition(worldPositionB4, view);
                const ::float3 viewPositionC = TransformPosition(worldPositionC4, view);
                ClipTriangleAgainstNearPlane(viewPositionA, viewPositionB, viewPositionC, nearPlaneDistance, clippedVertices);
                if (clippedVertices.size() < 3u) {
                    continue;
                }

                for (std::size_t clippedIndex = 1u; (clippedIndex + 1u) < clippedVertices.size(); clippedIndex++) {
                    float screenAX = 0.0f;
                    float screenAY = 0.0f;
                    float screenAZ = 0.0f;
                    float screenBX = 0.0f;
                    float screenBY = 0.0f;
                    float screenBZ = 0.0f;
                    float screenCX = 0.0f;
                    float screenCY = 0.0f;
                    float screenCZ = 0.0f;
                    std::uint64_t positionARegister = 0;
                    std::uint64_t positionBRegister = 0;
                    std::uint64_t positionCRegister = 0;
                    if (!TryBuildVertexPositionRegister(clippedVertices[0], projection, viewport, gsGlobal, screenAX, screenAY, screenAZ, positionARegister)
                        || !TryBuildVertexPositionRegister(clippedVertices[clippedIndex], projection, viewport, gsGlobal, screenBX, screenBY, screenBZ, positionBRegister)
                        || !TryBuildVertexPositionRegister(clippedVertices[clippedIndex + 1u], projection, viewport, gsGlobal, screenCX, screenCY, screenCZ, positionCRegister)) {
                        continue;
                    }

                    if (!batch.Material->GetDoubleSided()
                        && !IsFrontFacingTriangle(screenAX, screenAY, screenBX, screenBY, screenCX, screenCY)) {
                        continue;
                    }

                    projectedVertices.push_back(positionARegister);
                    projectedVertices.push_back(positionBRegister);
                    projectedVertices.push_back(positionCRegister);
                    const float minX = std::min({ screenAX, screenBX, screenCX });
                    const float minY = std::min({ screenAY, screenBY, screenCY });
                    const float maxX = std::max({ screenAX, screenBX, screenCX });
                    const float maxY = std::max({ screenAY, screenBY, screenCY });
                    if (SubmittedTriangleCount == 0u) {
                        SubmittedScreenBounds = ::float4(minX, minY, maxX, maxY);
                        SubmittedTriangleBoundsA = ::float4(minX, minY, maxX, maxY);
                        SubmittedTriangleVertexA0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);
                        SubmittedTriangleVertexA1 = ::float4(screenBX, screenBY, screenBZ, 0.0f);
                        SubmittedTriangleVertexA2 = ::float4(screenCX, screenCY, screenCZ, 0.0f);
                    } else {
                        SubmittedScreenBounds.X = std::min(SubmittedScreenBounds.X, minX);
                        SubmittedScreenBounds.Y = std::min(SubmittedScreenBounds.Y, minY);
                        SubmittedScreenBounds.Z = std::max(SubmittedScreenBounds.Z, maxX);
                        SubmittedScreenBounds.W = std::max(SubmittedScreenBounds.W, maxY);
                        if (SubmittedTriangleCount == 1u) {
                            SubmittedTriangleBoundsB = ::float4(minX, minY, maxX, maxY);
                            SubmittedTriangleVertexB0 = ::float4(screenAX, screenAY, screenAZ, 0.0f);
                            SubmittedTriangleVertexB1 = ::float4(screenBX, screenBY, screenBZ, 0.0f);
                            SubmittedTriangleVertexB2 = ::float4(screenCX, screenCY, screenCZ, 0.0f);
                        }
                    }
                    SubmittedTriangleCount++;
                }
            }
        }

        if (projectedVertices.empty()) {
            return;
        }

        std::uint32_t emittedVertexCount = static_cast<std::uint32_t>(projectedVertices.size());
        std::uint32_t maxPacketQwordCount = std::max<std::uint32_t>(1024u, MinimumVifPacketOverheadQwords + 32u + emittedVertexCount);
        if (maxPacketQwordCount > 0xFFFFu) {
            throw std::runtime_error("PS2 VU packet exceeds packet2 qword capacity.");
        }
        LastCompletedPhase = 2;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        std::unique_ptr<packet2_t, decltype(&packet2_free)> packet(CreatePacketOrThrow(static_cast<std::uint16_t>(maxPacketQwordCount), P2_MODE_CHAIN), &packet2_free);
        std::uint32_t gifPacketQwordCapacity = std::max<std::uint32_t>(32u, emittedVertexCount + 16u);
        std::unique_ptr<packet2_t, decltype(&packet2_free)> gifPacket(CreatePacketOrThrow(static_cast<std::uint16_t>(gifPacketQwordCapacity), P2_MODE_NORMAL), &packet2_free);
        prim_t prim = {};
        prim.type = PRIM_TRIANGLE;
        prim.shading = PRIM_SHADE_FLAT;
        prim.mapping = 0;
        prim.fogging = 0;
        prim.blending = 0;
        prim.antialiasing = 0;
        prim.mapping_type = PRIM_MAP_ST;
        prim.colorfix = PRIM_UNFIXED;
        LastCompletedPhase = 3;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        color_t flatColor = {};
        flatColor.r = batch.Material->GetBaseColorR();
        flatColor.g = batch.Material->GetBaseColorG();
        flatColor.b = batch.Material->GetBaseColorB();
        flatColor.a = batch.Material->GetBaseColorA();
        packet2_update(gifPacket.get(), draw_prim_start(gifPacket.get()->base, 0, &prim, &flatColor));
        LastCompletedPhase = 4;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        for (std::uint64_t positionRegister : projectedVertices) {
            packet2_add_u64(gifPacket.get(), positionRegister);
        }
        packet2_pad128(gifPacket.get(), 0);
        packet2_update(gifPacket.get(), draw_prim_end(gifPacket.get()->next, 1, static_cast<u64>(GIF_REG_XYZ2) << 0));
        GifPacketBytes.resize(static_cast<std::size_t>(packet2_get_qw_count(gifPacket.get())) * 16u);
        std::memcpy(GifPacketBytes.data(), gifPacket.get()->base, GifPacketBytes.size());
        LastCompletedPhase = 5;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
        std::memcpy(packet.get()->next, GifPacketBytes.data(), GifPacketBytes.size());
        packet2_advance_next(packet.get(), GifPacketBytes.size());
        packet2_utils_vu_close_unpack(packet.get());
        LastCompletedPhase = 6;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }
        packet2_chain_open_cnt(packet.get(), 0, 0, 0);
        packet2_vif_flush(packet.get(), 0);
        packet2_vif_mscal(packet.get(), 0, 0);
        packet2_chain_close_tag(packet.get());
        LastCompletedPhase = 8;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        packet2_chain_open_end(packet.get(), 0, 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_chain_close_tag(packet.get());
        LastCompletedPhase = 9;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }
        LastCompletedPhase = 10;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        Packet = packet.release();
        std::uint32_t packetQwordCount = packet2_get_qw_count(Packet);
        (void)packetQwordCount;
        LastCompletedPhase = 11;
    }

    packet2_t* Ps2VuVifPacketBuilder::GetPacket() const {
        return Packet;
    }

    std::size_t Ps2VuVifPacketBuilder::GetPacketByteCount() const {
        if (Packet == nullptr) {
            return 0;
        }

        return static_cast<std::size_t>(packet2_get_qw_count(Packet)) * 16u;
    }

    const std::vector<std::uint8_t>& Ps2VuVifPacketBuilder::GetGifPacketBytes() const {
        return GifPacketBytes;
    }

    std::uint32_t Ps2VuVifPacketBuilder::GetLastCompletedPhase() const {
        return LastCompletedPhase;
    }

    std::size_t Ps2VuVifPacketBuilder::GetSubmittedTriangleCount() const {
        return SubmittedTriangleCount;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedScreenBounds() const {
        return SubmittedScreenBounds;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleBoundsA() const {
        return SubmittedTriangleBoundsA;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleBoundsB() const {
        return SubmittedTriangleBoundsB;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexA0() const {
        return SubmittedTriangleVertexA0;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexA1() const {
        return SubmittedTriangleVertexA1;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexA2() const {
        return SubmittedTriangleVertexA2;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexB0() const {
        return SubmittedTriangleVertexB0;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexB1() const {
        return SubmittedTriangleVertexB1;
    }

    ::float4 Ps2VuVifPacketBuilder::GetSubmittedTriangleVertexB2() const {
        return SubmittedTriangleVertexB2;
    }
}
