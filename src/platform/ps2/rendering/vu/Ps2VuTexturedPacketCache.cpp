#include "platform/ps2/rendering/vu/Ps2VuTexturedPacketCache.hpp"

#include <algorithm>
#include <limits>
#include <stdexcept>

#include "platform/ps2/rendering/Ps2RuntimeModel.hpp"
#include "platform/ps2/rendering/vu/Ps2VuPackedModel.hpp"

namespace helengine::ps2 {
    const std::vector<Ps2VuTexturedTriangleSource>& Ps2VuTexturedPacketCache::ResolveTriangleSources(
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel) {
        Entry& entry = FindOrCreateEntry(packedModel, runtimeModel);
        entry.LastUsedFrame = CurrentFrame;
        return entry.TriangleSources;
    }

    const std::vector<Ps2VuTexturedPackedTriangleSource>& Ps2VuTexturedPacketCache::ResolvePackedTriangleSources(
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel) {
        Entry& entry = FindOrCreateEntry(packedModel, runtimeModel);
        entry.LastUsedFrame = CurrentFrame;
        return entry.PackedTriangleSources;
    }

    const std::vector<Ps2VuTexturedPackedTriangleSource>& Ps2VuTexturedPacketCache::ResolveReferencedPackedTriangleSources(
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel) {
        Entry& entry = FindOrCreateEntry(packedModel, runtimeModel);
        entry.LastUsedFrame = CurrentFrame;
        entry.ReferencedThisFrame = true;
        return entry.PackedTriangleSources;
    }

    const Ps2VuTexturedSourceSliceBounds& Ps2VuTexturedPacketCache::ResolveSourceSliceBounds(
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel,
        std::size_t firstSourceTriangle,
        std::size_t sourceTriangleCount) {
        if (sourceTriangleCount == 0u
            || sourceTriangleCount > TexturedVuSourceSliceTriangleCapacity
            || (firstSourceTriangle % TexturedVuSourceSliceTriangleCapacity) != 0u) {
            throw std::invalid_argument("PS2 textured source-slice bounds require an aligned non-empty VU source slice.");
        }

        Entry& entry = FindOrCreateEntry(packedModel, runtimeModel);
        entry.LastUsedFrame = CurrentFrame;
        const std::size_t sliceIndex = firstSourceTriangle / TexturedVuSourceSliceTriangleCapacity;
        if (sliceIndex >= entry.SourceSliceBounds.size()
            || (firstSourceTriangle + sourceTriangleCount) > entry.PackedTriangleSources.size()) {
            throw std::out_of_range("PS2 textured source-slice bounds exceed the cached source triangles.");
        }

        return entry.SourceSliceBounds[sliceIndex];
    }

    std::size_t Ps2VuTexturedPacketCache::GetBuildCount() const {
        return BuildCount;
    }

    void Ps2VuTexturedPacketCache::ResetFrame() {
        if (CurrentFrame == std::numeric_limits<std::uint64_t>::max()) {
            CurrentFrame = 0u;
            for (Entry& entry : Entries) {
                entry.LastUsedFrame = 0u;
                entry.ReferencedThisFrame = false;
            }
            return;
        }

        CurrentFrame++;
        for (Entry& entry : Entries) {
            entry.ReferencedThisFrame = false;
        }
    }

    Ps2VuTexturedPacketCache::Entry& Ps2VuTexturedPacketCache::FindOrCreateEntry(
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel) {
        const std::uint32_t triangleVertexCount = packedModel.GetTriangleVertexCount();
        Entry* leastRecentlyUsedEntry = nullptr;
        for (Entry& entry : Entries) {
            if (entry.PackedModel == &packedModel
                && entry.RuntimeModel == runtimeModel
                && entry.TriangleVertexCount == triangleVertexCount) {
                return entry;
            }
            if (entry.PackedModel == nullptr) {
                BuildTriangleSources(entry, packedModel, runtimeModel);
                return entry;
            }
            if (!entry.ReferencedThisFrame
                && (leastRecentlyUsedEntry == nullptr || entry.LastUsedFrame < leastRecentlyUsedEntry->LastUsedFrame)) {
                leastRecentlyUsedEntry = &entry;
            }
        }

        if (leastRecentlyUsedEntry == nullptr) {
            throw std::runtime_error("PS2 textured packet cache cannot evict a source entry referenced by the active VIF DMA frame.");
        }

        BuildTriangleSources(*leastRecentlyUsedEntry, packedModel, runtimeModel);
        return *leastRecentlyUsedEntry;
    }

