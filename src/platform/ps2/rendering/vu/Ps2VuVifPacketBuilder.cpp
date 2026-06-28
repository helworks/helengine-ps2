#include "platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp"

#include <algorithm>
#include <array>
#include <ctime>
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
        constexpr bool EnableVuFlatColorDiagnostics = false;
        constexpr bool EnableVuSingleDispatchDiagnostic = false;
        constexpr bool EnableVuPerTriangleTimingDiagnostics = false;
        constexpr std::size_t TriangleGifPacketTemplateQwordCount = 11u;
        constexpr std::size_t TriangleGifPacketTemplateByteCount = TriangleGifPacketTemplateQwordCount * 16u;
        constexpr std::size_t TexturedTrianglePacketWordCount = 22u;
        constexpr std::size_t TexturedTrianglePacketByteCount = TexturedTrianglePacketWordCount * sizeof(std::uint64_t);
        constexpr std::size_t UntexturedTriangleSourceQwordCount = 3u;
        constexpr std::size_t UntexturedTriangleGifPacketQwordOffset = UntexturedTriangleSourceQwordCount;
        constexpr std::size_t UntexturedTriangleRecordQwordCount = UntexturedTriangleGifPacketQwordOffset + TriangleGifPacketTemplateQwordCount;
        constexpr std::size_t UntexturedTriangleSharedStateQwordOffset = UntexturedTriangleRecordQwordCount;
        constexpr std::uint16_t UntexturedMicroProgramAddress = 0u;
        constexpr std::uint16_t TexturedMicroProgramAddress = 64u;
        constexpr std::uint64_t UntexturedTriangleRegisterList = static_cast<std::uint64_t>(GIF_REG_RGBAQ) << 0u
            | (static_cast<std::uint64_t>(GIF_REG_XYZ2) << 4u);
        constexpr std::uint64_t TexturedTriangleRegisterList = static_cast<std::uint64_t>(GIF_REG_RGBAQ)
            | (static_cast<std::uint64_t>(GIF_REG_UV) << 4u)
            | (static_cast<std::uint64_t>(GIF_REG_XYZ2) << 8u);
        constexpr std::uint32_t TexturedVuDiagnosticLogLimit = 16u;
        constexpr bool EnableTexturedWhiteColorDiagnostics = false;
        constexpr double CpuLightingScale = 191.0;
        constexpr double CpuLightingBias = 64.0;

        int ResolveGsTextureDimensionExponent(int textureDimension) {
            if (textureDimension <= 0) {
                throw std::invalid_argument("PS2 GS texture dimension must be greater than zero.");
            }

            int exponent = 0;
            int powerOfTwo = 1;
            while (powerOfTwo < textureDimension) {
                powerOfTwo <<= 1;
                exponent++;
            }

            return exponent;
        }

        struct alignas(16) Ps2VuGifQword final {
            std::uint64_t Low = 0;
            std::uint64_t High = 0;
        };

        struct alignas(16) Ps2VuOpaqueSourceTriangle final {
            float PositionA[4];
            float PositionB[4];
            float PositionC[4];
        };

        struct alignas(16) Ps2VuUntexturedTriangleRecord final {
            Ps2VuOpaqueSourceTriangle SourceTriangle;
            std::uint8_t GifPacketTemplate[TriangleGifPacketTemplateByteCount];
        };

        struct alignas(16) Ps2VuUntexturedSharedState final {
            float WorldViewProjectionMatrix[16];
            float GsScale[4];
            float GsOffset[4];
        };

        struct alignas(16) Ps2VuUntexturedTrianglePayload final {
            Ps2VuUntexturedTriangleRecord TriangleRecord;
            Ps2VuUntexturedSharedState SharedState;
        };

        struct Ps2VuGifTemplateCacheEntry final {
            std::uint32_t FlatColorKey = 0u;
            std::array<std::uint8_t, TriangleGifPacketTemplateByteCount> GifPacketTemplate {};
        };

        struct Ps2VuTexturedClipVertex final {
            ::float3 ViewPosition = ::float3::get_Zero();
            ::float2 TexCoord = ::float2(0.0f, 0.0f);
        };

        struct Ps2VuFlatColor final {
            std::uint8_t Red = 0xFF;
            std::uint8_t Green = 0xFF;
            std::uint8_t Blue = 0xFF;
            std::uint8_t Alpha = 0x80;
        };

        struct Ps2VuLightingConstants final {
            std::uint8_t BaseColorR = 0xFF;
            std::uint8_t BaseColorG = 0xFF;
            std::uint8_t BaseColorB = 0xFF;
            std::uint8_t BaseColorA = 0x80;
            bool Unlit = false;
            bool ShowcaseLit = false;
            bool ExpensiveModeAllowed = false;
            double DiffuseScale = 1.0;
            double SpecularPower = 4.0;
            double SpecularScale = 0.0;
            double EmissiveBoost = 0.0;
        };

        void PopulateTriangleGifPacketTemplate(
            const Ps2VuOpaqueBatch& batch,
            const Ps2VuFlatColor& flatColor,
            GSGLOBAL* gsGlobal,
            Ps2VuUntexturedTriangleRecord& record);

        packet2_t* CreatePacketOrThrow(std::uint16_t qwords, Packet2Mode mode);

        void PopulateLightingConstants(const Ps2RuntimeMaterial& material, Ps2VuLightingConstants& lightingConstants);

        std::uint64_t ResolveTexturedVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection);

        std::uint64_t ResolveTexturedVertexColor(const Ps2VuLightingConstants& lightingConstants, const ::float3& normalizedFaceNormal, const ::float3& normalizedLightDirection);

        static_assert((sizeof(Ps2VuUntexturedTriangleRecord) % 16u) == 0u);
        static_assert((sizeof(Ps2VuUntexturedSharedState) % 16u) == 0u);
        static_assert((sizeof(Ps2VuUntexturedTrianglePayload) % 16u) == 0u);
        static_assert((offsetof(Ps2VuUntexturedTriangleRecord, SourceTriangle) / 16u) == 0u);
        static_assert((offsetof(Ps2VuUntexturedTriangleRecord, GifPacketTemplate) / 16u) == UntexturedTriangleGifPacketQwordOffset);
        static_assert((offsetof(Ps2VuUntexturedTrianglePayload, TriangleRecord) / 16u) == 0u);
        static_assert((offsetof(Ps2VuUntexturedTrianglePayload, SharedState) / 16u) == UntexturedTriangleSharedStateQwordOffset);

        void CopyMatrixWords(const ::float4x4& matrix, float* destinationWords) {
            destinationWords[0] = matrix.M11;
            destinationWords[1] = matrix.M12;
            destinationWords[2] = matrix.M13;
            destinationWords[3] = matrix.M14;
            destinationWords[4] = matrix.M21;
            destinationWords[5] = matrix.M22;
            destinationWords[6] = matrix.M23;
            destinationWords[7] = matrix.M24;
            destinationWords[8] = matrix.M31;
            destinationWords[9] = matrix.M32;
            destinationWords[10] = matrix.M33;
            destinationWords[11] = matrix.M34;
            destinationWords[12] = matrix.M41;
            destinationWords[13] = matrix.M42;
            destinationWords[14] = matrix.M43;
            destinationWords[15] = matrix.M44;
        }

        void PopulateUntexturedSharedState(
            const ::float4x4& world,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            Ps2VuUntexturedSharedState& sharedState) {
            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4 projectionCopy = projection;
            ::float4x4 worldViewMatrix;
            ::float4x4 worldViewProjectionMatrix;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            ::float4x4::Multiply__ref0_ref1_out2(worldViewMatrix, projectionCopy, worldViewProjectionMatrix);
            CopyMatrixWords(worldViewProjectionMatrix, sharedState.WorldViewProjectionMatrix);
            sharedState.GsScale[0] = viewport.Z * 0.5f;
            sharedState.GsScale[1] = viewport.W * -0.5f;
            sharedState.GsScale[2] = -4194304.0f;
            sharedState.GsScale[3] = 0.0f;
            sharedState.GsOffset[0] = 2048.0f + viewport.X + (viewport.Z * 0.5f);
            sharedState.GsOffset[1] = 2048.0f + viewport.Y + (viewport.W * 0.5f);
            sharedState.GsOffset[2] = 4194304.0f;
            sharedState.GsOffset[3] = 0.0f;
        }

        std::uint32_t ResolveFlatColorKey(const Ps2VuFlatColor& flatColor) {
            return static_cast<std::uint32_t>(flatColor.Red)
                | (static_cast<std::uint32_t>(flatColor.Green) << 8u)
                | (static_cast<std::uint32_t>(flatColor.Blue) << 16u)
                | (static_cast<std::uint32_t>(flatColor.Alpha) << 24u);
        }

        void CopyCachedTriangleGifPacketTemplate(
            const Ps2VuOpaqueBatch& batch,
            const Ps2VuFlatColor& flatColor,
            GSGLOBAL* gsGlobal,
            std::vector<Ps2VuGifTemplateCacheEntry>& gifTemplateCache,
            Ps2VuUntexturedTriangleRecord& record) {
            const std::uint32_t flatColorKey = ResolveFlatColorKey(flatColor);
            for (const Ps2VuGifTemplateCacheEntry& cacheEntry : gifTemplateCache) {
                if (cacheEntry.FlatColorKey != flatColorKey) {
                    continue;
                }

                std::memcpy(record.GifPacketTemplate, cacheEntry.GifPacketTemplate.data(), TriangleGifPacketTemplateByteCount);
                return;
            }

            PopulateTriangleGifPacketTemplate(batch, flatColor, gsGlobal, record);
            Ps2VuGifTemplateCacheEntry cacheEntry {};
            cacheEntry.FlatColorKey = flatColorKey;
            std::memcpy(cacheEntry.GifPacketTemplate.data(), record.GifPacketTemplate, TriangleGifPacketTemplateByteCount);
            gifTemplateCache.push_back(cacheEntry);
        }

        void PopulateGifColorQword(
            Ps2VuGifQword& qword,
            std::uint8_t red,
            std::uint8_t green,
            std::uint8_t blue,
            std::uint8_t alpha) {
            std::uint32_t* colorWords = reinterpret_cast<std::uint32_t*>(&qword);
            colorWords[0] = red;
            colorWords[1] = green;
            colorWords[2] = blue;
            colorWords[3] = alpha;
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

        std::uint64_t ResolveOpaqueUntexturedTestRegister(GSGLOBAL* gsGlobal) {
            const bool rendererDepthTestEnabled = gsGlobal != nullptr && gsGlobal->ZBuffering == GS_SETTING_ON;
            const std::uint32_t rendererZTestMethod = rendererDepthTestEnabled
                ? ZTEST_METHOD_GREATER_EQUAL
                : ZTEST_METHOD_ALLPASS;
            return GS_SET_TEST(
                0,
                0,
                0,
                0,
                0,
                0,
                rendererDepthTestEnabled ? DRAW_ENABLE : DRAW_DISABLE,
                rendererZTestMethod);
        }

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

        bool IsTexturedVertexInsideNearPlane(const ::float3& viewPosition, float nearPlaneDistance) {
            return viewPosition.Z <= -nearPlaneDistance;
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

        std::uint64_t BuildScreenSpacePositionRegister(
            float screenX,
            float screenY,
            float screenZ,
            GSGLOBAL* gsGlobal) {
            if (gsGlobal == nullptr) {
                throw std::invalid_argument("PS2 GS position register packing requires a GS global.");
            }

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

        void AppendDiagnosticToHostBootLog(const std::string& message) {
            const char* candidatePaths[] = {
                "host:ps2_bootlog.txt",
                "host0:ps2_bootlog.txt"
            };

            for (const char* candidatePath : candidatePaths) {
                FILE* file = std::fopen(candidatePath, "a");
                if (file == nullptr) {
                    continue;
                }

                std::fwrite(message.c_str(), 1u, message.size(), file);
                std::fwrite("\n", 1u, 1u, file);
                std::fflush(file);
                std::fclose(file);
                return;
            }
        }

        void PopulateTriangleGifPacketTemplate(
            const Ps2VuOpaqueBatch& batch,
            const Ps2VuFlatColor& resolvedFlatColor,
            GSGLOBAL* gsGlobal,
            Ps2VuUntexturedTriangleRecord& record) {
            prim_t prim = {};
            prim.type = PRIM_TRIANGLE;
            prim.shading = PRIM_SHADE_FLAT;
            prim.mapping = 0;
            prim.fogging = 0;
            prim.blending = 0;
            prim.antialiasing = 0;
            prim.mapping_type = PRIM_MAP_ST;
            prim.colorfix = PRIM_UNFIXED;
            const std::uint8_t baseColorA = batch.Material->GetBaseColorA();
            color_t flatColor = {};
            if (EnableVuFlatColorDiagnostics) {
                flatColor.r = 0xFF;
                flatColor.g = 0x20;
                flatColor.b = 0x20;
                flatColor.a = baseColorA;
            }
            else {
                flatColor.r = resolvedFlatColor.Red;
                flatColor.g = resolvedFlatColor.Green;
                flatColor.b = resolvedFlatColor.Blue;
                flatColor.a = resolvedFlatColor.Alpha;
            }
            lod_t lod = {};
            lod.mag_filter = LOD_MAG_NEAREST;
            lod.min_filter = LOD_MIN_NEAREST;
            std::memset(&record, 0, sizeof(Ps2VuUntexturedTriangleRecord));
            std::unique_ptr<packet2_t, decltype(&packet2_free)> gifPacket(CreatePacketOrThrow(32u, P2_MODE_NORMAL), &packet2_free);
            packet2_utils_gif_add_set(gifPacket.get(), 1);
            packet2_add_2x_s64(
                gifPacket.get(),
                ResolveOpaqueUntexturedTestRegister(gsGlobal),
                GS_REG_TEST);
            packet2_utils_gif_add_set(gifPacket.get(), 1);
            packet2_utils_gs_add_lod(gifPacket.get(), &lod);
            packet2_utils_gs_add_prim_giftag(gifPacket.get(), &prim, 3u, UntexturedTriangleRegisterList, 2u, 0);
            std::memset(gifPacket.get()->next, 0, 6u * 16u);
            Ps2VuGifQword* triangleRegisters = reinterpret_cast<Ps2VuGifQword*>(gifPacket.get()->next);
            PopulateGifColorQword(
                triangleRegisters[0],
                flatColor.r,
                flatColor.g,
                flatColor.b,
                flatColor.a);
            PopulateGifColorQword(
                triangleRegisters[2],
                flatColor.r,
                flatColor.g,
                flatColor.b,
                flatColor.a);
            PopulateGifColorQword(
                triangleRegisters[4],
                flatColor.r,
                flatColor.g,
                flatColor.b,
                flatColor.a);
            packet2_advance_next(gifPacket.get(), 6u * 16u);
            std::memcpy(record.GifPacketTemplate, gifPacket.get()->base, TriangleGifPacketTemplateByteCount);
            static bool hasLoggedUntexturedGifTemplate = false;
            if (!hasLoggedUntexturedGifTemplate) {
                hasLoggedUntexturedGifTemplate = true;
                AppendDiagnosticToHostBootLog(
                    std::string("[helengine-ps2] untextured vu gif template:")
                    + FormatGifTemplateQwords(record.GifPacketTemplate, TriangleGifPacketTemplateQwordCount));
            }
            if (EnableVuGifTemplateLayoutDiagnostics) {
                throw std::runtime_error(
                    "PS2 lit triangle GIF template layout:"
                    + FormatGifTemplateQwords(record.GifPacketTemplate, TriangleGifPacketTemplateQwordCount));
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

        std::uint8_t ApplyIntensityToChannel(std::uint8_t channel, std::uint8_t intensity) {
            const std::uint32_t litChannel = (static_cast<std::uint32_t>(channel) * static_cast<std::uint32_t>(intensity)) + 127u;
            return static_cast<std::uint8_t>(litChannel / 255u);
        }

        void PopulateLightingConstants(const Ps2RuntimeMaterial& material, Ps2VuLightingConstants& lightingConstants) {
            lightingConstants.BaseColorR = material.GetBaseColorR();
            lightingConstants.BaseColorG = material.GetBaseColorG();
            lightingConstants.BaseColorB = material.GetBaseColorB();
            lightingConstants.BaseColorA = material.GetBaseColorA();
            lightingConstants.Unlit = material.GetLightingMode() == ::Ps2MaterialLightingMode::Unlit;
            lightingConstants.ShowcaseLit = material.GetLightingMode() == ::Ps2MaterialLightingMode::ShowcaseLit;
            lightingConstants.ExpensiveModeAllowed = material.GetExpensiveModeAllowed();
            if (lightingConstants.Unlit) {
                lightingConstants.DiffuseScale = 1.0;
                lightingConstants.SpecularPower = 4.0;
                lightingConstants.SpecularScale = 0.0;
                lightingConstants.EmissiveBoost = 0.0;
                return;
            }

            const double roughness = std::clamp(static_cast<double>(material.GetRoughness()), 0.0, 1.0);
            const double specularStrength = std::clamp(static_cast<double>(material.GetSpecularStrength()), 0.0, 1.0);
            const double emissiveStrength = std::clamp(static_cast<double>(material.GetEmissiveStrength()), 0.0, 1.0);
            lightingConstants.DiffuseScale = 1.0 - (roughness * 0.35);
            lightingConstants.SpecularPower = 4.0 + ((1.0 - roughness) * 8.0);
            lightingConstants.SpecularScale = specularStrength * (lightingConstants.ShowcaseLit ? 96.0 : 48.0);
            lightingConstants.EmissiveBoost = emissiveStrength * (lightingConstants.ShowcaseLit ? 72.0 : 24.0);
        }

        double ResolveApproximateSpecularFactor(const Ps2VuLightingConstants& lightingConstants, double ndotl) {
            if (lightingConstants.SpecularScale <= 0.0 || ndotl <= 0.0) {
                return 0.0;
            }

            const double ndotl2 = ndotl * ndotl;
            const double ndotl4 = ndotl2 * ndotl2;
            const double ndotl8 = ndotl4 * ndotl4;
            const double ndotl16 = ndotl8 * ndotl8;
            double specularFactor = ndotl4;
            if (lightingConstants.SpecularPower >= 10.0) {
                specularFactor = ndotl16;
            } else if (lightingConstants.SpecularPower >= 6.0) {
                specularFactor = ndotl8;
            }

            return specularFactor * lightingConstants.SpecularScale;
        }

        std::uint64_t ResolveTexturedVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection) {
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(material, lightingConstants);
            if (lightingConstants.Unlit) {
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
            return ResolveTexturedVertexColor(lightingConstants, normalizedFaceNormal, normalizedLightDirection);
        }

        std::uint64_t ResolveTexturedVertexColor(const Ps2VuLightingConstants& lightingConstants, const ::float3& normalizedFaceNormal, const ::float3& normalizedLightDirection) {
            if (lightingConstants.Unlit) {
                return GS_SETREG_RGBAQ(0xC0, 0xC0, 0xC0, 0x80, 0x00);
            }

            const double ndotl = std::max(
                0.0,
                (static_cast<double>(normalizedFaceNormal.X) * static_cast<double>(normalizedLightDirection.X))
                    + (static_cast<double>(normalizedFaceNormal.Y) * static_cast<double>(normalizedLightDirection.Y))
                    + (static_cast<double>(normalizedFaceNormal.Z) * static_cast<double>(normalizedLightDirection.Z)));
            const double baseIntensity = CpuLightingBias + (ndotl * CpuLightingScale * lightingConstants.DiffuseScale);
            const double specularBoost = ResolveApproximateSpecularFactor(lightingConstants, ndotl);
            double intensityValue = baseIntensity + specularBoost + lightingConstants.EmissiveBoost;
            if (lightingConstants.ShowcaseLit && lightingConstants.ExpensiveModeAllowed) {
                intensityValue += 24.0 + (ndotl * 28.0);
            }

            const std::uint8_t intensity = static_cast<std::uint8_t>(std::clamp(std::lround(intensityValue), 0l, 255l));
            return GS_SETREG_RGBAQ(
                ApplyIntensityToChannel(lightingConstants.BaseColorR, intensity),
                ApplyIntensityToChannel(lightingConstants.BaseColorG, intensity),
                ApplyIntensityToChannel(lightingConstants.BaseColorB, intensity),
                lightingConstants.BaseColorA,
                0x00);
        }

        std::array<std::uint64_t, TexturedTrianglePacketWordCount> BuildTexturedTriangleGifPacketBytes(
            GSGLOBAL* gsGlobal,
            const GSTEXTURE* texture,
            int textureWidth,
            int textureHeight,
            std::uint64_t triangleColor,
            const Ps2VuTexturedClipVertex& vertexA,
            const Ps2VuTexturedClipVertex& vertexB,
            const Ps2VuTexturedClipVertex& vertexC,
            float screenAX,
            float screenAY,
            float screenAZ,
            std::uint64_t positionARegister,
            float screenBX,
            float screenBY,
            float screenBZ,
            std::uint64_t positionBRegister,
            float screenCX,
            float screenCY,
            float screenCZ,
            std::uint64_t positionCRegister) {
            if (gsGlobal == nullptr) {
                throw std::invalid_argument("PS2 VU textured GIF packet encoding requires a GS global.");
            }
            if (texture == nullptr) {
                throw std::invalid_argument("PS2 VU textured GIF packet encoding requires a texture.");
            }

            const int textureWidthPower = ResolveGsTextureDimensionExponent(texture->Width);
            const int textureHeightPower = ResolveGsTextureDimensionExponent(texture->Height);
            prim_t prim = {};
            prim.type = PRIM_TRIANGLE;
            prim.shading = PRIM_SHADE_GOURAUD;
            prim.mapping = 1;
            prim.fogging = 0;
            prim.blending = 0;
            prim.antialiasing = 0;
            prim.mapping_type = PRIM_MAP_UV;
            prim.colorfix = PRIM_UNFIXED;
            const std::uint64_t texturedTriangleGifTag = GIF_TAG_TRIANGLE_GORAUD_TEXTURED(1);
            const std::uint64_t texturedTriangleGifRegisters = GIF_TAG_TRIANGLE_GORAUD_TEXTURED_REGS(gsGlobal->PrimContext);
            const ::float2 screenTexCoordA = ResolveGsTextureCoordinate(vertexA.TexCoord, textureWidth, textureHeight);
            const ::float2 screenTexCoordB = ResolveGsTextureCoordinate(vertexB.TexCoord, textureWidth, textureHeight);
            const ::float2 screenTexCoordC = ResolveGsTextureCoordinate(vertexC.TexCoord, textureWidth, textureHeight);
            const std::uint64_t uvRegisterA = BuildGsUvRegister(screenTexCoordA);
            const std::uint64_t uvRegisterB = BuildGsUvRegister(screenTexCoordB);
            const std::uint64_t uvRegisterC = BuildGsUvRegister(screenTexCoordC);
            std::array<std::uint64_t, TexturedTrianglePacketWordCount> packetWords {};
            std::size_t packetWordIndex = 0u;
            packetWords[packetWordIndex++] = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);
            packetWords[packetWordIndex++] = GIF_REG_AD;
            packetWords[packetWordIndex++] = ResolveOpaqueUntexturedTestRegister(gsGlobal);
            packetWords[packetWordIndex++] = GS_REG_TEST;
            packetWords[packetWordIndex++] = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);
            packetWords[packetWordIndex++] = GIF_REG_AD;
            packetWords[packetWordIndex++] = GS_SET_TEX1(0, 0, texture->Filter, texture->Filter, 0, 0, 0);
            packetWords[packetWordIndex++] = GS_REG_TEX1;
            packetWords[packetWordIndex++] = texturedTriangleGifTag;
            packetWords[packetWordIndex++] = texturedTriangleGifRegisters;
            if (texture->VramClut == 0) {
                packetWords[packetWordIndex++] = GS_SETREG_TEX0(
                    texture->Vram / 256,
                    texture->TBW,
                    texture->PSM,
                    textureWidthPower,
                    textureHeightPower,
                    gsGlobal->PrimAlphaEnable,
                    0,
                    0,
                    0,
                    0,
                    0,
                    GS_CLUT_STOREMODE_NOLOAD);
            } else {
                packetWords[packetWordIndex++] = GS_SETREG_TEX0(
                    texture->Vram / 256,
                    texture->TBW,
                    texture->PSM,
                    textureWidthPower,
                    textureHeightPower,
                    gsGlobal->PrimAlphaEnable,
                    0,
                    texture->VramClut / 256,
                    texture->ClutPSM,
                    texture->ClutStorageMode,
                    0,
                    GS_CLUT_STOREMODE_LOAD);
            }
            packetWords[packetWordIndex++] = GS_SETREG_PRIM(
                GS_PRIM_PRIM_TRIANGLE,
                prim.shading,
                prim.mapping,
                gsGlobal->PrimFogEnable,
                gsGlobal->PrimAlphaEnable,
                gsGlobal->PrimAAEnable,
                1,
                gsGlobal->PrimContext,
                0);
            packetWords[packetWordIndex++] = triangleColor;
            packetWords[packetWordIndex++] = uvRegisterA;
            packetWords[packetWordIndex++] = positionARegister;
            packetWords[packetWordIndex++] = triangleColor;
            packetWords[packetWordIndex++] = uvRegisterB;
            packetWords[packetWordIndex++] = positionBRegister;
            packetWords[packetWordIndex++] = triangleColor;
            packetWords[packetWordIndex++] = uvRegisterC;
            packetWords[packetWordIndex++] = positionCRegister;
            return packetWords;
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
        LastTriangleSetupMilliseconds = 0.0;
        LastPacketAssemblyMilliseconds = 0.0;
        LastTrianglePrepMilliseconds = 0.0;
        LastTriangleEmitMilliseconds = 0.0;
        LastTriangleLightingMilliseconds = 0.0;
        LastTrianglePayloadFillMilliseconds = 0.0;
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

    void Ps2VuVifPacketBuilder::AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance, const ::float3& lightDirection, GSGLOBAL* gsGlobal, GSTEXTURE* texture, int textureWidth, int textureHeight) {
        if (batch.Model == nullptr || batch.Material == nullptr) {
            return;
        }

        // Opaque VU path invariant:
        // - no CPU near-plane clipping
        // - no CPU projection to XYZ2
        // - no CPU screen-space front-face rejection
        std::uint32_t triangleVertexCount = batch.Model->GetTriangleVertexCount();
        if (triangleVertexCount == 0) {
            return;
        }

        LastCompletedPhase = 1;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
        const bool textured = batch.Textured && texture != nullptr && textureWidth > 0 && textureHeight > 0;
        std::uint32_t texturedSourceTriangleCount = 0u;
        std::uint32_t texturedClipRejectCount = 0u;
        std::uint32_t texturedProjectionRejectCount = 0u;
        std::uint32_t texturedCullRejectCount = 0u;
        std::uint32_t texturedEmittedTriangleCount = 0u;
        if (textured) {
            gsKit_set_texfilter(gsGlobal, texture->Filter);
        }
        std::vector<Ps2VuUntexturedTrianglePayload> untexturedTrianglePayloads;
        untexturedTrianglePayloads.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        std::vector<Ps2VuGifTemplateCacheEntry> gifTemplateCache;
        gifTemplateCache.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        Ps2VuUntexturedSharedState sharedStateTemplate {};
        std::vector<std::array<std::uint64_t, TexturedTrianglePacketWordCount>> texturedTrianglePackets;
        texturedTrianglePackets.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        const std::clock_t triangleSetupStartTicks = std::clock();
        if (EnableVuFixedTriangleDiagnostics) {
            Ps2VuUntexturedTrianglePayload payload {};
            const std::uint64_t triangleColor = ResolveTexturedVertexColor(*batch.Material, ::float3(0.0f, 0.0f, -1.0f), normalizedLightDirection);
            Ps2VuFlatColor flatColor {};
            flatColor.Red = static_cast<std::uint8_t>(triangleColor & 0xFFu);
            flatColor.Green = static_cast<std::uint8_t>((triangleColor >> 8u) & 0xFFu);
            flatColor.Blue = static_cast<std::uint8_t>((triangleColor >> 16u) & 0xFFu);
            flatColor.Alpha = static_cast<std::uint8_t>((triangleColor >> 24u) & 0xFFu);
            PopulateUntexturedSharedState(world, view, projection, viewport, sharedStateTemplate);
            std::memcpy(&payload.SharedState, &sharedStateTemplate, sizeof(sharedStateTemplate));
            CopyCachedTriangleGifPacketTemplate(batch, flatColor, gsGlobal, gifTemplateCache, payload.TriangleRecord);
            payload.TriangleRecord.SourceTriangle.PositionA[0] = 0.0f;
            payload.TriangleRecord.SourceTriangle.PositionA[1] = 0.5f;
            payload.TriangleRecord.SourceTriangle.PositionA[2] = 0.0f;
            payload.TriangleRecord.SourceTriangle.PositionA[3] = 1.0f;
            payload.TriangleRecord.SourceTriangle.PositionB[0] = -0.5f;
            payload.TriangleRecord.SourceTriangle.PositionB[1] = -0.5f;
            payload.TriangleRecord.SourceTriangle.PositionB[2] = 0.0f;
            payload.TriangleRecord.SourceTriangle.PositionB[3] = 1.0f;
            payload.TriangleRecord.SourceTriangle.PositionC[0] = 0.5f;
            payload.TriangleRecord.SourceTriangle.PositionC[1] = -0.5f;
            payload.TriangleRecord.SourceTriangle.PositionC[2] = 0.0f;
            payload.TriangleRecord.SourceTriangle.PositionC[3] = 1.0f;
            untexturedTrianglePayloads.push_back(payload);
            SubmittedTriangleCount = 1u;
            SubmittedScreenBounds = ::float4(FixedTriangleAX, FixedTriangleBY, FixedTriangleCX, FixedTriangleAY);
            SubmittedTriangleBoundsA = SubmittedScreenBounds;
            SubmittedTriangleVertexA0 = ::float4(FixedTriangleAX, FixedTriangleAY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA1 = ::float4(FixedTriangleBX, FixedTriangleBY, FixedTriangleZ, 0.0f);
            SubmittedTriangleVertexA2 = ::float4(FixedTriangleCX, FixedTriangleCY, FixedTriangleZ, 0.0f);
        } else if (!textured) {
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            PopulateUntexturedSharedState(world, view, projection, viewport, sharedStateTemplate);
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch.Material, lightingConstants);
            std::clock_t accumulatedTriangleLightingTicks = 0;
            std::clock_t accumulatedTrianglePayloadFillTicks = 0;
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
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(::float4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f), world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const std::clock_t trianglePrepEndTicks = std::clock();
                LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);

                const std::clock_t triangleEmitStartTicks = std::clock();

                const std::clock_t triangleLightingStartTicks = std::clock();
                const std::uint64_t triangleColor = ResolveTexturedVertexColor(lightingConstants, worldFaceNormal, normalizedLightDirection);
                Ps2VuFlatColor flatColor {};
                flatColor.Red = static_cast<std::uint8_t>(triangleColor & 0xFFu);
                flatColor.Green = static_cast<std::uint8_t>((triangleColor >> 8u) & 0xFFu);
                flatColor.Blue = static_cast<std::uint8_t>((triangleColor >> 16u) & 0xFFu);
                flatColor.Alpha = static_cast<std::uint8_t>((triangleColor >> 24u) & 0xFFu);
                const std::clock_t triangleLightingEndTicks = std::clock();
                accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);

                const std::clock_t trianglePayloadFillStartTicks = std::clock();
                Ps2VuUntexturedTrianglePayload payload {};
                CopyCachedTriangleGifPacketTemplate(batch, flatColor, gsGlobal, gifTemplateCache, payload.TriangleRecord);
                std::memcpy(&payload.SharedState, &sharedStateTemplate, sizeof(sharedStateTemplate));
                payload.TriangleRecord.SourceTriangle.PositionA[0] = packedPositionWords[positionWordIndexA + 0u];
                payload.TriangleRecord.SourceTriangle.PositionA[1] = packedPositionWords[positionWordIndexA + 1u];
                payload.TriangleRecord.SourceTriangle.PositionA[2] = packedPositionWords[positionWordIndexA + 2u];
                payload.TriangleRecord.SourceTriangle.PositionA[3] = 1.0f;
                payload.TriangleRecord.SourceTriangle.PositionB[0] = packedPositionWords[positionWordIndexB + 0u];
                payload.TriangleRecord.SourceTriangle.PositionB[1] = packedPositionWords[positionWordIndexB + 1u];
                payload.TriangleRecord.SourceTriangle.PositionB[2] = packedPositionWords[positionWordIndexB + 2u];
                payload.TriangleRecord.SourceTriangle.PositionB[3] = 1.0f;
                payload.TriangleRecord.SourceTriangle.PositionC[0] = packedPositionWords[positionWordIndexC + 0u];
                payload.TriangleRecord.SourceTriangle.PositionC[1] = packedPositionWords[positionWordIndexC + 1u];
                payload.TriangleRecord.SourceTriangle.PositionC[2] = packedPositionWords[positionWordIndexC + 2u];
                payload.TriangleRecord.SourceTriangle.PositionC[3] = 1.0f;
                untexturedTrianglePayloads.push_back(payload);
                const std::clock_t trianglePayloadFillEndTicks = std::clock();
                accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);
                SubmittedTriangleCount++;
                const std::clock_t triangleEmitEndTicks = std::clock();
                LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                if (EnableVuSingleDispatchDiagnostic) {
                    break;
                }
            }
            LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);
            LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);
        } else {
            std::vector<Ps2VuTexturedClipVertex> clippedTexturedVertices;
            clippedTexturedVertices.reserve(4u);
            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4 worldViewMatrix;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch.Material, lightingConstants);
            const Ps2RuntimeModel* runtimeModel = batch.Proxy != nullptr ? batch.Proxy->GetModel() : nullptr;
            const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
            const std::vector<::float3>* runtimeNormals = runtimeModel != nullptr ? &runtimeModel->GetNormals() : nullptr;
            const std::vector<::float2>* runtimeTexCoords = runtimeModel != nullptr ? &runtimeModel->GetTexCoords() : nullptr;
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            const float* packedTexCoordWords = textured ? reinterpret_cast<const float*>(batch.Model->GetTexCoordBlockBytes()) : nullptr;
            std::clock_t accumulatedTriangleLightingTicks = 0;
            std::clock_t accumulatedTrianglePayloadFillTicks = 0;
            for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
                texturedSourceTriangleCount++;
                std::clock_t trianglePrepStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    trianglePrepStartTicks = std::clock();
                }
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
                const ::float4 faceNormal4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f);
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(faceNormal4, world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 sourceTriangleNormal = NormalizeOrFallback(
                    ::float3(
                        sourceNormalA.X + sourceNormalB.X + sourceNormalC.X,
                        sourceNormalA.Y + sourceNormalB.Y + sourceNormalC.Y,
                        sourceNormalA.Z + sourceNormalB.Z + sourceNormalC.Z),
                    faceNormal);
                const ::float3 triangleWorldNormal = NormalizeOrFallback(
                    TransformPosition(::float4(sourceTriangleNormal.X, sourceTriangleNormal.Y, sourceTriangleNormal.Z, 0.0f), world),
                    worldFaceNormal);
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t trianglePrepEndTicks = std::clock();
                    LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);
                }
                std::clock_t triangleEmitStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    triangleEmitStartTicks = std::clock();
                }
                if (textured) {
                    const ::float3 viewPositionA = TransformPosition(positionA, worldViewMatrix);
                    const ::float3 viewPositionB = TransformPosition(positionB, worldViewMatrix);
                    const ::float3 viewPositionC = TransformPosition(positionC, worldViewMatrix);
                    const bool texturedVertexAInside = IsTexturedVertexInsideNearPlane(viewPositionA, nearPlaneDistance);
                    const bool texturedVertexBInside = IsTexturedVertexInsideNearPlane(viewPositionB, nearPlaneDistance);
                    const bool texturedVertexCInside = IsTexturedVertexInsideNearPlane(viewPositionC, nearPlaneDistance);
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
                    if (texturedVertexAInside && texturedVertexBInside && texturedVertexCInside) {
                        clippedTexturedVertices.clear();
                        clippedTexturedVertices.push_back(texturedVertexA);
                        clippedTexturedVertices.push_back(texturedVertexB);
                        clippedTexturedVertices.push_back(texturedVertexC);
                    } else if (!texturedVertexAInside && !texturedVertexBInside && !texturedVertexCInside) {
                        clippedTexturedVertices.clear();
                    } else {
                        ClipTexturedTriangleAgainstNearPlane(texturedVertexA, texturedVertexB, texturedVertexC, nearPlaneDistance, clippedTexturedVertices);
                    }
                }

                const std::size_t clippedVertexCount = clippedTexturedVertices.size();
                if (clippedVertexCount < 3u) {
                    texturedClipRejectCount++;
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
                    const ::float3& clippedPositionA = clippedTexturedVertices[0].ViewPosition;
                    const ::float3& clippedPositionB = clippedTexturedVertices[clippedIndex].ViewPosition;
                    const ::float3& clippedPositionC = clippedTexturedVertices[clippedIndex + 1u].ViewPosition;
                    if (!TryBuildVertexPositionRegister(clippedPositionA, projection, viewport, gsGlobal, screenAX, screenAY, screenAZ, positionARegister)
                        || !TryBuildVertexPositionRegister(clippedPositionB, projection, viewport, gsGlobal, screenBX, screenBY, screenBZ, positionBRegister)
                        || !TryBuildVertexPositionRegister(clippedPositionC, projection, viewport, gsGlobal, screenCX, screenCY, screenCZ, positionCRegister)) {
                        texturedProjectionRejectCount++;
                        continue;
                    }

                    const std::clock_t triangleLightingStartTicks = std::clock();
                    const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                        ? GS_SETREG_RGBAQ(
                            batch.Material->GetBaseColorR(),
                            batch.Material->GetBaseColorG(),
                            batch.Material->GetBaseColorB(),
                            batch.Material->GetBaseColorA(),
                            0x00)
                        : ResolveTexturedVertexColor(lightingConstants, triangleWorldNormal, normalizedLightDirection);
                    const std::clock_t triangleLightingEndTicks = std::clock();
                    accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);

                    const std::clock_t trianglePayloadFillStartTicks = std::clock();
                    texturedTrianglePackets.push_back(
                        BuildTexturedTriangleGifPacketBytes(
                            gsGlobal,
                            texture,
                            textureWidth,
                            textureHeight,
                            triangleColor,
                            clippedTexturedVertices[0],
                            clippedTexturedVertices[clippedIndex],
                            clippedTexturedVertices[clippedIndex + 1u],
                            screenAX,
                            screenAY,
                            screenAZ,
                            positionARegister,
                            screenBX,
                            screenBY,
                            screenBZ,
                            positionBRegister,
                            screenCX,
                            screenCY,
                            screenCZ,
                            positionCRegister));
                    const std::clock_t trianglePayloadFillEndTicks = std::clock();
                    accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);
                    texturedEmittedTriangleCount++;
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
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t triangleEmitEndTicks = std::clock();
                    LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                }
            }

            LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);
            LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);
        }
        const std::clock_t triangleSetupEndTicks = std::clock();
        LastTriangleSetupMilliseconds = ResolveMillisecondsFromClockTicks(triangleSetupStartTicks, triangleSetupEndTicks);

        if (!textured && untexturedTrianglePayloads.empty()) {
            return;
        }

        if (textured && texturedTrianglePackets.empty()) {
            static std::uint32_t texturedEmptyPacketDiagnosticCount = 0u;
            if (texturedEmptyPacketDiagnosticCount < TexturedVuDiagnosticLogLimit) {
                texturedEmptyPacketDiagnosticCount++;
                AppendDiagnosticToHostBootLog(
                    std::string("[helengine-ps2] textured-vu empty")
                    + " texture=" + batch.Material->GetTextureRelativePath()
                    + " src=" + std::to_string(texturedSourceTriangleCount)
                    + " clipReject=" + std::to_string(texturedClipRejectCount)
                    + " projReject=" + std::to_string(texturedProjectionRejectCount)
                    + " cullReject=" + std::to_string(texturedCullRejectCount)
                    + " emitted=" + std::to_string(texturedEmittedTriangleCount)
                    + " submitted=" + std::to_string(SubmittedTriangleCount));
            }
            return;
        }

        if (!textured && GifPacketBytes.empty()) {
            GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);
            std::memcpy(GifPacketBytes.data(), untexturedTrianglePayloads.front().TriangleRecord.GifPacketTemplate, TriangleGifPacketTemplateByteCount);
        } else if (textured && GifPacketBytes.empty()) {
            GifPacketBytes.resize(TexturedTrianglePacketByteCount);
            std::memcpy(GifPacketBytes.data(), texturedTrianglePackets.front().data(), TexturedTrianglePacketByteCount);
        }

        const std::uint32_t emittedTriangleCount = textured
            ? static_cast<std::uint32_t>(texturedTrianglePackets.size())
            : static_cast<std::uint32_t>(untexturedTrianglePayloads.size());
        std::uint32_t maxPacketQwordCount = std::max<std::uint32_t>(
            1024u,
            textured
                ? MinimumVifPacketOverheadQwords + (emittedTriangleCount * 24u)
                : MinimumVifPacketOverheadQwords + (emittedTriangleCount * static_cast<std::uint32_t>((sizeof(Ps2VuUntexturedTrianglePayload) / 16u) + 8u)));
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

        const std::clock_t packetAssemblyStartTicks = std::clock();
        if (textured) {
            for (const std::array<std::uint64_t, TexturedTrianglePacketWordCount>& trianglePacketWords : texturedTrianglePackets) {
                packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
                std::memcpy(packet.get()->next, trianglePacketWords.data(), TexturedTrianglePacketByteCount);
                packet2_advance_next(packet.get(), TexturedTrianglePacketByteCount);
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
            for (const Ps2VuUntexturedTrianglePayload& trianglePayload : untexturedTrianglePayloads) {
                packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
                std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuUntexturedTrianglePayload));
                packet2_advance_next(packet.get(), sizeof(Ps2VuUntexturedTrianglePayload));
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
        const std::clock_t packetAssemblyEndTicks = std::clock();
        LastPacketAssemblyMilliseconds = ResolveMillisecondsFromClockTicks(packetAssemblyStartTicks, packetAssemblyEndTicks);
        std::uint32_t packetQwordCount = packet2_get_qw_count(Packet);
        if (textured) {
            static std::uint32_t texturedPacketDiagnosticCount = 0u;
            if (texturedPacketDiagnosticCount < TexturedVuDiagnosticLogLimit) {
                texturedPacketDiagnosticCount++;
                AppendDiagnosticToHostBootLog(
                    std::string("[helengine-ps2] textured-vu packet")
                    + " texture=" + batch.Material->GetTextureRelativePath()
                    + " src=" + std::to_string(texturedSourceTriangleCount)
                    + " clipReject=" + std::to_string(texturedClipRejectCount)
                    + " projReject=" + std::to_string(texturedProjectionRejectCount)
                    + " cullReject=" + std::to_string(texturedCullRejectCount)
                    + " emitted=" + std::to_string(texturedEmittedTriangleCount)
                    + " submitted=" + std::to_string(SubmittedTriangleCount)
                    + " gifBytes=" + std::to_string(GifPacketBytes.size())
                    + " vifQwc=" + std::to_string(packetQwordCount)
                    + " vifBytes=" + std::to_string(static_cast<std::size_t>(packetQwordCount) * 16u)
                    + " phase=" + std::to_string(LastCompletedPhase)
                    + " texW=" + std::to_string(textureWidth)
                    + " texH=" + std::to_string(textureHeight)
                    + " filter=" + std::to_string(texture->Filter)
                    + " tbw=" + std::to_string(texture->TBW)
                    + " psm=" + std::to_string(texture->PSM));
            }

            static std::uint32_t texturedPacketDumpCount = 0u;
            if (texturedPacketDumpCount < 2u && !GifPacketBytes.empty()) {
                texturedPacketDumpCount++;
                AppendDiagnosticToHostBootLog(
                    std::string("[helengine-ps2] textured-vu gif qwords")
                    + " texture=" + batch.Material->GetTextureRelativePath()
                    + FormatGifTemplateQwords(GifPacketBytes.data(), GifPacketBytes.size() / 16u));
            }
        }
        LastCompletedPhase = 11;
    }

    void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(
        const std::vector<const Ps2VuOpaqueBatch*>& batches,
        const std::vector<::float4x4>& worlds,
        const ::float4x4& view,
        const ::float4x4& projection,
        const ::float4& viewport,
        float nearPlaneDistance,
        const ::float3& lightDirection,
        GSGLOBAL* gsGlobal,
        const std::vector<GSTEXTURE*>& textures,
        const std::vector<int>& textureWidths,
        const std::vector<int>& textureHeights) {
        if (batches.size() != worlds.size()
            || batches.size() != textures.size()
            || batches.size() != textureWidths.size()
            || batches.size() != textureHeights.size()) {
            throw std::invalid_argument("PS2 textured VU batch aggregation requires aligned batch, world, and texture inputs.");
        }

        if (batches.empty()) {
            return;
        }

        LastCompletedPhase = 1;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return;
        }

        const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
        std::size_t texturedTriangleCapacity = 0u;
        for (const Ps2VuOpaqueBatch* batch : batches) {
            if (batch == nullptr || batch->Model == nullptr) {
                continue;
            }

            texturedTriangleCapacity += static_cast<std::size_t>(batch->Model->GetTriangleVertexCount()) / 3u;
        }

        std::vector<std::array<std::uint64_t, TexturedTrianglePacketWordCount>> texturedTrianglePackets;
        texturedTrianglePackets.reserve(texturedTriangleCapacity);
        std::clock_t accumulatedTriangleLightingTicks = 0;
        std::clock_t accumulatedTrianglePayloadFillTicks = 0;
        const std::clock_t triangleSetupStartTicks = std::clock();
        for (std::size_t batchIndex = 0; batchIndex < batches.size(); batchIndex++) {
            const Ps2VuOpaqueBatch* batch = batches[batchIndex];
            if (batch == nullptr || batch->Model == nullptr || batch->Material == nullptr) {
                continue;
            }

            GSTEXTURE* texture = textures[batchIndex];
            const int textureWidth = textureWidths[batchIndex];
            const int textureHeight = textureHeights[batchIndex];
            const bool textured = batch->Textured && texture != nullptr && textureWidth > 0 && textureHeight > 0;
            if (!textured) {
                continue;
            }

            gsKit_set_texfilter(gsGlobal, texture->Filter);
            const ::float4x4& world = worlds[batchIndex];
            const std::uint32_t triangleVertexCount = batch->Model->GetTriangleVertexCount();
            if (triangleVertexCount == 0u) {
                continue;
            }

            std::vector<Ps2VuTexturedClipVertex> clippedTexturedVertices;
            clippedTexturedVertices.reserve(4u);
            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4 worldViewMatrix;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch->Material, lightingConstants);
            const Ps2RuntimeModel* runtimeModel = batch->Proxy != nullptr ? batch->Proxy->GetModel() : nullptr;
            const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
            const std::vector<::float3>* runtimeNormals = runtimeModel != nullptr ? &runtimeModel->GetNormals() : nullptr;
            const std::vector<::float2>* runtimeTexCoords = runtimeModel != nullptr ? &runtimeModel->GetTexCoords() : nullptr;
            const float* packedPositionWords = reinterpret_cast<const float*>(batch->Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch->Model->GetNormalBlockBytes());
            const float* packedTexCoordWords = reinterpret_cast<const float*>(batch->Model->GetTexCoordBlockBytes());
            for (std::uint32_t vertexIndex = 0; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
                std::clock_t trianglePrepStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    trianglePrepStartTicks = std::clock();
                }

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
                const ::float4 faceNormal4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f);
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(faceNormal4, world),
                    ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 sourceTriangleNormal = NormalizeOrFallback(
                    ::float3(
                        sourceNormalA.X + sourceNormalB.X + sourceNormalC.X,
                        sourceNormalA.Y + sourceNormalB.Y + sourceNormalC.Y,
                        sourceNormalA.Z + sourceNormalB.Z + sourceNormalC.Z),
                    faceNormal);
                const ::float3 triangleWorldNormal = NormalizeOrFallback(
                    TransformPosition(::float4(sourceTriangleNormal.X, sourceTriangleNormal.Y, sourceTriangleNormal.Z, 0.0f), world),
                    worldFaceNormal);
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t trianglePrepEndTicks = std::clock();
                    LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);
                }

                std::clock_t triangleEmitStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    triangleEmitStartTicks = std::clock();
                }

                const ::float3 viewPositionA = TransformPosition(positionA, worldViewMatrix);
                const ::float3 viewPositionB = TransformPosition(positionB, worldViewMatrix);
                const ::float3 viewPositionC = TransformPosition(positionC, worldViewMatrix);
                const bool texturedVertexAInside = IsTexturedVertexInsideNearPlane(viewPositionA, nearPlaneDistance);
                const bool texturedVertexBInside = IsTexturedVertexInsideNearPlane(viewPositionB, nearPlaneDistance);
                const bool texturedVertexCInside = IsTexturedVertexInsideNearPlane(viewPositionC, nearPlaneDistance);
                const ::float2 sourceTexCoordA = runtimeTexCoords != nullptr && sourceIndexA < runtimeTexCoords->size()
                    ? (*runtimeTexCoords)[sourceIndexA]
                    : ::float2(packedTexCoordWords[positionWordIndexA + 0u], packedTexCoordWords[positionWordIndexA + 1u]);
                const ::float2 sourceTexCoordB = runtimeTexCoords != nullptr && sourceIndexB < runtimeTexCoords->size()
                    ? (*runtimeTexCoords)[sourceIndexB]
                    : ::float2(packedTexCoordWords[positionWordIndexB + 0u], packedTexCoordWords[positionWordIndexB + 1u]);
                const ::float2 sourceTexCoordC = runtimeTexCoords != nullptr && sourceIndexC < runtimeTexCoords->size()
                    ? (*runtimeTexCoords)[sourceIndexC]
                    : ::float2(packedTexCoordWords[positionWordIndexC + 0u], packedTexCoordWords[positionWordIndexC + 1u]);
                const Ps2VuTexturedClipVertex texturedVertexA { viewPositionA, sourceTexCoordA };
                const Ps2VuTexturedClipVertex texturedVertexB { viewPositionB, sourceTexCoordB };
                const Ps2VuTexturedClipVertex texturedVertexC { viewPositionC, sourceTexCoordC };
                if (texturedVertexAInside && texturedVertexBInside && texturedVertexCInside) {
                    clippedTexturedVertices.clear();
                    clippedTexturedVertices.push_back(texturedVertexA);
                    clippedTexturedVertices.push_back(texturedVertexB);
                    clippedTexturedVertices.push_back(texturedVertexC);
                } else if (!texturedVertexAInside && !texturedVertexBInside && !texturedVertexCInside) {
                    clippedTexturedVertices.clear();
                } else {
                    ClipTexturedTriangleAgainstNearPlane(texturedVertexA, texturedVertexB, texturedVertexC, nearPlaneDistance, clippedTexturedVertices);
                }

                const std::size_t clippedVertexCount = clippedTexturedVertices.size();
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
                    std::uint64_t positionARegister = 0u;
                    std::uint64_t positionBRegister = 0u;
                    std::uint64_t positionCRegister = 0u;
                    const ::float3& clippedPositionA = clippedTexturedVertices[0].ViewPosition;
                    const ::float3& clippedPositionB = clippedTexturedVertices[clippedIndex].ViewPosition;
                    const ::float3& clippedPositionC = clippedTexturedVertices[clippedIndex + 1u].ViewPosition;
                    if (!TryBuildVertexPositionRegister(clippedPositionA, projection, viewport, gsGlobal, screenAX, screenAY, screenAZ, positionARegister)
                        || !TryBuildVertexPositionRegister(clippedPositionB, projection, viewport, gsGlobal, screenBX, screenBY, screenBZ, positionBRegister)
                        || !TryBuildVertexPositionRegister(clippedPositionC, projection, viewport, gsGlobal, screenCX, screenCY, screenCZ, positionCRegister)) {
                        continue;
                    }

                    const std::clock_t triangleLightingStartTicks = std::clock();
                    const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                        ? GS_SETREG_RGBAQ(
                            batch->Material->GetBaseColorR(),
                            batch->Material->GetBaseColorG(),
                            batch->Material->GetBaseColorB(),
                            batch->Material->GetBaseColorA(),
                            0x00)
                        : ResolveTexturedVertexColor(lightingConstants, triangleWorldNormal, normalizedLightDirection);
                    const std::clock_t triangleLightingEndTicks = std::clock();
                    accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);

                    const std::clock_t trianglePayloadFillStartTicks = std::clock();
                    texturedTrianglePackets.push_back(
                        BuildTexturedTriangleGifPacketBytes(
                            gsGlobal,
                            texture,
                            textureWidth,
                            textureHeight,
                            triangleColor,
                            clippedTexturedVertices[0],
                            clippedTexturedVertices[clippedIndex],
                            clippedTexturedVertices[clippedIndex + 1u],
                            screenAX,
                            screenAY,
                            screenAZ,
                            positionARegister,
                            screenBX,
                            screenBY,
                            screenBZ,
                            positionBRegister,
                            screenCX,
                            screenCY,
                            screenCZ,
                            positionCRegister));
                    const std::clock_t trianglePayloadFillEndTicks = std::clock();
                    accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);

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

                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t triangleEmitEndTicks = std::clock();
                    LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                }
            }
        }

        LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);
        LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);
        const std::clock_t triangleSetupEndTicks = std::clock();
        LastTriangleSetupMilliseconds = ResolveMillisecondsFromClockTicks(triangleSetupStartTicks, triangleSetupEndTicks);
        if (texturedTrianglePackets.empty()) {
            return;
        }

        GifPacketBytes.resize(TexturedTrianglePacketByteCount);
        std::memcpy(GifPacketBytes.data(), texturedTrianglePackets.front().data(), TexturedTrianglePacketByteCount);
        const std::uint32_t emittedTriangleCount = static_cast<std::uint32_t>(texturedTrianglePackets.size());
        std::uint32_t maxPacketQwordCount = std::max<std::uint32_t>(
            1024u,
            MinimumVifPacketOverheadQwords + (emittedTriangleCount * 24u));
        if (maxPacketQwordCount > 0xFFFFu) {
            throw std::runtime_error("PS2 textured VU aggregate packet exceeds packet2 qword capacity.");
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

        const std::clock_t packetAssemblyStartTicks = std::clock();
        for (const std::array<std::uint64_t, TexturedTrianglePacketWordCount>& trianglePacketWords : texturedTrianglePackets) {
            packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
            std::memcpy(packet.get()->next, trianglePacketWords.data(), TexturedTrianglePacketByteCount);
            packet2_advance_next(packet.get(), TexturedTrianglePacketByteCount);
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
        const std::clock_t packetAssemblyEndTicks = std::clock();
        LastPacketAssemblyMilliseconds = ResolveMillisecondsFromClockTicks(packetAssemblyStartTicks, packetAssemblyEndTicks);
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

    double Ps2VuVifPacketBuilder::GetLastTriangleSetupMilliseconds() const {
        return LastTriangleSetupMilliseconds;
    }

    double Ps2VuVifPacketBuilder::GetLastPacketAssemblyMilliseconds() const {
        return LastPacketAssemblyMilliseconds;
    }

    double Ps2VuVifPacketBuilder::GetLastTrianglePrepMilliseconds() const {
        return LastTrianglePrepMilliseconds;
    }

    double Ps2VuVifPacketBuilder::GetLastTriangleEmitMilliseconds() const {
        return LastTriangleEmitMilliseconds;
    }

    double Ps2VuVifPacketBuilder::GetLastTriangleLightingMilliseconds() const {
        return LastTriangleLightingMilliseconds;
    }

    double Ps2VuVifPacketBuilder::GetLastTrianglePayloadFillMilliseconds() const {
        return LastTrianglePayloadFillMilliseconds;
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
