#include "platform/ps2/rendering/Ps2NativeTextureUpload.hpp"

#include <gsInline.h>
#include <kernel.h>
#include <malloc.h>
#include <cstring>
#include <stdexcept>
#include <unordered_map>

namespace helengine::ps2 {
    namespace {
        /// Retains only sampler metadata; uploaded texture objects are borrowed and remain owned by their renderer.
        std::unordered_map<const GSTEXTURE*, Ps2NativeTextureLayout> NativeTextureLayouts;

        /// Sends a compact host-to-local stream without relying on GSKit's incomplete PSM size switch.
        void SendNativeTextureImage(const void* pixels, std::uint32_t dmaBytes, std::uint32_t width,
            std::uint32_t height, std::uint32_t vram, std::uint32_t psm, std::uint32_t bufferWidth64) {
            constexpr std::uint32_t imageChunkQwords = 16384;
            const std::uint32_t qwords = dmaBytes / 16;
            const std::uint32_t chunks = (qwords + imageChunkQwords - 1) / imageChunkQwords;
            const std::size_t packetQwords = 9 + std::size_t(chunks) * 3;
            std::uint64_t* packet = static_cast<std::uint64_t*>(memalign(64, packetQwords * 16));
            if (packet == nullptr) throw std::runtime_error("PS2 native texture DMA packet allocation failed.");
            std::uint64_t* output = packet;
            *output++ = DMA_TAG(5, 0, DMA_CNT, 0, 0, 0); *output++ = 0;
            *output++ = GIF_TAG(4, 0, 0, 0, GSKIT_GIF_FLG_PACKED, 1); *output++ = GIF_AD;
            *output++ = GS_SETREG_BITBLTBUF(0, 0, 0, vram / 256, bufferWidth64, psm); *output++ = GS_BITBLTBUF;
            *output++ = GS_SETREG_TRXPOS(0, 0, 0, 0, 0); *output++ = GS_TRXPOS;
            *output++ = GS_SETREG_TRXREG(width, height); *output++ = GS_TRXREG;
            *output++ = GS_SETREG_TRXDIR(0); *output++ = GS_TRXDIR;
            std::uint32_t remaining = qwords;
            const std::uint8_t* source = static_cast<const std::uint8_t*>(pixels);
            while (remaining != 0) {
                const std::uint32_t count = remaining > imageChunkQwords ? imageChunkQwords : remaining;
                *output++ = DMA_TAG(1, 0, DMA_CNT, 0, 0, 0); *output++ = 0;
                *output++ = GIF_TAG(count, 0, 0, 0, GSKIT_GIF_FLG_IMAGE, 0); *output++ = 0;
                *output++ = DMA_TAG(count, 0, DMA_REF, 0, reinterpret_cast<std::uintptr_t>(source), 0); *output++ = 0;
                source += count * 16;
                remaining -= count;
            }
            *output++ = DMA_TAG(2, 0, DMA_END, 0, 0, 0); *output++ = 0;
            *output++ = GIF_TAG(1, 1, 0, 0, GSKIT_GIF_FLG_PACKED, 1); *output++ = GIF_AD;
            *output++ = 0; *output++ = GS_TEXFLUSH;
            FlushCache(0);
            dmaKit_wait_fast();
            dmaKit_send_chain_ucab(DMA_CHANNEL_GIF, packet);
            dmaKit_wait_fast();
            free(packet);
        }
    }

    bool PrepareNativeTexture(GSTEXTURE& texture, std::uint32_t format, std::uint32_t platformPsm, std::uint32_t clutPlatformPsm,
        std::uint32_t alphaMode, std::uint32_t width, std::uint32_t height,
        const std::uint8_t* pixels, std::size_t pixelBytes, const std::uint8_t* palette, std::size_t paletteBytes,
        Ps2NativeTextureLayout& layout) {
        if (!TryBuildNativeTextureLayout(format, platformPsm, clutPlatformPsm, alphaMode, width, height, pixels,
            pixelBytes, palette, paletteBytes, layout)) throw std::invalid_argument("Invalid PS2 native texture dimensions, format, alpha, or compact pixel/palette payload.");
        texture.Width = width;
        texture.Height = height;
        texture.PSM = layout.HardwarePsm;
        texture.ClutPSM = layout.ClutHardwarePsm;
        texture.TBW = layout.BufferWidth64;
        texture.Filter = GS_FILTER_NEAREST;
        texture.ClutStorageMode = GS_CLUT_STORAGE_CSM1;
        texture.Vram = 0;
        texture.VramClut = 0;
        texture.Clut = nullptr;
        texture.Mem = static_cast<u32*>(memalign(128, layout.DmaPixelBytes));
        if (texture.Mem == nullptr) return false;
        std::memset(texture.Mem, 0, layout.DmaPixelBytes);
        std::memcpy(texture.Mem, pixels, pixelBytes);
        if (layout.PaletteBytes != 0) {
            texture.Clut = static_cast<u32*>(memalign(128, layout.DmaPaletteBytes));
            if (texture.Clut == nullptr) {
                ReleaseNativeTextureStaging(texture);
                return false;
            }
            std::memset(texture.Clut, 0, layout.DmaPaletteBytes);
            std::memcpy(texture.Clut, palette, paletteBytes);
        }
        return true;
    }

