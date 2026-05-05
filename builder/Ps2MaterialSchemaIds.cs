namespace helengine.ps2.builder;

/// <summary>
/// Defines the material schema and field identifiers owned by the PS2 builder.
/// </summary>
public static class Ps2MaterialSchemaIds {
    /// <summary>
    /// Schema identifier for the unlit textured PS2 material path.
    /// </summary>
    public const string UnlitTextured = "ps2-unlit-textured";

    /// <summary>
    /// Schema identifier for the simple-lit textured PS2 material path.
    /// </summary>
    public const string SimpleLitTextured = "ps2-simple-lit-textured";

    /// <summary>
    /// Schema identifier for the showcase-only lit textured PS2 material path.
    /// </summary>
    public const string ShowcaseLitTextured = "ps2-showcase-lit-textured";

    /// <summary>
    /// Field identifier for the cooked PS2 texture path.
    /// </summary>
    public const string TextureRelativePathFieldId = "texture-relative-path";

    /// <summary>
    /// Field identifier for the alpha behavior selection.
    /// </summary>
    public const string AlphaModeFieldId = "alpha-mode";

    /// <summary>
    /// Field identifier for the double-sided toggle.
    /// </summary>
    public const string DoubleSidedFieldId = "double-sided";

    /// <summary>
    /// Field identifier for whether the material contributes to shadow passes.
    /// </summary>
    public const string CastShadowsFieldId = "cast-shadows";

    /// <summary>
    /// Field identifier for the vertex-color mode.
    /// </summary>
    public const string VertexColorModeFieldId = "vertex-color-mode";

    /// <summary>
    /// Field identifier for the expensive showcase-path toggle.
    /// </summary>
    public const string ExpensiveModeAllowedFieldId = "expensive-mode-allowed";

    /// <summary>
    /// Field identifier for the fixed-shader roughness control.
    /// </summary>
    public const string RoughnessFieldId = "roughness";

    /// <summary>
    /// Field identifier for the fixed-shader specular-strength control.
    /// </summary>
    public const string SpecularStrengthFieldId = "specular-strength";

    /// <summary>
    /// Field identifier for the fixed-shader emissive-strength control.
    /// </summary>
    public const string EmissiveStrengthFieldId = "emissive-strength";
}
