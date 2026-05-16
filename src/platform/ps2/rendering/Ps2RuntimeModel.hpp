#pragma once

#include <cstdint>
#include <vector>

#include "float2.hpp"
#include "RuntimeModel.hpp"
#include "float3.hpp"
#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

class ModelAsset;

namespace helengine::ps2 {
    class Ps2RuntimeModel final : public ::RuntimeModel {
    public:
        Ps2RuntimeModel();

        void LoadFromRaw(::ModelAsset* modelAsset);
        void LoadFromRawWithoutPackedMesh(::ModelAsset* modelAsset);

        const std::vector<::float3>& GetNormals() const;
        const std::vector<std::uint16_t>& GetIndices() const;
        const std::vector<::float3>& GetPositions() const;
        const std::vector<::float2>& GetTexCoords() const;
        const ::float3& GetBoundsMinimum() const;
        const ::float3& GetBoundsMaximum() const;
        const ::float3& GetBoundsCenter() const;
        float GetBoundsRadius() const;
        const Ps2VuPackedModel* GetVuPackedModel() const;

    private:
        std::vector<std::uint16_t> Indices;
        std::vector<::float3> Normals;
        std::vector<::float3> Positions;
        std::vector<::float2> TexCoords;
        ::float3 BoundsMinimum;
        ::float3 BoundsMaximum;
        ::float3 BoundsCenter;
        float BoundsRadius;
        Ps2VuPackedModel* VuPackedModel;
    };
}
