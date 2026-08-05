#pragma once

#include <cstddef>

namespace helengine::ps2 {
    /// <summary>
    /// Defines the fixed source and expanded-output capacities shared by the textured VU1 packet builder and microprograms.
    /// </summary>
    constexpr std::size_t TexturedVuSourceTriangleCapacity = 32u;

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
    /// Counts the VU1 qwords reserved for the clipped textured path's shared state before pretransformed source records.
    /// </summary>
    constexpr std::size_t TexturedVuSharedStateQwordCount = 21u;

    /// <summary>
    /// Counts the homogeneous positions, raw texture coordinates, and face normal qwords in one clipped source triangle.
    /// </summary>
    constexpr std::size_t TexturedVuClippedTriangleSourceQwordCount = 7u;

    /// <summary>
    /// Identifies the first VU1 data-memory qword the VIF double-buffer base points textured input uploads at.
    /// </summary>
    constexpr std::size_t TexturedVuInputBaseQword = 8u;

    /// <summary>
    /// Limits pretransformed clipped input records to the qwords between the input base and the textured GIF output region.
    /// </summary>
    constexpr std::size_t TexturedVuClippedInputTriangleCapacity =
        (TexturedVuOutputStartQword - TexturedVuInputBaseQword - TexturedVuSharedStateQwordCount)
        / TexturedVuClippedTriangleSourceQwordCount;

    /// <summary>
    /// Limits generated clipped output records to the qwords between the GIF state and the end of VU1 data memory.
    /// </summary>
    constexpr std::size_t TexturedVuClippedOutputTriangleCapacity =
        (TexturedVuDataMemoryQwordCount - TexturedVuOutputStartQword - TexturedVuGifStateQwordCount)
        / TexturedVuOutputQwordsPerTriangle;

    /// <summary>
    /// Selects the smaller input or output limit so every clipped batch fits both VU1 memory regions.
    /// </summary>
    constexpr std::size_t TexturedVuClippedTriangleCapacity =
        TexturedVuClippedInputTriangleCapacity < TexturedVuClippedOutputTriangleCapacity
            ? TexturedVuClippedInputTriangleCapacity
            : TexturedVuClippedOutputTriangleCapacity;

    /// <summary>
    /// Counts the qwords one complete textured GIF output buffer occupies: the copied GIF state plus every expanded triangle.
    /// </summary>
    constexpr std::size_t TexturedVuOutputBufferQwordStride = TexturedVuGifStateQwordCount
        + (TexturedVuClippedTriangleCapacity * TexturedVuOutputQwordsPerTriangle);

    /// <summary>
    /// Identifies the first qword of the second textured GIF output buffer so consecutive dispatches never overwrite an in-flight XGKICK transfer.
    /// </summary>
    constexpr std::size_t TexturedVuSecondOutputStartQword = TexturedVuOutputStartQword + TexturedVuOutputBufferQwordStride;

    static_assert(TexturedVuClippedTriangleCapacity == 32u);
    static_assert(TexturedVuSecondOutputStartQword + TexturedVuOutputBufferQwordStride <= TexturedVuDataMemoryQwordCount);
    static_assert(TexturedVuInputBaseQword
        + TexturedVuSharedStateQwordCount
        + (TexturedVuClippedTriangleCapacity * TexturedVuClippedTriangleSourceQwordCount)
        <= TexturedVuOutputStartQword);
    static_assert(TexturedVuInputBaseQword
        + TexturedVuSharedStateQwordCount
        + (TexturedVuSourceTriangleCapacity * TexturedVuClippedTriangleSourceQwordCount)
        <= TexturedVuOutputStartQword);
    static_assert(TexturedVuOutputStartQword + TexturedVuGifStateQwordCount
        + (TexturedVuClippedTriangleCapacity * TexturedVuOutputQwordsPerTriangle)
        <= TexturedVuDataMemoryQwordCount);
}
