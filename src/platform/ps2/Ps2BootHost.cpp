#include "platform/ps2/Ps2BootHost.hpp"

#include <dmaKit.h>
#include <gsKit.h>

namespace helengine::ps2 {
    Ps2BootHost::Ps2BootHost()
        : GsGlobal(0) {
    }

    int Ps2BootHost::Run() {
        if (!InitializeGraphics()) {
            return 1;
        }

        PresentBootFrame();
        return 0;
    }

    bool Ps2BootHost::InitializeGraphics() {
        GsGlobal = gsKit_init_global();

        if (GsGlobal == 0) {
            return false;
        }

        GsGlobal->Mode = GS_MODE_NTSC;
        GsGlobal->PSM = GS_PSM_CT32;
        GsGlobal->DoubleBuffering = GS_SETTING_OFF;
        GsGlobal->ZBuffering = GS_SETTING_OFF;

        dmaKit_init(
            D_CTRL_RELE_OFF,
            D_CTRL_MFD_OFF,
            D_CTRL_STS_UNSPEC,
            D_CTRL_STD_OFF,
            D_CTRL_RCYC_8,
            1 << DMA_CHANNEL_GIF);
        dmaKit_chan_init(DMA_CHANNEL_GIF);

        gsKit_init_screen(GsGlobal);
        gsKit_mode_switch(GsGlobal, GS_ONESHOT);
        return true;
    }

    void Ps2BootHost::PresentBootFrame() {
        if (GsGlobal == 0) {
            return;
        }

        while (true) {
            gsKit_clear(GsGlobal, GS_SETREG_RGBAQ(0x00, 0xFF, 0x00, 0x00, 0x00));
            gsKit_queue_exec(GsGlobal);
            gsKit_sync_flip(GsGlobal);
        }
    }
}
