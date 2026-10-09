#include "platform/ps2/rendering/Ps2NativeTextureUpload.hpp"
#include <algorithm>
#include <cassert>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>

/// One actual upload chain captured before the uploader releases its packet and staging.
struct ImageTransfer { std::uint32_t Psm=0, Width=0, Height=0, Vram=0, BufferWidth=0; std::vector<std::uint8_t> Bytes; };
/// Borrowed EE DMA source addresses indexed by host-only DMA_REF tags.
static std::vector<const std::uint8_t*> DmaSources;
/// Captured native IMAGE transfers, including untouched compact bytes and DMA padding.
static std::vector<ImageTransfer> Transfers;
/// Number of GS allocator calls used to reserve each entire texture and CLUT atomically.
static int Allocations = 0;
/// Number of cache flushes performed before DMA starts.
static int CacheFlushes = 0;
/// Number of waits used to complete source consumption before freeing DMA packets.
static int Waits = 0;
/// Storage for one ordinary queued TEXA A+D packet.
static std::uint64_t AlphaQueue[4];

std::uint64_t TestDmaTag(std::uint32_t count,std::uint32_t id,std::uintptr_t address) {
    std::size_t index=0;
    if(id==DMA_REF){DmaSources.push_back(reinterpret_cast<const std::uint8_t*>(address));index=DmaSources.size();}
    return count|(std::uint64_t(id)<<28)|(std::uint64_t(index)<<32);
}
u32 gsKit_vram_alloc(GSGLOBAL* global,u32 bytes,std::uint8_t) {
    Allocations++;
    const auto base=global->CurrentPointer;
    global->CurrentPointer+=bytes;
    return base;
}
void dmaKit_wait_fast(){Waits++;}
void FlushCache(int mode){assert(mode==0);CacheFlushes++;}
void* gsKit_heap_alloc(GSGLOBAL*,int qwords,int bytes,int type) {
    assert(qwords==1&&bytes==16&&type==GIF_AD);
    std::memset(AlphaQueue,0,sizeof(AlphaQueue));
    return AlphaQueue;
}
void dmaKit_send_chain_ucab(int channel,void* packet) {
    assert(channel==DMA_CHANNEL_GIF);
    const auto* words=static_cast<const std::uint64_t*>(packet);
    assert(words[0]==(5u|(std::uint64_t(DMA_CNT)<<28))&&words[1]==0);
    assert(words[2]==GIF_TAG(4,0,0,0,GSKIT_GIF_FLG_PACKED,1)&&words[3]==GIF_AD);
    assert(words[5]==GS_BITBLTBUF&&words[7]==GS_TRXPOS&&words[9]==GS_TRXREG&&words[11]==GS_TRXDIR);
    ImageTransfer transfer;
    transfer.Psm=(words[4]>>56)&63;
    transfer.BufferWidth=(words[4]>>48)&63;
    transfer.Vram=((words[4]>>32)&0x3fff)*256;
    transfer.Width=words[8]&0xfff;
    transfer.Height=(words[8]>>32)&0xfff;
    std::size_t offset=12;
    while(((words[offset]>>28)&7)==DMA_CNT){
        assert((words[offset]&0xffff)==1);
        const std::uint32_t count=words[offset+2]&0x7fff;
        assert(count>0&&count<=16384&&((words[offset+2]>>58)&3)==GSKIT_GIF_FLG_IMAGE);
        assert(((words[offset+4]>>28)&7)==DMA_REF&&(words[offset+4]&0xffff)==count);
        const auto index=std::size_t(words[offset+4]>>32);
        assert(index>0&&index<=DmaSources.size());
        transfer.Bytes.insert(transfer.Bytes.end(),DmaSources[index-1],DmaSources[index-1]+count*16);
        offset+=6;
    }
    assert(words[offset]==(2u|(std::uint64_t(DMA_END)<<28)));
    assert(words[offset+2]==GIF_TAG(1,1,0,0,GSKIT_GIF_FLG_PACKED,1));
    assert(words[offset+4]==0&&words[offset+5]==GS_TEXFLUSH);
    Transfers.push_back(transfer);
}

