namespace helengine {
    /// <summary>
    /// Identifies the PS2-owned cooked asset type stored in one PS2 runtime binary payload.
    /// </summary>
    public enum Ps2AssetBinaryValueKind : ushort {
        /// <summary>
        /// The payload stores one <see cref="Ps2MaterialAsset"/>.
        /// </summary>
        Ps2MaterialAsset = 1,

        /// <summary>
        /// The payload stores one <see cref="Ps2TextureAsset"/>.
        /// </summary>
        Ps2TextureAsset = 2,

        /// <summary>
        /// The payload stores one <see cref="Ps2ModelAsset"/>.
        /// </summary>
        Ps2ModelAsset = 3
    }
}
