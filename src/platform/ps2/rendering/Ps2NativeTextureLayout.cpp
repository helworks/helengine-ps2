#include "platform/ps2/rendering/Ps2NativeTextureLayout.hpp"

namespace helengine::ps2 {
    bool PatchNativeRgba32Region(const Ps2NativeTextureLayout& layout, std::uint8_t* pixels, std::size_t pixelBytes,
        std::uint32_t alphaMode, bool preserveRawAlpha, std::int32_t x, std::int32_t y, std::int32_t width,
        std::int32_t height, const std::uint8_t* rgba8, std::size_t sourceBytes, std::int32_t sourceRowPitch) {
        if (layout.HardwarePsm != 0 || layout.TransferBits != 32 || layout.PaletteBytes != 0 || pixels == nullptr
            || rgba8 == nullptr || pixelBytes < layout.PixelBytes || alphaMode > 2 || x < 0 || y < 0
            || width <= 0 || height <= 0 || sourceRowPitch <= 0
            || std::uint64_t(x) + width > layout.Width || std::uint64_t(y) + height > layout.Height
            || std::uint64_t(width) * 4 > std::uint64_t(sourceRowPitch)
            || std::uint64_t(height - 1) * sourceRowPitch + std::uint64_t(width) * 4 > sourceBytes
            || std::uint64_t(layout.Width) * layout.Height * 4 != layout.PixelBytes) return false;
        for (std::int32_t row = 0; row < height; row++) {
            const std::uint8_t* source = rgba8 + std::size_t(row) * sourceRowPitch;
            std::uint8_t* target = pixels + (std::size_t(y + row) * layout.Width + x) * 4;
            for (std::int32_t column = 0; column < width; column++) {
                target[0] = source[0]; target[1] = source[1]; target[2] = source[2];
                target[3] = preserveRawAlpha ? source[3] : alphaMode == 0 ? 128
                    : alphaMode == 2 ? (source[3] >= 128 ? 128 : 0) : (std::uint32_t(source[3]) * 128 + 127) / 255;
                source += 4; target += 4;
            }
        }
        return true;
    }

    bool TryResolveNativeTexturePsm(std::uint32_t platformPsm, std::uint32_t& hardwarePsm) {
        static constexpr std::uint32_t modes[] = {0, 19, 20, 1, 2, 10, 27, 36, 44, 48, 49, 50, 58};
        hardwarePsm = 0;
        if (platformPsm >= sizeof(modes) / sizeof(modes[0])) return false;
        hardwarePsm = modes[platformPsm];
        return true;
    }

    bool TryBuildNativeTextureStorage(std::uint32_t platformPsm, std::uint32_t width, std::uint32_t height, Ps2NativeTextureLayout& layout) {
        layout = {};
        if (width == 0 || height == 0 || width > 1024 || height > 1024 || !TryResolveNativeTexturePsm(platformPsm, layout.HardwarePsm)) return false;
        std::uint32_t pageWidth = 64;
        std::uint32_t pageHeight = 32;
        std::uint32_t bits = 32;
        switch (platformPsm) {
            case 1: bits = 8; pageWidth = 128; pageHeight = 64; break;
            case 2: bits = 4; pageWidth = 128; pageHeight = 128; break;
            case 3: case 10: bits = 24; break;
            case 4: case 5: case 11: case 12: bits = 16; pageHeight = 64; break;
            case 6: bits = 8; break;
            case 7: case 8: bits = 4; break;
            default: break;
        }
        layout.Width = width;
        layout.Height = height;
        layout.TransferBits = bits;
        layout.BufferWidth64 = ((width + pageWidth - 1) / pageWidth) * pageWidth / 64;
        layout.PixelBytes = (width * height * bits + 7) / 8;
        layout.DmaPixelBytes = (layout.PixelBytes + 15) & ~15u;
        layout.VramPixelBytes = ((width + pageWidth - 1) / pageWidth) * ((height + pageHeight - 1) / pageHeight) * 8192;
        return true;
    }

