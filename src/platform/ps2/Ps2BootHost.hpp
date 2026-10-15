#pragma once

#include <string>

#include "platform/ps2/Ps2VideoModeBootRecordReader.hpp"

typedef struct gsGlobal GSGLOBAL;

class Core;
class CoreInitializationOptions;
class IAudioBackend;
class IRuntimeDiagnosticsProvider;
class Asset;
class PlatformInfo;
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
        /// <summary>Reads VIDMODE.BIN from the disc into ActiveVideoSettings, halting when it is missing or invalid.</summary>
        bool LoadVideoModeBootRecord();
        bool LoadPackagedStartupScene();
        void PresentBootFrame();
        std::string ResolveApplicationDirectoryPath() const;

        ::Core* EngineCore;
        ::CoreInitializationOptions* EngineOptions;
        ::IRuntimeDiagnosticsProvider* EngineRuntimeDiagnosticsProvider;
        ::PlatformInfo* EnginePlatformInfo;
        ::IAudioBackend* EngineAudioBackend;
        Ps2InputBackend* EngineInputBackend;
        ::RenderManager2D* EngineRenderManager2D;
        ::RenderManager3D* EngineRenderManager3D;
        GSGLOBAL* GsGlobal;
        /// <summary>The gsKit screen fields of the video mode the disc was built for; set before InitializeGraphics runs.</summary>
        Ps2GsVideoSettings ActiveVideoSettings;
        bool StartupSceneLoaded;
    };
}
