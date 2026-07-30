#pragma once

#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Defines the fixed source and expanded-output capacities shared by the textured VU1 packet builder and microprograms.
    /// </summary>
    constexpr std::size_t TexturedVuSourceTriangleCapacity = 32u;

    /// <summary>
    /// Limits clipped submissions so full five-plane frustum expansion remains inside VU1 data memory.
    /// </summary>
    constexpr std::size_t TexturedVuClippedSourceTriangleCapacity = 8u;

    /// <summary>
    /// Bounds the convex polygon produced when one triangle is clipped by the near and four side planes.
    /// </summary>
    constexpr std::size_t TexturedVuMaximumClipPolygonVertexCount = 8u;

    /// <summary>
    /// Counts the largest triangle fan produced from one fully clipped source polygon.
    /// </summary>
    constexpr std::size_t TexturedVuMaximumOutputTrianglesPerClippedSource = TexturedVuMaximumClipPolygonVertexCount - 2u;

    /// <summary>
    /// Accounts for the largest output produced when every clipped source triangle becomes a full polygon fan.
    /// </summary>
    constexpr std::size_t TexturedVuMaximumClippedTriangleCount = TexturedVuClippedSourceTriangleCapacity
        * TexturedVuMaximumOutputTrianglesPerClippedSource;

    /// <summary>
    /// Counts the GIF state qwords copied ahead of compacted textured triangle output.
    /// </summary>
    constexpr std::size_t TexturedVuGifStateQwordCount = 8u;

    /// <summary>
    /// Counts the three ST, RGBAQ, and XYZ2 qword groups emitted for one textured triangle.
    /// </summary>
    constexpr std::size_t TexturedVuOutputQwordsPerTriangle = 9u;

    /// <summary>
    /// Identifies the first VU1 data-memory qword reserved for textured GIF output.
    /// </summary>
    constexpr std::size_t TexturedVuOutputStartQword = 0x100u;

    /// <summary>
    /// Describes the PS2 VU1 data-memory capacity in qwords.
    /// </summary>
    constexpr std::size_t TexturedVuDataMemoryQwordCount = 1024u;

    /// <summary>
    /// Identifies the exclusive worst-case output end after full frustum-clipping expansion.
    /// </summary>
    constexpr std::size_t TexturedVuMaximumOutputEndQword = TexturedVuOutputStartQword
        + TexturedVuGifStateQwordCount
        + (TexturedVuMaximumClippedTriangleCount * TexturedVuOutputQwordsPerTriangle);

    static_assert(TexturedVuMaximumOutputEndQword <= TexturedVuDataMemoryQwordCount);
}
