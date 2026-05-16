#include <cstdint>

#include "system/io/file-stream.hpp"

#include <sys/stat.h>
#include <algorithm>
#include <cstring>
#include <stdexcept>

#if defined(_WIN32)
#include <io.h>
#else
#include <unistd.h>
#endif

namespace {
    const char* GetFileMode(FileMode mode) {
        switch (mode) {
        case FileMode::Append:
            return "a+b";
        case FileMode::Create:
            return "w+b";
        case FileMode::CreateNew:
            return "wbx+";
        case FileMode::Open:
            return "rb";
        case FileMode::OpenOrCreate:
            return "r+b";
        case FileMode::Truncate:
            return "wb";
        default:
            throw std::runtime_error("Invalid FileMode");
        }
    }
}

FileStream::FileStream(const char* path, FileMode mode)
    : file(nullptr),
      position(0),
      length(0),
      usesMemoryBuffer(false),
      memoryBuffer() {
    file = std::fopen(path, GetFileMode(mode));
    if (file == nullptr) {
        throw std::runtime_error(std::string("Failed to open file: ") + path);
    }

    UpdateLength();
}

FileStream::FileStream(const char* path, FileMode mode, FileAccess, FileShare)
    : FileStream(path, mode) {
}

FileStream::FileStream(const std::string& path, FileMode mode)
    : FileStream(path.c_str(), mode) {
}

FileStream::FileStream(const std::string& path, FileMode mode, FileAccess access, FileShare share)
    : FileStream(path.c_str(), mode, access, share) {
}

FileStream::~FileStream() {
    Close();
}

size_t FileStream::Read(uint8_t* buffer, size_t offset, size_t count) {
    if (!CanRead() || buffer == nullptr) {
        return 0;
    }

    if (usesMemoryBuffer) {
        const size_t remaining = position < memoryBuffer.size() ? memoryBuffer.size() - position : 0;
        const size_t bytesRead = std::min(count, remaining);
        if (bytesRead > 0) {
            std::memcpy(buffer + offset, memoryBuffer.data() + position, bytesRead);
            position += bytesRead;
        }

        return bytesRead;
    }

    std::fseek(file, static_cast<long>(position), SEEK_SET);
    const size_t bytesRead = std::fread(buffer + offset, 1, count, file);
    position += bytesRead;
    return bytesRead;
}

void FileStream::Write(const uint8_t* buffer, size_t offset, size_t count) {
    if (!CanWrite() || buffer == nullptr) {
        return;
    }

    if (usesMemoryBuffer) {
        throw std::runtime_error("Cannot write to memory-backed file stream.");
    }

    std::fseek(file, static_cast<long>(position), SEEK_SET);
    const size_t bytesWritten = std::fwrite(buffer + offset, 1, count, file);
    position += bytesWritten;
    UpdateLength();
}

size_t FileStream::Seek(int64_t offset, SeekOrigin origin) {
    if (!CanSeek()) {
        return position;
    }

    if (usesMemoryBuffer) {
        int64_t targetPosition = 0;
        switch (origin) {
        case SeekOrigin::Begin:
            targetPosition = offset;
            break;
        case SeekOrigin::Current:
            targetPosition = static_cast<int64_t>(position) + offset;
            break;
        case SeekOrigin::End:
            targetPosition = static_cast<int64_t>(length) + offset;
            break;
        default:
            targetPosition = static_cast<int64_t>(position);
            break;
        }

        if (targetPosition < 0) {
            targetPosition = 0;
        } else if (static_cast<size_t>(targetPosition) > length) {
            targetPosition = static_cast<int64_t>(length);
        }

        position = static_cast<size_t>(targetPosition);
        return position;
    }

    int seekMode = SEEK_SET;
    switch (origin) {
    case SeekOrigin::Begin:
        seekMode = SEEK_SET;
        break;
    case SeekOrigin::Current:
        seekMode = SEEK_CUR;
        break;
    case SeekOrigin::End:
        seekMode = SEEK_END;
        break;
    default:
        seekMode = SEEK_SET;
        break;
    }

    std::fseek(file, static_cast<long>(offset), seekMode);
    position = static_cast<size_t>(std::ftell(file));
    return position;
}

void FileStream::SetLength(size_t newLength) {
    if (file == nullptr) {
        return;
    }

    std::fflush(file);
#if defined(_WIN32)
    _chsize_s(_fileno(file), newLength);
#else
    ftruncate(fileno(file), static_cast<off_t>(newLength));
#endif
    UpdateLength();
}

void FileStream::UpdateLength() {
    if (file == nullptr) {
        return;
    }

    struct stat fileStat {};
    if (fstat(fileno(file), &fileStat) == 0) {
        length = static_cast<size_t>(fileStat.st_size);
    }
}

bool FileStream::CanRead() const {
    return file != nullptr || usesMemoryBuffer;
}

bool FileStream::CanWrite() const {
    return file != nullptr && !usesMemoryBuffer;
}

bool FileStream::CanSeek() const {
    return file != nullptr || usesMemoryBuffer;
}

size_t FileStream::Length() const {
    return length;
}

size_t FileStream::Position() const {
    return position;
}

void FileStream::SetPosition(size_t value) {
    position = std::min(value, length);
}

void FileStream::InternalReserve(size_t) {
}

void FileStream::InternalWriteByte(uint8_t byte) {
    Write(&byte, 0, 1);
}

int FileStream::InternalReadByte() {
    uint8_t byte = 0;
    return Read(&byte, 0, 1) > 0 ? byte : -1;
}

void FileStream::Flush() {
    if (file != nullptr) {
        std::fflush(file);
    }
}

void FileStream::Close() {
    if (file != nullptr) {
        std::fclose(file);
        file = nullptr;
    }
}

void FileStream::Dispose() {
    Close();
}
