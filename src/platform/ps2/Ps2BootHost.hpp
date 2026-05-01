#pragma once

typedef struct gsGlobal GSGLOBAL;

namespace helengine::ps2 {
    class Ps2BootHost {
    public:
        Ps2BootHost();

        int Run();

    private:
        bool InitializeGraphics();
        void PresentBootFrame();

        GSGLOBAL* GsGlobal;
    };
}
