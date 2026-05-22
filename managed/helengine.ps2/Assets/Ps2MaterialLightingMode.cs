namespace helengine {
    /// <summary>
    /// Identifies the lighting path that one cooked PS2 runtime material should use.
    /// </summary>
    public enum Ps2MaterialLightingMode {
        /// <summary>
        /// Uses the unlit PS2 material path.
        /// </summary>
        Unlit = 0,

        /// <summary>
        /// Uses the standard low-cost lit PS2 material path.
        /// </summary>
        SimpleLit = 1,

        /// <summary>
        /// Uses the more expensive showcase lit PS2 material path.
        /// </summary>
        ShowcaseLit = 2
    }
}
