namespace helengine {
    /// <summary>
    /// Identifies how one cooked PS2 material should handle alpha during rasterization.
    /// </summary>
    public enum Ps2MaterialAlphaMode {
        /// <summary>
        /// Treats the material as fully opaque.
        /// </summary>
        Opaque = 0,

        /// <summary>
        /// Uses alpha testing to reject low-alpha pixels.
        /// </summary>
        AlphaTest = 1,

        /// <summary>
        /// Uses alpha blending for translucent pixels.
        /// </summary>
        AlphaBlend = 2,

        /// <summary>
        /// Uses additive blending for emissive or glow-style materials.
        /// </summary>
        Additive = 3
    }
}
