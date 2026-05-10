#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace helengine::ps2 {
    class Ps2VuPackedModel final {
    public:
        Ps2VuPackedModel();

        void LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length);

        const std::vector<std::uint8_t>& GetPackedBytes() const;

    private:
        std::vector<std::uint8_t> PackedBytes;
    };
}
