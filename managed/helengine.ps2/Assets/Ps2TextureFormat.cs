namespace helengine {
    /// <summary>
    /// Identifies the packed PS2 texture payload format stored in one cooked runtime texture.
    /// </summary>
    public enum Ps2TextureFormat : byte {
        /// <summary>
        /// Stores one 32-bit RGBA texture payload.
        /// </summary>
        Rgba32 = 0,

        /// <summary>
        /// Stores one 8-bit indexed texture payload plus one palette payload.
        /// </summary>
        Indexed8 = 1,

        /// <summary>
        /// Stores one 4-bit indexed texture payload plus one palette payload.
        /// </summary>
        Indexed4 = 2,

        /// <summary>Stores three-byte RGB in the native GS transfer representation.</summary>
        Rgb24 = 3,

        /// <summary>Stores little-endian A1B5G5R5 in the native GS transfer representation.</summary>
        Rgba16 = 4,

        /// <summary>Stores A1B5G5R5 with signed-layout addressing in the native GS transfer representation.</summary>
        Rgba16S = 5,

        /// <summary>Stores eight-bit high-byte indices in the native GS transfer representation.</summary>
        Indexed8H = 6,

        /// <summary>Stores four-bit low-half high-byte indices in the native GS transfer representation.</summary>
        Indexed4HL = 7,

        /// <summary>Stores four-bit high-half high-byte indices in the native GS transfer representation.</summary>
        Indexed4HH = 8,

        /// <summary>Stores raw 32-bit color words in depth-addressed storage in the native GS transfer representation.</summary>
        Depth32 = 9,

        /// <summary>Stores raw 24-bit RGB words in depth-addressed storage in the native GS transfer representation.</summary>
        Depth24 = 10,

        /// <summary>Stores raw A1B5G5R5 words in depth-addressed storage in the native GS transfer representation.</summary>
        Depth16 = 11,

        /// <summary>Stores raw A1B5G5R5 words in alternate depth-addressed storage in the native GS transfer representation.</summary>
        Depth16S = 12
    }
}