    bool TryBuildNativeTextureLayout(std::uint32_t format, std::uint32_t platformPsm, std::uint32_t clutPlatformPsm,
        std::uint32_t alphaMode, std::uint32_t width, std::uint32_t height,
        const std::uint8_t* pixels, std::size_t pixelBytes, const std::uint8_t* palette, std::size_t paletteBytes,
        Ps2NativeTextureLayout& layout) {
        layout = {};
        Ps2NativeTextureLayout candidate;
        if (format != platformPsm || !TryBuildNativeTextureStorage(platformPsm, width, height, candidate) ||
            pixels == nullptr || pixelBytes != candidate.PixelBytes || alphaMode > 2) return false;
        const bool indexed = platformPsm == 1 || platformPsm == 2 || platformPsm == 6 || platformPsm == 7 || platformPsm == 8;
        std::uint32_t alphaPsm = platformPsm;
        if (indexed) {
            if (clutPlatformPsm != 0 && clutPlatformPsm != 4 && clutPlatformPsm != 5) return false;
            const std::uint32_t entries = candidate.TransferBits == 8 ? 256 : 16;
            candidate.ClutWidth = entries == 16 ? 8 : 16;
            candidate.ClutHeight = entries == 16 ? 2 : 16;
            candidate.PaletteBytes = entries * (clutPlatformPsm == 0 ? 4 : 2);
            candidate.DmaPaletteBytes = (candidate.PaletteBytes + 15) & ~15u;
            Ps2NativeTextureLayout clut;
            if (palette == nullptr || paletteBytes != candidate.PaletteBytes ||
                !TryBuildNativeTextureStorage(clutPlatformPsm, candidate.ClutWidth, candidate.ClutHeight, clut)) return false;
            candidate.ClutHardwarePsm = clut.HardwarePsm;
            candidate.VramPaletteBytes = clut.VramPixelBytes;
            alphaPsm = clutPlatformPsm;
        } else if (clutPlatformPsm != 0 || paletteBytes != 0) {
            return false;
        }
        const bool sixteenBitAlpha = alphaPsm == 4 || alphaPsm == 5 || alphaPsm == 11 || alphaPsm == 12;
        const bool opaqueOnly = alphaPsm == 3 || alphaPsm == 10;
        if ((sixteenBitAlpha && alphaMode == 1) || (opaqueOnly && alphaMode != 0)) return false;
        const std::uint64_t alphaZero = alphaMode == 0 ? 128 : 0;
        candidate.TexA = alphaZero | (std::uint64_t(128) << 32);
        if (candidate.TransferBits == 4 && ((width * height) & 1) != 0 && (pixels[pixelBytes - 1] & 0xf0) != 0) return false;
        const std::uint8_t* rgba = indexed ? palette : pixels;
        const std::size_t rgbaBytes = indexed ? paletteBytes : pixelBytes;
        if (!sixteenBitAlpha && !opaqueOnly) {
            for (std::size_t offset = 3; offset < rgbaBytes; offset += 4) if (rgba[offset] > 128) return false;
        }
        layout = candidate;
        return true;
    }

    std::uint32_t NativeTexturePagePadding(std::uint32_t currentPointer) {
        return (0u - currentPointer) & 8191u;
    }

    void BuildNativeTextureStatePrefix(std::uint64_t test, std::uint64_t testRegister, std::uint64_t tex1,
        std::uint64_t tex1Register, std::uint64_t texA, std::uint64_t* words) {
        words[0] = 3u | (std::uint64_t(1) << 60);
        words[1] = 14;
        words[2] = test;
        words[3] = testRegister;
        words[4] = tex1;
        words[5] = tex1Register;
        words[6] = texA;
        words[7] = 0x3b;
    }

    std::uint8_t SampleNativeTextureAlpha(const Ps2NativeTextureLayout& layout, const std::uint8_t* pixels,
        const std::uint8_t* palette, std::size_t pixelIndex) {
        if (pixels == nullptr || pixelIndex >= std::size_t(layout.Width) * layout.Height) return 128;
        const bool indexed = layout.HardwarePsm == 19 || layout.HardwarePsm == 20 || layout.HardwarePsm == 27 || layout.HardwarePsm == 36 || layout.HardwarePsm == 44;
        const std::uint8_t* source = pixels;
        std::uint32_t psm = layout.HardwarePsm;
        std::size_t entry = pixelIndex;
        if (indexed) {
            if (palette == nullptr) return 128;
            entry = layout.TransferBits == 8 ? pixels[pixelIndex] : (pixels[pixelIndex / 2] >> ((pixelIndex & 1) * 4)) & 15;
            if (layout.TransferBits == 8) entry = (entry & ~std::size_t(24)) | ((entry & 8) << 1) | ((entry & 16) >> 1);
            source = palette;
            psm = layout.ClutHardwarePsm;
        }
        if (psm == 1 || psm == 49) return static_cast<std::uint8_t>(layout.TexA & 255);
        if (psm == 2 || psm == 10 || psm == 50 || psm == 58) {
            return static_cast<std::uint8_t>((source[entry * 2 + 1] & 128) != 0 ? (layout.TexA >> 32) & 255 : layout.TexA & 255);
        }
        return source[entry * 4 + 3];
    }
}
