#include "Ps2HostDebugSession.hpp"

#include <iostream>
#include <stdexcept>
#include <filesystem>
#include <cstdlib>

#include "Asset.hpp"
#include "ContentManager.hpp"
#include "Core.hpp"
#include "CoreInitializationOptions.hpp"
#include "EngineBinaryEndianness.hpp"
#include "EngineBinaryReader.hpp"
#include "FontAsset.hpp"
#include "ModelAsset.hpp"
#include "PlatformInfo.hpp"
#include "Ps2MaterialAsset.hpp"
#include "RuntimeSceneCatalog.hpp"
#include "RuntimeSceneCatalogEntry.hpp"
#include "RuntimeContentManagerConfiguration.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "RuntimeMaterial.hpp"
#include "RuntimeModel.hpp"
#include "SceneAsset.hpp"
#include "SceneAssetReference.hpp"
#include "SceneComponentAssetRecord.hpp"
#include "SceneEntityAsset.hpp"
#include "SceneLoadMode.hpp"
#include "SceneManager.hpp"
#include "runtime/native_exceptions.hpp"
#include "runtime/array.hpp"
#include "runtime/runtime_ps2_asset_path_manifest.hpp"
#include "runtime/runtime_scene_catalog_manifest.hpp"
#include "system/io/memory-stream.hpp"

namespace helengine::ps2::host {
    namespace {
        constexpr const char* Ps2HostDiscRootEnvironmentVariable = "HELENGINE_PS2_HOST_DISC_ROOT";
    }

    Ps2HostDebugSession::Ps2HostDebugSession()
        : ExportRootPath(),
          Mode("load-only"),
          FileSystem(),
          EngineCore(nullptr),
          EnginePlatformInfo(nullptr),
          AssetContentManager(nullptr),
          RenderManager3D(),
          RenderManager2D(),
          InputBackend() {
    }

    int Ps2HostDebugSession::Run(int argc, char** argv) {
        ParseArguments(argc, argv);
        FileSystem = std::make_unique<Ps2HostFileSystem>(ExportRootPath);
        const std::string discRootPath = (std::filesystem::path(ExportRootPath) / "disc").string();
        const std::string runtimeContentRootPath = "cdrom0:\\";
#if defined(_WIN32)
        _putenv_s(Ps2HostDiscRootEnvironmentVariable, discRootPath.c_str());
#else
        setenv(Ps2HostDiscRootEnvironmentVariable, discRootPath.c_str(), 1);
#endif
        EngineCore = new ::Core();
        CoreInitializationOptions* engineOptions = EngineCore->get_InitializationOptions();
        engineOptions->set_ContentRootPath(runtimeContentRootPath);
        engineOptions->set_UpdateOrderLayers(1);
        engineOptions->set_RenderOrderLayers3D(1);
        engineOptions->set_UpdateListInitialCapacity(4);
        engineOptions->set_RenderList2DInitialCapacity(4);
        engineOptions->set_RenderList3DInitialCapacity(4);
        engineOptions->set_SceneCatalog(BuildRuntimeSceneCatalogFromManifest());
        RenderManager3D.AddWindow(0, 640, 448);
        EnginePlatformInfo = new ::PlatformInfo("ps2-host-debug", "1.0.0");
        EngineCore->Initialize(&RenderManager3D, &RenderManager2D, &InputBackend, EnginePlatformInfo, engineOptions);
        AssetContentManager = EngineCore->GetContentManager();
        const int result = Mode == "draw-once" ? ExecuteDrawOnce() : ExecuteLoadOnly();
        EngineCore->Dispose();
        Core::set_Instance(nullptr);
        return result;
    }

    void Ps2HostDebugSession::ParseArguments(int argc, char** argv) {
        for (int argumentIndex = 1; argumentIndex < argc; argumentIndex++) {
            const std::string argument = argv[argumentIndex];
            if (argument == "--export-root") {
                argumentIndex += 1;
                if (argumentIndex >= argc) {
                    throw std::invalid_argument("One export-root value is required.");
                }

                ExportRootPath = argv[argumentIndex];
            } else if (argument == "--mode") {
                argumentIndex += 1;
                if (argumentIndex >= argc) {
                    throw std::invalid_argument("One mode value is required.");
                }

                Mode = argv[argumentIndex];
            } else if (argument == "--help" || argument == "-h") {
                WriteUsage();
                throw std::invalid_argument("Help requested.");
            } else {
                throw std::invalid_argument("One unsupported host-debug argument was provided.");
            }
        }

        if (ExportRootPath.empty()) {
            throw std::invalid_argument("One export-root path is required.");
        } else if (Mode != "load-only" && Mode != "draw-once") {
            throw std::invalid_argument("Only load-only and draw-once modes are supported right now.");
        }
    }

