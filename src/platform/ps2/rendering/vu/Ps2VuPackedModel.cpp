#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

#include <stdexcept>

namespace helengine::ps2 {
    Ps2VuPackedModel::Ps2VuPackedModel() = default;

    void Ps2VuPackedModel::LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length) {
        if (bytes == nullptr) {
            throw std::invalid_argument("Packed PS2 mesh bytes are required.");
        } else if (length == 0) {
            throw std::invalid_argument("Packed PS2 mesh bytes must not be empty.");
        } else if ((length % 16) != 0) {
            throw std::invalid_argument("Packed PS2 mesh bytes must be qword aligned.");
        }

        PackedBytes.assign(bytes, bytes + length);
    }

    const std::vector<std::uint8_t>& Ps2VuPackedModel::GetPackedBytes() const {
        return PackedBytes;
    }
}
