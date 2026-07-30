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
        const float centerClipZ = (bounds.Center.X * worldViewProjection.M13)
            + (bounds.Center.Y * worldViewProjection.M23)
            + (bounds.Center.Z * worldViewProjection.M33)
            + worldViewProjection.M43;
        const float radiusClipZ = (std::abs(worldViewProjection.M13) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M23) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M33) * bounds.Extents.Z);
        const float minimumClipZ = centerClipZ - radiusClipZ;
        const float maximumClipZ = centerClipZ + radiusClipZ;
        const float centerClipW = (bounds.Center.X * worldViewProjection.M14)
            + (bounds.Center.Y * worldViewProjection.M24)
            + (bounds.Center.Z * worldViewProjection.M34)
            + worldViewProjection.M44;
        const float radiusClipW = (std::abs(worldViewProjection.M14) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M24) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M34) * bounds.Extents.Z);
        const float minimumClipW = centerClipW - radiusClipW;
        const float maximumClipW = centerClipW + radiusClipW;
        const float centerClipX = (bounds.Center.X * worldViewProjection.M11)
            + (bounds.Center.Y * worldViewProjection.M21)
            + (bounds.Center.Z * worldViewProjection.M31)
            + worldViewProjection.M41;
        const float centerClipY = (bounds.Center.X * worldViewProjection.M12)
            + (bounds.Center.Y * worldViewProjection.M22)
            + (bounds.Center.Z * worldViewProjection.M32)
            + worldViewProjection.M42;
        const float centerClipLeft = centerClipX + centerClipW;
        const float radiusClipLeft = (std::abs(worldViewProjection.M11 + worldViewProjection.M14) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M21 + worldViewProjection.M24) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M31 + worldViewProjection.M34) * bounds.Extents.Z);
        const float minimumClipLeft = centerClipLeft - radiusClipLeft;
        const float maximumClipLeft = centerClipLeft + radiusClipLeft;
        const float centerClipRight = centerClipW - centerClipX;
        const float radiusClipRight = (std::abs(worldViewProjection.M14 - worldViewProjection.M11) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M24 - worldViewProjection.M21) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M34 - worldViewProjection.M31) * bounds.Extents.Z);
        const float minimumClipRight = centerClipRight - radiusClipRight;
        const float maximumClipRight = centerClipRight + radiusClipRight;
        const float centerClipBottom = centerClipY + centerClipW;
        const float radiusClipBottom = (std::abs(worldViewProjection.M12 + worldViewProjection.M14) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M22 + worldViewProjection.M24) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M32 + worldViewProjection.M34) * bounds.Extents.Z);
        const float minimumClipBottom = centerClipBottom - radiusClipBottom;
        const float maximumClipBottom = centerClipBottom + radiusClipBottom;
        const float centerClipTop = centerClipW - centerClipY;
        const float radiusClipTop = (std::abs(worldViewProjection.M14 - worldViewProjection.M12) * bounds.Extents.X)
            + (std::abs(worldViewProjection.M24 - worldViewProjection.M22) * bounds.Extents.Y)
            + (std::abs(worldViewProjection.M34 - worldViewProjection.M32) * bounds.Extents.Z);
        const float minimumClipTop = centerClipTop - radiusClipTop;
        const float maximumClipTop = centerClipTop + radiusClipTop;
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