/// Executes the production uploader for every actual format, including indexed CLUT variants and a multi-chunk IMAGE.
int main() {
    using namespace helengine::ps2;
    std::size_t combinations=0;
    for(std::uint32_t format=0;format<13;format++){
        const bool indexed=format==1||format==2||format==6||format==7||format==8;
        for(std::uint32_t clut : {0u,4u,5u}){
            if(!indexed&&clut!=0)continue;
            Ps2NativeTextureLayout layout;
            assert(TryBuildNativeTextureStorage(format,3,5,layout));
            std::vector<std::uint8_t> pixels(layout.PixelBytes,0);
            pixels[0]=1;
            const std::uint32_t entries=indexed?(layout.TransferBits==8?256:16):0;
            std::vector<std::uint8_t> palette(entries*(clut==0?4:2),0);
            if(indexed)palette[0]=23;
            const std::uint32_t alpha=format==3||format==10?0:clut!=0||format==4||format==5||format==11||format==12?2:1;
            GSTEXTURE texture;
            assert(PrepareNativeTexture(texture,format,format,clut,alpha,3,5,pixels.data(),pixels.size(),
                palette.empty()?nullptr:palette.data(),palette.size(),layout));
            GSGLOBAL global;global.CurrentPointer=0x100100;
            const auto previousAllocations=Allocations;
            Transfers.clear();
            assert(UploadNativeTexture(&global,texture,layout));
            assert(Allocations==previousAllocations+1);
            assert(texture.Vram==0x102000);
            assert(global.CurrentPointer==0x102000+layout.VramPixelBytes+layout.VramPaletteBytes);
            assert(Transfers.size()==(indexed?2u:1u));
            assert(Transfers[0].Psm==layout.HardwarePsm&&Transfers[0].Width==3&&Transfers[0].Height==5);
            assert(Transfers[0].BufferWidth==layout.BufferWidth64&&Transfers[0].Vram==texture.Vram);
            assert(Transfers[0].Bytes.size()==layout.DmaPixelBytes);
            assert(std::equal(pixels.begin(),pixels.end(),Transfers[0].Bytes.begin()));
            for(std::size_t i=pixels.size();i<Transfers[0].Bytes.size();i++)assert(Transfers[0].Bytes[i]==0);
            if(indexed){
                assert(texture.VramClut==texture.Vram+layout.VramPixelBytes);
                assert(Transfers[1].Psm==layout.ClutHardwarePsm&&Transfers[1].Vram==texture.VramClut);
                assert(Transfers[1].Width==layout.ClutWidth&&Transfers[1].Height==layout.ClutHeight&&Transfers[1].BufferWidth==1);
                assert(std::equal(palette.begin(),palette.end(),Transfers[1].Bytes.begin()));
            }
            assert(GetNativeTextureAlpha(&texture)==layout.TexA);
            QueueNativeTextureAlpha(&global,&texture);
            assert(AlphaQueue[0]==GIF_TAG_AD(1)&&AlphaQueue[1]==GIF_AD&&AlphaQueue[2]==layout.TexA&&AlphaQueue[3]==GS_TEXA);
            ReleaseNativeTextureStaging(texture);
            assert(texture.Mem==nullptr&&texture.Clut==nullptr&&GetNativeTextureAlpha(&texture)==layout.TexA);
            ReleaseNativeTexture(texture);
            assert(FindNativeTextureLayout(&texture)==nullptr);
            combinations++;
        }
    }
    assert(combinations==23&&Waits==CacheFlushes*2);
    // Region updates retain the untouched pixels and DMA tail, and never allocate a second GS region.
    for (std::uint32_t alpha : {0u, 1u, 2u}) {
        Ps2NativeTextureLayout updateLayout;
        const std::uint8_t initial[] = {10,20,30,0, 11,21,31,0, 12,22,32,0,
            40,50,60,0, 41,51,61,0, 42,52,62,0};
        GSTEXTURE updated;
        assert(PrepareNativeTexture(updated,0,0,0,alpha,3,2,initial,sizeof(initial),nullptr,0,updateLayout));
        GSGLOBAL updateGlobal;updateGlobal.CurrentPointer=0x100100;
        Transfers.clear();assert(!ReuploadNativeTexture(&updateGlobal,updated,updateLayout));
        assert(Transfers.empty());
        assert(UploadNativeTexture(&updateGlobal,updated,updateLayout));
        const auto pointer=updateGlobal.CurrentPointer;
        const auto vram=updated.Vram;
        const auto allocations=Allocations;
        const auto texA=GetNativeTextureAlpha(&updated);
        const auto* metadata=FindNativeTextureLayout(&updated);
        const std::uint8_t incoming[] = {1,2,3,0, 4,5,6,127, 99,99,99,99,
            7,8,9,128, 10,11,12,255}; // A padded first source row; no padding required after the last.
        assert(PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,false,1,0,2,2,incoming,sizeof(incoming),12));
        const auto* bytes=reinterpret_cast<const std::uint8_t*>(updated.Mem);
        assert(std::equal(initial,initial+4,bytes)&&std::equal(initial+12,initial+16,bytes+12));
        assert(bytes[4]==1&&bytes[8]==4&&bytes[16]==7&&bytes[20]==10);
        assert(bytes[7]==(alpha==0?128:0));
        assert(bytes[11]==(alpha==0?128:alpha==1?64:0));
        assert(bytes[19]==(alpha==0?128:alpha==1?64:128)&&bytes[23]==128);
        for(std::size_t i=sizeof(initial);i<updateLayout.DmaPixelBytes;i++)assert(bytes[i]==0);
        const std::vector<std::uint8_t> patched(bytes,bytes+updateLayout.DmaPixelBytes);
        // Every rejected update leaves both pixels and padding unchanged.
        assert(!PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,false,2,0,2,2,incoming,sizeof(incoming),12));
        assert(!PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,false,-1,0,2,2,incoming,sizeof(incoming),12));
        assert(!PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,false,1,0,2,2,incoming,sizeof(incoming),7));
        assert(!PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,false,1,0,2,2,incoming,sizeof(incoming)-1,12));
        assert(!PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),sizeof(initial)-1,
            alpha,false,1,0,2,2,incoming,sizeof(incoming),12));
        assert(std::equal(patched.begin(),patched.end(),bytes));
        Transfers.clear();assert(ReuploadNativeTexture(&updateGlobal,updated,updateLayout));
        assert(Allocations==allocations&&updateGlobal.CurrentPointer==pointer&&updated.Vram==vram&&updated.VramClut==0);
        assert(Transfers.size()==1&&Transfers[0].Vram==vram&&Transfers[0].Psm==0&&Transfers[0].Bytes==patched);
        assert(FindNativeTextureLayout(&updated)==metadata&&GetNativeTextureAlpha(&updated)==texA);
        auto incompatible=updateLayout;incompatible.Width++;
        Transfers.clear();assert(!ReuploadNativeTexture(&updateGlobal,updated,incompatible)&&Transfers.empty());
        // Anonymous font coverage must remain 0..255 rather than being converted into cooked GS alpha units.
        assert(PatchNativeRgba32Region(updateLayout,reinterpret_cast<std::uint8_t*>(updated.Mem),updateLayout.DmaPixelBytes,
            alpha,true,1,0,2,2,incoming,sizeof(incoming),12));
        assert(bytes[7]==0&&bytes[11]==127&&bytes[19]==128&&bytes[23]==255);
        ReleaseNativeTexture(updated);
        assert(!ReuploadNativeTexture(&updateGlobal,updated,updateLayout));
    }
    Ps2NativeTextureLayout unsupported;
    assert(TryBuildNativeTextureStorage(1,3,2,unsupported));
    std::uint8_t indexedPixels[32]{};const std::uint8_t rgbaPixel[4]{};
    assert(!PatchNativeRgba32Region(unsupported,indexedPixels,sizeof(indexedPixels),1,false,0,0,1,1,rgbaPixel,4,4));
    Ps2NativeTextureLayout layout;
    std::vector<std::uint8_t> large(512*256*4,0);
    large.front()=17;large.back()=128;
    GSTEXTURE texture;
    assert(PrepareNativeTexture(texture,0,0,0,1,512,256,large.data(),large.size(),nullptr,0,layout));
    GSGLOBAL global;global.CurrentPointer=0x100000;
    Transfers.clear();assert(UploadNativeTexture(&global,texture,layout));
    assert(Transfers.size()==1&&Transfers[0].Bytes==large); // Multiple IMAGE and REF chunks retain all bytes.
    ReleaseNativeTexture(texture);
    const std::uint8_t pixel[]={1};std::vector<std::uint8_t> palette(1024,0);
    assert(PrepareNativeTexture(texture,1,1,0,1,1,1,pixel,1,palette.data(),palette.size(),layout));
    global.CurrentPointer=4194304-8192;
    const auto allocated=Allocations;Transfers.clear();
    assert(!UploadNativeTexture(&global,texture,layout));
    assert(global.CurrentPointer==4194304-8192&&Allocations==allocated&&Transfers.empty());
    assert(texture.Vram==0&&texture.VramClut==0&&texture.Mem!=nullptr&&texture.Clut!=nullptr);
    ReleaseNativeTexture(texture);
    std::cout<<"PASS: production native uploads for 23 combinations, native PSM IMAGE bytes, DMA zero padding/chunks, atomic exact VRAM reservation, per-bind TEXA and metadata lifetime; RGBA region stride/bounds/cooked and raw alpha, in-place reupload without allocations.\n";
}
