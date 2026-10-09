#pragma once

#include <gsKit.h>
#include "platform/ps2/rendering/Ps2NativeTextureLayout.hpp"

namespace helengine::ps2 {
    /// Validates and copies compact native pixels into aligned, zero-padded DMA staging buffers; the caller retains GSTEXTURE ownership.
    bool PrepareNativeTexture(GSTEXTURE& texture, std::uint32_t format, std::uint32_t platformPsm, std::uint32_t clutPlatformPsm,
        std::uint32_t alphaMode, std::uint32_t width, std::uint32_t height,
        const std::uint8_t* pixels, std::size_t pixelBytes, const std::uint8_t* palette, std::size_t paletteBytes,
        Ps2NativeTextureLayout& layout);
    /// Atomically reserves page-aligned texel and palette storage, then uploads compact IMAGE streams using their actual native PSMs.
    bool UploadNativeTexture(GSGLOBAL* global, GSTEXTURE& texture, const Ps2NativeTextureLayout& layout);
    /// Reuploads retained pixels to the existing registered VRAM allocation without reserving storage or changing sampler metadata.
    bool ReuploadNativeTexture(GSGLOBAL* global, GSTEXTURE& texture, const Ps2NativeTextureLayout& layout);
    /// Queues per-texture TEXA immediately before an ordinary GSKit draw on the same GIF queue.
    void QueueNativeTextureAlpha(GSGLOBAL* global, const GSTEXTURE* texture);
    /// Gets the alpha expansion state retained for an uploaded texture; an unregistered texture is an integration error.
    std::uint64_t GetNativeTextureAlpha(const GSTEXTURE* texture);
    /// Gets borrowed immutable metadata until that uploaded texture is released.
    const Ps2NativeTextureLayout* FindNativeTextureLayout(const GSTEXTURE* texture);
    /// Releases uploaded CPU staging after DMA consumption, while retaining its native sampler metadata.
    void ReleaseNativeTextureStaging(GSTEXTURE& texture);
    /// Removes borrowed texture metadata and releases remaining CPU staging without changing live VRAM allocations.
    void ReleaseNativeTexture(GSTEXTURE& texture);
}
