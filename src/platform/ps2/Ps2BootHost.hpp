#pragma once

#include <string>

typedef struct gsGlobal GSGLOBAL;

class Core;
class CoreInitializationOptions;
class Asset;
class RenderManager2D;
class RenderManager3D;

namespace helengine::ps2 {
    class Ps2InputBackend;

    class Ps2BootHost {
    public:
        Ps2BootHost();

        int Run();

    private:
        bool InitializeRuntime();
        bool InitializeGraphics();
        bool LoadPackagedStartupScene();
        ::Asset* LoadPackagedPhysicalAsset(const std::string& physicalPath);
        void PresentBootFrame();
        std::string ResolveApplicationDirectoryPath() const;

        ::Core* EngineCore;
        ::CoreInitializationOptions* EngineOptions;
        Ps2InputBackend* EngineInputBackend;
        ::RenderManager2D* EngineRenderManager2D;
        ::RenderManager3D* EngineRenderManager3D;
        GSGLOBAL* GsGlobal;
        bool StartupSceneLoaded;
    };
}
