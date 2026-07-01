#include "platform/ps2/Ps2DiscFileSystem.hpp"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <fcntl.h>
#include <fstream>
#include <libcdvd.h>
#include <malloc.h>
#include <stdexcept>
#include <string>
#include <unistd.h>
#include <vector>

#include "runtime/runtime_ps2_asset_path_manifest.hpp"
#include "system/io/file-access.hpp"
#include "system/io/file-mode.hpp"
#include "system/io/file-share.hpp"
#include "system/io/file-stream.hpp"

namespace helengine::ps2 {
    bool Ps2DiscFileSystem::CanHandlePath(const char* path) {
        if (path == nullptr) {
            return false;
        }

        return IsLogicalCookedPath(path) || IsDiscRuntimePath(path);
    }

    bool Ps2DiscFileSystem::Exists(const char* path) {
        if (!CanHandlePath(path)) {
            return false;
        }

        const std::string physicalPath = ResolvePhysicalPath(path);
        if (physicalPath.empty()) {
            return false;
        }

        if (!IsDiscRuntimePath(physicalPath)) {
            std::ifstream file(physicalPath);
            return file.good();
        }

        const std::vector<std::string> candidatePaths = BuildDiscReadCandidates(physicalPath);
        for (size_t candidateIndex = 0; candidateIndex < candidatePaths.size(); candidateIndex++) {
            sceCdlFILE fileInfo {};
            if (sceCdSearchFile(&fileInfo, candidatePaths[candidateIndex].c_str()) == 0) {
                continue;
            }

            return true;
        }

        return false;
    }

    FileStream* Ps2DiscFileSystem::OpenRead(const char* path) {
        if (path == nullptr) {
            throw std::invalid_argument("PS2 disc file system requires one path.");
        }

        const std::string resolvedPhysicalPath = ResolvePhysicalPath(path);
        if (resolvedPhysicalPath.empty()) {
            throw std::runtime_error(std::string("Failed to resolve PS2 cooked runtime path: ") + path);
        }

        if (IsDiscRuntimePath(resolvedPhysicalPath)) {
            std::vector<uint8_t> fileBytes = ReadDiscFileBytes(resolvedPhysicalPath);
            return new FileStream(fileBytes.data(), fileBytes.size());
        }

        return new FileStream(resolvedPhysicalPath, FileMode::Open, FileAccess::Read, FileShare::Read);
    }

    bool Ps2DiscFileSystem::IsLogicalCookedPath(const std::string& path) {
        return path.rfind("/cooked/", 0) == 0;
    }

    bool Ps2DiscFileSystem::IsDiscRuntimePath(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0;
    }

    std::string Ps2DiscFileSystem::ResolvePhysicalPath(const char* logicalPath) {
        if (logicalPath == nullptr || logicalPath[0] == '\0') {
            return std::string();
        }

        if (!IsLogicalCookedPath(logicalPath)) {
            return logicalPath;
        }

        const char* physicalPath = he_get_runtime_ps2_asset_physical_path(logicalPath);
        if (physicalPath == nullptr || physicalPath[0] == '\0') {
            return std::string();
        }

        return physicalPath;
    }

    std::vector<std::string> Ps2DiscFileSystem::BuildDiscReadCandidates(const std::string& path) {
        std::vector<std::string> candidatePaths;
        candidatePaths.push_back(path);

        if (path.rfind("cdrom0:", 0) == 0 && path.length() > 7) {
            std::string discTokenPath = path.substr(7);
            std::replace(discTokenPath.begin(), discTokenPath.end(), '/', '\\');
            candidatePaths.push_back(discTokenPath);
        }

        std::string normalizedPath = path;
        std::replace(normalizedPath.begin(), normalizedPath.end(), '\\', '/');
        if (normalizedPath != path) {
            candidatePaths.push_back(normalizedPath);
        }

        if (normalizedPath.rfind("cdrom0:/", 0) == 0 && normalizedPath.length() > 8) {
            std::string normalizedDiscTokenPath = normalizedPath.substr(7);
            std::replace(normalizedDiscTokenPath.begin(), normalizedDiscTokenPath.end(), '/', '\\');
            candidatePaths.push_back(normalizedDiscTokenPath);
        }

        if (path.rfind("cdrom0:\\", 0) == 0) {
            candidatePaths.push_back("cdrom0:" + path.substr(8));
        }

        if (normalizedPath.rfind("cdrom0:/", 0) == 0) {
            candidatePaths.push_back("cdrom0:" + normalizedPath.substr(8));
        }

        const size_t originalCandidateCount = candidatePaths.size();
        for (size_t candidateIndex = 0; candidateIndex < originalCandidateCount; candidateIndex++) {
            const std::string& candidatePath = candidatePaths[candidateIndex];
            if (candidatePath.length() > 2 && candidatePath.ends_with(";1")) {
                candidatePaths.push_back(candidatePath.substr(0, candidatePath.length() - 2));
            }
        }

        return candidatePaths;
    }

    std::vector<uint8_t> Ps2DiscFileSystem::ReadDiscFileBytes(const std::string& path) {
        const std::vector<std::string> candidatePaths = BuildDiscReadCandidates(path);
        for (size_t candidateIndex = 0; candidateIndex < candidatePaths.size(); candidateIndex++) {
            const std::string& candidatePath = candidatePaths[candidateIndex];
            sceCdlFILE fileInfo {};
            if (sceCdSearchFile(&fileInfo, candidatePath.c_str()) == 0) {
                continue;
            }

            if (fileInfo.size <= 0) {
                return std::vector<uint8_t>();
            }

            constexpr size_t SectorSize = 2048;
            const size_t fileSize = static_cast<size_t>(fileInfo.size);
            const size_t sectorCount = (fileSize + SectorSize - 1) / SectorSize;
            const size_t alignedSize = sectorCount * SectorSize;
            void* sectorBuffer = memalign(64, alignedSize);
            if (sectorBuffer == nullptr) {
                throw std::runtime_error(std::string("Failed to allocate disc buffer: ") + candidatePath);
            }
            std::memset(sectorBuffer, 0, alignedSize);

            sceCdRMode readMode {};
            readMode.trycount = 0;
            readMode.spindlctrl = SCECdSpinNom;
            readMode.datapattern = SCECdSecS2048;
            if (sceCdRead(fileInfo.lsn, static_cast<u32>(sectorCount), sectorBuffer, &readMode) == 0) {
                free(sectorBuffer);
                throw std::runtime_error(std::string("Failed to read disc file: ") + candidatePath);
            }
            sceCdSync(0);

            std::vector<uint8_t> bytes(fileSize);
            std::memcpy(bytes.data(), sectorBuffer, fileSize);
            free(sectorBuffer);
            return bytes;
        }

        std::string message = std::string("Failed to open file: ") + path + " candidates=";
        for (size_t candidateIndex = 0; candidateIndex < candidatePaths.size(); candidateIndex++) {
            if (candidateIndex > 0) {
                message += ",";
            }

            message += candidatePaths[candidateIndex];
        }

        throw std::runtime_error(message);
    }
}
