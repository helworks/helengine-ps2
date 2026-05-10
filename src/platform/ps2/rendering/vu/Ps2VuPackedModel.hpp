#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace helengine::ps2 {
    class Ps2VuPackedModel final {
    public:
        Ps2VuPackedModel();

        void LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length);

        std::uint32_t GetTriangleVertexCount() const;
        const std::uint8_t* GetPositionBlockBytes() const;
        const std::uint8_t* GetNormalBlockBytes() const;
        const std::uint8_t* GetTexCoordBlockBytes() const;
        const std::vector<std::uint8_t>& GetPackedBytes() const;

    private:
        std::uint32_t ReadUInt32(std::size_t offset) const;

        std::vector<std::uint8_t> PackedBytes;
        std::uint32_t TriangleVertexCount;
        std::uint32_t PositionBlockOffsetQwords;
        std::uint32_t NormalBlockOffsetQwords;
        std::uint32_t TexCoordBlockOffsetQwords;
    };
}
