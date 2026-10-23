#include "platform/ps2/rendering/vu/Ps2VuNearPlaneSliceClassifier.hpp"

#include <cmath>

namespace helengine::ps2 {
    namespace {
        constexpr float NearPlaneClassificationEpsilon = 0.00001f;
        constexpr float CameraPlaneClassificationEpsilon = 0.0001f;
        constexpr float FrustumSideClassificationEpsilon = 0.00001f;
    }

    Ps2VuNearPlaneRoute Ps2VuNearPlaneSliceClassifier::Classify(
        const Ps2VuSourceSliceBounds& bounds,
        const ::float4x4& worldViewProjection) {
        const RealEvaluation centerClipZ = (bounds.Center.X * worldViewProjection.M13)
            + (bounds.Center.Y * worldViewProjection.M23)
            + (bounds.Center.Z * worldViewProjection.M33)
            + worldViewProjection.M43;
        const RealEvaluation radiusClipZ = (RealMath::Abs(worldViewProjection.M13) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M23) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M33) * bounds.Extents.Z);
        const RealEvaluation minimumClipZ = centerClipZ - radiusClipZ;
        const RealEvaluation maximumClipZ = centerClipZ + radiusClipZ;
        const RealEvaluation centerClipW = (bounds.Center.X * worldViewProjection.M14)
            + (bounds.Center.Y * worldViewProjection.M24)
            + (bounds.Center.Z * worldViewProjection.M34)
            + worldViewProjection.M44;
        const RealEvaluation radiusClipW = (RealMath::Abs(worldViewProjection.M14) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M24) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M34) * bounds.Extents.Z);
        const RealEvaluation minimumClipW = centerClipW - radiusClipW;
        const RealEvaluation maximumClipW = centerClipW + radiusClipW;
        const RealEvaluation centerClipX = (bounds.Center.X * worldViewProjection.M11)
            + (bounds.Center.Y * worldViewProjection.M21)
            + (bounds.Center.Z * worldViewProjection.M31)
            + worldViewProjection.M41;
        const RealEvaluation centerClipY = (bounds.Center.X * worldViewProjection.M12)
            + (bounds.Center.Y * worldViewProjection.M22)
            + (bounds.Center.Z * worldViewProjection.M32)
            + worldViewProjection.M42;
        const RealEvaluation centerClipLeft = centerClipX + centerClipW;
        const RealEvaluation radiusClipLeft = (RealMath::Abs(worldViewProjection.M11 + worldViewProjection.M14) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M21 + worldViewProjection.M24) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M31 + worldViewProjection.M34) * bounds.Extents.Z);
        const RealEvaluation minimumClipLeft = centerClipLeft - radiusClipLeft;
        const RealEvaluation maximumClipLeft = centerClipLeft + radiusClipLeft;
        const RealEvaluation centerClipRight = centerClipW - centerClipX;
        const RealEvaluation radiusClipRight = (RealMath::Abs(worldViewProjection.M14 - worldViewProjection.M11) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M24 - worldViewProjection.M21) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M34 - worldViewProjection.M31) * bounds.Extents.Z);
        const RealEvaluation minimumClipRight = centerClipRight - radiusClipRight;
        const RealEvaluation maximumClipRight = centerClipRight + radiusClipRight;
        const RealEvaluation centerClipBottom = centerClipY + centerClipW;
        const RealEvaluation radiusClipBottom = (RealMath::Abs(worldViewProjection.M12 + worldViewProjection.M14) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M22 + worldViewProjection.M24) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M32 + worldViewProjection.M34) * bounds.Extents.Z);
        const RealEvaluation minimumClipBottom = centerClipBottom - radiusClipBottom;
        const RealEvaluation maximumClipBottom = centerClipBottom + radiusClipBottom;
        const RealEvaluation centerClipTop = centerClipW - centerClipY;
        const RealEvaluation radiusClipTop = (RealMath::Abs(worldViewProjection.M14 - worldViewProjection.M12) * bounds.Extents.X)
            + (RealMath::Abs(worldViewProjection.M24 - worldViewProjection.M22) * bounds.Extents.Y)
            + (RealMath::Abs(worldViewProjection.M34 - worldViewProjection.M32) * bounds.Extents.Z);
        const RealEvaluation minimumClipTop = centerClipTop - radiusClipTop;
        const RealEvaluation maximumClipTop = centerClipTop + radiusClipTop;
        if (minimumClipZ >= NearPlaneClassificationEpsilon
            && minimumClipW >= CameraPlaneClassificationEpsilon
            && minimumClipLeft >= FrustumSideClassificationEpsilon
            && minimumClipRight >= FrustumSideClassificationEpsilon
            && minimumClipBottom >= FrustumSideClassificationEpsilon
            && minimumClipTop >= FrustumSideClassificationEpsilon) {
            return Ps2VuNearPlaneRoute::Fast;
        } else if (maximumClipZ < -NearPlaneClassificationEpsilon
            || maximumClipW < CameraPlaneClassificationEpsilon
            || maximumClipLeft < -FrustumSideClassificationEpsilon
            || maximumClipRight < -FrustumSideClassificationEpsilon
            || maximumClipBottom < -FrustumSideClassificationEpsilon
            || maximumClipTop < -FrustumSideClassificationEpsilon) {
            return Ps2VuNearPlaneRoute::Rejected;
        }

        return Ps2VuNearPlaneRoute::Clipped;
    }
}
