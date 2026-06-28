#include "platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp"

#include <gsKit.h>

#include "platform/ps2/rendering/Ps2RuntimeMaterial.hpp"

namespace helengine::ps2 {
    void Ps2VuGifStateEncoder::EncodeOpaqueState(const Ps2VuOpaqueBatch& batch, GSGLOBAL* gsGlobal) const {
        if (gsGlobal == nullptr) {
            return;
        }

        if (gsGlobal->ZBuffering == GS_SETTING_ON) {
            gsKit_set_test(gsGlobal, GS_ZTEST_ON);
        } else {
            gsKit_set_test(gsGlobal, GS_ZTEST_OFF);
        }

        const Ps2RuntimeMaterial* material = batch.Material;
        if (material == nullptr || material->GetAlphaMode() == ::Ps2MaterialAlphaMode::Opaque) {
            gsKit_set_test(gsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        } else if (material->GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaTest) {
            gsKit_set_test(gsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 0, 0, 0, 0), 0);
            gsGlobal->PrimAlphaEnable = GS_SETTING_OFF;
        } else if (material->GetAlphaMode() == ::Ps2MaterialAlphaMode::AlphaBlend) {
            gsKit_set_test(gsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(gsGlobal, GS_BLEND_BACK2FRONT, 0);
            gsGlobal->PrimAlphaEnable = GS_SETTING_ON;
        } else if (material->GetAlphaMode() == ::Ps2MaterialAlphaMode::Additive) {
            gsKit_set_test(gsGlobal, GS_ATEST_OFF);
            gsKit_set_primalpha(gsGlobal, GS_SETREG_ALPHA(0, 2, 2, 1, 0x80), 0);
            gsGlobal->PrimAlphaEnable = GS_SETTING_ON;
        }
    }
}
