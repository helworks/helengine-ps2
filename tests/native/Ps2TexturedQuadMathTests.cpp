#include "platform/ps2/Ps2TexturedQuadMath.hpp"

#include <array>
#include <cassert>
#include <cmath>
#include <cstring>
#include <iostream>
#include <limits>

using Rect = std::array<float, 4>;

/// Retains the previous renderer arithmetic as the independent equivalence oracle.
static bool LegacyClip(const Rect& clip, Rect& bounds, Rect& source) {
    const double boundsLeft = bounds[0];
    const double boundsTop = bounds[1];
    const double boundsRight = static_cast<double>(bounds[0]) + static_cast<double>(bounds[2]);
    const double boundsBottom = static_cast<double>(bounds[1]) + static_cast<double>(bounds[3]);
    const double clipLeft = clip[0];
    const double clipTop = clip[1];
    const double clipRight = static_cast<double>(clip[0]) + static_cast<double>(clip[2]);
    const double clipBottom = static_cast<double>(clip[1]) + static_cast<double>(clip[3]);
    const double clippedLeft = std::max(boundsLeft, clipLeft);
    const double clippedTop = std::max(boundsTop, clipTop);
    const double clippedRight = std::min(boundsRight, clipRight);
    const double clippedBottom = std::min(boundsBottom, clipBottom);
    const double boundsWidth = boundsRight - boundsLeft;
    const double boundsHeight = boundsBottom - boundsTop;
    if (boundsWidth <= 0.0 || boundsHeight <= 0.0 || clippedRight <= clippedLeft || clippedBottom <= clippedTop) return false;
    const double leftRatio = (clippedLeft - boundsLeft) / boundsWidth;
    const double topRatio = (clippedTop - boundsTop) / boundsHeight;
    const double rightRatio = (clippedRight - boundsLeft) / boundsWidth;
    const double bottomRatio = (clippedBottom - boundsTop) / boundsHeight;
    const double sourceLeft = static_cast<double>(source[0]) + static_cast<double>(source[2]) * leftRatio;
    const double sourceTop = static_cast<double>(source[1]) + static_cast<double>(source[3]) * topRatio;
    const double sourceRight = static_cast<double>(source[0]) + static_cast<double>(source[2]) * rightRatio;
    const double sourceBottom = static_cast<double>(source[1]) + static_cast<double>(source[3]) * bottomRatio;
    bounds = { static_cast<float>(clippedLeft), static_cast<float>(clippedTop),
        static_cast<float>(clippedRight - clippedLeft), static_cast<float>(clippedBottom - clippedTop) };
    source = { static_cast<float>(sourceLeft), static_cast<float>(sourceTop),
        static_cast<float>(sourceRight - sourceLeft), static_cast<float>(sourceBottom - sourceTop) };
    return true;
}

/// Verifies result and every output bit, including NaN payloads and negative zero.
static void Verify(const Rect& clip, const Rect& inputBounds, const Rect& inputSource) {
    Rect expectedBounds = inputBounds;
    Rect expectedSource = inputSource;
    Rect actualBounds = inputBounds;
    Rect actualSource = inputSource;
    const bool expected = LegacyClip(clip, expectedBounds, expectedSource);
    const bool actual = helengine::ps2::Ps2TexturedQuadMath::TryClip(clip[0], clip[1], clip[2], clip[3],
        actualBounds[0], actualBounds[1], actualBounds[2], actualBounds[3],
        actualSource[0], actualSource[1], actualSource[2], actualSource[3]);
    assert(expected == actual);
    assert(std::memcmp(expectedBounds.data(), actualBounds.data(), sizeof(Rect)) == 0);
    assert(std::memcmp(expectedSource.data(), actualSource.data(), sizeof(Rect)) == 0);
}

/// Generates repeatable bit patterns without relying on locale or random-library distributions.
static uint32_t NextBits(uint32_t& state) {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return state;
}

