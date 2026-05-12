#include "platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp"

#include <algorithm>
#include <array>
#include <cstdio>
#include <cmath>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
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
#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"
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
        constexpr bool EnableVuGifTemplateLayoutDiagnostics = false;
        constexpr std::size_t LightingPaletteEntryCount = 16u;
        constexpr float LightingAmbientBias = 0.25f;
        constexpr float LightingDiffuseScale = 0.75f;
        constexpr float LightingPaletteScale = static_cast<float>(LightingPaletteEntryCount - 1u);
        constexpr std::size_t TriangleGifPacketTemplateQwordCount = 6u;
        constexpr std::size_t TriangleGifPacketTemplateByteCount = TriangleGifPacketTemplateQwordCount * 16u;
        constexpr std::size_t LitTrianglePaletteQwordCount = LightingPaletteEntryCount;
        constexpr std::size_t LitTrianglePayloadQwordCount = LitTrianglePaletteQwordCount + TriangleGifPacketTemplateQwordCount + 3u;
        constexpr std::size_t LitTriangleGifPacketQwordOffset = LitTrianglePaletteQwordCount;
        constexpr std::size_t LitTriangleFaceNormalQwordOffset = LitTriangleGifPacketQwordOffset + TriangleGifPacketTemplateQwordCount;
        constexpr std::size_t LitTriangleLightDirectionQwordOffset = LitTriangleFaceNormalQwordOffset + 1u;
        constexpr std::size_t LitTriangleLightConstantsQwordOffset = LitTriangleLightDirectionQwordOffset + 1u;

        struct alignas(16) Ps2VuGifQword final {
            std::uint64_t Low = 0;
            std::uint64_t High = 0;
        };

        struct alignas(16) Ps2VuLitTrianglePayload final {
            Ps2VuGifQword LightingPalette[LightingPaletteEntryCount];
            std::uint8_t GifPacketTemplate[TriangleGifPacketTemplateByteCount];
            float FaceNormal[4];
            float LightDirection[4];
            float LightConstants[4];
        };

        static_assert(sizeof(Ps2VuLitTrianglePayload) == (LitTrianglePayloadQwordCount * 16u));

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
            const float gsDepth = 1.0f - screenZ;
            const std::uint32_t gsZ = static_cast<std::uint32_t>(gsDepth * static_cast<float>(ResolveMaximumDepth(gsGlobal)));
            positionRegister = GS_SETREG_XYZ2(gsX, gsY, gsZ);
            return true;
        }

        std::uint64_t BuildFixedTrianglePositionRegister(float screenX, float screenY, float screenZ, GSGLOBAL* gsGlobal) {
            const std::int32_t gsX = static_cast<std::int32_t>((2048.0f + screenX) * 16.0f);
            const std::int32_t gsY = static_cast<std::int32_t>((2048.0f + screenY) * 16.0f);
            const float gsDepth = 1.0f - screenZ;
            const std::uint32_t gsZ = static_cast<std::uint32_t>(gsDepth * static_cast<float>(ResolveMaximumDepth(gsGlobal)));
            return GS_SETREG_XYZ2(gsX, gsY, gsZ);
        }

        ::float3 NormalizeOrFallback(const ::float3& value, const ::float3& fallback) {
            const float lengthSquared = ::float3::Dot(value, value);
            if (lengthSquared <= 0.000001f) {
                return fallback;
            }

            return ::float3::Normalize(value);
        }

        std::string FormatGifTemplateQwords(const std::uint8_t* bytes, std::size_t qwordCount) {
            std::string formatted;
            for (std::size_t qwordIndex = 0; qwordIndex < qwordCount; qwordIndex++) {
                std::uint64_t lowWord = 0;
                std::uint64_t highWord = 0;
                std::memcpy(&lowWord, bytes + (qwordIndex * 16u), sizeof(std::uint64_t));
                std::memcpy(&highWord, bytes + (qwordIndex * 16u) + sizeof(std::uint64_t), sizeof(std::uint64_t));
                char buffer[96];
                std::snprintf(
                    buffer,
                    sizeof(buffer),
                    " q%zu=%016llx:%016llx",
                    qwordIndex,
                    static_cast<unsigned long long>(highWord),
                    static_cast<unsigned long long>(lowWord));
                formatted += buffer;
            }

            return formatted;
        }

        void PopulateLightingPalette(
            Ps2VuLitTrianglePayload& payload,
            std::uint8_t baseColorR,
            std::uint8_t baseColorG,
            std::uint8_t baseColorB,
            std::uint8_t baseColorA) {
            for (std::size_t paletteIndex = 0; paletteIndex < LightingPaletteEntryCount; paletteIndex++) {
                const float normalizedShade = static_cast<float>(paletteIndex) / LightingPaletteScale;
                const float intensity = LightingAmbientBias + (normalizedShade * LightingDiffuseScale);
                const std::uint8_t litColorR = static_cast<std::uint8_t>(std::clamp(std::lround(static_cast<double>(baseColorR) * static_cast<double>(intensity)), 0l, 255l));
                const std::uint8_t litColorG = static_cast<std::uint8_t>(std::clamp(std::lround(static_cast<double>(baseColorG) * static_cast<double>(intensity)), 0l, 255l));
                const std::uint8_t litColorB = static_cast<std::uint8_t>(std::clamp(std::lround(static_cast<double>(baseColorB) * static_cast<double>(intensity)), 0l, 255l));
                payload.LightingPalette[paletteIndex].Low = GS_SETREG_RGBAQ(litColorR, litColorG, litColorB, baseColorA, 0x00);
                payload.LightingPalette[paletteIndex].High = static_cast<std::uint64_t>(GIF_REG_RGBAQ);
            }
        }

        void PopulateTriangleGifPacketTemplate(
            const Ps2VuOpaqueBatch& batch,
            std::uint64_t positionARegister,
            std::uint64_t positionBRegister,
            std::uint64_t positionCRegister,
            Ps2VuLitTrianglePayload& payload) {
            std::unique_ptr<packet2_t, decltype(&packet2_free)> gifPacket(CreatePacketOrThrow(16u, P2_MODE_NORMAL), &packet2_free);
            prim_t prim = {};
            prim.type = PRIM_TRIANGLE;
            prim.shading = PRIM_SHADE_FLAT;
            prim.mapping = 0;
            prim.fogging = 0;
            prim.blending = 0;
            prim.antialiasing = 0;
            prim.mapping_type = PRIM_MAP_ST;
            prim.colorfix = PRIM_UNFIXED;
            const std::uint8_t baseColorR = batch.Material->GetBaseColorR();
            const std::uint8_t baseColorG = batch.Material->GetBaseColorG();
            const std::uint8_t baseColorB = batch.Material->GetBaseColorB();
            const std::uint8_t baseColorA = batch.Material->GetBaseColorA();
            color_t flatColor = {};
            flatColor.r = baseColorR;
            flatColor.g = baseColorG;
            flatColor.b = baseColorB;
            flatColor.a = baseColorA;
            packet2_update(gifPacket.get(), draw_prim_start(gifPacket.get()->base, 0, &prim, &flatColor));
            packet2_add_u64(gifPacket.get(), positionARegister);
            packet2_add_u64(gifPacket.get(), positionBRegister);
            packet2_add_u64(gifPacket.get(), positionCRegister);
            packet2_pad128(gifPacket.get(), 0);
            packet2_update(gifPacket.get(), draw_prim_end(gifPacket.get()->next, 1, static_cast<u64>(GIF_REG_XYZ2) << 0));
            const std::size_t gifPacketByteCount = static_cast<std::size_t>(packet2_get_qw_count(gifPacket.get())) * 16u;
            if (gifPacketByteCount != TriangleGifPacketTemplateByteCount) {
                throw std::runtime_error(
                    "PS2 lit triangle GIF template size changed unexpectedly. bytes="
                    + std::to_string(gifPacketByteCount)
                    + " qwords="
                    + std::to_string(packet2_get_qw_count(gifPacket.get())));
            }

            std::memset(&payload, 0, sizeof(Ps2VuLitTrianglePayload));
            PopulateLightingPalette(payload, baseColorR, baseColorG, baseColorB, baseColorA);
            std::memcpy(payload.GifPacketTemplate, gifPacket.get()->base, TriangleGifPacketTemplateByteCount);
            if (EnableVuGifTemplateLayoutDiagnostics) {
                throw std::runtime_error(
                    "PS2 lit triangle GIF template layout:"
                    + FormatGifTemplateQwords(payload.GifPacketTemplate, TriangleGifPacketTemplateQwordCount));
            }
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

    void Ps2VuVifPacketBuilder::AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal) {
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

        const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
        std::vector<Ps2VuLitTrianglePayload> trianglePayloads;
        trianglePayloads.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        if (EnableVuFixedTriangleDiagnostics) {
            Ps2VuLitTrianglePayload payload {};
            PopulateTriangleGifPacketTemplate(
                batch,
                BuildFixedTrianglePositionRegister(FixedTriangleAX, FixedTriangleAY, FixedTriangleZ, gsGlobal),
                BuildFixedTrianglePositionRegister(FixedTriangleBX, FixedTriangleBY, FixedTriangleZ, gsGlobal),
                BuildFixedTrianglePositionRegister(FixedTriangleCX, FixedTriangleCY, FixedTriangleZ, gsGlobal),
                payload);
            payload.FaceNormal[0] = 0.0f;
            payload.FaceNormal[1] = 0.0f;
            payload.FaceNormal[2] = -1.0f;
            payload.FaceNormal[3] = 0.0f;
            payload.LightDirection[0] = normalizedLightDirection.X;
            payload.LightDirection[1] = normalizedLightDirection.Y;
            payload.LightDirection[2] = normalizedLightDirection.Z;
            payload.LightDirection[3] = 0.0f;
            payload.LightConstants[0] = LightingDiffuseScale;
            payload.LightConstants[1] = LightingAmbientBias;
            payload.LightConstants[2] = LightingPaletteScale;
            payload.LightConstants[3] = 0.0f;
            trianglePayloads.push_back(payload);
            SubmittedTriangleCount = 1u;
            SubmittedScreenBounds = ::float4(FixedTriangleAX, FixedTriangleBY, FixedTriangleCX, FixedTriangleAY);
            SubmittedTriangleBoundsA = SubmittedScreenBounds;
            SubmittedTriangleVertexA0 = ::float4(FixedTriangleAX, FixedTriangleAY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA1 = ::float4(FixedTriangleBX, FixedTriangleBY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA2 = ::float4(FixedTriangleCX, FixedTriangleCY, FixedTriangleZ, 0.0f);
        } else {
            std::vector<::float3> clippedVertices;
            clippedVertices.reserve(4u);
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
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
                const ::float4 positionA(packedPositionA.X, packedPositionA.Y, packedPositionA.Z, 1.0f);
                const ::float4 positionB(packedPositionB.X, packedPositionB.Y, packedPositionB.Z, 1.0f);
                const ::float4 positionC(packedPositionC.X, packedPositionC.Y, packedPositionC.Z, 1.0f);
                const ::float3 worldPositionA = TransformPosition(positionA, world);
                const ::float3 worldPositionB = TransformPosition(positionB, world);
                const ::float3 worldPositionC = TransformPosition(positionC, world);
                const ::float4 worldPositionA4(worldPositionA.X, worldPositionA.Y, worldPositionA.Z, 1.0f);
                const ::float4 worldPositionB4(worldPositionB.X, worldPositionB.Y, worldPositionB.Z, 1.0f);
                const ::float4 worldPositionC4(worldPositionC.X, worldPositionC.Y, worldPositionC.Z, 1.0f);
                const ::float4 faceNormal4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f);
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(faceNormal4, world),
                    ::float3(0.0f, 0.0f, -1.0f));
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

                    Ps2VuLitTrianglePayload payload {};
                    PopulateTriangleGifPacketTemplate(batch, positionARegister, positionBRegister, positionCRegister, payload);
                    payload.FaceNormal[0] = worldFaceNormal.X;
                    payload.FaceNormal[1] = worldFaceNormal.Y;
                    payload.FaceNormal[2] = worldFaceNormal.Z;
                    payload.FaceNormal[3] = 0.0f;
                    payload.LightDirection[0] = normalizedLightDirection.X;
                    payload.LightDirection[1] = normalizedLightDirection.Y;
                    payload.LightDirection[2] = normalizedLightDirection.Z;
                    payload.LightDirection[3] = 0.0f;
                    payload.LightConstants[0] = LightingDiffuseScale;
                    payload.LightConstants[1] = LightingAmbientBias;
                    payload.LightConstants[2] = LightingPaletteScale;
                    payload.LightConstants[3] = 0.0f;
                    trianglePayloads.push_back(payload);
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

        if (trianglePayloads.empty()) {
            return;
        }

        if (GifPacketBytes.empty()) {
            GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);
            std::memcpy(GifPacketBytes.data(), trianglePayloads.front().GifPacketTemplate, TriangleGifPacketTemplateByteCount);
        }

        std::uint32_t emittedTriangleCount = static_cast<std::uint32_t>(trianglePayloads.size());
        std::uint32_t maxPacketQwordCount = std::max<std::uint32_t>(
            1024u,
            MinimumVifPacketOverheadQwords + (emittedTriangleCount * static_cast<std::uint32_t>(LitTrianglePayloadQwordCount + 8u)));
        if (maxPacketQwordCount > 0xFFFFu) {
            throw std::runtime_error("PS2 VU packet exceeds packet2 qword capacity.");
        }
        LastCompletedPhase = 2;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        std::unique_ptr<packet2_t, decltype(&packet2_free)> packet(CreatePacketOrThrow(static_cast<std::uint16_t>(maxPacketQwordCount), P2_MODE_CHAIN), &packet2_free);
        LastCompletedPhase = 3;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        for (const Ps2VuLitTrianglePayload& trianglePayload : trianglePayloads) {
            packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
            std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuLitTrianglePayload));
            packet2_advance_next(packet.get(), sizeof(Ps2VuLitTrianglePayload));
            packet2_utils_vu_close_unpack(packet.get());
            LastCompletedPhase = 4;
            if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
                return;
            }

            packet2_chain_open_cnt(packet.get(), 0, 0, 0);
            packet2_vif_flush(packet.get(), 0);
            packet2_vif_mscal(packet.get(), 0, 0);
            packet2_chain_close_tag(packet.get());
            LastCompletedPhase = 5;
            if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
                return;
            }
        }

        LastCompletedPhase = 6;
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
