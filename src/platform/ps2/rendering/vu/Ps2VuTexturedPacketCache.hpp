#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

#include "float2.hpp"
#include "float3.hpp"
#include "float4.hpp"

namespace helengine::ps2 {
    class Ps2RuntimeModel;
    class Ps2VuPackedModel;

    struct Ps2VuTexturedTriangleSource final {
        ::float4 PositionA;
        ::float4 PositionB;
        ::float4 PositionC;
        ::float3 FaceNormal;
        ::float2 TexCoordA;
        ::float2 TexCoordB;
        ::float2 TexCoordC;
    };

    struct alignas(16) Ps2VuTexturedPackedTriangleSource final {
        float PositionA[4];
        float PositionB[4];
        float PositionC[4];
        float TexCoordA[4];
        float TexCoordB[4];
        float TexCoordC[4];
        float FaceNormal[4];
    };

    struct Ps2VuTexturedSourceSliceBounds final {
        float MinimumX;
        float MinimumY;
        float MinimumZ;
        float MaximumX;
        float MaximumY;
        float MaximumZ;
    };

    class Ps2VuTexturedPacketCache final {
    public:
        static constexpr std::size_t TexturedVuSourceSliceTriangleCapacity = 32u;

        const std::vector<Ps2VuTexturedTriangleSource>& ResolveTriangleSources(
            const Ps2VuPackedModel& packedModel,
            const Ps2RuntimeModel* runtimeModel);
        const std::vector<Ps2VuTexturedPackedTriangleSource>& ResolvePackedTriangleSources(
            const Ps2VuPackedModel& packedModel,
            const Ps2RuntimeModel* runtimeModel);
        const std::vector<Ps2VuTexturedPackedTriangleSource>& ResolveReferencedPackedTriangleSources(
            const Ps2VuPackedModel& packedModel,
            const Ps2RuntimeModel* runtimeModel);
        const Ps2VuTexturedSourceSliceBounds& ResolveSourceSliceBounds(
            const Ps2VuPackedModel& packedModel,
            const Ps2RuntimeModel* runtimeModel,
            std::size_t firstSourceTriangle,
            std::size_t sourceTriangleCount);
        std::size_t GetBuildCount() const;
        void ResetFrame();

    private:
        struct Entry final {
            const Ps2VuPackedModel* PackedModel = nullptr;
            const Ps2RuntimeModel* RuntimeModel = nullptr;
            std::uint32_t TriangleVertexCount = 0u;
            std::uint64_t LastUsedFrame = 0u;
            bool ReferencedThisFrame = false;
            std::vector<Ps2VuTexturedTriangleSource> TriangleSources;
            std::vector<Ps2VuTexturedPackedTriangleSource> PackedTriangleSources;
            std::vector<Ps2VuTexturedSourceSliceBounds> SourceSliceBounds;
        };

        static constexpr std::size_t MaximumEntryCount = 16u;

        Entry& FindOrCreateEntry(const Ps2VuPackedModel& packedModel, const Ps2RuntimeModel* runtimeModel);
        void BuildTriangleSources(Entry& entry, const Ps2VuPackedModel& packedModel, const Ps2RuntimeModel* runtimeModel);

        std::array<Entry, MaximumEntryCount> Entries;
        std::size_t BuildCount = 0u;
        std::uint64_t CurrentFrame = 0u;
    };
}
