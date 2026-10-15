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

    /// One PS2 mode, the gsKit fields it must produce, the pixel aspect gsKit shows it at (as an exact fraction) and the
    /// VRAM one of its buffers occupies on the page grid.
    struct ExpectedMode {
        bool IsPal;
        bool IsProgressive;
        std::uint16_t Width;
        std::uint16_t Height;
        int MagH;
        int PixelAspectNumerator;
        int PixelAspectDenominator;
        std::uint32_t PageGridBufferBytes;
    };

    /// One record TryParse must refuse, the byte count handed to it and the check's readable name.
    struct RejectionCase {
        const std::uint8_t* Bytes;
        std::size_t Length;
        const char* Name;
    };
}

int main() {
    // gsKit 8ef73d05 gsInit.c: MagH + 1 = floor(2880 / W), MagV = 0, and the pixel aspect is (MagH + 1) * H / 1920 with
    // the 2560-VCK 640x448 picture as the 4:3 reference. 320-wide modes show 3:2 and 256-wide modes 22:15.
    const ExpectedMode modes[] = {
        { false, false, 640, 448, 3, 14, 15, 573440u }, { false, false, 512, 448, 4, 7, 6, 458752u },
        { false, true, 640, 224, 3, 7, 15, 327680u }, { false, true, 512, 224, 4, 7, 12, 262144u },
        { false, true, 320, 224, 8, 21, 20, 163840u }, { false, true, 256, 224, 10, 77, 60, 131072u },
        { true, false, 640, 512, 3, 16, 15, 655360u }, { true, false, 512, 512, 4, 4, 3, 524288u },
        { true, true, 640, 256, 3, 8, 15, 327680u }, { true, true, 512, 256, 4, 2, 3, 262144u },
        { true, true, 320, 256, 8, 6, 5, 163840u }, { true, true, 256, 256, 10, 22, 15, 131072u }
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
        Check(settings.MagH == mode.MagH, "MagH is gsKit's floor(2880 / W) - 1");
        Check(std::fabs(settings.PixelAspect - static_cast<double>(mode.PixelAspectNumerator) / mode.PixelAspectDenominator) < 1e-12,
            "pixel aspect follows gsKit's magnification, (MagH + 1) * H / 1920");
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

    helengine::ps2::Ps2VideoModeBootRecord goldenPalMode{ true, true, 320, 256 };
    Check(std::fabs(helengine::ps2::Ps2VideoModeBootRecordReader::ResolveGsSettings(goldenPalMode).PixelAspect - 6.0 / 5.0) < 1e-12,
        "the golden pal-320x256p record is shown 3:2, pixel aspect 6/5");

    helengine::ps2::Ps2VideoModeBootRecord defaultMode{ false, false, 640, 448 };
    Check(std::fabs(helengine::ps2::Ps2VideoModeBootRecordReader::ResolveGsSettings(defaultMode).PixelAspect - 14.0 / 15.0) < 1e-12,
        "640x448 shown 4:3 has pixel aspect 14/15");

    Check(helengine::ps2::Ps2VideoModeBootRecordReader::ResolvePageGridBufferBytes(65, 64) == 16384u,
        "a width one pixel past a page boundary takes a second page column");

    const std::array<std::uint8_t, 16> valid = MakeRecord(false, false, 640, 448);
    std::array<std::uint8_t, 16> badMagicFirst = valid; badMagicFirst[0] = 'X';
    std::array<std::uint8_t, 16> badMagicLast = valid; badMagicLast[3] = 'X';
    std::array<std::uint8_t, 16> badVersionLow = valid; badVersionLow[4] = 2;
    std::array<std::uint8_t, 16> badVersionHigh = valid; badVersionHigh[5] = 1;
    std::array<std::uint8_t, 16> badRegionFlag = valid; badRegionFlag[6] = 2;
    std::array<std::uint8_t, 16> badScanFlag = valid; badScanFlag[7] = 2;
    std::array<std::uint8_t, 16> badSize = MakeRecord(false, true, 640, 480);
    std::array<std::uint8_t, 16> wrongRegion = MakeRecord(true, false, 640, 448);
    std::array<std::uint8_t, 16> badPaddingFirst = valid; badPaddingFirst[12] = 1;
    std::array<std::uint8_t, 16> badPaddingLast = valid; badPaddingLast[15] = 1;

    const RejectionCase rejections[] = {
        { valid.data(), 15, "short record rejected" },
        { valid.data(), 17, "long record rejected" },
        { badMagicFirst.data(), 16, "bad magic byte 0 rejected" },
        { badMagicLast.data(), 16, "bad magic byte 3 rejected" },
        { badVersionLow.data(), 16, "version low byte 2 rejected" },
        { badVersionHigh.data(), 16, "version high byte 1 (version 257) rejected" },
        { badRegionFlag.data(), 16, "region flag 2 rejected" },
        { badScanFlag.data(), 16, "scan flag 2 rejected" },
        { badSize.data(), 16, "640x480 rejected" },
        { wrongRegion.data(), 16, "PAL 448 rejected" },
        { badPaddingFirst.data(), 16, "non-zero padding byte 12 rejected" },
        { badPaddingLast.data(), 16, "non-zero padding byte 15 rejected" }
    };

    for (const RejectionCase& rejection : rejections) {
        helengine::ps2::Ps2VideoModeBootRecord record{};
        const char* error = nullptr;
        const bool parsed = helengine::ps2::Ps2VideoModeBootRecordReader::TryParse(rejection.Bytes, rejection.Length, record, error);
        Check(!parsed, rejection.Name);
        if (error == nullptr || std::strlen(error) == 0) {
            ++FailureCount;
            std::cerr << "FAIL: rejection carries a diagnostic: " << rejection.Name << "\n";
        }
    }

    if (FailureCount == 0) {
        std::cout << "ps2-video-mode-tests: all checks passed\n";
    }
    return FailureCount == 0 ? 0 : 1;
}
