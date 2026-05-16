#pragma once

#include <memory>
#include <string>

#include "Ps2HostFileSystem.hpp"
#include "Ps2HostInputBackend.hpp"
#include "Ps2HostRenderManager2D.hpp"
#include "Ps2HostRenderManager3D.hpp"

class SceneAsset;
class SceneAssetReference;
class SceneComponentAssetRecord;
class SceneEntityAsset;
class EngineBinaryReader;
class ContentManager;
class Core;
class PlatformInfo;
class RuntimeSceneCatalog;

namespace helengine::ps2::host {
    /// <summary>
    /// Executes one packaged PS2 host-debug load-only session.
    /// </summary>
    class Ps2HostDebugSession final {
    public:
        Ps2HostDebugSession();

        int Run(int argc, char** argv);

    private:
        std::string ExportRootPath;
        std::string Mode;
        std::unique_ptr<Ps2HostFileSystem> FileSystem;
        Core* EngineCore;
        PlatformInfo* EnginePlatformInfo;
        ContentManager* AssetContentManager;
        Ps2HostRenderManager3D RenderManager3D;
        Ps2HostRenderManager2D RenderManager2D;
        Ps2HostInputBackend InputBackend;

        void ParseArguments(int argc, char** argv);
        int ExecuteLoadOnly();
        int ExecuteDrawOnce();
        SceneAsset* LoadStartupSceneAsset() const;
        void ResolveSceneReferences(SceneAsset* startupScene);
        void ResolveSceneEntity(SceneEntityAsset* entityAsset);
        void ResolveSceneComponent(SceneComponentAssetRecord* componentRecord);
        void ResolveSceneReference(const std::string& runtimePath);
        void ResolveSceneReference(SceneAssetReference* reference);
        RuntimeSceneCatalog* BuildRuntimeSceneCatalogFromManifest() const;
        std::string ResolveStartupSceneIdFromManifest() const;
        SceneAssetReference* ReadOptionalReference(EngineBinaryReader* reader) const;
        void ResolveMeshComponentPayload(SceneComponentAssetRecord* componentRecord);
        void ResolveTextComponentPayload(SceneComponentAssetRecord* componentRecord);
        static void WriteUsage();
    };
}
