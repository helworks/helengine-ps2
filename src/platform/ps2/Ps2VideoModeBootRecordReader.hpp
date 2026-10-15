#pragma once

#include <cstddef>
#include <cstdint>

namespace helengine::ps2 {
    /// <summary>
    /// The video mode the PS2 builder staged in the boot record: output region, scan and framebuffer size.
    /// </summary>
    struct Ps2VideoModeBootRecord {
        /// <summary>True for PAL output, false for NTSC.</summary>
        bool IsPal;
        /// <summary>True for progressive scan, false for interlaced.</summary>
        bool IsProgressive;
        /// <summary>Framebuffer width in pixels.</summary>
        std::uint16_t Width;
        /// <summary>Framebuffer height in pixels.</summary>
        std::uint16_t Height;
    };

    /// <summary>
    /// The gsKit screen fields for one mode, as plain values so host tests need no gsKit headers. The boot host maps them
    /// to GS_MODE_PAL/NTSC, GS_INTERLACED/NONINTERLACED and GS_FRAME/FIELD.
    /// </summary>
    struct Ps2GsVideoSettings {
        /// <summary>True selects GS_MODE_PAL, false GS_MODE_NTSC.</summary>
        bool IsPal;
        /// <summary>True selects GS_INTERLACED, false GS_NONINTERLACED.</summary>
        bool IsInterlaced;
        /// <summary>True selects GS_FRAME, false GS_FIELD.</summary>
        bool IsFrameMode;
        /// <summary>GsGlobal Width.</summary>
        int Width;
        /// <summary>GsGlobal Height.</summary>
        int Height;
        /// <summary>
        /// Displayed width of one framebuffer pixel divided by its displayed height. The GS shows every mode 4:3
        /// (GS_ASPECT_4_3), so it is 4H / 3W.
        /// </summary>
        double PixelAspect;
    };

    /// <summary>
    /// Reads the 16-byte video-mode boot record written by the C# Ps2VideoModeBootRecord (magic HVMR, version 1) and maps
    /// it to gsKit screen fields. Only the 12 PS2 modes are accepted; anything else is rejected with a diagnostic so the
    /// boot host halts instead of falling back to 640x448.
    /// </summary>
    class Ps2VideoModeBootRecordReader {
    public:
        /// <summary>Exact record size in bytes.</summary>
        static constexpr std::size_t RecordSize = 16;

        /// <summary>The only record version this runtime reads.</summary>
        static constexpr std::uint16_t CurrentVersion = 1;

        /// <summary>Disc path of the record (matches the builder's Ps2VideoModeBootRecord.RuntimePath).</summary>
        static constexpr const char* RuntimePath = "cdrom0:\\VIDMODE.BIN;1";

        /// <summary>Width term of the 4:3 aspect the GS shows every mode at (matches the builder's Ps2VideoModes).</summary>
        static constexpr int DisplayAspectWidth = 4;

        /// <summary>Height term of the 4:3 aspect the GS shows every mode at.</summary>
        static constexpr int DisplayAspectHeight = 3;

        /// <summary>Width and height, in pixels, of one CT16 or Z16 VRAM page.</summary>
        static constexpr int PageDimension = 64;

        /// <summary>Bytes in one GS VRAM page.</summary>
        static constexpr std::uint32_t PageBytes = 8192u;

        /// <summary>
        /// Parses and validates one record.
        /// </summary>
        /// <param name="bytes">Record bytes.</param>
        /// <param name="length">Number of bytes read (a longer file is rejected).</param>
        /// <param name="record">Receives the decoded mode on success.</param>
        /// <param name="error">Receives a static diagnostic on failure.</param>
        /// <returns>True when the record names one of the 12 PS2 modes.</returns>
        static bool TryParse(const std::uint8_t* bytes, std::size_t length, Ps2VideoModeBootRecord& record, const char*& error);

        /// <summary>
        /// Maps a validated record to gsKit screen fields: interlaced modes use field rendering, progressive ones frame
        /// rendering, as today's 640x448 setup does.
        /// </summary>
        /// <param name="record">Validated record.</param>
        /// <returns>gsKit screen fields.</returns>
        static Ps2GsVideoSettings ResolveGsSettings(const Ps2VideoModeBootRecord& record);

        /// <summary>
        /// Returns the VRAM one CT16 frame buffer or Z16 depth buffer of the given size occupies on the GS page grid: whole
        /// 64x64-pixel pages, so a 224-row buffer takes four page rows, not three and a half.
        /// </summary>
        /// <param name="width">Buffer width in pixels.</param>
        /// <param name="height">Buffer height in pixels.</param>
        /// <returns>Bytes from the buffer's first page to the end of its last page.</returns>
        static std::uint32_t ResolvePageGridBufferBytes(int width, int height);
    };
}