    bool UploadNativeTexture(GSGLOBAL* global, GSTEXTURE& texture, const Ps2NativeTextureLayout& layout) {
        if (global == nullptr || texture.Mem == nullptr || (layout.PaletteBytes != 0 && texture.Clut == nullptr)) return false;
        const std::uint32_t padding = NativeTexturePagePadding(global->CurrentPointer);
        const std::uint32_t reservation = padding + layout.VramPixelBytes + layout.VramPaletteBytes;
        if (std::uint64_t(global->CurrentPointer) + reservation >= 4194304) return false;
        const std::uint32_t base = gsKit_vram_alloc(global, reservation, GSKIT_ALLOC_USERBUFFER);
        if (base == GSKIT_ALLOC_ERROR) return false;
        texture.Vram = base + padding;
        texture.VramClut = layout.PaletteBytes == 0 ? 0 : texture.Vram + layout.VramPixelBytes;
        texture.TBW = layout.BufferWidth64;
        SendNativeTextureImage(texture.Mem, layout.DmaPixelBytes, layout.Width, layout.Height,
            texture.Vram, layout.HardwarePsm, layout.BufferWidth64);
        if (layout.PaletteBytes != 0) {
            SendNativeTextureImage(texture.Clut, layout.DmaPaletteBytes, layout.ClutWidth, layout.ClutHeight,
                texture.VramClut, layout.ClutHardwarePsm, 1);
        }
        NativeTextureLayouts[&texture] = layout;
        return true;
    }

    bool ReuploadNativeTexture(GSGLOBAL* global, GSTEXTURE& texture, const Ps2NativeTextureLayout& layout) {
        const auto found = NativeTextureLayouts.find(&texture);
        if (global == nullptr || texture.Mem == nullptr || found == NativeTextureLayouts.end()) return false;
        const Ps2NativeTextureLayout& registered = found->second;
        if (layout.Width != registered.Width || layout.Height != registered.Height || layout.HardwarePsm != registered.HardwarePsm
            || layout.BufferWidth64 != registered.BufferWidth64 || layout.DmaPixelBytes != registered.DmaPixelBytes
            || layout.PaletteBytes != registered.PaletteBytes || layout.TexA != registered.TexA
            || texture.Width != registered.Width || texture.Height != registered.Height || texture.PSM != registered.HardwarePsm
            || texture.TBW != registered.BufferWidth64 || (texture.Vram & 8191) != 0
            || std::uint64_t(texture.Vram) + registered.VramPixelBytes > 4194304) return false;
        SendNativeTextureImage(texture.Mem, registered.DmaPixelBytes, registered.Width, registered.Height,
            texture.Vram, registered.HardwarePsm, registered.BufferWidth64);
        return true;
    }

    void QueueNativeTextureAlpha(GSGLOBAL* global, const GSTEXTURE* texture) {
        if (global == nullptr) return;
        std::uint64_t* packet = static_cast<std::uint64_t*>(gsKit_heap_alloc(global, 1, 16, GIF_AD));
        *packet++ = GIF_TAG_AD(1); *packet++ = GIF_AD;
        *packet++ = GetNativeTextureAlpha(texture); *packet++ = GS_TEXA;
    }

    std::uint64_t GetNativeTextureAlpha(const GSTEXTURE* texture) {
        const auto found = NativeTextureLayouts.find(texture);
        if (found == NativeTextureLayouts.end()) throw std::runtime_error("PS2 texture sampler metadata was not registered before drawing.");
        return found->second.TexA;
    }

    const Ps2NativeTextureLayout* FindNativeTextureLayout(const GSTEXTURE* texture) {
        const auto found = NativeTextureLayouts.find(texture);
        return found == NativeTextureLayouts.end() ? nullptr : &found->second;
    }

    void ReleaseNativeTextureStaging(GSTEXTURE& texture) {
        free(texture.Mem); texture.Mem = nullptr;
        free(texture.Clut); texture.Clut = nullptr;
    }

    void ReleaseNativeTexture(GSTEXTURE& texture) {
        NativeTextureLayouts.erase(&texture);
        ReleaseNativeTextureStaging(texture);
    }
}
