namespace helengine {
    /// <summary>
    /// Stores one PS2-owned cooked runtime material payload emitted by the PS2 builder.
    /// </summary>
    public class Ps2MaterialAsset : Asset {
        /// <summary>
        /// Gets or sets the renderer-family identifier that produced this cooked PS2 material.
        /// </summary>
        public string RendererFamilyId;

        /// <summary>
        /// Gets or sets the PS2 lighting mode selected for this material.
        /// </summary>
        public Ps2MaterialLightingMode LightingMode;

        /// <summary>
        /// Gets or sets the PS2 alpha handling mode selected for this material.
        /// </summary>
        public Ps2MaterialAlphaMode AlphaMode;

        /// <summary>
        /// Gets or sets the coarse PS2 render-class bucket used for draw ordering.
        /// </summary>
        public Ps2RenderClass RenderClass;

        /// <summary>
        /// Gets or sets the cooked base-color red channel.
        /// </summary>
        public byte BaseColorR;

        /// <summary>
        /// Gets or sets the cooked base-color green channel.
        /// </summary>
        public byte BaseColorG;

        /// <summary>
        /// Gets or sets the cooked base-color blue channel.
        /// </summary>
        public byte BaseColorB;

        /// <summary>
        /// Gets or sets the cooked base-color alpha channel.
        /// </summary>
        public byte BaseColorA;

        /// <summary>
        /// Gets or sets the cooked PS2 runtime-relative texture path.
        /// </summary>
        public string TextureRelativePath;

        /// <summary>
        /// Gets or sets whether the material should render both winding directions.
        /// </summary>
        public bool DoubleSided;

        /// <summary>
        /// Gets or sets whether the material should contribute to PS2 shadow rendering.
        /// </summary>
        public bool CastShadows;

        /// <summary>
        /// Gets or sets whether vertex colors should modulate the material output.
        /// </summary>
        public bool UseVertexColor;

        /// <summary>
        /// Gets or sets whether the authored material allowed the more expensive showcase path.
        /// </summary>
        public bool ExpensiveModeAllowed;

        /// <summary>
        /// Gets or sets the cooked roughness parameter used by the PS2 runtime.
        /// </summary>
        public float Roughness;

        /// <summary>
        /// Gets or sets the cooked specular-strength parameter used by the PS2 runtime.
        /// </summary>
        public float SpecularStrength;

        /// <summary>
        /// Gets or sets the cooked emissive-strength parameter used by the PS2 runtime.
        /// </summary>
        public float EmissiveStrength;
    }
}
