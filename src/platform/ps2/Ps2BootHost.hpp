#pragma once

typedef struct gsGlobal GSGLOBAL;

class Core;
class CoreInitializationOptions;
class InputManager;
class RenderManager2D;
class RenderManager3D;

namespace helengine::ps2 {
    class Ps2BootHost {
    public:
        Ps2BootHost();

        int Run();

    private:
        bool InitializeRuntime();
        bool InitializeGraphics();
        void PresentBootFrame();

        ::Core* EngineCore;
        ::CoreInitializationOptions* EngineOptions;
        ::InputManager* EngineInputManager;
        ::RenderManager2D* EngineRenderManager2D;
        ::RenderManager3D* EngineRenderManager3D;
        GSGLOBAL* GsGlobal;
    };
}
