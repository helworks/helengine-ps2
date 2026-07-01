#pragma once

#include <cstdint>
#include <string>
#include <vector>

class FileStream;

namespace helengine::ps2 {
    class Ps2DiscFileSystem final {
    public:
        static bool CanHandlePath(const char* path);
        static bool Exists(const char* path);
        static FileStream* OpenRead(const char* path);

    private:
        static bool IsLogicalCookedPath(const std::string& path);
        static bool IsDiscRuntimePath(const std::string& path);
        static std::string ResolvePhysicalPath(const char* logicalPath);
        static std::vector<std::string> BuildDiscReadCandidates(const std::string& path);
        static std::vector<uint8_t> ReadDiscFileBytes(const std::string& path);
    };
}
