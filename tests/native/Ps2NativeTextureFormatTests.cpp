#include "platform/ps2/rendering/Ps2NativeTextureLayout.hpp"

#include <array>
#include <cassert>
#include <cstdint>
#include <iostream>
#include <vector>

using namespace helengine::ps2;

/// Exercises real GS format and transfer differences independently of generated engine objects or a console.
int main() {
    constexpr std::array<std::uint32_t, 13> hardware = {0, 19, 20, 1, 2, 10, 27, 36, 44, 48, 49, 50, 58};
    constexpr std::array<std::uint32_t, 13> bits = {32, 8, 4, 24, 16, 16, 8, 4, 4, 32, 24, 16, 16};
    std::size_t validated = 0;
    for (std::uint32_t format = 0; format < 13; format++) {
        std::uint32_t psm;
        assert(TryResolveNativeTexturePsm(format, psm));
        assert(psm == hardware[format]);
        const bool indexed = format == 1 || format == 2 || format == 6 || format == 7 || format == 8;
        for (std::uint32_t clut : {0u, 4u, 5u}) {
            if (!indexed && clut != 0) continue;
            Ps2NativeTextureLayout layout;
            assert(TryBuildNativeTextureStorage(format, 3, 5, layout));
            assert(layout.TransferBits == bits[format]);
            assert(layout.PixelBytes == (15 * bits[format] + 7) / 8);
            assert(layout.DmaPixelBytes % 16 == 0 && layout.DmaPixelBytes >= layout.PixelBytes);
            assert(layout.VramPixelBytes == 8192);
            assert(layout.BufferWidth64 == (format == 1 || format == 2 ? 2 : 1));
            std::vector<std::uint8_t> pixels(layout.PixelBytes, 0);
            const std::uint32_t entries = indexed ? (bits[format] == 8 ? 256 : 16) : 0;
            std::vector<std::uint8_t> palette(entries * (clut == 0 ? 4 : 2), 0);
            const std::uint32_t alpha = format == 3 || format == 10 ? 0 : clut != 0 || format == 4 || format == 5 || format == 11 || format == 12 ? 2 : 1;
            assert(TryBuildNativeTextureLayout(format, format, clut, alpha, 3, 5, pixels.data(), pixels.size(),
                palette.empty() ? nullptr : palette.data(), palette.size(), layout));
            assert(layout.HardwarePsm == hardware[format]);
            assert(layout.PaletteBytes == palette.size());
            assert(layout.VramPaletteBytes == (indexed ? 8192u : 0u));
            assert(layout.ClutWidth == (entries == 16 ? 8u : entries == 256 ? 16u : 0u));
            assert(layout.ClutHeight == (entries == 16 ? 2u : entries == 256 ? 16u : 0u));
            assert(layout.ClutHardwarePsm == hardware[clut]);
            assert(layout.TexA == (std::uint64_t(128) << 32) + (alpha == 0 ? 128u : 0u));
            assert(!TryBuildNativeTextureLayout(format, format, clut, alpha, 3, 5, pixels.data(), pixels.size() - 1,
                palette.empty() ? nullptr : palette.data(), palette.size(), layout));
            assert(layout.PixelBytes == 0);
            assert(!TryBuildNativeTextureLayout(format, format, clut, alpha, 3, 5, pixels.data(), pixels.size() + 1,
                palette.empty() ? nullptr : palette.data(), palette.size(), layout));
            assert(!TryBuildNativeTextureLayout(format, format, clut, 3, 3, 5, pixels.data(), pixels.size(),
                palette.empty() ? nullptr : palette.data(), palette.size(), layout));
            if (indexed) {
                assert(!TryBuildNativeTextureLayout(format, format, clut, alpha, 3, 5, pixels.data(), pixels.size(), nullptr, 0, layout));
                assert(!TryBuildNativeTextureLayout(format, format, clut, alpha, 3, 5, pixels.data(), pixels.size(), palette.data(), palette.size() - 1, layout));
            }
            validated++;
        }
    }
    assert(validated == 23);
    Ps2NativeTextureLayout layout;
    assert(TryBuildNativeTextureStorage(6, 65, 33, layout));
    assert(layout.VramPixelBytes == 32768 && layout.BufferWidth64 == 2 && layout.PixelBytes == 2145);
    assert(TryBuildNativeTextureStorage(1, 65, 33, layout));
    assert(layout.VramPixelBytes == 8192 && layout.BufferWidth64 == 2 && layout.PixelBytes == 2145);
    assert(TryBuildNativeTextureStorage(12, 65, 65, layout));
    assert(layout.HardwarePsm == 58 && layout.VramPixelBytes == 32768 && layout.PixelBytes == 8450);
    assert(TryBuildNativeTextureStorage(3, 3, 5, layout));
    assert(layout.PixelBytes == 45 && layout.DmaPixelBytes == 48);
    assert(TryBuildNativeTextureStorage(10, 3, 5, layout));
    assert(layout.HardwarePsm == 49 && layout.PixelBytes == 45 && layout.DmaPixelBytes == 48);
    assert(TryBuildNativeTextureStorage(0, 1024, 1024, layout));
    assert(layout.PixelBytes == 4194304 && layout.VramPixelBytes == 4194304);
    assert(!TryBuildNativeTextureStorage(0, 1025, 1, layout));
    assert(!TryBuildNativeTextureStorage(0, 1, 0, layout));
    assert(!TryBuildNativeTextureStorage(0, 0xffffffffu, 0xffffffffu, layout));
    std::uint32_t unknown;
    assert(!TryResolveNativeTexturePsm(13, unknown));
    assert(!TryResolveNativeTexturePsm(0xffffffffu, unknown));
    assert(NativeTexturePagePadding(0) == 0);
    assert(NativeTexturePagePadding(8192) == 0);
    assert(NativeTexturePagePadding(8448) == 7936);
    assert(NativeTexturePagePadding(8191) == 1);

    const std::uint8_t stream[] = {0x21, 0x43, 0x65, 0x07};
    std::vector<std::uint8_t> palette32(64, 0);
    palette32[1 * 4 + 3] = 17; palette32[4 * 4 + 3] = 64;
    assert(TryBuildNativeTextureLayout(7, 7, 0, 1, 3, 2, stream, 3, palette32.data(), palette32.size(), layout));
    assert(SampleNativeTextureAlpha(layout, stream, palette32.data(), 0) == 17);
    assert(SampleNativeTextureAlpha(layout, stream, palette32.data(), 3) == 64); // Odd row continues in the HIGH nibble.
    const std::uint8_t badPadding[] = {0x21, 0xf3};
    assert(!TryBuildNativeTextureLayout(2, 2, 0, 1, 3, 1, badPadding, 2, palette32.data(), palette32.size(), layout));
    assert(!TryBuildNativeTextureLayout(0, 3, 0, 0, 1, 1, stream, 3, nullptr, 0, layout));
    const std::uint8_t opaque24[] = {1, 2, 3};
    assert(!TryBuildNativeTextureLayout(3, 3, 0, 1, 1, 1, opaque24, 3, nullptr, 0, layout));
    assert(TryBuildNativeTextureLayout(3, 3, 0, 0, 1, 1, opaque24, 3, nullptr, 0, layout));
    assert(SampleNativeTextureAlpha(layout, opaque24, nullptr, 0) == 128);
    const std::uint8_t binary16[] = {0xff, 0x7f, 0xff, 0xff};
    assert(TryBuildNativeTextureLayout(5, 5, 0, 2, 2, 1, binary16, 4, nullptr, 0, layout));
    assert(SampleNativeTextureAlpha(layout, binary16, nullptr, 0) == 0);
    assert(SampleNativeTextureAlpha(layout, binary16, nullptr, 1) == 128);
    assert(!TryBuildNativeTextureLayout(5, 5, 0, 1, 2, 1, binary16, 4, nullptr, 0, layout));
    assert(TryBuildNativeTextureLayout(5, 5, 0, 0, 2, 1, binary16, 4, nullptr, 0, layout));
    assert(SampleNativeTextureAlpha(layout, binary16, nullptr, 0) == 128);
    const std::uint8_t indexed8[] = {8, 16};
    std::vector<std::uint8_t> palette16(512, 0);
    palette16[16 * 2 + 1] = 128;
    assert(TryBuildNativeTextureLayout(6, 6, 5, 2, 2, 1, indexed8, 2, palette16.data(), palette16.size(), layout));
    assert(SampleNativeTextureAlpha(layout, indexed8, palette16.data(), 0) == 128);
    assert(SampleNativeTextureAlpha(layout, indexed8, palette16.data(), 1) == 0);
    std::uint64_t prefix[8] = {};
    BuildNativeTextureStatePrefix(0x1234, 0x47, 0x5678, 0x14, 0x8000000000ull, prefix);
    assert(prefix[0] == 0x1000000000000003ull && prefix[1] == 0x0e);
    assert(prefix[2] == 0x1234 && prefix[3] == 0x47);
    assert(prefix[4] == 0x5678 && prefix[5] == 0x14);
    assert(prefix[6] == 0x8000000000ull && prefix[7] == 0x3b);
    std::cout << "PASS: 13 native PSMs / 23 CLUT combinations, compact transfers, page edges, TEXA, CSM1 alpha and unchanged VU prefix.\n";
}
