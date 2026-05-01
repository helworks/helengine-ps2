#include <cassert>
#include <cstdint>

#include "platform/ps2/Ps2DebugTexture.hpp"

int main() {
    const auto pixels = helengine::ps2::BuildCheckerboardDebugTexture(4, 4);

    assert(pixels.size() == 4u * 4u * 4u);

    const auto pixelAt = [&](int x, int y) -> const std::uint8_t* {
        return &pixels[static_cast<std::size_t>((y * 4 + x) * 4)];
    };

    const std::uint8_t* topLeft = pixelAt(0, 0);
    const std::uint8_t* topRight = pixelAt(3, 0);
    const std::uint8_t* bottomLeft = pixelAt(0, 3);
    const std::uint8_t* bottomRight = pixelAt(3, 3);
    const std::uint8_t* center = pixelAt(1, 1);

    assert(topLeft[0] == 255 && topLeft[1] == 0 && topLeft[2] == 0 && topLeft[3] == 255);
    assert(topRight[0] == 0 && topRight[1] == 255 && topRight[2] == 0 && topRight[3] == 255);
    assert(bottomLeft[0] == 0 && bottomLeft[1] == 0 && bottomLeft[2] == 255 && bottomLeft[3] == 255);
    assert(bottomRight[0] == 255 && bottomRight[1] == 255 && bottomRight[2] == 0 && bottomRight[3] == 255);
    assert(center[0] == 255 && center[1] == 255 && center[2] == 255 && center[3] == 255);

    return 0;
}
