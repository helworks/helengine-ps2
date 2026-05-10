#pragma once

#include <string>

/// PS2 runtime context shim used when staging generated helengine.core output for the EE build.
class AppContext {
public:
    inline static std::string BaseDirectory = ".";
};
