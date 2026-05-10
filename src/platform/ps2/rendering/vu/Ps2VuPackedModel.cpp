#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

#include <cstring>
#include <stdexcept>

namespace helengine::ps2 {
    Ps2VuPackedModel::Ps2VuPackedModel()
        : PackedBytes()
        , TriangleVertexCount(0)
        , PositionBlockOffsetQwords(0)
        , NormalBlockOffsetQwords(0)
        , TexCoordBlockOffsetQwords(0) {
    }

    void Ps2VuPackedModel::LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length) {
        if (bytes == nullptr) {
            throw std::invalid_argument("Packed PS2 mesh bytes are required.");
        } else if (length == 0) {
            throw std::invalid_argument("Packed PS2 mesh bytes must not be empty.");
        } else if ((length % 16) != 0) {
            throw std::invalid_argument("Packed PS2 mesh bytes must be qword aligned.");
        } else if (length < 32) {
            throw std::invalid_argument("Packed PS2 mesh bytes must include the fixed header.");
        }

        PackedBytes.assign(bytes, bytes + length);
        TriangleVertexCount = ReadUInt32(4);
        PositionBlockOffsetQwords = ReadUInt32(8);
        NormalBlockOffsetQwords = ReadUInt32(12);
        TexCoordBlockOffsetQwords = ReadUInt32(16);
    }

    std::uint32_t Ps2VuPackedModel::GetTriangleVertexCount() const {
        return TriangleVertexCount;
    }

    ::float3 Ps2VuPackedModel::GetPosition(std::uint32_t vertexIndex) const {
        if (vertexIndex >= TriangleVertexCount) {
            throw std::out_of_range("Packed PS2 mesh position index exceeded the triangle vertex stream.");
        }

        const std::size_t byteOffset = static_cast<std::size_t>(PositionBlockOffsetQwords + vertexIndex) * 16u;
        if ((byteOffset + 16u) > PackedBytes.size()) {
            throw std::out_of_range("Packed PS2 mesh position block read exceeded the embedded payload.");
        }

        float positionComponents[4] = { 0.0f, 0.0f, 0.0f, 0.0f };
        std::memcpy(positionComponents, PackedBytes.data() + byteOffset, sizeof(positionComponents));
        return ::float3(positionComponents[0], positionComponents[1], positionComponents[2]);
    }

    const std::uint8_t* Ps2VuPackedModel::GetPositionBlockBytes() const {
        return PackedBytes.data() + (PositionBlockOffsetQwords * 16u);
    }

    const std::uint8_t* Ps2VuPackedModel::GetNormalBlockBytes() const {
        return PackedBytes.data() + (NormalBlockOffsetQwords * 16u);
    }

    const std::uint8_t* Ps2VuPackedModel::GetTexCoordBlockBytes() const {
        return PackedBytes.data() + (TexCoordBlockOffsetQwords * 16u);
    }

    const std::vector<std::uint8_t>& Ps2VuPackedModel::GetPackedBytes() const {
        return PackedBytes;
    }

    std::uint32_t Ps2VuPackedModel::ReadUInt32(std::size_t offset) const {
        if ((offset + sizeof(std::uint32_t)) > PackedBytes.size()) {
            throw std::out_of_range("Packed PS2 mesh header read exceeded the embedded payload.");
        }

        std::uint32_t value = 0;
        std::memcpy(&value, PackedBytes.data() + offset, sizeof(std::uint32_t));
        return value;
    }
}
