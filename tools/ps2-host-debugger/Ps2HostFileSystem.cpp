#include "Ps2HostFileSystem.hpp"

#include <algorithm>
#include <iostream>
#include <stdexcept>

#include "Asset.hpp"
#include "AssetSerializer.hpp"
#include "system/io/file.hpp"

namespace helengine::ps2::host {
    Ps2HostFileSystem::Ps2HostFileSystem(std::filesystem::path exportRootPath)
        : DiscRootPath(std::move(exportRootPath)) {
        if (DiscRootPath.empty()) {
            throw std::invalid_argument("One packaged export root path is required.");
        }

        DiscRootPath /= "disc";
    }

    Asset* Ps2HostFileSystem::LoadAsset(const std::string& runtimePath) const {
        const std::filesystem::path hostPath = ResolveHostPath(runtimePath);
        std::cout << "[ps2-host-debug] host path=" << hostPath.string() << std::endl;
        FileStream* stream = File::OpenRead(hostPath.string());
        if (stream == nullptr) {
            throw std::runtime_error("Could not open one packaged host asset stream.");
        }

        Asset* asset = AssetSerializer::Deserialize(stream);
        delete stream;
        return asset;
    }

    std::filesystem::path Ps2HostFileSystem::ResolveHostPath(const std::string& runtimePath) const {
        const std::string relativePath = NormalizeRuntimeRelativePath(runtimePath);
        std::filesystem::path hostRelativePath(relativePath);
        return DiscRootPath / hostRelativePath;
    }

    bool Ps2HostFileSystem::IsRuntimeRootedPath(const std::string& runtimePath) {
        return runtimePath.rfind("cdrom0:\\", 0) == 0 || runtimePath.rfind("cdrom0:/", 0) == 0;
    }

    std::string Ps2HostFileSystem::NormalizeRuntimeRelativePath(const std::string& runtimePath) {
        if (!IsRuntimeRootedPath(runtimePath)) {
            throw std::invalid_argument("PS2 host debug requires one cdrom0 rooted runtime path.");
        }

        std::string relativePath = runtimePath.substr(8);
        const std::size_t versionSuffixIndex = relativePath.rfind(";1");
        if (versionSuffixIndex != std::string::npos && versionSuffixIndex == (relativePath.length() - 2)) {
            relativePath.erase(versionSuffixIndex);
        }

        std::replace(relativePath.begin(), relativePath.end(), '\\', '/');
        std::replace(relativePath.begin(), relativePath.end(), '/', '/');
        return relativePath;
    }
}
