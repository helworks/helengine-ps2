namespace helengine {
    /// <summary>
    /// Identifies the alpha-storage characteristics encoded into one cooked PS2 runtime texture.
    /// </summary>
    public enum Ps2TextureAlphaMode : byte {
        /// <summary>
        /// Indicates that the texture stores no meaningful alpha variation.
        /// </summary>
        Opaque = 0,

        /// <summary>
        /// Indicates that the texture stores a full alpha channel.
        /// </summary>
        Full = 1,

        /// <summary>Stores one alpha bit expanded through canonical TEXA transparent and opaque values.</summary>
        Binary = 2
    }
}
