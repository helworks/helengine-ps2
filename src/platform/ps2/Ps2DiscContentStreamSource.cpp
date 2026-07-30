#include "platform/ps2/Ps2DiscContentStreamSource.hpp"

#include "platform/ps2/Ps2DiscFileSystem.hpp"
#include "system/io/file-stream.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Opens one cooked runtime asset through the PS2 disc file system.
    /// </summary>
    /// <param name="assetPath">Cooked logical or disc-relative asset path to open.</param>
    /// <returns>A readable stream whose contents are owned by the returned stream instance.</returns>
    ::Stream* Ps2DiscContentStreamSource::OpenRead(std::string assetPath) {
        return Ps2DiscFileSystem::OpenRead(assetPath.c_str());
    }
}
