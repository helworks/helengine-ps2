#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "RenderManager3D.hpp"
#include "platform/ps2/rendering/Ps2FramePlanner.hpp"
#include "platform/ps2/rendering/Ps2RenderPerformanceMetrics.hpp"
#include "platform/ps2/rendering/Ps2RenderProxy.hpp"
#include "platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp"
#include "platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp"
#include "platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp"
#include "platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp"

typedef struct gsGlobal GSGLOBAL;
typedef struct gsTexture GSTEXTURE;

class CameraComponent;
class ModelAsset;
class PlatformMaterialAsset;
class Ps2MaterialAsset;
class RenderTarget;
class RuntimeMaterial;
class RuntimeModel;
class float3;
class float4;
class float4x4;

namespace helengine::ps2 {
    class Ps2FramePlan;
    class Ps2RuntimeMaterial;
    class Ps2RuntimeModel;

    class Ps2RenderManager3D final : public ::RenderManager3D {
    public:
        Ps2RenderManager3D();
        ~Ps2RenderManager3D();

        ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset) override;
        ::RuntimeMaterial* BuildMaterialFromCooked(std::string cookedAssetPath, IContentStreamSource* contentStreamSource) override;
        ::RuntimeModel* BuildModelFromCooked(std::string cookedAssetPath, IContentStreamSource* contentStreamSource) override;
        ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) override;
        ::RenderTarget* CreateRenderTarget(int32_t width, int32_t height) override;
        void Draw() override;
        void FlushReleasedAssets() override;
        void ReleaseMaterial(::RuntimeMaterial* material) override;
        void ReleaseModel(::RuntimeModel* model) override;
        void ClearCachedTextures();
        void SetHdrEnabled(bool enabled);
        void SetGsGlobal(GSGLOBAL* gsGlobal);
        std::size_t GetLastProxyCount() const;
        std::size_t GetLastOpaqueWorldCount() const;
        std::size_t GetLastOpaqueDynamicCount() const;
        std::size_t GetLastAlphaWorldCount() const;
        std::size_t GetLastAlphaDynamicCount() const;
        std::size_t GetLastClipRejectCount() const;
        std::size_t GetLastProjectionRejectCount() const;
        std::size_t GetLastCullRejectCount() const;
        std::size_t GetLastSubmittedTriangleCount() const;
        std::size_t GetLastTexturedSubmittedTriangleCount() const;
        std::size_t GetLastUntexturedSubmittedTriangleCount() const;
        std::size_t GetLastTexturedPacketCacheBuildCount() const;
        std::size_t GetLastFrustumRejectedBatchCount() const;
        std::size_t GetLastFrustumRejectedSliceCount() const;
        /// <summary>
        /// Returns the frame's original textured source triangles submitted through the unchanged fast VU1 route.
        /// </summary>
        std::size_t GetLastFastTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the frame's original textured source triangles processed by the clipped VU1 route.
        /// </summary>
        std::size_t GetLastClippedTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the frame's original textured source triangles omitted after classification or clipping.
        /// </summary>
        std::size_t GetLastRejectedTexturedSourceTriangleCount() const;
        /// <summary>
        /// Returns the frame's clipped fan triangles generated for the pretransformed VU1 route.
        /// </summary>
        std::size_t GetLastGeneratedClippedTexturedTriangleCount() const;
        /// <summary>
        /// Returns the frame's emitted pretransformed clipped VU1 batch count.
        /// </summary>
        std::size_t GetLastClippedTexturedBatchCount() const;
        /// <summary>
        /// Returns the triangle count the CPU emitted for the most recently dispatched pretransformed clipped VU1 batch this frame.
        /// </summary>
        std::size_t GetLastEmittedClippedBatchTriangleCount() const;
        /// <summary>
        /// Returns the number of complete triangles encoded in the most recently captured VU1 textured output packet.
        /// </summary>
        std::size_t GetLastVuOutputTriangleCount() const;
        /// <summary>
        /// Returns the first captured triangle's first GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexA0() const;
        /// <summary>
        /// Returns the first captured triangle's second GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexA1() const;
        /// <summary>
        /// Returns the first captured triangle's third GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexA2() const;
        /// <summary>
        /// Returns the second captured triangle's first GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexB0() const;
        /// <summary>
        /// Returns the second captured triangle's second GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexB1() const;
        /// <summary>
        /// Returns the second captured triangle's third GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexB2() const;
        /// <summary>
        /// Returns the third captured triangle's first GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexC0() const;
        /// <summary>
        /// Returns the third captured triangle's second GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexC1() const;
        /// <summary>
        /// Returns the third captured triangle's third GS-space XYZ vertex, with X and Y converted back to screen pixels.
        /// </summary>
        ::float4 GetLastVuOutputTriangleVertexC2() const;
        /// <summary>
        /// Returns the view matrix used by the most recent opaque VU-path render call, for offline diagnostic reproduction.
        /// </summary>
        ::float4x4 GetLastFrameView() const;
        /// <summary>
        /// Returns the projection matrix used by the most recent opaque VU-path render call, for offline diagnostic reproduction.
        /// </summary>
        ::float4x4 GetLastFrameProjection() const;
        std::size_t GetLastVuBatchDispatchCount() const;
        std::size_t GetLastVuTriangleVertexCount() const;
        std::size_t GetLastVuPacketByteCount() const;
        std::size_t GetLastVuRejectedMissingMaterialCount() const;
        std::size_t GetLastVuRejectedMissingModelCount() const;
        std::size_t GetLastVuRejectedMissingPackedModelCount() const;
        std::uint32_t GetLastVuPacketPhase() const;
        double GetLastProxySyncMilliseconds() const;
        double GetLastFramePlanMilliseconds() const;
        double GetLastVuBatchBuildMilliseconds() const;
        double GetLastVuWaitMilliseconds() const;
        double GetLastVuSubmitMilliseconds() const;
        double GetLastVuPacketEncodeMilliseconds() const;
        double GetLastVuTriangleSetupMilliseconds() const;
        double GetLastVuPacketAssemblyMilliseconds() const;
        double GetLastVuTrianglePrepMilliseconds() const;
        double GetLastVuTriangleEmitMilliseconds() const;
        double GetLastVuTriangleLightingMilliseconds() const;
        double GetLastVuTrianglePayloadFillMilliseconds() const;
        double GetLastTexturedVuStateBuildMilliseconds() const;
        double GetLastTexturedVuCommandEncodeMilliseconds() const;
        const Ps2RenderPerformanceMetrics& GetLastPerformanceMetrics() const;
        void SetLastVifDrainMilliseconds(double milliseconds);
        void SetLastGifDrainMilliseconds(double milliseconds);
        bool IsUsingLegacyCpuOpaquePath() const;
        ::float4 GetLastResolvedViewport() const;
        ::float4 GetLastSubmittedScreenBounds() const;
        ::float4 GetLastSubmittedTriangleBoundsA() const;
        ::float4 GetLastSubmittedTriangleBoundsB() const;
        ::float4 GetLastSubmittedTriangleVertexA0() const;
        ::float4 GetLastSubmittedTriangleVertexA1() const;
        ::float4 GetLastSubmittedTriangleVertexA2() const;
        ::float4 GetLastSubmittedTriangleVertexB0() const;
        ::float4 GetLastSubmittedTriangleVertexB1() const;
        ::float4 GetLastSubmittedTriangleVertexB2() const;

    private:
        ::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset);
        void RenderOpaqueWithVuPath(const Ps2FramePlan& plan, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void PublishPerformanceOverlayMetrics() const;
        /// <summary>
        /// Captures the bounded first two triangles from the completed textured VU1 GIF output packet for probe diagnostics.
        /// </summary>
        void CaptureTexturedVuOutputDiagnostic();
        /// <summary>
        /// Waits until VIF1 has consumed its post-MSCAL FLUSH command and VU1 is no longer executing before EE readback.
        /// </summary>
        void WaitForTexturedVuOutputDiagnostic();
        /// <summary>
        /// Reads one packed GS XYZ qword from VU1 data memory and converts its fixed-point X and Y values to screen pixels.
        /// </summary>
        /// <param name="qwordAddress">VU1 data-memory qword containing the packed XYZ value.</param>
        /// <returns>The decoded X, Y, Z, and ADC words.</returns>
        ::float4 ReadTexturedVuOutputDiagnosticVertex(std::size_t qwordAddress) const;
        void ReleaseVuPacketSlot(std::size_t slotIndex);
        void WaitForVif1BeforePacketReuse();
        ::float4x4 BuildWorldMatrix(const Ps2RenderProxy& proxy) const;
        bool CanUseTexturedVuFastPath(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& view, const ::float4x4& projection, float nearPlaneDistance) const;
        void DrawOpaqueProxyLegacy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawOpaqueProxyLegacyTimed(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawOpaqueProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawAlphaProxy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
        void DrawSoftwareDepthPass(
            const Ps2FramePlan& plan,
            const ::float4x4& view,
            const ::float4x4& projection,
            const ::float4& viewport,
            float nearPlaneDistance,
            const ::float3& cameraPosition,
            const ::float3& cameraForward);
        void ApplyDepthState(bool enabled);
        void ApplyMaterialAlphaState(const Ps2RuntimeMaterial& material);
        ::CameraComponent* GetActiveCamera() const;
        bool ShouldDrawAlphaTestTriangle(
            const Ps2RuntimeMaterial& material,
            GSTEXTURE* texture,
            const ::float2& texCoordA,
            const ::float2& texCoordB,
            const ::float2& texCoordC,
            std::uint8_t alphaA,
            std::uint8_t alphaB,
            std::uint8_t alphaC) const;
        bool ShouldEmitHdrGlow(const Ps2RuntimeMaterial& material, std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const;
        bool IsGlowColorBright(std::uint64_t color) const;
        float ComputeHdrGlowStrength(std::uint64_t colorA, std::uint64_t colorB, std::uint64_t colorC) const;
        std::uint64_t BoostHdrColor(std::uint64_t color, float glowStrength) const;
        void SortAlphaProxies(std::vector<const Ps2RenderProxy*>& proxies, const ::float3& cameraPosition, const ::float3& cameraForward);
        void RebuildProxies();
        bool ProjectWorldPosition(
            const ::float3& worldPosition,
            const ::float4x4& projection,
            const ::float4& viewport,
            float& screenX,
            float& screenY,
            float& screenZ) const;
        bool IsFrontFacingTriangle(
            float screenAX,
            float screenAY,
            float screenBX,
            float screenBY,
            float screenCX,
            float screenCY) const;
        std::uint8_t SampleTextureAlpha(GSTEXTURE* texture, const ::float2& texCoord) const;
        ::float3 TransformPosition(const ::float3& position, const ::float4x4& matrix) const;
        double ComputeProxyDepth(const Ps2RenderProxy& proxy, const ::float3& cameraPosition, const ::float3& cameraForward) const;
        bool TryResolveDirectionalLightDirection(::float3& lightDirection) const;
        std::uint64_t ResolveVertexColor(const Ps2RuntimeMaterial& material, const ::float3& normal, const ::float3& lightDirection);

        Ps2FramePlanner FramePlanner;
        Ps2VuOpaqueBatchBuilder VuOpaqueBatchBuilder;
        Ps2VuProgramRegistry VuProgramRegistry;
        Ps2VuVifPacketBuilder VuVifPacketBuilder;
        Ps2VuGifStateEncoder VuGifStateEncoder;
        packet2_t* VuPacketSlots[2] = { nullptr, nullptr };
        std::size_t ActiveVuPacketSlotIndex = 0u;
        bool UseLegacyCpuOpaquePath;
        bool HdrEnabled;
        GSGLOBAL* GsGlobal;
        std::vector<Ps2RenderProxy> Proxies;
        std::vector<::RuntimeMaterial*> PendingReleasedMaterials;
        std::vector<::RuntimeModel*> PendingReleasedModels;
        std::size_t LastProxyCount;
        std::size_t LastOpaqueWorldCount;
        std::size_t LastOpaqueDynamicCount;
        std::size_t LastAlphaWorldCount;
        std::size_t LastAlphaDynamicCount;
        std::size_t LastClipRejectCount;
        std::size_t LastProjectionRejectCount;
        std::size_t LastCullRejectCount;
        std::size_t LastSubmittedTriangleCount;
        /// <summary>
        /// Accumulates original textured source triangles emitted through the fast route for the current frame.
        /// </summary>
        std::size_t LastFastTexturedSourceTriangleCount;
        /// <summary>
        /// Accumulates original textured source triangles processed by the clipped route for the current frame.
        /// </summary>
        std::size_t LastClippedTexturedSourceTriangleCount;
        /// <summary>
        /// Accumulates original textured source triangles omitted after route classification or clipping for the current frame.
        /// </summary>
        std::size_t LastRejectedTexturedSourceTriangleCount;
        /// <summary>
        /// Accumulates generated clipped fan triangles for the current frame.
        /// </summary>
        std::size_t LastGeneratedClippedTexturedTriangleCount;
        /// <summary>
        /// Holds the triangle count the CPU emitted for the most recently dispatched pretransformed clipped VU1 batch this frame.
        /// </summary>
        std::size_t LastEmittedClippedBatchTriangleCount;
        /// <summary>
        /// Accumulates emitted pretransformed clipped batches for the current frame.
        /// </summary>
        std::size_t LastClippedTexturedBatchCount;
        /// <summary>
        /// Stores the triangle count decoded from the latest completed textured VU1 GIF tag.
        /// </summary>
        std::size_t LastVuOutputTriangleCount;
        /// <summary>
        /// Stores the first decoded screen-space vertex of captured VU1 output triangle A.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexA0;
        /// <summary>
        /// Stores the second decoded screen-space vertex of captured VU1 output triangle A.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexA1;
        /// <summary>
        /// Stores the third decoded screen-space vertex of captured VU1 output triangle A.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexA2;
        /// <summary>
        /// Stores the first decoded screen-space vertex of captured VU1 output triangle B.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexB0;
        /// <summary>
        /// Stores the second decoded screen-space vertex of captured VU1 output triangle B.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexB1;
        /// <summary>
        /// Stores the third decoded screen-space vertex of captured VU1 output triangle B.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexB2;
        /// <summary>
        /// Stores the first decoded screen-space vertex of captured VU1 output triangle C.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexC0;
        /// <summary>
        /// Stores the second decoded screen-space vertex of captured VU1 output triangle C.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexC1;
        /// <summary>
        /// Stores the third decoded screen-space vertex of captured VU1 output triangle C.
        /// </summary>
        ::float4 LastVuOutputTriangleVertexC2;
        /// <summary>
        /// Stores the view matrix passed into the most recent opaque VU-path render call, for offline diagnostic reproduction.
        /// </summary>
        ::float4x4 LastFrameView;
        /// <summary>
        /// Stores the projection matrix passed into the most recent opaque VU-path render call, for offline diagnostic reproduction.
        /// </summary>
        ::float4x4 LastFrameProjection;
        std::size_t LastVuBatchDispatchCount;
        std::size_t LastVuTriangleVertexCount;
        std::size_t LastVuPacketByteCount;
        std::size_t LastVuRejectedMissingMaterialCount;
        std::size_t LastVuRejectedMissingModelCount;
        std::size_t LastVuRejectedMissingPackedModelCount;
        std::uint32_t LastVuPacketPhase;
        double LastProxySyncMilliseconds;
        double LastFramePlanMilliseconds;
        double LastVuBatchBuildMilliseconds;
        double LastVuWaitMilliseconds;
        double LastVuSubmitMilliseconds;
        double LastVuPacketEncodeMilliseconds;
        double LastVuTriangleSetupMilliseconds;
        double LastVuPacketAssemblyMilliseconds;
        double LastVuTrianglePrepMilliseconds;
        double LastVuTriangleEmitMilliseconds;
        double LastVuTriangleLightingMilliseconds;
        double LastVuTrianglePayloadFillMilliseconds;
        double LastTexturedVuStateBuildMilliseconds;
        double LastTexturedVuCommandEncodeMilliseconds;
        Ps2RenderPerformanceMetrics LastPerformanceMetrics;
        ::float4 LastResolvedViewport;
        ::float4 LastSubmittedScreenBounds;
        ::float4 LastSubmittedTriangleBoundsA;
        ::float4 LastSubmittedTriangleBoundsB;
        ::float4 LastSubmittedTriangleVertexA0;
        ::float4 LastSubmittedTriangleVertexA1;
        ::float4 LastSubmittedTriangleVertexA2;
        ::float4 LastSubmittedTriangleVertexB0;
        ::float4 LastSubmittedTriangleVertexB1;
        ::float4 LastSubmittedTriangleVertexB2;
    };
}
