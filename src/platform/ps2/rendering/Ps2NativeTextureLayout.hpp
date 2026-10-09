#pragma once

#include <cstddef>
#include <cstdint>

namespace helengine::ps2 {
    /// Describes the native GS transfer, complete page reservation, CLUT, and alpha state of one validated cooked texture.
    struct Ps2NativeTextureLayout {
        /// Actual image width, independent of the GS buffer pitch.
        std::uint32_t Width = 0;
        /// Actual image height, independent of the GS page reservation.
        std::uint32_t Height = 0;
        /// Hardware PSM written unchanged to BITBLTBUF and TEX0.
        std::uint32_t HardwarePsm = 0;
        /// Hardware CPSM written unchanged to the CLUT transfer and TEX0.
        std::uint32_t ClutHardwarePsm = 0;
        /// Number of bits consumed per texel by a host-to-local IMAGE transfer.
        std::uint32_t TransferBits = 0;
        /// GS buffer width expressed in units of sixty-four pixels.
        std::uint32_t BufferWidth64 = 0;
        /// Compact texel stream size before DMA padding.
        std::uint32_t PixelBytes = 0;
        /// Sixteen-byte-rounded texel staging size, with zero padding.
        std::uint32_t DmaPixelBytes = 0;
        /// Compact CSM1 palette stream size, or zero for a direct texture.
        std::uint32_t PaletteBytes = 0;
        /// Sixteen-byte-rounded palette staging size.
        std::uint32_t DmaPaletteBytes = 0;
        /// Whole GS pages occupied by the texture in bytes.
        std::uint32_t VramPixelBytes = 0;
        /// Whole GS pages occupied by the CLUT in bytes.
        std::uint32_t VramPaletteBytes = 0;
        /// CLUT transfer width for its sixteen or two hundred fifty-six entries.
        std::uint32_t ClutWidth = 0;
        /// CLUT transfer height for its sixteen or two hundred fifty-six entries.
        std::uint32_t ClutHeight = 0;
        /// TEXA expansion state for this texture or its sixteen-bit CLUT.
        std::uint64_t TexA = 0;
    };

    /// Resolves a stable platform PSM enum into one of the thirteen actual hardware PSM values.
    bool TryResolveNativeTexturePsm(std::uint32_t platformPsm, std::uint32_t& hardwarePsm);
    /// Calculates exact transport and complete-page storage; dimensions must be within the GS texture limit of 1024 pixels.
    bool TryBuildNativeTextureStorage(std::uint32_t platformPsm, std::uint32_t width, std::uint32_t height, Ps2NativeTextureLayout& layout);
    /// Validates the complete cooked platform-owned payload and produces native upload and sampler metadata without converting pixels.
    bool TryBuildNativeTextureLayout(std::uint32_t format, std::uint32_t platformPsm, std::uint32_t clutPlatformPsm,
        std::uint32_t alphaMode, std::uint32_t width, std::uint32_t height,
        const std::uint8_t* pixels, std::size_t pixelBytes, const std::uint8_t* palette, std::size_t paletteBytes,
        Ps2NativeTextureLayout& layout);
    /// Measures the exact prefix needed to place a reservation on an eight-kibibyte GS page boundary.
    std::uint32_t NativeTexturePagePadding(std::uint32_t currentPointer);
    /// Patches retained CT32 rows from strided RGBA8, converting cooked alpha to GS units or preserving anonymous atlas coverage.
    bool PatchNativeRgba32Region(const Ps2NativeTextureLayout& layout, std::uint8_t* pixels, std::size_t pixelBytes,
        std::uint32_t alphaMode, bool preserveRawAlpha, std::int32_t x, std::int32_t y, std::int32_t width,
        std::int32_t height, const std::uint8_t* rgba8, std::size_t sourceBytes, std::int32_t sourceRowPitch);
    /// Builds the four-qword A+D prefix shared by CPU and VU textured packets without changing their existing memory layout.
    void BuildNativeTextureStatePrefix(std::uint64_t test, std::uint64_t testRegister, std::uint64_t tex1,
        std::uint64_t tex1Register, std::uint64_t texA, std::uint64_t* words);
    /// Reads the GS alpha value of a compact direct or CSM1 indexed texel, including sixteen-bit palette alpha expansion.
    std::uint8_t SampleNativeTextureAlpha(const Ps2NativeTextureLayout& layout, const std::uint8_t* pixels,
        const std::uint8_t* palette, std::size_t pixelIndex);
}
