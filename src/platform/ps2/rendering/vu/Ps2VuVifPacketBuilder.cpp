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
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

namespace helengine::ps2 {
    namespace {
        constexpr std::uint32_t XtopGifPacketAddress = 0;
        constexpr std::uint32_t MinimumVifPacketOverheadQwords = 32;
        constexpr std::uint32_t EnableVuPacketPhaseDiagnostics = 0;
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
        constexpr std::uint16_t UntexturedMicroProgramAddress = 0u;
        constexpr std::uint16_t TexturedMicroProgramAddress = 64u;
        constexpr std::uint64_t TexturedTriangleRegisterList = static_cast<std::uint64_t>(GIF_REG_RGBAQ)
            | (static_cast<std::uint64_t>(GIF_REG_UV) << 4u)
            | (static_cast<std::uint64_t>(GIF_REG_XYZ2) << 8u);
        constexpr bool EnableTexturedWhiteColorDiagnostics = false;
        constexpr double CpuLightingScale = 191.0;
        constexpr double CpuLightingBias = 64.0;

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

        struct Ps2VuTexturedClipVertex final {
            ::float3 ViewPosition = ::float3::get_Zero();
            ::float2 TexCoord = ::float2(0.0f, 0.0f);
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

        Ps2VuTexturedClipVertex InterpolateTexturedClipVertex(const Ps2VuTexturedClipVertex& start, const Ps2VuTexturedClipVertex& end, float amount) {
            Ps2VuTexturedClipVertex vertex;
            vertex.ViewPosition = InterpolateViewPosition(start.ViewPosition, end.ViewPosition, amount);
            vertex.TexCoord = ::float2(
                start.TexCoord.X + ((end.TexCoord.X - start.TexCoord.X) * amount),
                start.TexCoord.Y + ((end.TexCoord.Y - start.TexCoord.Y) * amount));
            return vertex;
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

        void ClipTexturedTriangleAgainstNearPlane(
            const Ps2VuTexturedClipVertex& first,
            const Ps2VuTexturedClipVertex& second,
            const Ps2VuTexturedClipVertex& third,
            float nearPlaneDistance,
            std::vector<Ps2VuTexturedClipVertex>& clippedVertices) {
            clippedVertices.clear();

            const float nearPlaneZ = -nearPlaneDistance;
            Ps2VuTexturedClipVertex previous = third;
            bool previousInside = previous.ViewPosition.Z <= nearPlaneZ;
            const Ps2VuTexturedClipVertex vertices[3] = { first, second, third };

            for (const Ps2VuTexturedClipVertex& current : vertices) {
                const bool currentInside = current.ViewPosition.Z <= nearPlaneZ;
                if (currentInside != previousInside) {
                    const float denominator = current.ViewPosition.Z - previous.ViewPosition.Z;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = (nearPlaneZ - previous.ViewPosition.Z) / denominator;
                        clippedVertices.push_back(InterpolateTexturedClipVertex(previous, current, amount));
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

        void PopulateLightingPalette(Ps2VuLitTrianglePayload& payload) {
            for (std::size_t paletteIndex = 0; paletteIndex < LightingPaletteEntryCount; paletteIndex++) {
                const float normalizedShade = static_cast<float>(paletteIndex) / LightingPaletteScale;
                const std::uint8_t shade = static_cast<std::uint8_t>(std::lround(normalizedShade * 255.0f));
                payload.LightingPalette[paletteIndex].Low = GS_SETREG_RGBAQ(shade, shade, shade, 0x80, 0x00);
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
            prim.shading = PRIM_SHADE_GOURAUD;
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
            PopulateLightingPalette(payload);
            std::memcpy(payload.GifPacketTemplate, gifPacket.get()->base, TriangleGifPacketTemplateByteCount);
            if (EnableVuGifTemplateLayoutDiagnostics) {
                throw std::runtime_error(
                    "PS2 lit triangle GIF template layout:"
                    + FormatGifTemplateQwords(payload.GifPacketTemplate, TriangleGifPacketTemplateQwordCount));
            }
        }

        ::float2 ResolveGsTextureCoordinate(const ::float2& normalizedTexCoord, int textureWidth, int textureHeight) {
            return ::float2(
                normalizedTexCoord.X * static_cast<float>(textureWidth),
                normalizedTexCoord.Y * static_cast<float>(textureHeight));
        }

        std::uint64_t BuildGsUvRegister(const ::float2& gsTexCoord) {
            const long u = std::clamp(std::lround(static_cast<double>(gsTexCoord.X) * 16.0), 0l, 16383l);
            const long v = std::clamp(std::lround(static_cast<double>(gsTexCoord.Y) * 16.0), 0l, 16383l);
            return GS_SETREG_UV(static_cast<std::uint32_t>(u), static_cast<std::uint32_t>(v));
        }

        std::uint64_t ResolveTexturedVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection) {
            const std::uint8_t baseColorR = material.GetBaseColorR();
            const std::uint8_t baseColorG = material.GetBaseColorG();
            const std::uint8_t baseColorB = material.GetBaseColorB();
            const std::uint8_t baseColorA = material.GetBaseColorA();
            if (material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit) {
                return GS_SETREG_RGBAQ(0xC0, 0xC0, 0xC0, 0x80, 0x00);
            }

            const double normalLengthSquared = static_cast<double>(normal.X) * static_cast<double>(normal.X)
                + static_cast<double>(normal.Y) * static_cast<double>(normal.Y)
                + static_cast<double>(normal.Z) * static_cast<double>(normal.Z);
            if (normalLengthSquared <= 0.000001) {
                return GS_SETREG_RGBAQ(0x40, 0x40, 0x40, 0x80, 0x00);
            }

            const ::float3 normalizedFaceNormal = NormalizeOrFallback(normal, ::float3(0.0f, 0.0f, -1.0f));
            const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
            const double ndotl = std::max(
                0.0,
                (static_cast<double>(normalizedFaceNormal.X) * static_cast<double>(normalizedLightDirection.X))
                    + (static_cast<double>(normalizedFaceNormal.Y) * static_cast<double>(normalizedLightDirection.Y))
                    + (static_cast<double>(normalizedFaceNormal.Z) * static_cast<double>(normalizedLightDirection.Z)));
            const double roughness = std::clamp(static_cast<double>(material.GetRoughness()), 0.0, 1.0);
            const double specularStrength = std::clamp(static_cast<double>(material.GetSpecularStrength()), 0.0, 1.0);
            const double emissiveStrength = std::clamp(static_cast<double>(material.GetEmissiveStrength()), 0.0, 1.0);
            const double diffuseScale = 1.0 - (roughness * 0.35);
            const double baseIntensity = CpuLightingBias + (ndotl * CpuLightingScale * diffuseScale);
            const double specularPower = 4.0 + ((1.0 - roughness) * 8.0);
            const double specularBoost = std::pow(ndotl, specularPower) * specularStrength * (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit ? 96.0 : 48.0);
            const double emissiveBoost = emissiveStrength * (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit ? 72.0 : 24.0);
            double intensityValue = baseIntensity + specularBoost + emissiveBoost;
            if (material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit && material.GetExpensiveModeAllowed()) {
                intensityValue += 24.0 + (ndotl * 28.0);
            }

            const std::uint8_t intensity = static_cast<std::uint8_t>(std::clamp(std::lround(intensityValue), 0l, 255l));
            const auto applyIntensity = [intensity](std::uint8_t channel) {
                const double litChannel = (static_cast<double>(channel) * static_cast<double>(intensity)) / 255.0;
                return static_cast<std::uint8_t>(std::clamp(std::lround(litChannel), 0l, 255l));
            };

            return GS_SETREG_RGBAQ(
                applyIntensity(baseColorR),
                applyIntensity(baseColorG),
                applyIntensity(baseColorB),
                baseColorA,
                0x00);
        }

        std::vector<std::uint8_t> BuildTexturedTriangleGifPacketBytes(
            int textureWidth,
            int textureHeight,
            std::uint64_t triangleColor,
            const Ps2VuTexturedClipVertex& vertexA,
            const Ps2VuTexturedClipVertex& vertexB,
            const Ps2VuTexturedClipVertex& vertexC,
            std::uint64_t positionARegister,
            std::uint64_t positionBRegister,
            std::uint64_t positionCRegister) {
            std::unique_ptr<packet2_t, decltype(&packet2_free)> gifPacket(CreatePacketOrThrow(32u, P2_MODE_NORMAL), &packet2_free);
            prim_t prim = {};
            prim.type = PRIM_TRIANGLE;
            prim.shading = PRIM_SHADE_FLAT;
            prim.mapping = 1;
            prim.fogging = 0;
            prim.blending = 0;
            prim.antialiasing = 0;
            prim.mapping_type = PRIM_MAP_UV;
            prim.colorfix = PRIM_UNFIXED;
            color_t flatColor = {};
            flatColor.r = static_cast<std::uint8_t>(triangleColor & 0xFFu);
            flatColor.g = static_cast<std::uint8_t>((triangleColor >> 8u) & 0xFFu);
            flatColor.b = static_cast<std::uint8_t>((triangleColor >> 16u) & 0xFFu);
            flatColor.a = static_cast<std::uint8_t>((triangleColor >> 24u) & 0xFFu);
            packet2_update(gifPacket.get(), draw_prim_start(gifPacket.get()->base, 0, &prim, &flatColor));
            packet2_add_u64(gifPacket.get(), triangleColor);
            packet2_add_u64(gifPacket.get(), BuildGsUvRegister(ResolveGsTextureCoordinate(vertexA.TexCoord, textureWidth, textureHeight)));
            packet2_add_u64(gifPacket.get(), positionARegister);
            packet2_add_u64(gifPacket.get(), triangleColor);
            packet2_add_u64(gifPacket.get(), BuildGsUvRegister(ResolveGsTextureCoordinate(vertexB.TexCoord, textureWidth, textureHeight)));
            packet2_add_u64(gifPacket.get(), positionBRegister);
            packet2_add_u64(gifPacket.get(), triangleColor);
            packet2_add_u64(gifPacket.get(), BuildGsUvRegister(ResolveGsTextureCoordinate(vertexC.TexCoord, textureWidth, textureHeight)));
            packet2_add_u64(gifPacket.get(), positionCRegister);
            packet2_pad128(gifPacket.get(), 0);
            packet2_update(gifPacket.get(), draw_prim_end(gifPacket.get()->next, 3, TexturedTriangleRegisterList));
            const std::size_t gifPacketByteCount = static_cast<std::size_t>(packet2_get_qw_count(gifPacket.get())) * 16u;
            std::vector<std::uint8_t> gifPacketBytes(gifPacketByteCount);
            std::memcpy(gifPacketBytes.data(), gifPacket.get()->base, gifPacketByteCount);
            return gifPacketBytes;
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

    void Ps2VuVifPacketBuilder::AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, int textureWidth, int textureHeight) {
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
        const bool textured = batch.Textured && textureWidth > 0 && textureHeight > 0;
        std::vector<Ps2VuLitTrianglePayload> trianglePayloads;
        trianglePayloads.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        std::vector<std::vector<std::uint8_t>> texturedTrianglePackets;
        texturedTrianglePackets.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
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
            std::vector<Ps2VuTexturedClipVertex> clippedTexturedVertices;
            clippedTexturedVertices.reserve(4u);
            const Ps2RuntimeModel* runtimeModel = batch.Proxy != nullptr ? batch.Proxy->GetModel() : nullptr;
            const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
            const std::vector<::float3>* runtimeNormals = runtimeModel != nullptr ? &runtimeModel->GetNormals() : nullptr;
            const std::vector<::float2>* runtimeTexCoords = runtimeModel != nullptr ? &runtimeModel->GetTexCoords() : nullptr;
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            const float* packedTexCoordWords = textured ? reinterpret_cast<const float*>(batch.Model->GetTexCoordBlockBytes()) : nullptr;
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
                const ::float4 faceNormal4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f);
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(faceNormal4, world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 worldNormalA = NormalizeOrFallback(
                    TransformPosition(::float4(sourceNormalA.X, sourceNormalA.Y, sourceNormalA.Z, 0.0f), world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 worldNormalB = NormalizeOrFallback(
                    TransformPosition(::float4(sourceNormalB.X, sourceNormalB.Y, sourceNormalB.Z, 0.0f), world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 worldNormalC = NormalizeOrFallback(
                    TransformPosition(::float4(sourceNormalC.X, sourceNormalC.Y, sourceNormalC.Z, 0.0f), world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 viewPositionA = TransformPosition(worldPositionA4, view);
                const ::float3 viewPositionB = TransformPosition(worldPositionB4, view);
                const ::float3 viewPositionC = TransformPosition(worldPositionC4, view);
                if (textured) {
                    const ::float2 sourceTexCoordA = runtimeTexCoords != nullptr && sourceIndexA < runtimeTexCoords->size()
                        ? (*runtimeTexCoords)[sourceIndexA]
                        : ::float2(packedTexCoordWords[positionWordIndexA + 0u], packedTexCoordWords[positionWordIndexA + 1u]);
                    const ::float2 sourceTexCoordB = runtimeTexCoords != nullptr && sourceIndexB < runtimeTexCoords->size()
                        ? (*runtimeTexCoords)[sourceIndexB]
                        : ::float2(packedTexCoordWords[positionWordIndexB + 0u], packedTexCoordWords[positionWordIndexB + 1u]);
                    const ::float2 sourceTexCoordC = runtimeTexCoords != nullptr && sourceIndexC < runtimeTexCoords->size()
                        ? (*runtimeTexCoords)[sourceIndexC]
                        : ::float2(packedTexCoordWords[positionWordIndexC + 0u], packedTexCoordWords[positionWordIndexC + 1u]);
                    const Ps2VuTexturedClipVertex texturedVertexA {
                        viewPositionA,
                        sourceTexCoordA };
                    const Ps2VuTexturedClipVertex texturedVertexB {
                        viewPositionB,
                        sourceTexCoordB };
                    const Ps2VuTexturedClipVertex texturedVertexC {
                        viewPositionC,
                        sourceTexCoordC };
                    ClipTexturedTriangleAgainstNearPlane(texturedVertexA, texturedVertexB, texturedVertexC, nearPlaneDistance, clippedTexturedVertices);
                } else {
                    ClipTriangleAgainstNearPlane(viewPositionA, viewPositionB, viewPositionC, nearPlaneDistance, clippedVertices);
                }

                const std::size_t clippedVertexCount = textured ? clippedTexturedVertices.size() : clippedVertices.size();
                if (clippedVertexCount < 3u) {
                    continue;
                }

                for (std::size_t clippedIndex = 1u; (clippedIndex + 1u) < clippedVertexCount; clippedIndex++) {
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
                    const ::float3& clippedPositionA = textured ? clippedTexturedVertices[0].ViewPosition : clippedVertices[0];
                    const ::float3& clippedPositionB = textured ? clippedTexturedVertices[clippedIndex].ViewPosition : clippedVertices[clippedIndex];
                    const ::float3& clippedPositionC = textured ? clippedTexturedVertices[clippedIndex + 1u].ViewPosition : clippedVertices[clippedIndex + 1u];
                    if (!TryBuildVertexPositionRegister(clippedPositionA, projection, viewport, gsGlobal, screenAX, screenAY, screenAZ, positionARegister)
                        || !TryBuildVertexPositionRegister(clippedPositionB, projection, viewport, gsGlobal, screenBX, screenBY, screenBZ, positionBRegister)
                        || !TryBuildVertexPositionRegister(clippedPositionC, projection, viewport, gsGlobal, screenCX, screenCY, screenCZ, positionCRegister)) {
                        continue;
                    }

                    if (!batch.Material->GetDoubleSided()
                        && !IsFrontFacingTriangle(screenAX, screenAY, screenBX, screenBY, screenCX, screenCY)) {
                        continue;
                    }

                    if (textured) {
                        const ::float3 triangleWorldNormal = NormalizeOrFallback(
                            ::float3(
                                worldNormalA.X + worldNormalB.X + worldNormalC.X,
                                worldNormalA.Y + worldNormalB.Y + worldNormalC.Y,
                                worldNormalA.Z + worldNormalB.Z + worldNormalC.Z),
                            worldFaceNormal);
                        const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                            ? GS_SETREG_RGBAQ(
                                batch.Material->GetBaseColorR(),
                                batch.Material->GetBaseColorG(),
                                batch.Material->GetBaseColorB(),
                                batch.Material->GetBaseColorA(),
                                0x00)
                            : ResolveTexturedVertexColor(*batch.Material, triangleWorldNormal, normalizedLightDirection);
                        texturedTrianglePackets.push_back(
                            BuildTexturedTriangleGifPacketBytes(
                                textureWidth,
                                textureHeight,
                                triangleColor,
                                clippedTexturedVertices[0],
                                clippedTexturedVertices[clippedIndex],
                                clippedTexturedVertices[clippedIndex + 1u],
                                positionARegister,
                                positionBRegister,
                                positionCRegister));
                    } else {
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
                    }
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

        if (!textured && trianglePayloads.empty()) {
            return;
        }

        if (textured && texturedTrianglePackets.empty()) {
            return;
        }

        if (!textured && GifPacketBytes.empty()) {
            GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);
            std::memcpy(GifPacketBytes.data(), trianglePayloads.front().GifPacketTemplate, TriangleGifPacketTemplateByteCount);
        } else if (textured && GifPacketBytes.empty()) {
            GifPacketBytes = texturedTrianglePackets.front();
        }

        const std::uint32_t emittedTriangleCount = textured
            ? static_cast<std::uint32_t>(texturedTrianglePackets.size())
            : static_cast<std::uint32_t>(trianglePayloads.size());
        std::uint32_t maxPacketQwordCount = std::max<std::uint32_t>(
            1024u,
            textured
                ? MinimumVifPacketOverheadQwords + (emittedTriangleCount * 24u)
                : MinimumVifPacketOverheadQwords + (emittedTriangleCount * static_cast<std::uint32_t>(LitTrianglePayloadQwordCount + 8u)));
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

        if (textured) {
            for (const std::vector<std::uint8_t>& trianglePacketBytes : texturedTrianglePackets) {
                packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
                std::memcpy(packet.get()->next, trianglePacketBytes.data(), trianglePacketBytes.size());
                packet2_advance_next(packet.get(), trianglePacketBytes.size());
                packet2_utils_vu_close_unpack(packet.get());
                LastCompletedPhase = 4;
                if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
                    return;
                }

                packet2_chain_open_cnt(packet.get(), 0, 0, 0);
                packet2_vif_flush(packet.get(), 0);
                packet2_vif_mscal(packet.get(), TexturedMicroProgramAddress, 0);
                packet2_chain_close_tag(packet.get());
                LastCompletedPhase = 5;
                if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
                    return;
                }
            }
        } else {
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
                packet2_vif_mscal(packet.get(), UntexturedMicroProgramAddress, 0);
                packet2_chain_close_tag(packet.get());
                LastCompletedPhase = 5;
                if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
                    return;
                }
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
