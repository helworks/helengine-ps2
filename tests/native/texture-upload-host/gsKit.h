#pragma once
#include <cstddef>
#include <cstdint>

using u32 = std::uint32_t;
/// Minimal SDK boundary used to execute the actual uploader against DMA and allocator spies on a host.
struct GSTEXTURE { u32 Width=0, Height=0, PSM=0, ClutPSM=0, TBW=0, Filter=0, ClutStorageMode=0, Vram=0, VramClut=0; u32* Mem=nullptr; u32* Clut=nullptr; };
/// The native uploader reads only the real SDK allocator's current pointer.
struct GSGLOBAL { u32 CurrentPointer=0; };
/// Encodes the actual DMA tag fields, retaining a host pointer index where the real EE tag stores a 32-bit physical address.
std::uint64_t TestDmaTag(std::uint32_t count, std::uint32_t id, std::uintptr_t address);
/// Records one allocator reservation using the SDK's existing monotonic pointer behavior.
u32 gsKit_vram_alloc(GSGLOBAL* global,u32 bytes,std::uint8_t kind);
/// Wait spy; the test requires the actual uploader to finish DMA consumption before releasing its packet.
void dmaKit_wait_fast();
/// Parses the actual uploaded chain and copies referenced pixel bytes while their staging is still alive.
void dmaKit_send_chain_ucab(int channel,void* packet);

#define GS_FILTER_NEAREST 0
#define GS_CLUT_STORAGE_CSM1 0
#define GSKIT_ALLOC_USERBUFFER 0
#define GSKIT_ALLOC_ERROR 0xffffffffu
#define DMA_CHANNEL_GIF 2
#define DMA_CNT 1
#define DMA_REF 3
#define DMA_END 7
#define DMA_TAG(qwc,irq,id,pce,addr,spr) TestDmaTag(qwc,id,static_cast<std::uintptr_t>(addr))
#define GSKIT_GIF_FLG_PACKED 0
#define GSKIT_GIF_FLG_IMAGE 2
#define GIF_AD 14
#define GIF_TAG(nloop,eop,pre,prim,flg,nreg) (std::uint64_t(nloop)|(std::uint64_t(eop)<<15)|(std::uint64_t(pre)<<46)|(std::uint64_t(prim)<<47)|(std::uint64_t(flg)<<58)|(std::uint64_t(nreg)<<60))
#define GIF_TAG_AD(nloop) GIF_TAG(nloop,0,0,0,GSKIT_GIF_FLG_PACKED,1)
#define GS_BITBLTBUF 0x50
#define GS_TRXPOS 0x51
#define GS_TRXREG 0x52
#define GS_TRXDIR 0x53
#define GS_TEXFLUSH 0x3f
#define GS_TEXA 0x3b
#define GS_SETREG_BITBLTBUF(sbp,sbw,spsm,dbp,dbw,dpsm) (std::uint64_t(sbp)|(std::uint64_t(sbw)<<16)|(std::uint64_t(spsm)<<24)|(std::uint64_t(dbp)<<32)|(std::uint64_t(dbw)<<48)|(std::uint64_t(dpsm)<<56))
#define GS_SETREG_TRXPOS(ssax,ssay,dsax,dsay,dir) (std::uint64_t(ssax)|(std::uint64_t(ssay)<<16)|(std::uint64_t(dsax)<<32)|(std::uint64_t(dsay)<<48)|(std::uint64_t(dir)<<59))
#define GS_SETREG_TRXREG(w,h) (std::uint64_t(w)|(std::uint64_t(h)<<32))
#define GS_SETREG_TRXDIR(dir) std::uint64_t(dir)