    int Ps2HostDebugSession::ExecuteLoadOnly() {
        std::cout << "[ps2-host-debug] mode=load-only" << std::endl;
        SceneAsset* startupScene = LoadStartupSceneAsset();
        std::cout << "[ps2-host-debug] startup scene loaded" << std::endl;
        std::cout << "[ps2-host-debug] scene reference resolution begin" << std::endl;
        ResolveSceneReferences(startupScene);
        std::cout << "[ps2-host-debug] scene references resolved" << std::endl;
        return 0;
    }

    int Ps2HostDebugSession::ExecuteDrawOnce() {
        std::cout << "[ps2-host-debug] mode=draw-once" << std::endl;
        const std::string startupSceneId = ResolveStartupSceneIdFromManifest();
        std::cout << "[ps2-host-debug] startup scene id=" << startupSceneId << std::endl;
        try {
            EngineCore->get_SceneManager()->LoadScene(startupSceneId, SceneLoadMode::Single);
            std::cout << "[ps2-host-debug] startup scene loaded through scene manager" << std::endl;
            EngineCore->Update(1.0 / 60.0);
            std::cout << "[ps2-host-debug] update completed" << std::endl;
            EngineCore->Draw();
            std::cout
                << "[ps2-host-debug] draw completed cameras=" << RenderManager3D.get_LastCameraCount()
                << " drawables3d=" << RenderManager3D.get_LastDrawable3DCount()
                << " drawables2d=" << RenderManager3D.get_LastDrawable2DCount()
                << " proxies=" << RenderManager3D.get_LastProxyCount()
                << " opaqueWorld=" << RenderManager3D.get_LastOpaqueWorldCount()
                << " opaqueDynamic=" << RenderManager3D.get_LastOpaqueDynamicCount()
                << " alphaWorld=" << RenderManager3D.get_LastAlphaWorldCount()
                << " alphaDynamic=" << RenderManager3D.get_LastAlphaDynamicCount()
                << " vuBatches=" << RenderManager3D.get_LastVuBatchCount()
                << " rejMissingMaterial=" << RenderManager3D.get_LastVuRejectedMissingMaterialCount()
                << " rejMissingModel=" << RenderManager3D.get_LastVuRejectedMissingModelCount()
                << " rejMissingPackedModel=" << RenderManager3D.get_LastVuRejectedMissingPackedModelCount()
                << " triSetupMs=" << RenderManager3D.get_LastVuTriangleSetupMilliseconds()
                << " triPrepMs=" << RenderManager3D.get_LastVuTrianglePrepMilliseconds()
                << " triEmitMs=" << RenderManager3D.get_LastVuTriangleEmitMilliseconds()
                << " submittedTriangles=" << RenderManager3D.get_LastVuSubmittedTriangleCount()
                << std::endl;
            return 0;
        } catch (Exception* exception) {
            std::cout << "[ps2-host-debug] engine exception during draw-once message=" << (exception != nullptr ? exception->what() : "null") << std::endl;
            throw;
        } catch (const std::exception& exception) {
            std::cout << "[ps2-host-debug] std exception during draw-once message=" << exception.what() << std::endl;
            throw;
        } catch (...) {
            std::cout << "[ps2-host-debug] unknown exception during draw-once" << std::endl;
            throw;
        }
    }

    SceneAsset* Ps2HostDebugSession::LoadStartupSceneAsset() const {
        if (FileSystem == nullptr) {
            throw std::logic_error("One host file system must be initialized before startup scene load.");
        }

        const char* runtimePath = he_get_runtime_ps2_startup_scene_path();
        if (runtimePath == nullptr || runtimePath[0] == '\0') {
            throw std::runtime_error("The packaged PS2 export did not publish one startup scene path.");
        }

        Asset* asset = FileSystem->LoadAsset(runtimePath);
        auto* startupScene = dynamic_cast<SceneAsset*>(asset);
        if (startupScene == nullptr) {
            throw std::runtime_error("The packaged startup asset is not one scene asset.");
        }

        return startupScene;
    }

    void Ps2HostDebugSession::ResolveSceneReferences(SceneAsset* startupScene) {
        if (startupScene == nullptr) {
            throw std::invalid_argument("One startup scene asset is required.");
        }

        Array<SceneAssetReference*>* references = startupScene->get_AssetReferences();
        if (references == nullptr) {
            return;
        }

        for (int32_t referenceIndex = 0; referenceIndex < references->Length; referenceIndex++) {
            SceneAssetReference* reference = references->Data[referenceIndex];
            std::cout << "[ps2-host-debug] root asset reference " << referenceIndex << std::endl;
            ResolveSceneReference(reference);
        }

        Array<SceneEntityAsset*>* rootEntities = startupScene->get_RootEntities();
        if (rootEntities == nullptr) {
            return;
        }

        for (int32_t entityIndex = 0; entityIndex < rootEntities->Length; entityIndex++) {
            std::cout << "[ps2-host-debug] root entity " << entityIndex << std::endl;
            ResolveSceneEntity(rootEntities->Data[entityIndex]);
        }
    }