    void Ps2VuTexturedPacketCache::BuildTriangleSources(
        Entry& entry,
        const Ps2VuPackedModel& packedModel,
        const Ps2RuntimeModel* runtimeModel) {
        BuildCount++;
        const std::uint32_t triangleVertexCount = packedModel.GetTriangleVertexCount();
        if ((triangleVertexCount % 3u) != 0u) {
            throw std::invalid_argument("PS2 textured packet cache requires complete source triangles.");
        }

        const float* packedPositionWords = reinterpret_cast<const float*>(packedModel.GetPositionBlockBytes());
        const float* packedNormalWords = reinterpret_cast<const float*>(packedModel.GetNormalBlockBytes());
        const float* packedTexCoordWords = reinterpret_cast<const float*>(packedModel.GetTexCoordBlockBytes());
        if (packedPositionWords == nullptr || packedNormalWords == nullptr || packedTexCoordWords == nullptr) {
            throw std::invalid_argument("PS2 textured packet cache requires packed positions, normals, and texture coordinates.");
        }

        const std::vector<std::uint16_t>* runtimeIndices = runtimeModel != nullptr ? &runtimeModel->GetIndices() : nullptr;
        const std::vector<::float2>* runtimeTexCoords = runtimeModel != nullptr ? &runtimeModel->GetTexCoords() : nullptr;
        entry.TriangleSources.clear();
        entry.TriangleSources.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        entry.PackedTriangleSources.clear();
        entry.PackedTriangleSources.reserve(static_cast<std::size_t>(triangleVertexCount) / 3u);
        entry.SourceSliceBounds.clear();
        for (std::uint32_t vertexIndex = 0u; vertexIndex < triangleVertexCount; vertexIndex += 3u) {
            const std::size_t positionWordIndexA = static_cast<std::size_t>(vertexIndex + 0u) * 4u;
            const std::size_t positionWordIndexB = static_cast<std::size_t>(vertexIndex + 1u) * 4u;
            const std::size_t positionWordIndexC = static_cast<std::size_t>(vertexIndex + 2u) * 4u;
            const std::uint16_t sourceIndexA = runtimeIndices != nullptr && vertexIndex < runtimeIndices->size()
                ? (*runtimeIndices)[vertexIndex + 0u]
                : static_cast<std::uint16_t>(vertexIndex + 0u);
            const std::uint16_t sourceIndexB = runtimeIndices != nullptr && (vertexIndex + 1u) < runtimeIndices->size()
                ? (*runtimeIndices)[vertexIndex + 1u]
                : static_cast<std::uint16_t>(vertexIndex + 1u);
            const std::uint16_t sourceIndexC = runtimeIndices != nullptr && (vertexIndex + 2u) < runtimeIndices->size()
                ? (*runtimeIndices)[vertexIndex + 2u]
                : static_cast<std::uint16_t>(vertexIndex + 2u);
            const ::float2 texCoordA = runtimeTexCoords != nullptr && sourceIndexA < runtimeTexCoords->size()
                ? (*runtimeTexCoords)[sourceIndexA]
                : ::float2(packedTexCoordWords[positionWordIndexA + 0u], packedTexCoordWords[positionWordIndexA + 1u]);
            const ::float2 texCoordB = runtimeTexCoords != nullptr && sourceIndexB < runtimeTexCoords->size()
                ? (*runtimeTexCoords)[sourceIndexB]
                : ::float2(packedTexCoordWords[positionWordIndexB + 0u], packedTexCoordWords[positionWordIndexB + 1u]);
            const ::float2 texCoordC = runtimeTexCoords != nullptr && sourceIndexC < runtimeTexCoords->size()
                ? (*runtimeTexCoords)[sourceIndexC]
                : ::float2(packedTexCoordWords[positionWordIndexC + 0u], packedTexCoordWords[positionWordIndexC + 1u]);
            entry.TriangleSources.push_back(Ps2VuTexturedTriangleSource {
                ::float4(packedPositionWords[positionWordIndexA + 0u], packedPositionWords[positionWordIndexA + 1u], packedPositionWords[positionWordIndexA + 2u], 1.0f),
                ::float4(packedPositionWords[positionWordIndexB + 0u], packedPositionWords[positionWordIndexB + 1u], packedPositionWords[positionWordIndexB + 2u], 1.0f),
                ::float4(packedPositionWords[positionWordIndexC + 0u], packedPositionWords[positionWordIndexC + 1u], packedPositionWords[positionWordIndexC + 2u], 1.0f),
                ::float3(
                    packedNormalWords[positionWordIndexA + 0u] + packedNormalWords[positionWordIndexB + 0u] + packedNormalWords[positionWordIndexC + 0u],
                    packedNormalWords[positionWordIndexA + 1u] + packedNormalWords[positionWordIndexB + 1u] + packedNormalWords[positionWordIndexC + 1u],
                    packedNormalWords[positionWordIndexA + 2u] + packedNormalWords[positionWordIndexB + 2u] + packedNormalWords[positionWordIndexC + 2u]),
                texCoordA,
                texCoordB,
                texCoordC
            });
            entry.PackedTriangleSources.push_back(Ps2VuTexturedPackedTriangleSource {
                { packedPositionWords[positionWordIndexA + 0u], packedPositionWords[positionWordIndexA + 1u], packedPositionWords[positionWordIndexA + 2u], 1.0f },
                { packedPositionWords[positionWordIndexB + 0u], packedPositionWords[positionWordIndexB + 1u], packedPositionWords[positionWordIndexB + 2u], 1.0f },
                { packedPositionWords[positionWordIndexC + 0u], packedPositionWords[positionWordIndexC + 1u], packedPositionWords[positionWordIndexC + 2u], 1.0f },
                { texCoordA.X, texCoordA.Y, 0.0f, 0.0f },
                { texCoordB.X, texCoordB.Y, 0.0f, 0.0f },
                { texCoordC.X, texCoordC.Y, 0.0f, 0.0f },
                {
                    packedNormalWords[positionWordIndexA + 0u] + packedNormalWords[positionWordIndexB + 0u] + packedNormalWords[positionWordIndexC + 0u],
                    packedNormalWords[positionWordIndexA + 1u] + packedNormalWords[positionWordIndexB + 1u] + packedNormalWords[positionWordIndexC + 1u],
                    packedNormalWords[positionWordIndexA + 2u] + packedNormalWords[positionWordIndexB + 2u] + packedNormalWords[positionWordIndexC + 2u],
                    255.0f
                }
            });
        }

        for (std::size_t firstSourceTriangle = 0u;
             firstSourceTriangle < entry.PackedTriangleSources.size();
             firstSourceTriangle += TexturedVuSourceSliceTriangleCapacity) {
            const std::size_t finalSourceTriangle = std::min(
                firstSourceTriangle + TexturedVuSourceSliceTriangleCapacity,
                entry.PackedTriangleSources.size());
            Ps2VuTexturedSourceSliceBounds sourceSliceBounds {
                std::numeric_limits<float>::max(),
                std::numeric_limits<float>::max(),
                std::numeric_limits<float>::max(),
                std::numeric_limits<float>::lowest(),
                std::numeric_limits<float>::lowest(),
                std::numeric_limits<float>::lowest()
            };
            for (std::size_t sourceTriangleIndex = firstSourceTriangle;
                 sourceTriangleIndex < finalSourceTriangle;
                 sourceTriangleIndex++) {
                const Ps2VuTexturedPackedTriangleSource& source = entry.PackedTriangleSources[sourceTriangleIndex];
                const float* positions[] = { source.PositionA, source.PositionB, source.PositionC };
                for (const float* position : positions) {
                    sourceSliceBounds.MinimumX = std::min(sourceSliceBounds.MinimumX, position[0]);
                    sourceSliceBounds.MinimumY = std::min(sourceSliceBounds.MinimumY, position[1]);
                    sourceSliceBounds.MinimumZ = std::min(sourceSliceBounds.MinimumZ, position[2]);
                    sourceSliceBounds.MaximumX = std::max(sourceSliceBounds.MaximumX, position[0]);
                    sourceSliceBounds.MaximumY = std::max(sourceSliceBounds.MaximumY, position[1]);
                    sourceSliceBounds.MaximumZ = std::max(sourceSliceBounds.MaximumZ, position[2]);
                }
            }
            entry.SourceSliceBounds.push_back(sourceSliceBounds);
        }

        entry.PackedModel = &packedModel;
        entry.RuntimeModel = runtimeModel;
        entry.TriangleVertexCount = triangleVertexCount;
        entry.LastUsedFrame = CurrentFrame;
    }
}
