namespace helengine {
    /// <summary>
    /// Stores one PS2-owned cooked runtime texture payload emitted by the PS2 builder.
    /// </summary>
    public class Ps2TextureAsset : Asset {
        /// <summary>
        /// Gets or sets the cooked texture width in pixels.
        /// </summary>
        public ushort Width;

        /// <summary>
        /// Gets or sets the cooked texture height in pixels.
        /// </summary>
        public ushort Height;

        /// <summary>
        /// Gets or sets the packed PS2 texture format.
        /// </summary>
        public Ps2TextureFormat Format;

        /// <summary>
        /// Gets or sets the packed PS2 alpha-storage mode.
        /// </summary>
        public Ps2TextureAlphaMode AlphaMode;

        /// <summary>
        /// Gets or sets the packed PS2 texture pixel payload.
        /// </summary>
        public byte[] PixelData;

        /// <summary>
        /// Gets or sets the optional packed PS2 palette payload.
        /// </summary>
        public byte[] PaletteData;
    }
}
