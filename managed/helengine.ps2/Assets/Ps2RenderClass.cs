namespace helengine {
    /// <summary>
    /// Identifies the coarse PS2 render bucket used to sort one cooked runtime material.
    /// </summary>
    public enum Ps2RenderClass {
        /// <summary>
        /// Routes the material through the opaque pass.
        /// </summary>
        Opaque = 0,

        /// <summary>
        /// Routes the material through the alpha-test pass.
        /// </summary>
        AlphaTest = 1,

        /// <summary>
        /// Routes the material through the transparent pass.
        /// </summary>
        Transparent = 2
    }
}
