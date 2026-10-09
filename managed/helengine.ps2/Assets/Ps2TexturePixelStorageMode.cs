namespace helengine {
    /// <summary>
    /// Identifies the GS pixel storage mode stored in one cooked PS2 texture payload.
    /// </summary>
    public enum Ps2TexturePixelStorageMode : byte {
        /// <summary>
        /// Stores one 32-bit direct-color texture payload.
        /// </summary>
        PsmCt32 = 0,

        /// <summary>
        /// Stores one 8-bit indexed texture payload.
        /// </summary>
        PsmT8 = 1,

        /// <summary>
        /// Stores one 4-bit indexed texture payload.
        /// </summary>
        PsmT4 = 2,

        /// <summary>Stores three-byte RGB in the native GS transfer representation.</summary>
        PsmCt24 = 3,

        /// <summary>Stores little-endian A1B5G5R5 in the native GS transfer representation.</summary>
        PsmCt16 = 4,

        /// <summary>Stores A1B5G5R5 with signed-layout addressing in the native GS transfer representation.</summary>
        PsmCt16S = 5,

        /// <summary>Stores eight-bit high-byte indices in the native GS transfer representation.</summary>
        PsmT8H = 6,

        /// <summary>Stores four-bit low-half high-byte indices in the native GS transfer representation.</summary>
        PsmT4HL = 7,

        /// <summary>Stores four-bit high-half high-byte indices in the native GS transfer representation.</summary>
        PsmT4HH = 8,

        /// <summary>Stores raw 32-bit color words in depth-addressed storage in the native GS transfer representation.</summary>
        PsmZ32 = 9,

        /// <summary>Stores raw 24-bit RGB words in depth-addressed storage in the native GS transfer representation.</summary>
        PsmZ24 = 10,

        /// <summary>Stores raw A1B5G5R5 words in depth-addressed storage in the native GS transfer representation.</summary>
        PsmZ16 = 11,

        /// <summary>Stores raw A1B5G5R5 words in alternate depth-addressed storage in the native GS transfer representation.</summary>
        PsmZ16S = 12
    }
}
