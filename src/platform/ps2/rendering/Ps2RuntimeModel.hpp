#pragma once

#include <cstdint>
#include <vector>

#include "float2.hpp"
#include "RuntimeModel.hpp"
#include "float3.hpp"

class ModelAsset;

namespace helengine::ps2 {
    class Ps2RuntimeModel final : public ::RuntimeModel {
    public:
        Ps2RuntimeModel();

        void LoadFromRaw(::ModelAsset* modelAsset);

        const std::vector<::float3>& GetNormals() const;
        const std::vector<std::uint16_t>& GetIndices() const;
        const std::vector<::float3>& GetPositions() const;
        const std::vector<::float2>& GetTexCoords() const;

    private:
        std::vector<std::uint16_t> Indices;
        std::vector<::float3> Normals;
        std::vector<::float3> Positions;
        std::vector<::float2> TexCoords;
    };
}
