#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>

#include "platform/ps2/rendering/vu/Ps2VuTexturedSourceLimits.hpp"

namespace helengine::ps2 {
    Ps2VuPackedModel::Ps2VuPackedModel()
        : PackedBytes()
        , TexturedSourceSliceBounds()
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
        BuildTexturedSourceSliceBounds();
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

    const Ps2VuSourceSliceBounds& Ps2VuPackedModel::GetTexturedSourceSliceBounds(
        std::size_t firstSourceTriangle,
        std::size_t sourceTriangleCount) const {
        const std::size_t sourceTriangleTotal = static_cast<std::size_t>(TriangleVertexCount) / 3u;
        if ((firstSourceTriangle % TexturedVuSourceTriangleCapacity) != 0u) {
            throw std::invalid_argument("Packed PS2 mesh textured slice start must align to the VU1 source capacity.");
        } else if (sourceTriangleCount == 0u || sourceTriangleCount > TexturedVuSourceTriangleCapacity) {
            throw std::invalid_argument("Packed PS2 mesh textured slice count exceeds the VU1 source capacity.");
        } else if (firstSourceTriangle >= sourceTriangleTotal
            || sourceTriangleCount > (sourceTriangleTotal - firstSourceTriangle)) {
            throw std::out_of_range("Packed PS2 mesh textured slice exceeds the source-triangle range.");
        }

        const std::size_t boundsIndex = firstSourceTriangle / TexturedVuSourceTriangleCapacity;
        if (boundsIndex >= TexturedSourceSliceBounds.size()) {
            throw std::out_of_range("Packed PS2 mesh textured slice bounds were not initialized.");
        }

        return TexturedSourceSliceBounds[boundsIndex];
    }

    void Ps2VuPackedModel::BuildTexturedSourceSliceBounds() {
        TexturedSourceSliceBounds.clear();
        if ((TriangleVertexCount % 3u) != 0u) {
            throw std::invalid_argument("Packed PS2 mesh triangle vertex count must be divisible by three.");
        }

        const std::size_t sourceTriangleTotal = static_cast<std::size_t>(TriangleVertexCount) / 3u;
        TexturedSourceSliceBounds.reserve(
            (sourceTriangleTotal + TexturedVuSourceTriangleCapacity - 1u) / TexturedVuSourceTriangleCapacity);
        for (std::size_t firstSourceTriangle = 0u;
            firstSourceTriangle < sourceTriangleTotal;
            firstSourceTriangle += TexturedVuSourceTriangleCapacity) {
            const std::size_t sourceTriangleCount = std::min(
                TexturedVuSourceTriangleCapacity,
                sourceTriangleTotal - firstSourceTriangle);
            const std::size_t firstSourceVertex = firstSourceTriangle * 3u;
            const std::size_t finalSourceVertex = firstSourceVertex + (sourceTriangleCount * 3u);
            ::float3 minimum(
                std::numeric_limits<float>::max(),
                std::numeric_limits<float>::max(),
                std::numeric_limits<float>::max());
            ::float3 maximum(
                std::numeric_limits<float>::lowest(),
                std::numeric_limits<float>::lowest(),
                std::numeric_limits<float>::lowest());
            for (std::size_t sourceVertex = firstSourceVertex; sourceVertex < finalSourceVertex; sourceVertex++) {
                const ::float3 position = GetPosition(static_cast<std::uint32_t>(sourceVertex));
                minimum.X = std::min(minimum.X, position.X);
                minimum.Y = std::min(minimum.Y, position.Y);
                minimum.Z = std::min(minimum.Z, position.Z);
                maximum.X = std::max(maximum.X, position.X);
                maximum.Y = std::max(maximum.Y, position.Y);
                maximum.Z = std::max(maximum.Z, position.Z);
            }

            const ::float3 center(
                (minimum.X + maximum.X) * 0.5f,
                (minimum.Y + maximum.Y) * 0.5f,
                (minimum.Z + maximum.Z) * 0.5f);
            const ::float3 extents(
                (maximum.X - minimum.X) * 0.5f,
                (maximum.Y - minimum.Y) * 0.5f,
                (maximum.Z - minimum.Z) * 0.5f);
            TexturedSourceSliceBounds.push_back(Ps2VuSourceSliceBounds { center, extents });
        }
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
