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
        Indexed4 = 2
    }
}
