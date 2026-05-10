#pragma once

#include <cstdint>
#include <vector>

namespace helengine::ps2 {
    inline std::vector<std::uint8_t> BuildCheckerboardDebugTexture(int width, int height) {
        std::vector<std::uint8_t> pixels;
        if (width <= 0 || height <= 0) {
            return pixels;
        }

        pixels.resize(static_cast<std::size_t>(width) * static_cast<std::size_t>(height) * 4u);

        for (int y = 0; y < height; ++y) {
            for (int x = 0; x < width; ++x) {
                std::uint8_t r = 255;
                std::uint8_t g = 255;
                std::uint8_t b = 255;
                std::uint8_t a = 255;

                if (((x + y) & 1) != 0) {
                    r = 0;
                    g = 0;
                    b = 0;
                }

                if (x == 0 && y == 0) {
                    r = 255;
                    g = 0;
                    b = 0;
                } else if (x == width - 1 && y == 0) {
                    r = 0;
                    g = 255;
                    b = 0;
                } else if (x == 0 && y == height - 1) {
                    r = 0;
                    g = 0;
                    b = 255;
                } else if (x == width - 1 && y == height - 1) {
                    r = 255;
                    g = 255;
                    b = 0;
                }

                const std::size_t index = static_cast<std::size_t>(y * width + x) * 4u;
                pixels[index + 0] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = a;
            }
        }

        return pixels;
    }
}
