#include <exception>
#include <cstdlib>
#include <iostream>

#include "Ps2HostDebugSession.hpp"
#include "runtime/native_exceptions.hpp"

int main(int argc, char** argv) {
    try {
        helengine::ps2::host::Ps2HostDebugSession session;
        const int exitCode = session.Run(argc, argv);
        std::cout.flush();
        std::cerr.flush();
        std::quick_exit(exitCode);
    } catch (Exception* exception) {
        std::cerr << "[ps2-host-debug] " << (exception != nullptr ? exception->what() : "null engine exception") << std::endl;
        delete exception;
        return 1;
    } catch (const std::exception& exception) {
        std::cerr << "[ps2-host-debug] " << exception.what() << std::endl;
        return 1;
    } catch (...) {
        std::cerr << "[ps2-host-debug] unknown exception" << std::endl;
        return 1;
    }
}
