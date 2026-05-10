#include "platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp"

namespace helengine::ps2 {
    Ps2VuProgramKind Ps2VuProgramRegistry::ResolveOpaqueProgram(const Ps2VuOpaqueBatch& batch) const {
        return batch.Textured ? Ps2VuProgramKind::OpaqueTextured : Ps2VuProgramKind::OpaqueUntextured;
    }
}
