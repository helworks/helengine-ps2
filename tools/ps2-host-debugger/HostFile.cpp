#include <cstdint>

#include "system/io/file.hpp"

#include <algorithm>
#include <cstdlib>
#include <cstdio>
#include <filesystem>
#include <fstream>

namespace {
    constexpr const char* Ps2HostDiscRootEnvironmentVariable = "HELENGINE_PS2_HOST_DISC_ROOT";

    bool StartsWithPs2CdromPrefix(const std::string& path) {
        return path.rfind("cdrom0:\\", 0) == 0 || path.rfind("cdrom0:/", 0) == 0;
    }

    std::string ResolveHostFilePath(const std::string& path) {
        if (!StartsWithPs2CdromPrefix(path)) {
            return path;
        }

        const char* discRoot = std::getenv(Ps2HostDiscRootEnvironmentVariable);
        if (discRoot == nullptr || discRoot[0] == '\0') {
            return path;
        }

        std::string relativePath = path.substr(8);
        const std::size_t versionSuffixIndex = relativePath.rfind(";1");
        if (versionSuffixIndex != std::string::npos && versionSuffixIndex == relativePath.length() - 2) {
            relativePath.erase(versionSuffixIndex);
        }

        std::replace(relativePath.begin(), relativePath.end(), '\\', '/');
        return (std::filesystem::path(discRoot) / std::filesystem::path(relativePath)).string();
    }
}

bool File::Exists(const char* fileName) {
    if (fileName == nullptr) {
        return false;
    }

    std::ifstream file(ResolveHostFilePath(fileName));
    return file.good();
}

bool File::Exists(const std::string& fileName) {
    return Exists(fileName.c_str());
}

bool File::Delete(const char* fileName) {
    if (fileName == nullptr) {
        return false;
    }

    return std::remove(fileName) == 0;
}

bool File::Delete(const std::string& fileName) {
    return Delete(fileName.c_str());
}

FileStream File::Open(const char* filePath, FileMode fileMode) {
    return FileStream(filePath, fileMode);
}

FileStream File::Open(const std::string& filePath, FileMode fileMode) {
    return Open(filePath.c_str(), fileMode);
}

FileStream* File::OpenRead(const char* filePath) {
    return new FileStream(ResolveHostFilePath(filePath), FileMode::Open, FileAccess::Read, FileShare::Read);
}

FileStream* File::OpenRead(const std::string& filePath) {
    return OpenRead(filePath.c_str());
}
