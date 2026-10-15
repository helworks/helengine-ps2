#include "platform/ps2/Ps2VideoModeBootRecordReader.hpp"

#include <stdexcept>

namespace helengine::ps2 {
    namespace {
        /// <summary>One accepted PS2 mode.</summary>
        struct AcceptedMode {
            /// <summary>PAL output.</summary>
            bool IsPal;
            /// <summary>Progressive scan.</summary>
            bool IsProgressive;
            /// <summary>Framebuffer width.</summary>
            std::uint16_t Width;
            /// <summary>Framebuffer height.</summary>
            std::uint16_t Height;
        };

        /// <summary>The 12 PS2 modes, matching Ps2VideoModes.CreateCapability in the builder.</summary>
        constexpr AcceptedMode AcceptedModes[] = {
            { false, false, 640, 448 }, { false, false, 512, 448 }, { false, true, 640, 224 },
            { false, true, 512, 224 }, { false, true, 320, 224 }, { false, true, 256, 224 },
            { true, false, 640, 512 }, { true, false, 512, 512 }, { true, true, 640, 256 },
            { true, true, 512, 256 }, { true, true, 320, 256 }, { true, true, 256, 256 }
        };

        /// <summary>Reads a little-endian 16-bit value.</summary>
        std::uint16_t ReadUInt16(const std::uint8_t* bytes) {
            return static_cast<std::uint16_t>(bytes[0] | (bytes[1] << 8));
        }
    }

    bool Ps2VideoModeBootRecordReader::TryParse(const std::uint8_t* bytes, std::size_t length, Ps2VideoModeBootRecord& record, const char*& error) {
        if (bytes == nullptr || length != RecordSize) {
            error = "video mode boot record is not 16 bytes";
            return false;
        } else if (bytes[0] != 'H' || bytes[1] != 'V' || bytes[2] != 'M' || bytes[3] != 'R') {
            error = "video mode boot record has the wrong magic";
            return false;
        } else if (ReadUInt16(bytes + 4) != CurrentVersion) {
            error = "video mode boot record is not version 1";
            return false;
        } else if (bytes[6] > 1 || bytes[7] > 1) {
            error = "video mode boot record has an invalid region or scan flag";
            return false;
        } else if (bytes[12] != 0 || bytes[13] != 0 || bytes[14] != 0 || bytes[15] != 0) {
            error = "video mode boot record reserved bytes are not zero";
            return false;
        }

        Ps2VideoModeBootRecord candidate{ bytes[6] == 1, bytes[7] == 1, ReadUInt16(bytes + 8), ReadUInt16(bytes + 10) };
        for (const AcceptedMode& mode : AcceptedModes) {
            if (mode.IsPal == candidate.IsPal && mode.IsProgressive == candidate.IsProgressive
                && mode.Width == candidate.Width && mode.Height == candidate.Height) {
                record = candidate;
                error = nullptr;
                return true;
            }
        }

        error = "video mode boot record names a size no PS2 mode produces";
        return false;
    }

    Ps2GsVideoSettings Ps2VideoModeBootRecordReader::ResolveGsSettings(const Ps2VideoModeBootRecord& record) {
        const int magH = GsKitNtscPalDisplayWidthVck / static_cast<int>(record.Width) - 1;
        // The 4:3 reference picture is 2560 VCK wide, so its full height measures 2560 * 3 / 4 = 1920 in VCK widths; one
        // framebuffer pixel is (MagH + 1) VCK wide and 1920 / Height of those units tall.
        const double referenceHeightInVck = static_cast<double>(ReferenceDisplayWidthVck) * DisplayAspectHeight / DisplayAspectWidth;
        return Ps2GsVideoSettings{
            record.IsPal,
            !record.IsProgressive,
            record.IsProgressive,
            static_cast<int>(record.Width),
            static_cast<int>(record.Height),
            magH,
            (static_cast<double>(magH + 1) * record.Height) / referenceHeightInVck
        };
    }

    Ps2GsScreenFields Ps2VideoModeBootRecordReader::ResolveGsScreenFields(const Ps2GsVideoSettings& settings) {
        return Ps2GsScreenFields{
            settings.IsPal ? GsModePal : GsModeNtsc,
            settings.IsInterlaced ? GsInterlaced : GsNonInterlaced,
            settings.IsFrameMode ? GsFrame : GsField,
            settings.Width,
            settings.Height,
            settings.PixelAspect
        };
    }

    bool Ps2VideoModeBootRecordReader::VerifyGsInit(const Ps2GsVideoSettings& settings, int magH, int magV, int displayWidth,
        int displayHeight, int field, int interlace, std::string& error) {
        const Ps2GsScreenFields screen = ResolveGsScreenFields(settings);
        const int expectedMagV = 0;
        const int expectedDisplayWidth = (settings.MagH + 1) * settings.Width;
        const int expectedDisplayHeight = settings.Height;
        if (magH == settings.MagH && magV == expectedMagV && displayWidth == expectedDisplayWidth && displayHeight == expectedDisplayHeight
            && field == screen.Field && interlace == screen.Interlace) {
            error.clear();
            return true;
        }

        error = std::string("gsKit screen setup differs from the video mode reader: expected MagH=") + std::to_string(settings.MagH)
            + " MagV=" + std::to_string(expectedMagV) + " DW=" + std::to_string(expectedDisplayWidth)
            + " DH=" + std::to_string(expectedDisplayHeight) + " Field=" + std::to_string(screen.Field)
            + " Interlace=" + std::to_string(screen.Interlace) + ", actual MagH=" + std::to_string(magH)
            + " MagV=" + std::to_string(magV) + " DW=" + std::to_string(displayWidth) + " DH=" + std::to_string(displayHeight)
            + " Field=" + std::to_string(field) + " Interlace=" + std::to_string(interlace);
        return false;
    }

    std::uint32_t Ps2VideoModeBootRecordReader::ResolvePageGridBufferBytes(int width, int height) {
        if (width <= 0 || height <= 0) {
            throw std::invalid_argument("buffer size must be positive");
        }

        const std::uint32_t pageColumns = static_cast<std::uint32_t>((width + PageDimension - 1) / PageDimension);
        const std::uint32_t pageRows = static_cast<std::uint32_t>((height + PageDimension - 1) / PageDimension);
        return pageColumns * pageRows * PageBytes;
    }
}