/// Constructs an arbitrary float pattern without aliasing or conversion of nonfinite values.
static float FromBits(uint32_t bits) {
    float value;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

int main() {
    for (unsigned component = 0; component < 256; ++component) {
        const double normalized = static_cast<double>(component) / 255.0;
        const auto expected = static_cast<uint8_t>(std::clamp(std::lround(normalized * 128.0), 0l, 128l));
        assert(helengine::ps2::Ps2TexturedQuadMath::ResolveColor(static_cast<uint8_t>(component)) == expected);
    }
    const float tiny = std::numeric_limits<float>::denorm_min();
    const float largest = std::numeric_limits<float>::max();
    const float infinity = std::numeric_limits<float>::infinity();
    const float nan = FromBits(0x7fc01234u);
    const Rect ordinarySource { 0.25f, 0.125f, 0.5f, 0.75f };
    Verify({ -100, -100, 500, 500 }, { 0, 0, 20, 30 }, ordinarySource);
    Verify({ -100, -100, 500, 500 }, { -0.0f, -0.0f, 20, 30 }, { -0.0f, -0.0f, -0.0f, -0.0f });
    Verify({ -100, -100, 500, 500 }, { 10, 20, 20, 30 }, { 1, 1, tiny, tiny });
    Verify({ -largest, -largest, largest, largest }, { -largest, -largest, largest, largest }, ordinarySource);
    Verify({ 1e20f, 1e20f, 1, 1 }, { 1e20f, 1e20f, 1, 1 }, ordinarySource);
    Verify({ 10, 15, 5, 10 }, { 0, 0, 20, 30 }, ordinarySource);
    Verify({ 100, 100, 5, 10 }, { 0, 0, 20, 30 }, ordinarySource);
    Verify({ 0, 0, 5, 5 }, { 0, 0, 0, 30 }, ordinarySource);
    Verify({ 0, 0, 5, 5 }, { 0, 0, -20, 30 }, ordinarySource);
    Verify({ 0, 0, infinity, infinity }, { 0, 0, 20, 30 }, ordinarySource);
    Verify({ 0, 0, infinity, infinity }, { 0, 0, infinity, 30 }, ordinarySource);
    Verify({ 0, 0, 50, 50 }, { nan, 0, 20, 30 }, ordinarySource);
    Verify({ nan, 0, 50, 50 }, { 0, 0, 20, 30 }, ordinarySource);
    Verify({ 0, 0, 50, 50 }, { 0, 0, 20, 30 }, { nan, -infinity, infinity, tiny });
    const std::array<float, 11> edgeValues { 0.0f, -0.0f, tiny, -tiny, 1.0f, -1.0f, largest, -largest, infinity, -infinity, nan };
    for (float edge : edgeValues) {
        for (int field = 0; field < 4; ++field) {
            Rect bounds { 0, 0, 20, 30 };
            Rect source = ordinarySource;
            Rect clip { -100, -100, 500, 500 };
            bounds[field] = edge;
            Verify(clip, bounds, source);
            bounds = { 0, 0, 20, 30 };
            source[field] = edge;
            Verify(clip, bounds, source);
            source = ordinarySource;
            clip[field] = edge;
            Verify(clip, bounds, source);
        }
    }
    uint32_t state = 0xa9314b21u;
    for (int iteration = 0; iteration < 100000; ++iteration) {
        Rect bounds;
        Rect clip;
        Rect source;
        for (int field = 0; field < 4; ++field) {
            bounds[field] = FromBits(NextBits(state));
            clip[field] = FromBits(NextBits(state));
            source[field] = FromBits(NextBits(state));
        }
        Verify(iteration % 2 == 0 ? bounds : clip, bounds, source);
    }
    std::cout << "PS2_TEXTURED_QUAD_MATH_EQUIVALENCE_PASS colors=256 randomCases=100000 edgeCases=146\n";
}
