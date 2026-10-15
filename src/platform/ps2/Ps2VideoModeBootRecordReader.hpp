#pragma once

#include <cstddef>
#include <cstdint>
#include <string>

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
        /// The horizontal magnification gsKit_init_screen picks for NTSC and PAL: floor(2880 / Width) - 1 (3 for 640, 4 for
        /// 512, 8 for 320, 10 for 256). MagV is always 0, so the picture is (MagH + 1) * Width VCK wide and Height rasters tall.
        /// </summary>
        int MagH;
        /// <summary>
        /// Displayed width of one framebuffer pixel divided by its displayed height: (MagH + 1) * Height / 1920. The
        /// 2560-VCK 640x448 picture is the 4:3 reference, so 640- and 512-wide modes are shown 4:3, 320-wide modes 3:2 and
        /// 256-wide modes 22:15. gsKit never reads GsGlobal->Aspect (GS_ASPECT_4_3) for the display; only DW, MagH and DH
        /// set the picture's shape.
        /// </summary>
        double PixelAspect;
    };

    /// <summary>
    /// The values Ps2BootHost stores in GSGLOBAL before gsKit_init_screen, as plain gsKit GS_* numbers so host tests need no
    /// gsKit headers, plus the pixel aspect it reports to the core's main window once the window exists.
    /// </summary>
    struct Ps2GsScreenFields {
        /// <summary>GsGlobal Mode: GS_MODE_PAL or GS_MODE_NTSC.</summary>
        int Mode;
        /// <summary>GsGlobal Interlace: GS_INTERLACED or GS_NONINTERLACED.</summary>
        int Interlace;
        /// <summary>GsGlobal Field: GS_FIELD for interlaced modes (GS_FRAME would underflow gsKit's MagV), GS_FRAME otherwise.</summary>
        int Field;
        /// <summary>GsGlobal Width.</summary>
        int Width;
        /// <summary>GsGlobal Height.</summary>
        int Height;
        /// <summary>The pixel aspect the boot host hands RenderManager3D::SetWindowPixelAspect for the main window.</summary>
        double WindowPixelAspect;
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

        /// <summary>Width, in VCK, of the span gsKit magnifies NTSC and PAL framebuffers into (its DW).</summary>
        static constexpr int GsKitNtscPalDisplayWidthVck = 2880;

        /// <summary>Width, in VCK, of the picture that counts as 4:3: the 640x448 framebuffer magnified 4x.</summary>
        static constexpr int ReferenceDisplayWidthVck = 2560;

        /// <summary>
        /// Width term of the 4:3 aspect the 2560-VCK reference picture is shown at (matches the builder's
        /// Ps2GsDisplayAspect).
        /// </summary>
        static constexpr int DisplayAspectWidth = 4;

        /// <summary>Height term of the 4:3 aspect the 2560-VCK reference picture is shown at.</summary>
        static constexpr int DisplayAspectHeight = 3;

        /// <summary>gsKit's GS_MODE_NTSC (gsInit.h at 8ef73d05).</summary>
        static constexpr int GsModeNtsc = 0x02;

        /// <summary>gsKit's GS_MODE_PAL.</summary>
        static constexpr int GsModePal = 0x03;

        /// <summary>gsKit's GS_NONINTERLACED.</summary>
        static constexpr int GsNonInterlaced = 0x00;

        /// <summary>gsKit's GS_INTERLACED.</summary>
        static constexpr int GsInterlaced = 0x01;

        /// <summary>gsKit's GS_FIELD.</summary>
        static constexpr int GsField = 0x00;

        /// <summary>gsKit's GS_FRAME.</summary>
        static constexpr int GsFrame = 0x01;

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
        /// rendering, as today's 640x448 setup does. MagH and the pixel aspect follow gsKit's integer magnification.
        /// </summary>
        /// <param name="record">Validated record.</param>
        /// <returns>gsKit screen fields.</returns>
        static Ps2GsVideoSettings ResolveGsSettings(const Ps2VideoModeBootRecord& record);

        /// <summary>
        /// Maps resolved settings to the GSGLOBAL values the boot host stores before gsKit_init_screen and the pixel aspect it
        /// reports to the core.
        /// </summary>
        /// <param name="settings">Settings from ResolveGsSettings.</param>
        /// <returns>The GS mode, interlace, field and size as gsKit numbers, and the window pixel aspect.</returns>
        static Ps2GsScreenFields ResolveGsScreenFields(const Ps2GsVideoSettings& settings);

        /// <summary>
        /// Checks the display fields gsKit_init_screen left in GSGLOBAL against what these settings assume: MagH as derived,
        /// MagV 0, DW = (MagH + 1) * Width, DH = Height, and the field and interlace ResolveGsScreenFields chose. The pixel
        /// aspect handed to the core is only right when all of them hold.
        /// </summary>
        /// <param name="settings">Settings the screen was set up from.</param>
        /// <param name="magH">GsGlobal->MagH after gsKit_init_screen.</param>
        /// <param name="magV">GsGlobal->MagV.</param>
        /// <param name="displayWidth">GsGlobal->DW.</param>
        /// <param name="displayHeight">GsGlobal->DH.</param>
        /// <param name="field">GsGlobal->Field.</param>
        /// <param name="interlace">GsGlobal->Interlace.</param>
        /// <param name="error">Cleared on success; on failure, a diagnostic giving every expected and actual value.</param>
        /// <returns>True when gsKit set the screen up as the settings assume.</returns>
        static bool VerifyGsInit(const Ps2GsVideoSettings& settings, int magH, int magV, int displayWidth, int displayHeight, int field,
            int interlace, std::string& error);

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
