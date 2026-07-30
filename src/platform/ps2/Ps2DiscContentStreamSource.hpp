#pragma once

#include <string>

#include "IContentStreamSource.hpp"

namespace helengine::ps2 {
    /// <summary>
    /// Opens runtime content through the PS2 disc file system for the generated core content manager.
    /// </summary>
    class Ps2DiscContentStreamSource final : public ::IContentStreamSource {
    public:
        /// <summary>
        /// Opens the requested cooked asset through the generated PS2 disc-layout manifest.
        /// </summary>
        /// <param name="assetPath">Cooked logical or disc-relative asset path to open.</param>
        /// <returns>A readable stream whose contents are owned by the returned stream instance.</returns>
        ::Stream* OpenRead(std::string assetPath) override;
    };
}
