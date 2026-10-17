#include "platform/ps2/Ps2GlyphBatch.hpp"
#include <array>
#include <cassert>
#include <cstring>
#include <iostream>

/// Keeps the previous floating-point color formula as an independent reference.
static uint8_t LegacyChannel(uint8_t value) {
    return static_cast<uint8_t>(std::clamp(std::lround(static_cast<double>(value) / 255.0 * 128.0), 0l, 128l));
}

/// Creates the previous individual sprite's fixed-point vertices with the actual installed SDK conversions.
static std::array<GSPRIMUVPOINT, 2> LegacyVertices(const GSGLOBAL& global, const GSTEXTURE& texture,
    float x, float y, float width, float height, float u, float v, float uw, float vh,
    uint8_t r, uint8_t g, uint8_t b, uint8_t a, bool clipped) {
    const double tx = static_cast<double>(u) * texture.Width;
    const double ty = static_cast<double>(v) * texture.Height;
    const double tw = static_cast<double>(uw) * texture.Width;
    const double th = static_cast<double>(vh) * texture.Height;
    const float right = clipped ? static_cast<float>(tx + tw)
        : static_cast<float>((static_cast<double>(u) + static_cast<double>(uw)) * texture.Width);
    const float bottom = clipped ? static_cast<float>(ty + th)
        : static_cast<float>((static_cast<double>(v) + static_cast<double>(vh)) * texture.Height);
    const uint64_t rgba = GS_SETREG_RGBAQ(LegacyChannel(r), LegacyChannel(g), LegacyChannel(b), LegacyChannel(a), 0);
    std::array<GSPRIMUVPOINT, 2> output{};
    output[0].rgbaq.color.rgbaq = rgba;
    output[0].rgbaq.tag = GS_RGBAQ;
    output[0].uv.coord.u = gsKit_float_to_int_u(&texture, static_cast<float>(tx));
    output[0].uv.coord.v = gsKit_float_to_int_v(&texture, static_cast<float>(ty));
    output[0].uv.tag = GS_UV;
    output[0].xyz2 = vertex_to_XYZ2(&global, x, y, 0);
    output[1].rgbaq.color.rgbaq = rgba;
    output[1].rgbaq.tag = GS_RGBAQ;
    output[1].uv.coord.u = gsKit_float_to_int_u(&texture, right);
    output[1].uv.coord.v = gsKit_float_to_int_v(&texture, bottom);
    output[1].uv.tag = GS_UV;
    output[1].xyz2 = vertex_to_XYZ2(&global, x + width, y + height, 0);
    return output;
}

/// Generates finite sample coordinates and colors deterministically.
static uint32_t Next(uint32_t& state) {
    state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state;
}

/// Compares every actual SDK vertex byte after preserving the renderer's original clipping step.
static void Verify(GSGLOBAL& global, GSTEXTURE& texture, float x, float y, float width, float height,
    float u, float v, float uw, float vh, uint32_t color, bool clipped) {
    if (clipped && !helengine::ps2::Ps2TexturedQuadMath::TryClip(-4, -8, 200, 150, x, y, width, height, u, v, uw, vh)) return;
    const auto expected = LegacyVertices(global, texture, x, y, width, height, u, v, uw, vh,
        color & 255, (color >> 8) & 255, (color >> 16) & 255, color >> 24, clipped);
    std::vector<GSPRIMUVPOINT> actual;
    helengine::ps2::Ps2GlyphBatch::Append(actual, &global, texture, x, y, width, height, u, v, uw, vh,
        color & 255, (color >> 8) & 255, (color >> 16) & 255, color >> 24, clipped);
    assert(actual.size() == 2);
    assert(std::memcmp(expected.data(), actual.data(), sizeof(expected)) == 0);
}

/// Exercises packed vertex equivalence, bounded split ordering and the unchanged uncapped static path.
int main() {
    GSGLOBAL global{};
    global.OffsetX = 2048 * 16;
    global.OffsetY = 2048 * 16;
    GSTEXTURE texture{};
    texture.Width = 341;
    texture.Height = 273;
    Verify(global, texture, -0.0f, -0.0f, 20, 30, -0.0f, 1, 1e-30f, 1e-30f, 0x01fe80ff, true);
    Verify(global, texture, -10.125f, 5.0625f, 30.5f, 9.5f, 0.1f, 0.2f, 0.3f, 0.4f, 0x807f0100, true);
    Verify(global, texture, 2, 2, 20, 30, 1, 1, 1e-30f, 1e-30f, 0xff000000, false);
    uint32_t state = 0x7fc319a2;
    for (int index = 0; index < 30000; ++index) {
        texture.Width = 1 + Next(state) % 1024;
        texture.Height = 1 + Next(state) % 1024;
        const float x = static_cast<int>(Next(state) % 5000) / 16.0f - 100;
        const float y = static_cast<int>(Next(state) % 4000) / 16.0f - 100;
        const float width = (Next(state) % 2000) / 16.0f;
        const float height = (Next(state) % 2000) / 16.0f;
        const float u = (Next(state) % 30000) / 17001.0f - 0.25f;
        const float v = (Next(state) % 30000) / 19303.0f - 0.25f;
        const float uw = (Next(state) % 4000) / 7137.0f;
        const float vh = (Next(state) % 4000) / 8161.0f;
        Verify(global, texture, x, y, width, height, u, v, uw, vh, Next(state), index % 2 == 0);
    }
    texture.Width = 512; texture.Height = 256;
    std::vector<GSPRIMUVPOINT> buffer;
    std::vector<uint64_t> colorsInOrder;
    unsigned flushes = 0;
    for (unsigned index = 0; index < 513; ++index) {
        if (helengine::ps2::Ps2GlyphBatch::IsFull(buffer.size())) {
            for (std::size_t vertex = 0; vertex < buffer.size(); vertex += 2) colorsInOrder.push_back(buffer[vertex].rgbaq.color.rgbaq);
            buffer.clear(); ++flushes;
        }
        helengine::ps2::Ps2GlyphBatch::Append(buffer, &global, texture, 0, 0, 10, 10, 0, 0, 0.1f, 0.1f,
            index & 255, 1, 2, 255, true);
        assert(buffer.size() <= 256);
        assert(buffer.capacity() <= 256);
    }
    for (std::size_t vertex = 0; vertex < buffer.size(); vertex += 2) colorsInOrder.push_back(buffer[vertex].rgbaq.color.rgbaq);
    assert(flushes == 4 && colorsInOrder.size() == 513);
    for (unsigned index = 0; index < colorsInOrder.size(); ++index) {
        assert((colorsInOrder[index] & 255) == helengine::ps2::Ps2TexturedQuadMath::ResolveColor(index & 255));
    }
    // Legacy static construction bypasses IsFull, retaining every glyph before copying its existing cache.
    buffer.clear();
    for (int index = 0; index < 257; ++index) {
        helengine::ps2::Ps2GlyphBatch::Append(buffer, &global, texture, 0, 0, 10, 10, 0, 0, 0.1f, 0.1f, 1, 2, 3, 255, false);
    }
    assert(buffer.size() == 514);
    std::cout << "PS2_GLYPH_BATCH_REAL_SDK_VERTICES_AND_BOUND_PASS randomCases=30000 normalQuads=513 staticQuads=257\n";
}
