#pragma once

#include <filesystem>
#include <string>

class Asset;

namespace helengine::ps2::host {
    /// <summary>
    /// Maps packaged PS2 runtime paths onto one host export directory.
    /// </summary>
    class Ps2HostFileSystem final {
    public:
        explicit Ps2HostFileSystem(std::filesystem::path exportRootPath);

        Asset* LoadAsset(const std::string& runtimePath) const;
        std::filesystem::path ResolveHostPath(const std::string& runtimePath) const;

    private:
        std::filesystem::path DiscRootPath;

        static bool IsRuntimeRootedPath(const std::string& runtimePath);
        static std::string NormalizeRuntimeRelativePath(const std::string& runtimePath);
    };
}
