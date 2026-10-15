#include "platform/ps2/Ps2VideoModeBootRecordReader.hpp"

#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>

namespace {
    /// Counts failed checks so main can return non-zero.
    int FailureCount = 0;

    /// Records one failed check with a readable message.
    void Check(bool condition, const char* message) {
        if (!condition) {
            ++FailureCount;
            std::cerr << "FAIL: " << message << "\n";
        }
    }

    /// Builds a record exactly as the C# Ps2VideoModeBootRecord.Encode does.
    std::array<std::uint8_t, 16> MakeRecord(bool isPal, bool isProgressive, std::uint16_t width, std::uint16_t height) {
        std::array<std::uint8_t, 16> bytes{};
        bytes[0] = 'H'; bytes[1] = 'V'; bytes[2] = 'M'; bytes[3] = 'R';
        bytes[4] = 1; bytes[5] = 0;
        bytes[6] = isPal ? 1 : 0;
        bytes[7] = isProgressive ? 1 : 0;
        bytes[8] = static_cast<std::uint8_t>(width & 0xFF); bytes[9] = static_cast<std::uint8_t>(width >> 8);
        bytes[10] = static_cast<std::uint8_t>(height & 0xFF); bytes[11] = static_cast<std::uint8_t>(height >> 8);
        return bytes;
    }

    /// One PS2 mode, the gsKit fields it must produce and the VRAM one of its buffers occupies on the page grid.
    struct ExpectedMode {
        bool IsPal;
        bool IsProgressive;
        std::uint16_t Width;
        std::uint16_t Height;
        std::uint32_t PageGridBufferBytes;
    };
}

int main() {
    const ExpectedMode modes[] = {
        { false, false, 640, 448, 573440u }, { false, false, 512, 448, 458752u }, { false, true, 640, 224, 327680u },
        { false, true, 512, 224, 262144u }, { false, true, 320, 224, 163840u }, { false, true, 256, 224, 131072u },
        { true, false, 640, 512, 655360u }, { true, false, 512, 512, 524288u }, { true, true, 640, 256, 327680u },
        { true, true, 512, 256, 262144u }, { true, true, 320, 256, 163840u }, { true, true, 256, 256, 131072u }
    };

    for (const ExpectedMode& mode : modes) {
        std::array<std::uint8_t, 16> bytes = MakeRecord(mode.IsPal, mode.IsProgressive, mode.Width, mode.Height);
        helengine::ps2::Ps2VideoModeBootRecord record{};
        const char* error = nullptr;
        bool parsed = helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(bytes.data(), bytes.size(), record, error);
        Check(parsed, "every PS2 mode parses");
        helengine::ps2::Ps2GsVideoSettings settings = helengine::ps2::Ps2VideoModeBootRecordReader::ResolveGsSettings(record);
        Check(settings.IsPal == mode.IsPal, "region maps to GS_MODE_PAL/NTSC");
        Check(settings.IsInterlaced == !mode.IsProgressive, "scan maps to GS_INTERLACED/NONINTERLACED");
        Check(settings.IsFrameMode == mode.IsProgressive, "progressive uses GS_FRAME, interlaced GS_FIELD");
        Check(settings.Width == mode.Width && settings.Height == mode.Height, "size maps to GsGlobal Width/Height");
        Check(std::fabs(settings.PixelAspect - (4.0 * mode.Height) / (3.0 * mode.Width)) < 1e-12, "pixel aspect shows the mode 4:3");
        Check(helengine::ps2::Ps2VideoModeBootRecordReader::ResolvePageGridBufferBytes(mode.Width, mode.Height) == mode.PageGridBufferBytes,
            "buffers occupy whole 64x64 pages (224 rows round up to 256)");
    }

    // Golden bytes pinned by the C# Ps2VideoModeBootRecordTests.Encode_ProducesTheGoldenBytes (Task B11).
    const std::array<std::uint8_t, 16> goldenNtsc640x448i = {
        0x48, 0x56, 0x4D, 0x52, 0x01, 0x00, 0x00, 0x00, 0x80, 0x02, 0xC0, 0x01, 0x00, 0x00, 0x00, 0x00
    };
    const std::array<std::uint8_t, 16> goldenPal320x256p = {
        0x48, 0x56, 0x4D, 0x52, 0x01, 0x00, 0x01, 0x01, 0x40, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00
    };
    {
        helengine::ps2::Ps2VideoModeBootRecord golden{};
        const char* error = nullptr;
        Check(helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(goldenNtsc640x448i.data(), goldenNtsc640x448i.size(), golden, error),
            "C# golden ntsc-640x448i parses");
        Check(!golden.IsPal && !golden.IsProgressive && golden.Width == 640 && golden.Height == 448,
            "C# golden ntsc-640x448i decodes to NTSC interlaced 640x448");
        Check(helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(goldenPal320x256p.data(), goldenPal320x256p.size(), golden, error),
            "C# golden pal-320x256p parses");
        Check(golden.IsPal && golden.IsProgressive && golden.Width == 320 && golden.Height == 256,
            "C# golden pal-320x256p decodes to PAL progressive 320x256");
        Check(MakeRecord(false, false, 640, 448) == goldenNtsc640x448i && MakeRecord(true, true, 320, 256) == goldenPal320x256p,
            "the test record builder matches the C# golden bytes");
    }

    helengine::ps2::Ps2VideoModeBootRecord defaultMode{ false, false, 640, 448 };
    Check(std::fabs(helengine::ps2::Ps2VideoModeBootRecordReader::ResolveGsSettings(defaultMode).PixelAspect - 14.0 / 15.0) < 1e-12,
        "640x448 shown 4:3 has pixel aspect 14/15");

    helengine::ps2::Ps2VideoModeBootRecord record{};
    const char* error = nullptr;
    std::array<std::uint8_t, 16> valid = MakeRecord(false, false, 640, 448);

    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(valid.data(), 15, record, error), "short record rejected");
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(valid.data(), 17, record, error), "long record rejected");
    std::array<std::uint8_t, 16> badMagic = valid; badMagic[0] = 'X';
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(badMagic.data(), 16, record, error), "bad magic rejected");
    std::array<std::uint8_t, 16> badVersion = valid; badVersion[4] = 2;
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(badVersion.data(), 16, record, error), "other version rejected");
    std::array<std::uint8_t, 16> badSize = MakeRecord(false, true, 640, 480);
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(badSize.data(), 16, record, error), "640x480 rejected");
    std::array<std::uint8_t, 16> wrongRegion = MakeRecord(true, false, 640, 448);
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(wrongRegion.data(), 16, record, error), "PAL 448 rejected");
    std::array<std::uint8_t, 16> badPadding = valid; badPadding[12] = 1;
    Check(!helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(badPadding.data(), 16, record, error), "non-zero padding rejected");
    Check(error != nullptr && std::strlen(error) > 0, "rejection carries a diagnostic");

    if (FailureCount == 0) {
        std::cout << "ps2-video-mode-tests: all checks passed\n";
    }
    return FailureCount == 0 ? 0 : 1;
}
