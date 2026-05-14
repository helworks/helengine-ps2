#include "platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp"

#include <gsKit.h>

namespace helengine::ps2 {
    void Ps2VuGifStateEncoder::EncodeOpaqueState(const Ps2VuOpaqueBatch& batch, GSGLOBAL* gsGlobal) const {
        (void)batch;
        if (gsGlobal == nullptr) {
            return;
        }

        if (gsGlobal->ZBuffering == GS_SETTING_ON) {
            gsKit_set_test(gsGlobal, GS_ZTEST_ON);
        } else {
            gsKit_set_test(gsGlobal, GS_ZTEST_OFF);
        }

        gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
        gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
    }
}