    void Ps2HostDebugSession::ResolveSceneEntity(SceneEntityAsset* entityAsset) {
        if (entityAsset == nullptr) {
            return;
        }

        Array<SceneComponentAssetRecord*>* components = entityAsset->get_Components();
        if (components != nullptr) {
            for (int32_t componentIndex = 0; componentIndex < components->Length; componentIndex++) {
                std::cout << "[ps2-host-debug] component " << componentIndex << " type=" << (components->Data[componentIndex] != nullptr ? components->Data[componentIndex]->get_ComponentTypeId() : std::string("<null>")) << std::endl;
                ResolveSceneComponent(components->Data[componentIndex]);
            }
        }

        Array<SceneEntityAsset*>* children = entityAsset->get_Children();
        if (children == nullptr) {
            return;
        }

        for (int32_t childIndex = 0; childIndex < children->Length; childIndex++) {
            std::cout << "[ps2-host-debug] child entity " << childIndex << std::endl;
            ResolveSceneEntity(children->Data[childIndex]);
        }
    }

    void Ps2HostDebugSession::ResolveSceneComponent(SceneComponentAssetRecord* componentRecord) {
        if (componentRecord == nullptr) {
            return;
        }

        const std::string componentTypeId = componentRecord->get_ComponentTypeId();
        if (componentTypeId == "helengine.MeshComponent") {
            ResolveMeshComponentPayload(componentRecord);
        } else if (componentTypeId == "helengine.TextComponent") {
            ResolveTextComponentPayload(componentRecord);
        }
    }

    void Ps2HostDebugSession::ResolveSceneReference(const std::string& runtimePath) {
        if (FileSystem == nullptr) {
            throw std::logic_error("One host file system must exist before asset resolution.");
        }

        const std::string normalizedRuntimePath =
            runtimePath.rfind("cdrom0:\\", 0) == 0 || runtimePath.rfind("cdrom0:/", 0) == 0
                ? runtimePath
                : std::string("cdrom0:\\") + runtimePath;
        std::cout << "[ps2-host-debug] resolve asset path=" << normalizedRuntimePath << std::endl;
        try {
            const std::filesystem::path hostPath = FileSystem->ResolveHostPath(normalizedRuntimePath);
            if (hostPath.extension() == ".HEF" || hostPath.extension() == ".hef") {
                AssetContentManager->Load<FontAsset*>(normalizedRuntimePath, RuntimeContentProcessorIds::FontAsset);
                std::cout << "[ps2-host-debug] font resolved path=" << normalizedRuntimePath << std::endl;
                return;
            }

            Asset* asset = nullptr;
            asset = FileSystem->LoadAsset(normalizedRuntimePath);
            if (auto* materialAsset = dynamic_cast<PlatformMaterialAsset*>(asset)) {
                RuntimeMaterial* runtimeMaterial = RenderManager3D.BuildMaterialFromCooked(materialAsset);
                std::cout << "[ps2-host-debug] cooked material resolved id=" << materialAsset->get_Id() << std::endl;
                delete runtimeMaterial;
                return;
            }

            if (auto* modelAsset = dynamic_cast<ModelAsset*>(asset)) {
                RuntimeModel* runtimeModel = RenderManager3D.BuildModelFromRaw(modelAsset);
                std::cout << "[ps2-host-debug] model resolved id=" << modelAsset->get_Id() << std::endl;
                delete runtimeModel;
                return;
            }

            std::cout << "[ps2-host-debug] skipped asset path=" << normalizedRuntimePath << std::endl;
        } catch (Exception* exception) {
            std::cout << "[ps2-host-debug] engine exception path=" << normalizedRuntimePath << " message=" << (exception != nullptr ? exception->what() : "null") << std::endl;
            delete exception;
            throw;
        } catch (const std::exception& exception) {
            std::cout << "[ps2-host-debug] std exception path=" << normalizedRuntimePath << " message=" << exception.what() << std::endl;
            throw;
        } catch (...) {
            std::cout << "[ps2-host-debug] unknown exception path=" << normalizedRuntimePath << std::endl;
            throw;
        }
    }

    void Ps2HostDebugSession::ResolveSceneReference(SceneAssetReference* reference) {
        if (reference == nullptr) {
            return;
        }

        ResolveSceneReference(reference->get_RelativePath());
    }

