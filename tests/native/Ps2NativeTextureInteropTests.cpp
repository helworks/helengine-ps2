#include "platform/ps2/rendering/Ps2NativeTextureLayout.hpp"

#include <algorithm>
#include <cassert>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>

namespace {
    /// Reads one managed fixture verbatim without translating its native payload.
    std::vector<std::uint8_t> Read(const std::filesystem::path& path) {
        std::ifstream stream(path, std::ios::binary);
        assert(stream.good());
        return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
    }

    /// Reads a bounded little-endian fixture or HELE word independently of native host endianness.
    std::uint32_t Word(const std::vector<std::uint8_t>& bytes, std::size_t offset, std::size_t width = 4) {
        assert(width <= 4 && offset + width <= bytes.size());
        std::uint32_t value = 0;
        for (std::size_t index = 0; index < width; index++) value |= std::uint32_t(bytes[offset + index]) << (8 * index);
        return value;
    }

    /// Checks the real version-two serialized asset independently of the managed reader.
    void CheckContainer(const std::vector<std::uint8_t>& bytes, const std::vector<std::uint8_t>& meta,
        const std::vector<std::uint8_t>& pixels, const std::vector<std::uint8_t>& palette) {
        assert(bytes.size() >= 12 && bytes[0] == 'H' && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'E');
        assert(bytes[4] == Word(meta, 60) && bytes[5] == 2 && Word(bytes, 6, 2) == 2);
        std::size_t offset = 12;
        const auto identityBytes = Word(bytes, offset);
        assert(identityBytes == 11);
        offset += 4 + identityBytes;
        assert(Word(bytes, offset) == 0x23456789 && Word(bytes, offset + 4) == 1);
        offset += 8;
        assert(Word(bytes, offset, 2) == Word(meta, 4) && Word(bytes, offset + 2, 2) == Word(meta, 8));
        offset += 4;
        for (std::size_t index = 0; index < 4; index++) assert(bytes.at(offset + index) == Word(meta, 12 + index * 4));
        offset += 4;
        assert(Word(bytes, offset) == pixels.size());
        offset += 4;
        assert(offset + pixels.size() <= bytes.size());
        assert(std::equal(pixels.begin(), pixels.end(), bytes.begin() + offset));
        offset += pixels.size();
        assert(Word(bytes, offset) == palette.size());
        offset += 4;
        assert(offset + palette.size() == bytes.size());
        assert(std::equal(palette.begin(), palette.end(), bytes.begin() + offset));
    }
}

/// Exercises native validation and alpha sampling using actual cooker, codec and serializer outputs.
int main(int argc, char** argv) {
    assert(argc == 2);
    std::size_t count = 0;
    for (const auto& entry : std::filesystem::directory_iterator(argv[1])) {
        if (entry.path().extension() != ".meta") continue;
        auto prefix = entry.path(); prefix.replace_extension();
        const auto meta = Read(entry.path());
        const auto pixels = Read(prefix.string() + ".pixels");
        const auto palette = Read(prefix.string() + ".palette");
        const auto rgba = Read(prefix.string() + ".rgba");
        assert(meta.size() == 64 && Word(meta, 0) == 0x32505347);
        helengine::ps2::Ps2NativeTextureLayout layout;
        const auto width = Word(meta, 4), height = Word(meta, 8);
        const auto format = Word(meta, 12), psm = Word(meta, 16), clut = Word(meta, 20), alpha = Word(meta, 24);
        assert(helengine::ps2::TryBuildNativeTextureLayout(format, psm, clut, alpha, width, height,
            pixels.data(), pixels.size(), palette.empty() ? nullptr : palette.data(), palette.size(), layout));
        assert(layout.PixelBytes == Word(meta, 28) && layout.PaletteBytes == Word(meta, 32));
        assert(layout.VramPixelBytes == Word(meta, 36) && layout.VramPaletteBytes == Word(meta, 40));
        assert(layout.HardwarePsm == Word(meta, 44) && layout.ClutHardwarePsm == Word(meta, 48));
        assert(layout.BufferWidth64 == Word(meta, 52) && rgba.size() == Word(meta, 56));
        assert(layout.DmaPixelBytes % 16 == 0 && layout.DmaPixelBytes >= pixels.size());
        for (std::size_t pixel = 0; pixel < std::size_t(width) * height; pixel++) {
            const auto nativeAlpha = helengine::ps2::SampleNativeTextureAlpha(layout, pixels.data(), palette.data(), pixel);
            assert((std::uint32_t(nativeAlpha) * 255 + 64) / 128 == rgba[pixel * 4 + 3]);
        }
        CheckContainer(Read(prefix.string() + ".ps2tex"), meta, pixels, palette);
        helengine::ps2::Ps2NativeTextureLayout rejected;
        assert(!helengine::ps2::TryBuildNativeTextureLayout(format, psm, clut, alpha, width, height,
            pixels.data(), pixels.size() - 1, palette.data(), palette.size(), rejected));
        assert(rejected.Width == 0 && rejected.PixelBytes == 0);
        assert(!helengine::ps2::TryBuildNativeTextureLayout(format, psm, clut, 255, width, height,
            pixels.data(), pixels.size(), palette.data(), palette.size(), rejected));
        count++;
    }
    assert(count == 69);
    std::cout << count << " actual PS2 managed payloads passed native layout, sampled alpha and HELE v2 validation across 23 selections.\n";
}
