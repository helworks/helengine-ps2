#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace helengine::ps2 {
    /// Stateless playback arithmetic shared by the EE renderer and host equivalence tests.
    class Ps2TexturedQuadMath final {
    public:
        /// Matches positive lround(component / 255.0 * 128.0) for every byte value.
        static uint8_t ResolveColor(uint8_t component) {
            return static_cast<uint8_t>((static_cast<uint32_t>(component) * 128u + 127u) / 255u);
        }

        /// Clips primitive float fields in place while retaining the renderer's double recomposition.
        /// Fully contained finite extents use exact endpoint ratios; all other inputs retain division.
        static bool TryClip(float clipX, float clipY, float clipWidth, float clipHeight,
            float& boundsX, float& boundsY, float& width, float& height,
            float& sourceX, float& sourceY, float& sourceWidth, float& sourceHeight) {
            const double boundsLeft = boundsX;
            const double boundsTop = boundsY;
            const double boundsRight = static_cast<double>(boundsX) + static_cast<double>(width);
            const double boundsBottom = static_cast<double>(boundsY) + static_cast<double>(height);
            const double clipLeft = clipX;
            const double clipTop = clipY;
            const double clipRight = static_cast<double>(clipX) + static_cast<double>(clipWidth);
            const double clipBottom = static_cast<double>(clipY) + static_cast<double>(clipHeight);
            const double clippedLeft = std::max(boundsLeft, clipLeft);
            const double clippedTop = std::max(boundsTop, clipTop);
            const double clippedRight = std::min(boundsRight, clipRight);
            const double clippedBottom = std::min(boundsBottom, clipBottom);
            const double boundsWidth = boundsRight - boundsLeft;
            const double boundsHeight = boundsBottom - boundsTop;
            if (boundsWidth <= 0.0 || boundsHeight <= 0.0 || clippedRight <= clippedLeft || clippedBottom <= clippedTop) {
                return false;
            }

            const bool contained = std::isfinite(boundsWidth) && std::isfinite(boundsHeight)
                && boundsWidth > 0.0 && boundsHeight > 0.0
                && clippedLeft == boundsLeft && clippedTop == boundsTop
                && clippedRight == boundsRight && clippedBottom == boundsBottom;
            const double leftRatio = contained ? 0.0 : (clippedLeft - boundsLeft) / boundsWidth;
            const double topRatio = contained ? 0.0 : (clippedTop - boundsTop) / boundsHeight;
            const double rightRatio = contained ? 1.0 : (clippedRight - boundsLeft) / boundsWidth;
            const double bottomRatio = contained ? 1.0 : (clippedBottom - boundsTop) / boundsHeight;
            const double sourceLeft = static_cast<double>(sourceX) + (static_cast<double>(sourceWidth) * leftRatio);
            const double sourceTop = static_cast<double>(sourceY) + (static_cast<double>(sourceHeight) * topRatio);
            const double sourceRight = static_cast<double>(sourceX) + (static_cast<double>(sourceWidth) * rightRatio);
            const double sourceBottom = static_cast<double>(sourceY) + (static_cast<double>(sourceHeight) * bottomRatio);
            boundsX = static_cast<float>(clippedLeft);
            boundsY = static_cast<float>(clippedTop);
            width = static_cast<float>(clippedRight - clippedLeft);
            height = static_cast<float>(clippedBottom - clippedTop);
            sourceX = static_cast<float>(sourceLeft);
            sourceY = static_cast<float>(sourceTop);
            sourceWidth = static_cast<float>(sourceRight - sourceLeft);
            sourceHeight = static_cast<float>(sourceBottom - sourceTop);
            return true;
        }
    };
}
