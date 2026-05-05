#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"

#include <limits>
#include <stdexcept>

#include "ModelAsset.hpp"
#include "float2.hpp"
#include "float3.hpp"

namespace helengine::ps2 {
    Ps2RuntimeModel::Ps2RuntimeModel() {
    }

    void Ps2RuntimeModel::LoadFromRaw(::ModelAsset* modelAsset) {
        if (modelAsset == nullptr) {
            throw std::invalid_argument("PS2 raw model data is required.");
        }

        this->set_Id(modelAsset->get_Id());
        Positions.clear();
        Normals.clear();
        Indices.clear();
        TexCoords.clear();

        if (modelAsset->Positions == nullptr || modelAsset->Positions->Length <= 0) {
            throw std::invalid_argument("PS2 raw model data must include positions.");
        }

        Positions.reserve(static_cast<std::size_t>(modelAsset->Positions->Length));
        for (int32_t index = 0; index < modelAsset->Positions->Length; index++) {
            Positions.push_back(modelAsset->Positions->Data[index]);
        }

        if (modelAsset->Normals != nullptr && modelAsset->Normals->Length > 0) {
            Normals.reserve(static_cast<std::size_t>(modelAsset->Normals->Length));
            for (int32_t index = 0; index < modelAsset->Normals->Length; index++) {
                Normals.push_back(modelAsset->Normals->Data[index]);
            }
        }

        if (modelAsset->TexCoords != nullptr && modelAsset->TexCoords->Length > 0) {
            TexCoords.reserve(static_cast<std::size_t>(modelAsset->TexCoords->Length));
            for (int32_t index = 0; index < modelAsset->TexCoords->Length; index++) {
                TexCoords.push_back(modelAsset->TexCoords->Data[index]);
            }
        }

        if (modelAsset->Indices16 != nullptr && modelAsset->Indices16->Length > 0) {
            Indices.reserve(static_cast<std::size_t>(modelAsset->Indices16->Length));
            for (int32_t index = 0; index < modelAsset->Indices16->Length; index++) {
                Indices.push_back(modelAsset->Indices16->Data[index]);
            }
            return;
        }

        if (modelAsset->Indices32 != nullptr && modelAsset->Indices32->Length > 0) {
            Indices.reserve(static_cast<std::size_t>(modelAsset->Indices32->Length));
            for (int32_t index = 0; index < modelAsset->Indices32->Length; index++) {
                std::uint32_t rawIndex = modelAsset->Indices32->Data[index];
                if (rawIndex > static_cast<std::uint32_t>(std::numeric_limits<std::uint16_t>::max())) {
                    throw std::invalid_argument("PS2 runtime models require 16-bit indices.");
                }

                Indices.push_back(static_cast<std::uint16_t>(rawIndex));
            }
        }
    }

    const std::vector<::float3>& Ps2RuntimeModel::GetNormals() const {
        return Normals;
    }

    const std::vector<std::uint16_t>& Ps2RuntimeModel::GetIndices() const {
        return Indices;
    }

    const std::vector<::float3>& Ps2RuntimeModel::GetPositions() const {
        return Positions;
    }

    const std::vector<::float2>& Ps2RuntimeModel::GetTexCoords() const {
        return TexCoords;
    }
}
