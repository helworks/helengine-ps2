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
        PsmT4 = 2
    }
}