    RuntimeSceneCatalog* Ps2HostDebugSession::BuildRuntimeSceneCatalogFromManifest() const {
        std::size_t count = 0;
        const HERuntimeSceneCatalogEntry* manifestEntries = he_runtime_scene_catalog_entries(&count);
        Array<RuntimeSceneCatalogEntry*>* entries = new Array<RuntimeSceneCatalogEntry*>(static_cast<int32_t>(count));
        for (std::size_t index = 0; index < count; index++) {
            const HERuntimeSceneCatalogEntry& manifestEntry = manifestEntries[index];
            (*entries)[static_cast<int32_t>(index)] = new ::RuntimeSceneCatalogEntry(manifestEntry.SceneId, manifestEntry.CookedRelativePath);
        }

        return new ::RuntimeSceneCatalog(entries);
    }

    std::string Ps2HostDebugSession::ResolveStartupSceneIdFromManifest() const {
        const char* startupScenePath = he_get_runtime_ps2_startup_scene_path();
        if (startupScenePath == nullptr || startupScenePath[0] == '\0') {
            throw std::runtime_error("The packaged PS2 export did not publish one startup scene path.");
        }

        std::size_t count = 0;
        const HERuntimeSceneCatalogEntry* manifestEntries = he_runtime_scene_catalog_entries(&count);
        for (std::size_t index = 0; index < count; index++) {
            const HERuntimeSceneCatalogEntry& manifestEntry = manifestEntries[index];
            if (manifestEntry.CookedRelativePath == nullptr || manifestEntry.SceneId == nullptr) {
                continue;
            }

            if (std::string(manifestEntry.CookedRelativePath) == startupScenePath) {
                return manifestEntry.SceneId;
            }
        }

        throw std::runtime_error("The startup scene path was not found in the runtime scene catalog manifest.");
    }

    SceneAssetReference* Ps2HostDebugSession::ReadOptionalReference(EngineBinaryReader* reader) const {
        if (reader == nullptr) {
            throw std::invalid_argument("One component payload reader is required.");
        }

        if (reader->ReadByte() == 0) {
            return nullptr;
        }

        auto* reference = new ::SceneAssetReference();
        reference->set_SourceKind(static_cast<SceneAssetReferenceSourceKind>(reader->ReadInt32()));
        reference->set_RelativePath(reader->ReadString());
        reference->set_ProviderId(reader->ReadString());
        reference->set_AssetId(reader->ReadString());
        return reference;
    }

    void Ps2HostDebugSession::ResolveMeshComponentPayload(SceneComponentAssetRecord* componentRecord) {
        Array<uint8_t>* payload = componentRecord->get_Payload();
        MemoryStream* stream = new ::MemoryStream(payload != nullptr ? payload : Array<uint8_t>::Empty(), false);
        EngineBinaryReader* reader = EngineBinaryReader::Create(stream, EngineBinaryEndianness::LittleEndian, true);
        const uint8_t version = reader->ReadByte();
        SceneAssetReference* modelReference = ReadOptionalReference(reader);
        ResolveSceneReference(modelReference);

        if (version == 1) {
            SceneAssetReference* materialReference = ReadOptionalReference(reader);
            ResolveSceneReference(materialReference);
            return;
        }

        if (version != 2) {
            throw std::invalid_argument("Unsupported mesh component payload version.");
        }

        const int32_t materialReferenceCount = reader->ReadInt32();
        if (materialReferenceCount < 0) {
            throw std::invalid_argument("Mesh component payload material count must be non-negative.");
        }

        for (int32_t materialIndex = 0; materialIndex < materialReferenceCount; materialIndex++) {
            SceneAssetReference* materialReference = ReadOptionalReference(reader);
            ResolveSceneReference(materialReference);
        }
    }

    void Ps2HostDebugSession::ResolveTextComponentPayload(SceneComponentAssetRecord* componentRecord) {
        Array<uint8_t>* payload = componentRecord->get_Payload();
        MemoryStream* stream = new ::MemoryStream(payload != nullptr ? payload : Array<uint8_t>::Empty(), false);
        EngineBinaryReader* reader = EngineBinaryReader::Create(stream, EngineBinaryEndianness::LittleEndian, true);
        const uint8_t version = reader->ReadByte();
        if (version != 1) {
            throw std::invalid_argument("Unsupported text component payload version.");
        }

        SceneAssetReference* fontReference = ReadOptionalReference(reader);
        ResolveSceneReference(fontReference);
    }

    void Ps2HostDebugSession::WriteUsage() {
        std::cout
            << "ps2-host-debugger --export-root <path> --mode <load-only|draw-once>"
            << std::endl;
    }
}
