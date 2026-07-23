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
#include <gsInline.h>
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
        constexpr std::uint16_t MaximumOpaqueUntexturedPacketQwords = 4096u;
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
        constexpr std::size_t UntexturedTriangleDirectGifPacketWordCount = 18u;
        constexpr std::size_t UntexturedTriangleDirectGifPacketByteCount = UntexturedTriangleDirectGifPacketWordCount * sizeof(std::uint64_t);
        constexpr std::size_t TexturedTrianglePacketWordCount = 22u;
        constexpr std::size_t TexturedTrianglePacketByteCount = TexturedTrianglePacketWordCount * sizeof(std::uint64_t);
        constexpr std::size_t MaximumTexturedVuSourceTriangleCount = 12u;
        constexpr std::size_t UntexturedTriangleSourceQwordCount = 3u;
        constexpr std::size_t UntexturedTriangleGifPacketQwordOffset = UntexturedTriangleSourceQwordCount;
        constexpr std::size_t UntexturedTriangleRecordQwordCount = UntexturedTriangleGifPacketQwordOffset + TriangleGifPacketTemplateQwordCount;
        constexpr std::size_t UntexturedTriangleSharedStateQwordOffset = UntexturedTriangleRecordQwordCount;
        constexpr std::uint16_t UntexturedMicroProgramAddress = 0u;
        constexpr std::uint16_t TexturedMicroProgramAddress = 64u;
        constexpr std::uint64_t UntexturedTriangleRegisterList = static_cast<std::uint64_t>(GIF_REG_RGBAQ) << 0u
            | (static_cast<std::uint64_t>(GIF_REG_XYZ2) << 4u);
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

        float ResolveProjectionClipW(const ::float3& viewPosition, const ::float4x4& projection) {
            return (viewPosition.X * projection.M14)
                + (viewPosition.Y * projection.M24)
                + (viewPosition.Z * projection.M34)
                + projection.M44;
        }

        std::uint64_t BuildPerspectiveTextureRegisterList(int context) {
            return (static_cast<std::uint64_t>(GS_TEX0_1 + context) << 0u)
                | (static_cast<std::uint64_t>(GS_PRIM) << 4u)
                | (static_cast<std::uint64_t>(GS_RGBAQ) << 8u)
                | (static_cast<std::uint64_t>(GS_ST) << 12u)
                | (static_cast<std::uint64_t>(GS_XYZ2) << 16u)
                | (static_cast<std::uint64_t>(GS_RGBAQ) << 20u)
                | (static_cast<std::uint64_t>(GS_ST) << 24u)
                | (static_cast<std::uint64_t>(GS_XYZ2) << 28u)
                | (static_cast<std::uint64_t>(GS_RGBAQ) << 32u)
                | (static_cast<std::uint64_t>(GS_ST) << 36u)
                | (static_cast<std::uint64_t>(GS_XYZ2) << 40u)
                | (static_cast<std::uint64_t>(GIF_NOP) << 44u);
        }

        constexpr std::uint64_t UntexturedTriangleDirectGifRegisterList = (static_cast<std::uint64_t>(GS_PRIM) << 0u)
            | (static_cast<std::uint64_t>(GS_RGBAQ) << 4u)
            | (static_cast<std::uint64_t>(GS_XYZ2) << 8u)
            | (static_cast<std::uint64_t>(GS_RGBAQ) << 12u)
            | (static_cast<std::uint64_t>(GS_XYZ2) << 16u)
            | (static_cast<std::uint64_t>(GS_RGBAQ) << 20u)
            | (static_cast<std::uint64_t>(GS_XYZ2) << 24u)
            | (static_cast<std::uint64_t>(GIF_NOP) << 28u);

        float ResolvePerspectiveTextureReciprocalW(const ::float3& viewPosition, const ::float4x4& projection) {
            const float clipW = ResolveProjectionClipW(viewPosition, projection);
            if (clipW <= MinimumClipW) {
                throw std::invalid_argument("PS2 VU textured GIF packet encoding requires a positive clip W.");
            }

            return 1.0f / clipW;
        }

        std::uint64_t BuildPerspectiveTextureCoordinateRegister(const ::float2& texCoord, float reciprocalClipW) {
            const gs_stq coordinate = vertex_to_STQ(texCoord.X * reciprocalClipW, texCoord.Y * reciprocalClipW);
            return coordinate.st.st;
        }

        std::uint64_t BuildPerspectiveTextureColorRegister(std::uint64_t triangleColor, float reciprocalClipW) {
            return rgba_to_RGBAQ(static_cast<std::uint32_t>(triangleColor), reciprocalClipW).color.rgbaq;
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

        struct Ps2VuGifTemplateCacheEntry final {
            std::uint32_t FlatColorKey = 0u;
            std::array<std::uint8_t, TriangleGifPacketTemplateByteCount> GifPacketTemplate {};
        };

        struct Ps2VuTexturedClipVertex final {
            ::float3 ViewPosition = ::float3::get_Zero();
            ::float2 TexCoord = ::float2(0.0f, 0.0f);
        };

        struct Ps2VuUntexturedClipVertex final {
            ::float3 ViewPosition = ::float3::get_Zero();
        };

        enum class Ps2VuScreenFrustumPlane {
            Left,
            Right,
            Bottom,
            Top
        };

        struct Ps2VuFlatColor final {
            std::uint8_t Red = 0xFF;
            std::uint8_t Green = 0xFF;
            std::uint8_t Blue = 0xFF;
            std::uint8_t Alpha = 0x80;
        };

        /// <summary>
        /// Supplies one local-space textured triangle to the VU1 perspective transform path.
        /// </summary>
        struct alignas(16) Ps2VuTexturedSourceTriangle final {
            float PositionA[4];
            float PositionB[4];
            float PositionC[4];
            float TexCoordA[4];
            float TexCoordB[4];
            float TexCoordC[4];
        };

        /// <summary>
        /// Supplies matrix, GS, material, and GIF state shared by one bounded VU1 textured batch.
        /// </summary>
        struct alignas(16) Ps2VuTexturedSharedState final {
            float WorldViewProjectionMatrix[16];
            float GsScale[4];
            float GsOffset[4];
            float FlatColor[4];
            std::uint32_t TriangleCount[4];
            Ps2VuGifQword StateTemplate[6];
        };

        struct alignas(16) Ps2VuUntexturedTrianglePayload final {
            Ps2VuUntexturedTriangleRecord TriangleRecord;
            Ps2VuUntexturedSharedState SharedState;
            Ps2VuFlatColor DirectGifFlatColor;
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
        static_assert((sizeof(Ps2VuTexturedSourceTriangle) % 16u) == 0u);
        static_assert((sizeof(Ps2VuTexturedSharedState) % 16u) == 0u);
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
            const ::float4x4& worldViewProjection,
            const ::float4& viewport,
            Ps2VuUntexturedSharedState& sharedState) {
            CopyMatrixWords(worldViewProjection, sharedState.WorldViewProjectionMatrix);
            sharedState.GsScale[0] = viewport.Z * 0.5f;
            sharedState.GsScale[1] = viewport.W * -0.5f;
            sharedState.GsScale[2] = -4194304.0f;
            sharedState.GsScale[3] = 0.0f;
            sharedState.GsOffset[0] = 2048.0f + viewport.X + (viewport.Z * 0.5f);
            sharedState.GsOffset[1] = 2048.0f + viewport.Y + (viewport.W * 0.5f);
            sharedState.GsOffset[2] = 4194304.0f;
            sharedState.GsOffset[3] = 0.0f;
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
            PopulateUntexturedSharedState(worldViewProjectionMatrix, viewport, sharedState);
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

        Ps2VuUntexturedClipVertex InterpolateUntexturedClipVertex(
            const Ps2VuUntexturedClipVertex& start,
            const Ps2VuUntexturedClipVertex& end,
            float amount) {
            Ps2VuUntexturedClipVertex vertex;
            vertex.ViewPosition = InterpolateViewPosition(start.ViewPosition, end.ViewPosition, amount);
            return vertex;
        }

        void ClipUntexturedTriangleAgainstNearPlane(
            const Ps2VuUntexturedClipVertex& first,
            const Ps2VuUntexturedClipVertex& second,
            const Ps2VuUntexturedClipVertex& third,
            float nearPlaneDistance,
            std::vector<Ps2VuUntexturedClipVertex>& clippedVertices) {
            clippedVertices.clear();

            const float nearPlaneZ = -nearPlaneDistance;
            Ps2VuUntexturedClipVertex previous = third;
            bool previousInside = previous.ViewPosition.Z <= nearPlaneZ;
            const Ps2VuUntexturedClipVertex vertices[3] = { first, second, third };

            for (const Ps2VuUntexturedClipVertex& current : vertices) {
                const bool currentInside = current.ViewPosition.Z <= nearPlaneZ;
                if (currentInside != previousInside) {
                    const float denominator = current.ViewPosition.Z - previous.ViewPosition.Z;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = (nearPlaneZ - previous.ViewPosition.Z) / denominator;
                        clippedVertices.push_back(InterpolateUntexturedClipVertex(previous, current, amount));
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

        bool IsUntexturedVertexInsideNearPlane(const ::float3& viewPosition, float nearPlaneDistance) {
            return viewPosition.Z <= -nearPlaneDistance;
        }

        float ResolveUntexturedScreenFrustumPlaneDistance(
            const Ps2VuUntexturedClipVertex& vertex,
            const ::float4x4& projection,
            Ps2VuScreenFrustumPlane plane) {
            const float clipX = (vertex.ViewPosition.X * projection.M11)
                + (vertex.ViewPosition.Y * projection.M21)
                + (vertex.ViewPosition.Z * projection.M31)
                + projection.M41;
            const float clipY = (vertex.ViewPosition.X * projection.M12)
                + (vertex.ViewPosition.Y * projection.M22)
                + (vertex.ViewPosition.Z * projection.M32)
                + projection.M42;
            const float clipW = (vertex.ViewPosition.X * projection.M14)
                + (vertex.ViewPosition.Y * projection.M24)
                + (vertex.ViewPosition.Z * projection.M34)
                + projection.M44;

            if (plane == Ps2VuScreenFrustumPlane::Left) {
                return clipX + clipW;
            } else if (plane == Ps2VuScreenFrustumPlane::Right) {
                return clipW - clipX;
            } else if (plane == Ps2VuScreenFrustumPlane::Bottom) {
                return clipY + clipW;
            }

            return clipW - clipY;
        }

        void ClipUntexturedPolygonAgainstScreenFrustumPlane(
            const std::vector<Ps2VuUntexturedClipVertex>& inputVertices,
            const ::float4x4& projection,
            Ps2VuScreenFrustumPlane plane,
            std::vector<Ps2VuUntexturedClipVertex>& outputVertices) {
            outputVertices.clear();
            if (inputVertices.empty()) {
                return;
            }

            Ps2VuUntexturedClipVertex previous = inputVertices.back();
            float previousDistance = ResolveUntexturedScreenFrustumPlaneDistance(previous, projection, plane);
            bool previousInside = previousDistance >= 0.0f;
            for (const Ps2VuUntexturedClipVertex& current : inputVertices) {
                const float currentDistance = ResolveUntexturedScreenFrustumPlaneDistance(current, projection, plane);
                const bool currentInside = currentDistance >= 0.0f;
                if (currentInside != previousInside) {
                    const float denominator = currentDistance - previousDistance;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = -previousDistance / denominator;
                        outputVertices.push_back(InterpolateUntexturedClipVertex(previous, current, amount));
                    }
                }

                if (currentInside) {
                    outputVertices.push_back(current);
                }

                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
        }

        bool IsUntexturedVertexInsideScreenFrustum(
            const Ps2VuUntexturedClipVertex& vertex,
            float nearPlaneDistance,
            const ::float4x4& projection) {
            if (!IsUntexturedVertexInsideNearPlane(vertex.ViewPosition, nearPlaneDistance)) {
                return false;
            }

            return ResolveUntexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Left) >= 0.0f
                && ResolveUntexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Right) >= 0.0f
                && ResolveUntexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Bottom) >= 0.0f
                && ResolveUntexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Top) >= 0.0f;
        }

        void ClipUntexturedTriangleAgainstScreenFrustum(
            const Ps2VuUntexturedClipVertex& first,
            const Ps2VuUntexturedClipVertex& second,
            const Ps2VuUntexturedClipVertex& third,
            float nearPlaneDistance,
            const ::float4x4& projection,
            std::vector<Ps2VuUntexturedClipVertex>& clippedVertices) {
            std::vector<Ps2VuUntexturedClipVertex> inputVertices;
            inputVertices.reserve(8u);
            ClipUntexturedTriangleAgainstNearPlane(first, second, third, nearPlaneDistance, inputVertices);

            std::vector<Ps2VuUntexturedClipVertex> outputVertices;
            outputVertices.reserve(8u);
            const Ps2VuScreenFrustumPlane planes[] = {
                Ps2VuScreenFrustumPlane::Left,
                Ps2VuScreenFrustumPlane::Right,
                Ps2VuScreenFrustumPlane::Bottom,
                Ps2VuScreenFrustumPlane::Top
            };
            for (Ps2VuScreenFrustumPlane plane : planes) {
                ClipUntexturedPolygonAgainstScreenFrustumPlane(inputVertices, projection, plane, outputVertices);
                inputVertices.swap(outputVertices);
                if (inputVertices.empty()) {
                    break;
                }
            }

            clippedVertices.swap(inputVertices);
        }

        float ResolveTexturedScreenFrustumPlaneDistance(
            const Ps2VuTexturedClipVertex& vertex,
            const ::float4x4& projection,
            Ps2VuScreenFrustumPlane plane) {
            const Ps2VuUntexturedClipVertex untexturedVertex { vertex.ViewPosition };
            return ResolveUntexturedScreenFrustumPlaneDistance(untexturedVertex, projection, plane);
        }

        void ClipTexturedPolygonAgainstScreenFrustumPlane(
            const std::vector<Ps2VuTexturedClipVertex>& inputVertices,
            const ::float4x4& projection,
            Ps2VuScreenFrustumPlane plane,
            std::vector<Ps2VuTexturedClipVertex>& outputVertices) {
            outputVertices.clear();
            if (inputVertices.empty()) {
                return;
            }

            Ps2VuTexturedClipVertex previous = inputVertices.back();
            float previousDistance = ResolveTexturedScreenFrustumPlaneDistance(previous, projection, plane);
            bool previousInside = previousDistance >= 0.0f;
            for (const Ps2VuTexturedClipVertex& current : inputVertices) {
                const float currentDistance = ResolveTexturedScreenFrustumPlaneDistance(current, projection, plane);
                const bool currentInside = currentDistance >= 0.0f;
                if (currentInside != previousInside) {
                    const float denominator = currentDistance - previousDistance;
                    if (std::abs(denominator) > MinimumNearPlaneEpsilon) {
                        const float amount = -previousDistance / denominator;
                        outputVertices.push_back(InterpolateTexturedClipVertex(previous, current, amount));
                    }
                }

                if (currentInside) {
                    outputVertices.push_back(current);
                }

                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
        }

        bool IsTexturedVertexInsideScreenFrustum(
            const Ps2VuTexturedClipVertex& vertex,
            float nearPlaneDistance,
            const ::float4x4& projection) {
            if (!IsTexturedVertexInsideNearPlane(vertex.ViewPosition, nearPlaneDistance)) {
                return false;
            }

            return ResolveTexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Left) >= 0.0f
                && ResolveTexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Right) >= 0.0f
                && ResolveTexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Bottom) >= 0.0f
                && ResolveTexturedScreenFrustumPlaneDistance(vertex, projection, Ps2VuScreenFrustumPlane::Top) >= 0.0f;
        }

        void ClipTexturedTriangleAgainstScreenFrustum(
            const Ps2VuTexturedClipVertex& first,
            const Ps2VuTexturedClipVertex& second,
            const Ps2VuTexturedClipVertex& third,
            float nearPlaneDistance,
            const ::float4x4& projection,
            std::vector<Ps2VuTexturedClipVertex>& clippedVertices) {
            std::vector<Ps2VuTexturedClipVertex> inputVertices;
            inputVertices.reserve(8u);
            ClipTexturedTriangleAgainstNearPlane(first, second, third, nearPlaneDistance, inputVertices);

            std::vector<Ps2VuTexturedClipVertex> outputVertices;
            outputVertices.reserve(8u);
            const Ps2VuScreenFrustumPlane planes[] = {
                Ps2VuScreenFrustumPlane::Left,
                Ps2VuScreenFrustumPlane::Right,
                Ps2VuScreenFrustumPlane::Bottom,
                Ps2VuScreenFrustumPlane::Top
            };
            for (Ps2VuScreenFrustumPlane plane : planes) {
                ClipTexturedPolygonAgainstScreenFrustumPlane(inputVertices, projection, plane, outputVertices);
                inputVertices.swap(outputVertices);
                if (inputVertices.empty()) {
                    break;
                }
            }

            clippedVertices.swap(inputVertices);
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

        bool TryClassifyAndBuildTexturedVertexPositionRegister(
            const Ps2VuTexturedClipVertex& vertex,
            float nearPlaneDistance,
            const ::float4x4& projection,
            const ::float4& viewport,
            GSGLOBAL* gsGlobal,
            bool& isInsideScreenFrustum,
            float& reciprocalClipW,
            float& screenX,
            float& screenY,
            float& screenZ,
            std::uint64_t& positionRegister) {
            const ::float3& viewPosition = vertex.ViewPosition;
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
            isInsideScreenFrustum = viewPosition.Z <= -nearPlaneDistance
                && (clipX + clipW) >= 0.0f
                && (clipW - clipX) >= 0.0f
                && (clipY + clipW) >= 0.0f
                && (clipW - clipY) >= 0.0f;
            if (clipW <= MinimumClipW) {
                isInsideScreenFrustum = false;
                reciprocalClipW = 0.0f;
                return false;
            }

            reciprocalClipW = 1.0f / clipW;
            const float normalizedX = clipX * reciprocalClipW;
            const float normalizedY = clipY * reciprocalClipW;
            const float normalizedZ = clipZ * reciprocalClipW;
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

        bool BuildUntexturedTriangleGifPacketBytes(
            const Ps2VuUntexturedTrianglePayload& trianglePayload,
            const ::float4x4& projection,
            const ::float4& viewport,
            GSGLOBAL* gsGlobal,
            std::array<std::uint64_t, UntexturedTriangleDirectGifPacketWordCount>& packetWords) {
            const Ps2VuOpaqueSourceTriangle& sourceTriangle = trianglePayload.TriangleRecord.SourceTriangle;
            const ::float3 positionA(sourceTriangle.PositionA[0], sourceTriangle.PositionA[1], sourceTriangle.PositionA[2]);
            const ::float3 positionB(sourceTriangle.PositionB[0], sourceTriangle.PositionB[1], sourceTriangle.PositionB[2]);
            const ::float3 positionC(sourceTriangle.PositionC[0], sourceTriangle.PositionC[1], sourceTriangle.PositionC[2]);
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
            if (!TryBuildVertexPositionRegister(positionA, projection, viewport, gsGlobal, screenAX, screenAY, screenAZ, positionARegister)
                || !TryBuildVertexPositionRegister(positionB, projection, viewport, gsGlobal, screenBX, screenBY, screenBZ, positionBRegister)
                || !TryBuildVertexPositionRegister(positionC, projection, viewport, gsGlobal, screenCX, screenCY, screenCZ, positionCRegister)) {
                return false;
            }

            const Ps2VuFlatColor& flatColor = trianglePayload.DirectGifFlatColor;
            const std::uint64_t flatColorRegister = GS_SETREG_RGBAQ(
                flatColor.Red,
                flatColor.Green,
                flatColor.Blue,
                flatColor.Alpha,
                0);
            prim_t prim = {};
            prim.type = PRIM_TRIANGLE;
            prim.shading = PRIM_SHADE_FLAT;
            prim.mapping = 0;
            prim.fogging = 0;
            prim.blending = 0;
            prim.antialiasing = 0;
            prim.mapping_type = PRIM_MAP_ST;
            prim.colorfix = PRIM_UNFIXED;
            std::size_t packetWordIndex = 0u;
            packetWords[packetWordIndex++] = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);
            packetWords[packetWordIndex++] = GIF_REG_AD;
            packetWords[packetWordIndex++] = ResolveOpaqueUntexturedTestRegister(gsGlobal);
            packetWords[packetWordIndex++] = GS_REG_TEST;
            packetWords[packetWordIndex++] = GIF_SET_TAG(1, 1, 0, 0, GIF_FLG_REGLIST, 8);
            packetWords[packetWordIndex++] = UntexturedTriangleDirectGifRegisterList;
            packetWords[packetWordIndex++] = GS_SETREG_PRIM(
                GS_PRIM_PRIM_TRIANGLE,
                prim.shading,
                prim.mapping,
                gsGlobal->PrimFogEnable,
                gsGlobal->PrimAlphaEnable,
                gsGlobal->PrimAAEnable,
                0,
                gsGlobal->PrimContext,
                0);
            packetWords[packetWordIndex++] = flatColorRegister;
            packetWords[packetWordIndex++] = positionARegister;
            packetWords[packetWordIndex++] = flatColorRegister;
            packetWords[packetWordIndex++] = positionBRegister;
            packetWords[packetWordIndex++] = flatColorRegister;
            packetWords[packetWordIndex++] = positionCRegister;
            packetWords[packetWordIndex++] = 0u;
            return true;
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
            float reciprocalClipWA,
            float reciprocalClipWB,
            float reciprocalClipWC,
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
            prim.mapping_type = PRIM_MAP_ST;
            prim.colorfix = PRIM_UNFIXED;
            const std::uint64_t texturedTriangleGifTag = GIF_TAG_TRIANGLE_GORAUD_TEXTURED(1);
            const std::uint64_t texturedTriangleGifRegisters = BuildPerspectiveTextureRegisterList(gsGlobal->PrimContext);
            const std::uint64_t stqRegisterA = BuildPerspectiveTextureCoordinateRegister(vertexA.TexCoord, reciprocalClipWA);
            const std::uint64_t stqRegisterB = BuildPerspectiveTextureCoordinateRegister(vertexB.TexCoord, reciprocalClipWB);
            const std::uint64_t stqRegisterC = BuildPerspectiveTextureCoordinateRegister(vertexC.TexCoord, reciprocalClipWC);
            const std::uint64_t rgbaqRegisterA = BuildPerspectiveTextureColorRegister(triangleColor, reciprocalClipWA);
            const std::uint64_t rgbaqRegisterB = BuildPerspectiveTextureColorRegister(triangleColor, reciprocalClipWB);
            const std::uint64_t rgbaqRegisterC = BuildPerspectiveTextureColorRegister(triangleColor, reciprocalClipWC);
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
                0,
                gsGlobal->PrimContext,
                0);
            packetWords[packetWordIndex++] = rgbaqRegisterA;
            packetWords[packetWordIndex++] = stqRegisterA;
            packetWords[packetWordIndex++] = positionARegister;
            packetWords[packetWordIndex++] = rgbaqRegisterB;
            packetWords[packetWordIndex++] = stqRegisterB;
            packetWords[packetWordIndex++] = positionBRegister;
            packetWords[packetWordIndex++] = rgbaqRegisterC;
            packetWords[packetWordIndex++] = stqRegisterC;
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
        // - view-frustum crossings are clipped in view space before VU perspective division
        // - no CPU projection to XYZ2 for untextured triangles
        // - no CPU screen-space front-face rejection for untextured triangles
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
        Ps2VuUntexturedSharedState clippedSharedStateTemplate {};
        ::float4x4 worldViewMatrix;
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
            payload.DirectGifFlatColor = flatColor;
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
            PopulateUntexturedSharedState(projection, viewport, clippedSharedStateTemplate);
            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch.Material, lightingConstants);
            std::vector<Ps2VuUntexturedClipVertex> clippedUntexturedVertices;
            clippedUntexturedVertices.reserve(4u);
            std::clock_t accumulatedTriangleLightingTicks = 0;
            std::clock_t accumulatedTrianglePayloadFillTicks = 0;
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
                const ::float3 faceNormal(
                    ::float3(
                        packedNormalA.X + packedNormalB.X + packedNormalC.X,
                        packedNormalA.Y + packedNormalB.Y + packedNormalC.Y,
                        packedNormalA.Z + packedNormalB.Z + packedNormalC.Z));
                const ::float3 worldFaceNormal = NormalizeOrFallback(
                    TransformPosition(::float4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f), world),
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
                const ::float3 viewPositionA = TransformPosition(positionA, worldViewMatrix);
                const ::float3 viewPositionB = TransformPosition(positionB, worldViewMatrix);
                const ::float3 viewPositionC = TransformPosition(positionC, worldViewMatrix);
                const Ps2VuUntexturedClipVertex untexturedVertexA { viewPositionA };
                const Ps2VuUntexturedClipVertex untexturedVertexB { viewPositionB };
                const Ps2VuUntexturedClipVertex untexturedVertexC { viewPositionC };
                const bool untexturedVertexAInside = IsUntexturedVertexInsideScreenFrustum(untexturedVertexA, nearPlaneDistance, projection);
                const bool untexturedVertexBInside = IsUntexturedVertexInsideScreenFrustum(untexturedVertexB, nearPlaneDistance, projection);
                const bool untexturedVertexCInside = IsUntexturedVertexInsideScreenFrustum(untexturedVertexC, nearPlaneDistance, projection);
                const bool triangleCrossesFrustumBoundary = !(untexturedVertexAInside && untexturedVertexBInside && untexturedVertexCInside);
                if (!triangleCrossesFrustumBoundary) {
                    clippedUntexturedVertices.clear();
                } else {
                    ClipUntexturedTriangleAgainstScreenFrustum(untexturedVertexA, untexturedVertexB, untexturedVertexC, nearPlaneDistance, projection, clippedUntexturedVertices);
                }

                if (triangleCrossesFrustumBoundary && clippedUntexturedVertices.size() < 3u) {
                    continue;
                }
                std::clock_t triangleEmitStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t trianglePrepEndTicks = std::clock();
                    LastTrianglePrepMilliseconds += ResolveMillisecondsFromClockTicks(trianglePrepStartTicks, trianglePrepEndTicks);
                    triangleEmitStartTicks = std::clock();
                }

                std::clock_t triangleLightingStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    triangleLightingStartTicks = std::clock();
                }
                const std::uint64_t triangleColor = ResolveTexturedVertexColor(lightingConstants, worldFaceNormal, normalizedLightDirection);
                Ps2VuFlatColor flatColor {};
                flatColor.Red = static_cast<std::uint8_t>(triangleColor & 0xFFu);
                flatColor.Green = static_cast<std::uint8_t>((triangleColor >> 8u) & 0xFFu);
                flatColor.Blue = static_cast<std::uint8_t>((triangleColor >> 16u) & 0xFFu);
                flatColor.Alpha = static_cast<std::uint8_t>((triangleColor >> 24u) & 0xFFu);
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t triangleLightingEndTicks = std::clock();
                    accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);
                }

                std::clock_t trianglePayloadFillStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    trianglePayloadFillStartTicks = std::clock();
                }
                const std::size_t emittedTriangleCount = triangleCrossesFrustumBoundary ? clippedUntexturedVertices.size() - 2u : 1u;
                for (std::size_t clippedIndex = 0u; clippedIndex < emittedTriangleCount; clippedIndex++) {
                    const ::float3& emittedPositionA = triangleCrossesFrustumBoundary ? clippedUntexturedVertices[0u].ViewPosition : packedPositionA;
                    const ::float3& emittedPositionB = triangleCrossesFrustumBoundary ? clippedUntexturedVertices[clippedIndex + 1u].ViewPosition : packedPositionB;
                    const ::float3& emittedPositionC = triangleCrossesFrustumBoundary ? clippedUntexturedVertices[clippedIndex + 2u].ViewPosition : packedPositionC;
                    Ps2VuUntexturedTrianglePayload payload {};
                    CopyCachedTriangleGifPacketTemplate(batch, flatColor, gsGlobal, gifTemplateCache, payload.TriangleRecord);
                    payload.DirectGifFlatColor = flatColor;
                    const Ps2VuUntexturedSharedState& emittedSharedState = triangleCrossesFrustumBoundary ? clippedSharedStateTemplate : sharedStateTemplate;
                    std::memcpy(&payload.SharedState, &emittedSharedState, sizeof(emittedSharedState));
                    payload.TriangleRecord.SourceTriangle.PositionA[0] = emittedPositionA.X;
                    payload.TriangleRecord.SourceTriangle.PositionA[1] = emittedPositionA.Y;
                    payload.TriangleRecord.SourceTriangle.PositionA[2] = emittedPositionA.Z;
                    payload.TriangleRecord.SourceTriangle.PositionA[3] = 1.0f;
                    payload.TriangleRecord.SourceTriangle.PositionB[0] = emittedPositionB.X;
                    payload.TriangleRecord.SourceTriangle.PositionB[1] = emittedPositionB.Y;
                    payload.TriangleRecord.SourceTriangle.PositionB[2] = emittedPositionB.Z;
                    payload.TriangleRecord.SourceTriangle.PositionB[3] = 1.0f;
                    payload.TriangleRecord.SourceTriangle.PositionC[0] = emittedPositionC.X;
                    payload.TriangleRecord.SourceTriangle.PositionC[1] = emittedPositionC.Y;
                    payload.TriangleRecord.SourceTriangle.PositionC[2] = emittedPositionC.Z;
                    payload.TriangleRecord.SourceTriangle.PositionC[3] = 1.0f;
                    untexturedTrianglePayloads.push_back(payload);
                }
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t trianglePayloadFillEndTicks = std::clock();
                    accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);
                }
                SubmittedTriangleCount += emittedTriangleCount;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    const std::clock_t triangleEmitEndTicks = std::clock();
                    LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                }
                if (EnableVuSingleDispatchDiagnostic) {
                    break;
                }
            }
            if (EnableVuPerTriangleTimingDiagnostics) {
                LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);
                LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);
            }
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
                const ::float3 faceNormal(
                    ::float3(
                        packedNormalA.X + packedNormalB.X + packedNormalC.X,
                        packedNormalA.Y + packedNormalB.Y + packedNormalC.Y,
                        packedNormalA.Z + packedNormalB.Z + packedNormalC.Z));
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
                    const bool texturedVertexAInside = IsTexturedVertexInsideScreenFrustum(texturedVertexA, nearPlaneDistance, projection);
                    const bool texturedVertexBInside = IsTexturedVertexInsideScreenFrustum(texturedVertexB, nearPlaneDistance, projection);
                    const bool texturedVertexCInside = IsTexturedVertexInsideScreenFrustum(texturedVertexC, nearPlaneDistance, projection);
                    if (texturedVertexAInside && texturedVertexBInside && texturedVertexCInside) {
                        clippedTexturedVertices.clear();
                        clippedTexturedVertices.push_back(texturedVertexA);
                        clippedTexturedVertices.push_back(texturedVertexB);
                        clippedTexturedVertices.push_back(texturedVertexC);
                    } else if (!texturedVertexAInside && !texturedVertexBInside && !texturedVertexCInside) {
                        clippedTexturedVertices.clear();
                    } else {
                        ClipTexturedTriangleAgainstScreenFrustum(texturedVertexA, texturedVertexB, texturedVertexC, nearPlaneDistance, projection, clippedTexturedVertices);
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
                    if (!IsFrontFacingTriangle(screenAX, screenAY, screenBX, screenBY, screenCX, screenCY)) {
                        texturedCullRejectCount++;
                        continue;
                    }

                    std::clock_t triangleLightingStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        triangleLightingStartTicks = std::clock();
                    }
                    const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                        ? GS_SETREG_RGBAQ(
                            batch.Material->GetBaseColorR(),
                            batch.Material->GetBaseColorG(),
                            batch.Material->GetBaseColorB(),
                            batch.Material->GetBaseColorA(),
                            0x00)
                        : ResolveTexturedVertexColor(lightingConstants, triangleWorldNormal, normalizedLightDirection);
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        const std::clock_t triangleLightingEndTicks = std::clock();
                        accumulatedTriangleLightingTicks += (triangleLightingEndTicks - triangleLightingStartTicks);
                    }

                    std::clock_t trianglePayloadFillStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        trianglePayloadFillStartTicks = std::clock();
                    }
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
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[0].ViewPosition, projection),
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[clippedIndex].ViewPosition, projection),
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[clippedIndex + 1u].ViewPosition, projection),
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
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        const std::clock_t trianglePayloadFillEndTicks = std::clock();
                        accumulatedTrianglePayloadFillTicks += (trianglePayloadFillEndTicks - trianglePayloadFillStartTicks);
                    }
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

    std::size_t Ps2VuVifPacketBuilder::AddOpaqueUntexturedBatches(
        const std::vector<const Ps2VuOpaqueBatch*>& batches,
        const std::vector<::float4x4>& worlds,
        const ::float4x4& view,
        const ::float4x4& projection,
        const ::float4& viewport,
        float nearPlaneDistance,
        const ::float3& lightDirection,
        GSGLOBAL* gsGlobal,
        bool createVifPacket) {
        if (batches.size() != worlds.size()) {
            throw std::invalid_argument("PS2 untextured VU batch aggregation requires aligned batch and world inputs.");
        }

        if (batches.empty()) {
            return 0u;
        }

        const std::size_t untexturedTrianglePacketQwordCount = (sizeof(Ps2VuUntexturedTrianglePayload) / 16u) + 8u;
        std::size_t acceptedBatchCount = 0u;
        std::size_t estimatedPacketQwordCount = MinimumVifPacketOverheadQwords;
        for (std::size_t batchIndex = 0u; batchIndex < batches.size(); batchIndex++) {
            const Ps2VuOpaqueBatch* batch = batches[batchIndex];
            if (batch == nullptr || batch->Model == nullptr || batch->Material == nullptr || batch->Textured) {
                return acceptedBatchCount;
            }

            const std::size_t maximumEmittedTriangleCount = (static_cast<std::size_t>(batch->Model->GetTriangleVertexCount()) / 3u) * 2u;
            const std::size_t nextPacketQwordCount = estimatedPacketQwordCount + (maximumEmittedTriangleCount * untexturedTrianglePacketQwordCount);
            if (nextPacketQwordCount > MaximumOpaqueUntexturedPacketQwords) {
                return acceptedBatchCount;
            }

            estimatedPacketQwordCount = nextPacketQwordCount;
            acceptedBatchCount++;
        }

        if (acceptedBatchCount == 0u) {
            return 0u;
        }

        LastCompletedPhase = 1;
        if (EnableVuPacketPhaseDiagnostics != 0 && LastCompletedPhase >= VuPacketDiagnosticCutoffPhase) {
            return acceptedBatchCount;
        }

        const ::float3 normalizedLightDirection = NormalizeOrFallback(lightDirection, ::float3(0.0f, 0.0f, -1.0f));
        static bool hasWrittenDirectUntexturedWorldViewDiagnostic = false;
        const bool writeDirectUntexturedWorldViewDiagnostic = !hasWrittenDirectUntexturedWorldViewDiagnostic;
        std::vector<Ps2VuUntexturedTrianglePayload> untexturedTrianglePayloads;
        std::vector<Ps2VuGifTemplateCacheEntry> gifTemplateCache;
        const std::clock_t triangleSetupStartTicks = std::clock();
        for (std::size_t batchIndex = 0u; batchIndex < acceptedBatchCount; batchIndex++) {
            const Ps2VuOpaqueBatch& batch = *batches[batchIndex];
            const ::float4x4& world = worlds[batchIndex];
            const std::uint32_t triangleVertexCount = batch.Model->GetTriangleVertexCount();
            const float* packedPositionWords = reinterpret_cast<const float*>(batch.Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch.Model->GetNormalBlockBytes());
            Ps2VuUntexturedSharedState sharedStateTemplate {};
            Ps2VuUntexturedSharedState clippedSharedStateTemplate {};
            PopulateUntexturedSharedState(world, view, projection, viewport, sharedStateTemplate);
            PopulateUntexturedSharedState(projection, viewport, clippedSharedStateTemplate);
            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4 worldViewMatrix;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            if (writeDirectUntexturedWorldViewDiagnostic) {
                AppendDiagnosticToHostBootLog(
                    std::string("[helengine-ps2] direct untextured world view")
                    + " batch=" + std::to_string(batchIndex)
                    + " world=" + std::to_string(world.M41) + "," + std::to_string(world.M42) + "," + std::to_string(world.M43)
                    + " view=" + std::to_string(worldViewMatrix.M41) + "," + std::to_string(worldViewMatrix.M42) + "," + std::to_string(worldViewMatrix.M43));
            }
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch.Material, lightingConstants);
            std::vector<Ps2VuUntexturedClipVertex> clippedUntexturedVertices;
            clippedUntexturedVertices.reserve(4u);
            for (std::uint32_t vertexIndex = 0u; (vertexIndex + 2u) < triangleVertexCount; vertexIndex += 3u) {
                const std::size_t positionWordIndexA = static_cast<std::size_t>(vertexIndex + 0u) * 4u;
                const std::size_t positionWordIndexB = static_cast<std::size_t>(vertexIndex + 1u) * 4u;
                const std::size_t positionWordIndexC = static_cast<std::size_t>(vertexIndex + 2u) * 4u;
                const ::float3 packedNormalA(packedNormalWords[positionWordIndexA], packedNormalWords[positionWordIndexA + 1u], packedNormalWords[positionWordIndexA + 2u]);
                const ::float3 packedNormalB(packedNormalWords[positionWordIndexB], packedNormalWords[positionWordIndexB + 1u], packedNormalWords[positionWordIndexB + 2u]);
                const ::float3 packedNormalC(packedNormalWords[positionWordIndexC], packedNormalWords[positionWordIndexC + 1u], packedNormalWords[positionWordIndexC + 2u]);
                const ::float3 faceNormal = NormalizeOrFallback(::float3(packedNormalA.X + packedNormalB.X + packedNormalC.X, packedNormalA.Y + packedNormalB.Y + packedNormalC.Y, packedNormalA.Z + packedNormalB.Z + packedNormalC.Z), ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 worldFaceNormal = NormalizeOrFallback(TransformPosition(::float4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f), world), ::float3(0.0f, 0.0f, -1.0f));
                const ::float3 packedPositionA(packedPositionWords[positionWordIndexA], packedPositionWords[positionWordIndexA + 1u], packedPositionWords[positionWordIndexA + 2u]);
                const ::float3 packedPositionB(packedPositionWords[positionWordIndexB], packedPositionWords[positionWordIndexB + 1u], packedPositionWords[positionWordIndexB + 2u]);
                const ::float3 packedPositionC(packedPositionWords[positionWordIndexC], packedPositionWords[positionWordIndexC + 1u], packedPositionWords[positionWordIndexC + 2u]);
                const Ps2VuUntexturedClipVertex vertexA { TransformPosition(::float4(packedPositionA.X, packedPositionA.Y, packedPositionA.Z, 1.0f), worldViewMatrix) };
                const Ps2VuUntexturedClipVertex vertexB { TransformPosition(::float4(packedPositionB.X, packedPositionB.Y, packedPositionB.Z, 1.0f), worldViewMatrix) };
                const Ps2VuUntexturedClipVertex vertexC { TransformPosition(::float4(packedPositionC.X, packedPositionC.Y, packedPositionC.Z, 1.0f), worldViewMatrix) };
                const bool triangleCrossesNearPlane = !(IsUntexturedVertexInsideNearPlane(vertexA.ViewPosition, nearPlaneDistance)
                    && IsUntexturedVertexInsideNearPlane(vertexB.ViewPosition, nearPlaneDistance)
                    && IsUntexturedVertexInsideNearPlane(vertexC.ViewPosition, nearPlaneDistance));
                if (triangleCrossesNearPlane) {
                    ClipUntexturedTriangleAgainstNearPlane(vertexA, vertexB, vertexC, nearPlaneDistance, clippedUntexturedVertices);
                } else {
                    clippedUntexturedVertices.clear();
                }

                if (triangleCrossesNearPlane && clippedUntexturedVertices.size() < 3u) {
                    continue;
                }

                const std::uint64_t triangleColor = ResolveTexturedVertexColor(lightingConstants, worldFaceNormal, normalizedLightDirection);
                Ps2VuFlatColor flatColor {};
                flatColor.Red = static_cast<std::uint8_t>(triangleColor & 0xFFu);
                flatColor.Green = static_cast<std::uint8_t>((triangleColor >> 8u) & 0xFFu);
                flatColor.Blue = static_cast<std::uint8_t>((triangleColor >> 16u) & 0xFFu);
                flatColor.Alpha = static_cast<std::uint8_t>((triangleColor >> 24u) & 0xFFu);
                const std::size_t emittedTriangleCount = triangleCrossesNearPlane ? clippedUntexturedVertices.size() - 2u : 1u;
                for (std::size_t clippedIndex = 0u; clippedIndex < emittedTriangleCount; clippedIndex++) {
                    const ::float3& emittedPositionA = triangleCrossesNearPlane
                        ? clippedUntexturedVertices[0u].ViewPosition
                        : (createVifPacket ? packedPositionA : vertexA.ViewPosition);
                    const ::float3& emittedPositionB = triangleCrossesNearPlane
                        ? clippedUntexturedVertices[clippedIndex + 1u].ViewPosition
                        : (createVifPacket ? packedPositionB : vertexB.ViewPosition);
                    const ::float3& emittedPositionC = triangleCrossesNearPlane
                        ? clippedUntexturedVertices[clippedIndex + 2u].ViewPosition
                        : (createVifPacket ? packedPositionC : vertexC.ViewPosition);
                    Ps2VuUntexturedTrianglePayload payload {};
                    CopyCachedTriangleGifPacketTemplate(batch, flatColor, gsGlobal, gifTemplateCache, payload.TriangleRecord);
                    payload.DirectGifFlatColor = flatColor;
                    const Ps2VuUntexturedSharedState& emittedSharedState = triangleCrossesNearPlane ? clippedSharedStateTemplate : sharedStateTemplate;
                    std::memcpy(&payload.SharedState, &emittedSharedState, sizeof(emittedSharedState));
                    payload.TriangleRecord.SourceTriangle.PositionA[0] = emittedPositionA.X;
                    payload.TriangleRecord.SourceTriangle.PositionA[1] = emittedPositionA.Y;
                    payload.TriangleRecord.SourceTriangle.PositionA[2] = emittedPositionA.Z;
                    payload.TriangleRecord.SourceTriangle.PositionA[3] = 1.0f;
                    payload.TriangleRecord.SourceTriangle.PositionB[0] = emittedPositionB.X;
                    payload.TriangleRecord.SourceTriangle.PositionB[1] = emittedPositionB.Y;
                    payload.TriangleRecord.SourceTriangle.PositionB[2] = emittedPositionB.Z;
                    payload.TriangleRecord.SourceTriangle.PositionB[3] = 1.0f;
                    payload.TriangleRecord.SourceTriangle.PositionC[0] = emittedPositionC.X;
                    payload.TriangleRecord.SourceTriangle.PositionC[1] = emittedPositionC.Y;
                    payload.TriangleRecord.SourceTriangle.PositionC[2] = emittedPositionC.Z;
                    payload.TriangleRecord.SourceTriangle.PositionC[3] = 1.0f;
                    untexturedTrianglePayloads.push_back(payload);
                }
                SubmittedTriangleCount += emittedTriangleCount;
            }
        }

        if (writeDirectUntexturedWorldViewDiagnostic) {
            hasWrittenDirectUntexturedWorldViewDiagnostic = true;
        }

        const std::clock_t triangleSetupEndTicks = std::clock();
        LastTriangleSetupMilliseconds = ResolveMillisecondsFromClockTicks(triangleSetupStartTicks, triangleSetupEndTicks);
        if (untexturedTrianglePayloads.empty()) {
            if (writeDirectUntexturedWorldViewDiagnostic) {
                hasWrittenDirectUntexturedWorldViewDiagnostic = true;
            }
            return acceptedBatchCount;
        }

        if (!createVifPacket) {
            std::vector<std::array<std::uint64_t, UntexturedTriangleDirectGifPacketWordCount>> untexturedTrianglePackets;
            untexturedTrianglePackets.reserve(untexturedTrianglePayloads.size());
            for (const Ps2VuUntexturedTrianglePayload& trianglePayload : untexturedTrianglePayloads) {
                std::array<std::uint64_t, UntexturedTriangleDirectGifPacketWordCount> trianglePacketWords {};
                if (BuildUntexturedTriangleGifPacketBytes(trianglePayload, projection, viewport, gsGlobal, trianglePacketWords)) {
                    untexturedTrianglePackets.push_back(trianglePacketWords);
                }
            }

            GifPacketBytes.resize(untexturedTrianglePackets.size() * UntexturedTriangleDirectGifPacketByteCount);
            for (std::size_t trianglePacketIndex = 0u; trianglePacketIndex < untexturedTrianglePackets.size(); trianglePacketIndex++) {
                std::memcpy(
                    GifPacketBytes.data() + (trianglePacketIndex * UntexturedTriangleDirectGifPacketByteCount),
                    untexturedTrianglePackets[trianglePacketIndex].data(),
                    UntexturedTriangleDirectGifPacketByteCount);
            }
            SubmittedTriangleCount = untexturedTrianglePackets.size();
            LastCompletedPhase = 11;
            return acceptedBatchCount;
        }

        GifPacketBytes.resize(TriangleGifPacketTemplateByteCount);
        std::memcpy(GifPacketBytes.data(), untexturedTrianglePayloads.front().TriangleRecord.GifPacketTemplate, TriangleGifPacketTemplateByteCount);
        std::unique_ptr<packet2_t, decltype(&packet2_free)> packet(CreatePacketOrThrow(static_cast<std::uint16_t>(estimatedPacketQwordCount), P2_MODE_CHAIN), &packet2_free);
        const std::clock_t packetAssemblyStartTicks = std::clock();
        for (const Ps2VuUntexturedTrianglePayload& trianglePayload : untexturedTrianglePayloads) {
            packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
            std::memcpy(packet.get()->next, &trianglePayload, sizeof(Ps2VuUntexturedTrianglePayload));
            packet2_advance_next(packet.get(), sizeof(Ps2VuUntexturedTrianglePayload));
            packet2_utils_vu_close_unpack(packet.get());
            packet2_chain_open_cnt(packet.get(), 0, 0, 0);
            packet2_vif_flush(packet.get(), 0);
            packet2_vif_mscal(packet.get(), UntexturedMicroProgramAddress, 0);
            packet2_chain_close_tag(packet.get());
        }

        packet2_chain_open_end(packet.get(), 0, 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_chain_close_tag(packet.get());
        Packet = packet.release();
        const std::clock_t packetAssemblyEndTicks = std::clock();
        LastPacketAssemblyMilliseconds = ResolveMillisecondsFromClockTicks(packetAssemblyStartTicks, packetAssemblyEndTicks);
        LastCompletedPhase = 11;
        return acceptedBatchCount;
    }

    void Ps2VuVifPacketBuilder::AddOpaqueTexturedVuBatches(
        const std::vector<Ps2VuOpaqueBatchSlice>& batches,
        const std::vector<::float4x4>& worlds,
        const ::float4x4& view,
        const ::float4x4& projection,
        const ::float4& viewport,
        GSGLOBAL* gsGlobal,
        const std::vector<GSTEXTURE*>& textures,
        const std::vector<int>& textureWidths,
        const std::vector<int>& textureHeights) {
        if (batches.size() != worlds.size()
            || batches.size() != textures.size()
            || batches.size() != textureWidths.size()
            || batches.size() != textureHeights.size()) {
            throw std::invalid_argument("PS2 textured VU source packing requires aligned batch, world, and texture inputs.");
        }
        if (gsGlobal == nullptr) {
            throw std::invalid_argument("PS2 textured VU source packing requires a GS global.");
        }
        if (batches.empty()) {
            return;
        }

        const std::size_t maximumPacketQwordCount = MinimumVifPacketOverheadQwords
            + (batches.size() * ((sizeof(Ps2VuTexturedSharedState) + (sizeof(Ps2VuTexturedSourceTriangle) * MaximumTexturedVuSourceTriangleCount)) / 16u));
        if (maximumPacketQwordCount > 0xFFFFu) {
            throw std::runtime_error("PS2 textured VU source packet exceeds packet2 qword capacity.");
        }

        std::unique_ptr<packet2_t, decltype(&packet2_free)> packet(CreatePacketOrThrow(static_cast<std::uint16_t>(maximumPacketQwordCount), P2_MODE_CHAIN), &packet2_free);
        const std::clock_t packetAssemblyStartTicks = std::clock();
        for (std::size_t batchIndex = 0u; batchIndex < batches.size(); batchIndex++) {
            const Ps2VuOpaqueBatchSlice& batchSlice = batches[batchIndex];
            const Ps2VuOpaqueBatch* batch = batchSlice.Batch;
            GSTEXTURE* texture = textures[batchIndex];
            const int textureWidth = textureWidths[batchIndex];
            const int textureHeight = textureHeights[batchIndex];
            if (batch == nullptr || batch->Model == nullptr || batch->Material == nullptr) {
                throw std::invalid_argument("PS2 textured VU source packing requires a complete opaque batch.");
            }
            if (texture == nullptr || textureWidth <= 0 || textureHeight <= 0) {
                throw std::invalid_argument("PS2 textured VU source packing requires a resolved texture.");
            }
            if (batchSlice.SourceTriangleCount == 0u || batchSlice.SourceTriangleCount > MaximumTexturedVuSourceTriangleCount) {
                throw std::invalid_argument("PS2 textured VU source packing received an unsupported triangle count.");
            }

            const std::size_t firstSourceVertex = batchSlice.FirstSourceTriangle * 3u;
            const std::size_t sourceVertexCount = batchSlice.SourceTriangleCount * 3u;
            const std::size_t finalSourceVertex = firstSourceVertex + sourceVertexCount;
            if (finalSourceVertex > batch->Model->GetTriangleVertexCount()) {
                throw std::out_of_range("PS2 textured VU source packing exceeds packed model triangle data.");
            }

            const float* packedPositionWords = reinterpret_cast<const float*>(batch->Model->GetPositionBlockBytes());
            const float* packedTexCoordWords = reinterpret_cast<const float*>(batch->Model->GetTexCoordBlockBytes());
            if (packedPositionWords == nullptr || packedTexCoordWords == nullptr) {
                throw std::runtime_error("PS2 textured VU source packing requires packed positions and texture coordinates.");
            }

            ::float4x4 worldView;
            ::float4x4 worldViewProjection;
            ::float4x4::Multiply__ref0_ref1_out2(worlds[batchIndex], view, worldView);
            ::float4x4::Multiply__ref0_ref1_out2(worldView, projection, worldViewProjection);
            Ps2VuTexturedSharedState sharedState {};
            CopyMatrixWords(worldViewProjection, sharedState.WorldViewProjectionMatrix);
            sharedState.GsScale[0] = viewport.Z * 0.5f;
            sharedState.GsScale[1] = viewport.W * -0.5f;
            sharedState.GsScale[2] = -4194304.0f;
            sharedState.GsScale[3] = 0.0f;
            sharedState.GsOffset[0] = 2048.0f + viewport.X + (viewport.Z * 0.5f);
            sharedState.GsOffset[1] = 2048.0f + viewport.Y + (viewport.W * 0.5f);
            sharedState.GsOffset[2] = 4194304.0f;
            sharedState.GsOffset[3] = 0.0f;
            const std::uint32_t flatColor = GS_SETREG_RGBAQ(
                batch->Material->GetBaseColorR(),
                batch->Material->GetBaseColorG(),
                batch->Material->GetBaseColorB(),
                batch->Material->GetBaseColorA(),
                0x00);
            std::memcpy(&sharedState.FlatColor[0], &flatColor, sizeof(flatColor));
            sharedState.TriangleCount[0] = static_cast<std::uint32_t>(batchSlice.SourceTriangleCount);
            sharedState.StateTemplate[0].Low = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);
            sharedState.StateTemplate[0].High = GIF_REG_AD;
            sharedState.StateTemplate[1].Low = ResolveOpaqueUntexturedTestRegister(gsGlobal);
            sharedState.StateTemplate[1].High = GS_REG_TEST;
            sharedState.StateTemplate[2].Low = GIF_SET_TAG(1, 0, 0, 0, GIF_FLG_PACKED, 1);
            sharedState.StateTemplate[2].High = GIF_REG_AD;
            sharedState.StateTemplate[3].Low = GS_SET_TEX1(0, 0, texture->Filter, texture->Filter, 0, 0, 0);
            sharedState.StateTemplate[3].High = GS_REG_TEX1;
            const int textureWidthPower = ResolveGsTextureDimensionExponent(textureWidth);
            const int textureHeightPower = ResolveGsTextureDimensionExponent(textureHeight);
            sharedState.StateTemplate[4].Low = texture->VramClut == 0
                ? GS_SETREG_TEX0(texture->Vram / 256, texture->TBW, texture->PSM, textureWidthPower, textureHeightPower, gsGlobal->PrimAlphaEnable, 0, 0, 0, 0, 0, GS_CLUT_STOREMODE_NOLOAD)
                : GS_SETREG_TEX0(texture->Vram / 256, texture->TBW, texture->PSM, textureWidthPower, textureHeightPower, gsGlobal->PrimAlphaEnable, 0, texture->VramClut / 256, texture->ClutPSM, texture->ClutStorageMode, 0, GS_CLUT_STOREMODE_LOAD);
            sharedState.StateTemplate[4].High = GS_SETREG_PRIM(GS_PRIM_PRIM_TRIANGLE, PRIM_SHADE_GOURAUD, 1, gsGlobal->PrimFogEnable, gsGlobal->PrimAlphaEnable, gsGlobal->PrimAAEnable, 0, gsGlobal->PrimContext, 0);
            sharedState.StateTemplate[5].Low = GIF_TAG_TRIANGLE_GORAUD_TEXTURED(1);
            sharedState.StateTemplate[5].High = BuildPerspectiveTextureRegisterList(gsGlobal->PrimContext);

            std::vector<Ps2VuTexturedSourceTriangle> sourceTriangles;
            sourceTriangles.reserve(batchSlice.SourceTriangleCount);
            for (std::size_t sourceVertex = firstSourceVertex; sourceVertex < finalSourceVertex; sourceVertex += 3u) {
                const std::size_t positionWordIndexA = sourceVertex * 4u;
                const std::size_t positionWordIndexB = (sourceVertex + 1u) * 4u;
                const std::size_t positionWordIndexC = (sourceVertex + 2u) * 4u;
                Ps2VuTexturedSourceTriangle sourceTriangle {};
                sourceTriangle.PositionA[0] = packedPositionWords[positionWordIndexA + 0u];
                sourceTriangle.PositionA[1] = packedPositionWords[positionWordIndexA + 1u];
                sourceTriangle.PositionA[2] = packedPositionWords[positionWordIndexA + 2u];
                sourceTriangle.PositionA[3] = 1.0f;
                sourceTriangle.PositionB[0] = packedPositionWords[positionWordIndexB + 0u];
                sourceTriangle.PositionB[1] = packedPositionWords[positionWordIndexB + 1u];
                sourceTriangle.PositionB[2] = packedPositionWords[positionWordIndexB + 2u];
                sourceTriangle.PositionB[3] = 1.0f;
                sourceTriangle.PositionC[0] = packedPositionWords[positionWordIndexC + 0u];
                sourceTriangle.PositionC[1] = packedPositionWords[positionWordIndexC + 1u];
                sourceTriangle.PositionC[2] = packedPositionWords[positionWordIndexC + 2u];
                sourceTriangle.PositionC[3] = 1.0f;
                sourceTriangle.TexCoordA[0] = packedTexCoordWords[positionWordIndexA + 0u];
                sourceTriangle.TexCoordA[1] = packedTexCoordWords[positionWordIndexA + 1u];
                sourceTriangle.TexCoordB[0] = packedTexCoordWords[positionWordIndexB + 0u];
                sourceTriangle.TexCoordB[1] = packedTexCoordWords[positionWordIndexB + 1u];
                sourceTriangle.TexCoordC[0] = packedTexCoordWords[positionWordIndexC + 0u];
                sourceTriangle.TexCoordC[1] = packedTexCoordWords[positionWordIndexC + 1u];
                sourceTriangles.push_back(sourceTriangle);
            }

            packet2_utils_vu_open_unpack(packet.get(), XtopGifPacketAddress, 1);
            std::memcpy(packet.get()->next, &sharedState, sizeof(sharedState));
            packet2_advance_next(packet.get(), sizeof(sharedState));
            std::memcpy(packet.get()->next, sourceTriangles.data(), sourceTriangles.size() * sizeof(Ps2VuTexturedSourceTriangle));
            packet2_advance_next(packet.get(), sourceTriangles.size() * sizeof(Ps2VuTexturedSourceTriangle));
            packet2_utils_vu_close_unpack(packet.get());
            packet2_chain_open_cnt(packet.get(), 0, 0, 0);
            packet2_vif_flush(packet.get(), 0);
            packet2_vif_mscal(packet.get(), TexturedMicroProgramAddress, 0);
            packet2_chain_close_tag(packet.get());
            SubmittedTriangleCount += sourceTriangles.size();
        }

        packet2_chain_open_end(packet.get(), 0, 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_vif_nop(packet.get(), 0);
        packet2_chain_close_tag(packet.get());
        Packet = packet.release();
        LastPacketAssemblyMilliseconds = ResolveMillisecondsFromClockTicks(packetAssemblyStartTicks, std::clock());
        LastCompletedPhase = 11;
    }

    void Ps2VuVifPacketBuilder::AddOpaqueTexturedBatches(
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
        bool createVifPacket) {
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
        for (const Ps2VuOpaqueBatchSlice& batchSlice : batches) {
            if (batchSlice.Batch == nullptr || batchSlice.Batch->Model == nullptr) {
                continue;
            }

            texturedTriangleCapacity += batchSlice.SourceTriangleCount;
        }

        std::vector<std::array<std::uint64_t, TexturedTrianglePacketWordCount>> texturedTrianglePackets;
        texturedTrianglePackets.reserve(texturedTriangleCapacity);
        std::vector<std::uint64_t> directGifPacketWords;
        if (!createVifPacket) {
            directGifPacketWords.reserve((texturedTriangleCapacity * 14u) + (batches.size() * 8u));
        }
        std::vector<Ps2VuTexturedClipVertex> clippedTexturedVertices;
        clippedTexturedVertices.reserve(4u);
        std::clock_t accumulatedTrianglePrepTicks = 0;
        std::clock_t accumulatedTriangleEmitTicks = 0;
        std::clock_t accumulatedTriangleLightingTicks = 0;
        std::clock_t accumulatedTrianglePayloadFillTicks = 0;
        const std::clock_t triangleSetupStartTicks = std::clock();
        for (std::size_t batchIndex = 0; batchIndex < batches.size(); batchIndex++) {
            const Ps2VuOpaqueBatchSlice& batchSlice = batches[batchIndex];
            const Ps2VuOpaqueBatch* batch = batchSlice.Batch;
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
            const std::size_t firstSourceVertex = batchSlice.FirstSourceTriangle * 3u;
            const std::size_t sourceVertexCount = batchSlice.SourceTriangleCount * 3u;
            const std::size_t finalSourceVertex = firstSourceVertex + sourceVertexCount;
            if (sourceVertexCount == 0u || finalSourceVertex > batch->Model->GetTriangleVertexCount()) {
                continue;
            }

            ::float4x4 worldCopy = world;
            ::float4x4 viewCopy = view;
            ::float4x4 worldViewMatrix;
            ::float4x4::Multiply__ref0_ref1_out2(worldCopy, viewCopy, worldViewMatrix);
            Ps2VuLightingConstants lightingConstants {};
            PopulateLightingConstants(*batch->Material, lightingConstants);
            const Ps2RuntimeModel* runtimeModel = batch->Proxy != nullptr ? batch->Proxy->GetModel() : nullptr;
            const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
            const std::vector<::float2>* runtimeTexCoords = runtimeModel != nullptr ? &runtimeModel->GetTexCoords() : nullptr;
            const float* packedPositionWords = reinterpret_cast<const float*>(batch->Model->GetPositionBlockBytes());
            const float* packedNormalWords = reinterpret_cast<const float*>(batch->Model->GetNormalBlockBytes());
            const float* packedTexCoordWords = reinterpret_cast<const float*>(batch->Model->GetTexCoordBlockBytes());
            const std::size_t firstTexturedTrianglePacketIndex = texturedTrianglePackets.size();
            for (std::size_t vertexIndex = firstSourceVertex; (vertexIndex + 2u) < finalSourceVertex; vertexIndex += 3u) {
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
                const ::float3 faceNormal(
                    packedNormalA.X + packedNormalB.X + packedNormalC.X,
                    packedNormalA.Y + packedNormalB.Y + packedNormalC.Y,
                    packedNormalA.Z + packedNormalB.Z + packedNormalC.Z);
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
                const std::uint16_t sourceIndexA = runtimeIndices != nullptr && vertexIndex < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 0u]
                    : static_cast<std::uint16_t>(vertexIndex + 0u);
                const std::uint16_t sourceIndexB = runtimeIndices != nullptr && (vertexIndex + 1u) < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 1u]
                    : static_cast<std::uint16_t>(vertexIndex + 1u);
                const std::uint16_t sourceIndexC = runtimeIndices != nullptr && (vertexIndex + 2u) < runtimeIndices->size()
                    ? (*runtimeIndices)[vertexIndex + 2u]
                    : static_cast<std::uint16_t>(vertexIndex + 2u);
                const ::float4 faceNormal4(faceNormal.X, faceNormal.Y, faceNormal.Z, 0.0f);
                const ::float3 triangleWorldNormal = NormalizeOrFallback(
                    TransformPosition(faceNormal4, world),
                    ::float3(0.0f, 0.0f, -1.0f));
                if (EnableVuPerTriangleTimingDiagnostics) {
                    accumulatedTrianglePrepTicks += (std::clock() - trianglePrepStartTicks);
                }

                std::clock_t triangleEmitStartTicks = 0;
                if (EnableVuPerTriangleTimingDiagnostics) {
                    triangleEmitStartTicks = std::clock();
                }

                const ::float3 viewPositionA = TransformPosition(positionA, worldViewMatrix);
                const ::float3 viewPositionB = TransformPosition(positionB, worldViewMatrix);
                const ::float3 viewPositionC = TransformPosition(positionC, worldViewMatrix);
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
                bool texturedVertexAInside = false;
                bool texturedVertexBInside = false;
                bool texturedVertexCInside = false;
                float reciprocalClipWA = 0.0f;
                float reciprocalClipWB = 0.0f;
                float reciprocalClipWC = 0.0f;
                const bool texturedVertexAProjected = TryClassifyAndBuildTexturedVertexPositionRegister(
                    texturedVertexA,
                    nearPlaneDistance,
                    projection,
                    viewport,
                    gsGlobal,
                    texturedVertexAInside,
                    reciprocalClipWA,
                    screenAX,
                    screenAY,
                    screenAZ,
                    positionARegister);
                const bool texturedVertexBProjected = TryClassifyAndBuildTexturedVertexPositionRegister(
                    texturedVertexB,
                    nearPlaneDistance,
                    projection,
                    viewport,
                    gsGlobal,
                    texturedVertexBInside,
                    reciprocalClipWB,
                    screenBX,
                    screenBY,
                    screenBZ,
                    positionBRegister);
                const bool texturedVertexCProjected = TryClassifyAndBuildTexturedVertexPositionRegister(
                    texturedVertexC,
                    nearPlaneDistance,
                    projection,
                    viewport,
                    gsGlobal,
                    texturedVertexCInside,
                    reciprocalClipWC,
                    screenCX,
                    screenCY,
                    screenCZ,
                    positionCRegister);
                texturedVertexAInside = texturedVertexAProjected && texturedVertexAInside;
                texturedVertexBInside = texturedVertexBProjected && texturedVertexBInside;
                texturedVertexCInside = texturedVertexCProjected && texturedVertexCInside;
                if (texturedVertexAInside && texturedVertexBInside && texturedVertexCInside) {
                    std::clock_t triangleLightingStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        triangleLightingStartTicks = std::clock();
                    }
                    const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                        ? GS_SETREG_RGBAQ(
                            batch->Material->GetBaseColorR(),
                            batch->Material->GetBaseColorG(),
                            batch->Material->GetBaseColorB(),
                            batch->Material->GetBaseColorA(),
                            0x00)
                        : ResolveTexturedVertexColor(lightingConstants, triangleWorldNormal, normalizedLightDirection);
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        accumulatedTriangleLightingTicks += (std::clock() - triangleLightingStartTicks);
                    }

                    std::clock_t trianglePayloadFillStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        trianglePayloadFillStartTicks = std::clock();
                    }
                    texturedTrianglePackets.push_back(
                        BuildTexturedTriangleGifPacketBytes(
                            gsGlobal,
                            texture,
                            textureWidth,
                            textureHeight,
                            triangleColor,
                            texturedVertexA,
                            texturedVertexB,
                            texturedVertexC,
                            reciprocalClipWA,
                            reciprocalClipWB,
                            reciprocalClipWC,
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
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        accumulatedTrianglePayloadFillTicks += (std::clock() - trianglePayloadFillStartTicks);
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
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        const std::clock_t triangleEmitEndTicks = std::clock();
                        LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                    }

                    continue;
                } else if (!texturedVertexAInside && !texturedVertexBInside && !texturedVertexCInside) {
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        const std::clock_t triangleEmitEndTicks = std::clock();
                        LastTriangleEmitMilliseconds += ResolveMillisecondsFromClockTicks(triangleEmitStartTicks, triangleEmitEndTicks);
                    }

                    continue;
                } else {
                    clippedTexturedVertices.clear();
                    ClipTexturedTriangleAgainstScreenFrustum(texturedVertexA, texturedVertexB, texturedVertexC, nearPlaneDistance, projection, clippedTexturedVertices);
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

                    std::clock_t triangleLightingStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        triangleLightingStartTicks = std::clock();
                    }
                    const std::uint64_t triangleColor = EnableTexturedWhiteColorDiagnostics
                        ? GS_SETREG_RGBAQ(
                            batch->Material->GetBaseColorR(),
                            batch->Material->GetBaseColorG(),
                            batch->Material->GetBaseColorB(),
                            batch->Material->GetBaseColorA(),
                            0x00)
                        : ResolveTexturedVertexColor(lightingConstants, triangleWorldNormal, normalizedLightDirection);
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        accumulatedTriangleLightingTicks += (std::clock() - triangleLightingStartTicks);
                    }

                    std::clock_t trianglePayloadFillStartTicks = 0;
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        trianglePayloadFillStartTicks = std::clock();
                    }
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
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[0].ViewPosition, projection),
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[clippedIndex].ViewPosition, projection),
                            ResolvePerspectiveTextureReciprocalW(clippedTexturedVertices[clippedIndex + 1u].ViewPosition, projection),
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
                    if (EnableVuPerTriangleTimingDiagnostics) {
                        accumulatedTrianglePayloadFillTicks += (std::clock() - trianglePayloadFillStartTicks);
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

                if (EnableVuPerTriangleTimingDiagnostics) {
                    accumulatedTriangleEmitTicks += (std::clock() - triangleEmitStartTicks);
                }
            }

            if (!createVifPacket && texturedTrianglePackets.size() > firstTexturedTrianglePacketIndex) {
                const std::array<std::uint64_t, TexturedTrianglePacketWordCount>& firstTrianglePacket = texturedTrianglePackets[firstTexturedTrianglePacketIndex];
                directGifPacketWords.insert(directGifPacketWords.end(), firstTrianglePacket.begin(), firstTrianglePacket.begin() + 8u);
                for (std::size_t trianglePacketIndex = firstTexturedTrianglePacketIndex; trianglePacketIndex < texturedTrianglePackets.size(); trianglePacketIndex++) {
                    const std::array<std::uint64_t, TexturedTrianglePacketWordCount>& trianglePacket = texturedTrianglePackets[trianglePacketIndex];
                    directGifPacketWords.insert(directGifPacketWords.end(), trianglePacket.begin() + 8u, trianglePacket.end());
                }
            }

        }

        LastTrianglePrepMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePrepTicks);
        LastTriangleEmitMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleEmitTicks);
        LastTriangleLightingMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTriangleLightingTicks);
        LastTrianglePayloadFillMilliseconds = ResolveMillisecondsFromClockTicks(0, accumulatedTrianglePayloadFillTicks);
        const std::clock_t triangleSetupEndTicks = std::clock();
        LastTriangleSetupMilliseconds = ResolveMillisecondsFromClockTicks(triangleSetupStartTicks, triangleSetupEndTicks);
        if (texturedTrianglePackets.empty()) {
            return;
        }

        if (!createVifPacket) {
            GifPacketBytes.resize(directGifPacketWords.size() * sizeof(std::uint64_t));
            std::memcpy(GifPacketBytes.data(), directGifPacketWords.data(), GifPacketBytes.size());
            LastCompletedPhase = 11;
            return;
        }
        GifPacketBytes.resize(texturedTrianglePackets.size() * TexturedTrianglePacketByteCount);
        for (std::size_t triangleIndex = 0u; triangleIndex < texturedTrianglePackets.size(); triangleIndex++) {
            std::memcpy(
                GifPacketBytes.data() + (triangleIndex * TexturedTrianglePacketByteCount),
                texturedTrianglePackets[triangleIndex].data(),
                TexturedTrianglePacketByteCount);
        }
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

    packet2_t* Ps2VuVifPacketBuilder::ReleasePacket() {
        packet2_t* packet = Packet;
        Packet = nullptr;
        return packet;
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
