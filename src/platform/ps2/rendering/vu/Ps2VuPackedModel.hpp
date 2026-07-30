#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

#include "float3.hpp"
#include "platform/ps2/rendering/vu/Ps2VuSourceSliceBounds.hpp"

namespace helengine::ps2 {
    class Ps2VuPackedModel final {
    public:
        Ps2VuPackedModel();

        void LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length);

        std::uint32_t GetTriangleVertexCount() const;
        ::float3 GetPosition(std::uint32_t vertexIndex) const;
        const std::uint8_t* GetPositionBlockBytes() const;
        const std::uint8_t* GetNormalBlockBytes() const;
        const std::uint8_t* GetTexCoordBlockBytes() const;
        const std::vector<std::uint8_t>& GetPackedBytes() const;
        /// <summary>
        /// Returns the conservative local bounds cached for an aligned textured VU1 source slice.
        /// </summary>
        const Ps2VuSourceSliceBounds& GetTexturedSourceSliceBounds(
            std::size_t firstSourceTriangle,
            std::size_t sourceTriangleCount) const;

    private:
        /// <summary>
        /// Calculates immutable bounds for every fixed-capacity textured source slice after packed data loads.
        /// </summary>
        void BuildTexturedSourceSliceBounds();
        std::uint32_t ReadUInt32(std::size_t offset) const;

        std::vector<std::uint8_t> PackedBytes;
        /// <summary>
        /// Stores one conservative bounds record for each aligned textured source slice.
        /// </summary>
        std::vector<Ps2VuSourceSliceBounds> TexturedSourceSliceBounds;
        std::uint32_t TriangleVertexCount;
        std::uint32_t PositionBlockOffsetQwords;
        std::uint32_t NormalBlockOffsetQwords;
        std::uint32_t TexCoordBlockOffsetQwords;
    };
}
