#pragma once

#include "platform/ps2/Ps2TexturedQuadMath.hpp"
#include <gsKit.h>
#include <gsInline.h>
#include <vector>

namespace helengine::ps2 {
    /// Encodes the existing sprite coordinates into the renderer's existing vertex vector.
    class Ps2GlyphBatch final {
    public:
        /// Bounds normal playback batches; legacy static-cache construction explicitly bypasses it.
        static constexpr std::size_t MaximumQuads = 128;
        /// Reports whether the next quad would exceed the normal playback vertex limit.
        static bool IsFull(std::size_t vertexCount) { return vertexCount >= MaximumQuads * 2; }
        /// Appends two SDK vertices with per-quad color and the original path-specific UV arithmetic.
        /// Clipped sprites use sum-of-products; existing unclipped/static glyphs use product-of-sum.
        static void Append(std::vector<GSPRIMUVPOINT>& vertices, const GSGLOBAL* global, const GSTEXTURE& texture,
            float x, float y, float width, float height, float u, float v, float uw, float vh,
            uint8_t red, uint8_t green, uint8_t blue, uint8_t alpha, bool clipped) {
            const double textureWidth = static_cast<double>(texture.Width);
            const double textureHeight = static_cast<double>(texture.Height);
            const double sourceX = static_cast<double>(u) * textureWidth;
            const double sourceY = static_cast<double>(v) * textureHeight;
            const float sourceLeft = static_cast<float>(sourceX);
            const float sourceTop = static_cast<float>(sourceY);
            const float sourceRight = clipped
                ? static_cast<float>(sourceX + static_cast<double>(uw) * textureWidth)
                : static_cast<float>((static_cast<double>(u) + static_cast<double>(uw)) * textureWidth);
            const float sourceBottom = clipped
                ? static_cast<float>(sourceY + static_cast<double>(vh) * textureHeight)
                : static_cast<float>((static_cast<double>(v) + static_cast<double>(vh)) * textureHeight);
            const uint64_t rgba = GS_SETREG_RGBAQ(Ps2TexturedQuadMath::ResolveColor(red), Ps2TexturedQuadMath::ResolveColor(green),
                Ps2TexturedQuadMath::ResolveColor(blue), Ps2TexturedQuadMath::ResolveColor(alpha), 0);
            GSPRIMUVPOINT first {};
            first.rgbaq.color.rgbaq = rgba;
            first.rgbaq.tag = GS_RGBAQ;
            first.uv.coord.u = static_cast<u16>(gsKit_float_to_int_u(&texture, sourceLeft));
            first.uv.coord.v = static_cast<u16>(gsKit_float_to_int_v(&texture, sourceTop));
            first.uv.tag = GS_UV;
            first.xyz2 = vertex_to_XYZ2(global, x, y, 0);
            GSPRIMUVPOINT second {};
            second.rgbaq.color.rgbaq = rgba;
            second.rgbaq.tag = GS_RGBAQ;
            second.uv.coord.u = static_cast<u16>(gsKit_float_to_int_u(&texture, sourceRight));
            second.uv.coord.v = static_cast<u16>(gsKit_float_to_int_v(&texture, sourceBottom));
            second.uv.tag = GS_UV;
            second.xyz2 = vertex_to_XYZ2(global, x + width, y + height, 0);
            vertices.push_back(first);
            vertices.push_back(second);
        }
    };
}
