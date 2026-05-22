using helengine;
using helengine.baseplatform.Requests;
using helengine.baseplatform.Results;
using System.Globalization;

namespace helengine.ps2.builder;

/// <summary>
/// Translates PS2 material schema payloads into cooked PS2 runtime material assets.
/// </summary>
public sealed class Ps2MaterialCooker {
    /// <summary>
    /// Generic material schema id authored by the editor for standard lit materials.
    /// </summary>
    const string StandardShaderSchemaId = "standard-shader";

    /// <summary>
    /// Generic standard-shader texture field id authored by the editor.
    /// </summary>
    const string StandardShaderTextureFieldId = "texture-id";

    /// <summary>
    /// Generic standard-shader cast-shadow field id authored by the editor.
    /// </summary>
    const string StandardShaderCastShadowsFieldId = "casts-shadow";

    /// <summary>
    /// Generic standard-shader base-color field id authored by the editor.
    /// </summary>
    const string StandardShaderBaseColorFieldId = "base-color";

    /// <summary>
    /// Cooks one PS2 material request into a serialized PS2 runtime material payload.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Serialized PS2 material asset plus any referenced shader dependencies.</returns>
    public PlatformMaterialCookResult Cook(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        Ps2MaterialAlphaMode alphaMode = ResolveAlphaMode(request);
        ResolveBaseColor(request, out byte baseColorR, out byte baseColorG, out byte baseColorB, out byte baseColorA);
        Ps2MaterialAsset cookedAsset = new Ps2MaterialAsset {
            Id = request.MaterialAssetId,
            RendererFamilyId = request.SelectedGraphicsProfileId,
            LightingMode = ResolveLightingMode(request),
            AlphaMode = alphaMode,
            RenderClass = ResolveRenderClass(alphaMode),
            BaseColorR = baseColorR,
            BaseColorG = baseColorG,
            BaseColorB = baseColorB,
            BaseColorA = baseColorA,
            TextureRelativePath = ResolveTextureRelativePath(request),
            DoubleSided = ResolveDoubleSided(request),
            CastShadows = ResolveCastShadows(request),
            UseVertexColor = ResolveUseVertexColor(request),
            ExpensiveModeAllowed = ResolveExpensiveModeAllowed(request),
            Roughness = ResolveRoughness(request),
            SpecularStrength = ResolveSpecularStrength(request),
            EmissiveStrength = ResolveEmissiveStrength(request)
        };

        return new PlatformMaterialCookResult(Ps2AssetSerializer.SerializeToBytes(cookedAsset), Array.Empty<string>());
    }

    /// <summary>
    /// Resolves the lighting mode for one PS2 material schema identifier.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Lighting mode consumed by the PS2 runtime.</returns>
    static Ps2MaterialLightingMode ResolveLightingMode(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        string schemaId = request.SchemaId;
        if (string.Equals(schemaId, Ps2MaterialSchemaIds.UnlitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.Unlit;
        } else if (string.Equals(schemaId, Ps2MaterialSchemaIds.SimpleLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.SimpleLit;
        } else if (string.Equals(schemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.ShowcaseLit;
        } else if (IsStandardShaderSchema(schemaId)) {
            if (string.Equals(request.SelectedGraphicsProfileId, "ps2-showcase-forward", StringComparison.OrdinalIgnoreCase)) {
                return Ps2MaterialLightingMode.ShowcaseLit;
            }

            return Ps2MaterialLightingMode.SimpleLit;
        }

        throw new InvalidOperationException($"PS2 material schema '{schemaId}' is not supported.");
    }

    /// <summary>
    /// Resolves the alpha mode for one serialized schema field value.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>PS2 alpha mode consumed by the runtime.</returns>
    static Ps2MaterialAlphaMode ResolveAlphaMode(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        string alphaModeValue = IsStandardShaderSchema(request.SchemaId)
            ? "opaque"
            : ReadRequiredField(request.FieldValues, Ps2MaterialSchemaIds.AlphaModeFieldId);
        if (string.Equals(alphaModeValue, "opaque", StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialAlphaMode.Opaque;
        } else if (string.Equals(alphaModeValue, "alpha-test", StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialAlphaMode.AlphaTest;
        } else if (string.Equals(alphaModeValue, "alpha-blend", StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialAlphaMode.AlphaBlend;
        } else if (string.Equals(alphaModeValue, "additive", StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialAlphaMode.Additive;
        }

        throw new InvalidOperationException($"PS2 alpha mode '{alphaModeValue}' is not supported.");
    }

    /// <summary>
    /// Resolves the coarse render class from one PS2 alpha mode.
    /// </summary>
    /// <param name="alphaMode">Alpha mode selected for the material.</param>
    /// <returns>Coarse render class used by the PS2 frame planner.</returns>
    static Ps2RenderClass ResolveRenderClass(Ps2MaterialAlphaMode alphaMode) {
        if (alphaMode == Ps2MaterialAlphaMode.Opaque) {
            return Ps2RenderClass.Opaque;
        } else if (alphaMode == Ps2MaterialAlphaMode.AlphaTest) {
            return Ps2RenderClass.AlphaTest;
        }

        return Ps2RenderClass.Transparent;
    }

    /// <summary>
    /// Resolves whether the cooked material should use vertex color modulation.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>True when vertex color should affect the final color.</returns>
    static bool ResolveUseVertexColor(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (IsStandardShaderSchema(request.SchemaId)) {
            return false;
        }

        string vertexColorModeValue = ReadRequiredField(request.FieldValues, Ps2MaterialSchemaIds.VertexColorModeFieldId);
        if (string.Equals(vertexColorModeValue, "multiply", StringComparison.OrdinalIgnoreCase)) {
            return true;
        } else if (string.Equals(vertexColorModeValue, "ignore", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        throw new InvalidOperationException($"PS2 vertex color mode '{vertexColorModeValue}' is not supported.");
    }

    /// <summary>
    /// Resolves whether the material explicitly allows the expensive showcase path.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>True when the material allows the expensive showcase path.</returns>
    static bool ResolveExpensiveModeAllowed(PlatformMaterialCookRequest request) {
        if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadRequiredBooleanField(request.FieldValues, Ps2MaterialSchemaIds.ExpensiveModeAllowedFieldId);
        } else if (IsStandardShaderSchema(request.SchemaId)) {
            return string.Equals(request.SelectedGraphicsProfileId, "ps2-showcase-forward", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Resolves the authored base color for one PS2 material request.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Cooked base color consumed by the PS2 runtime.</returns>
    static void ResolveBaseColor(
        PlatformMaterialCookRequest request,
        out byte red,
        out byte green,
        out byte blue,
        out byte alpha) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.UnlitTextured, StringComparison.OrdinalIgnoreCase)) {
            red = 255;
            green = 255;
            blue = 255;
            alpha = 255;
            return;
        }

        string colorValue = IsStandardShaderSchema(request.SchemaId)
            ? ReadOptionalField(request.FieldValues, StandardShaderBaseColorFieldId)
            : ReadOptionalField(request.FieldValues, Ps2MaterialSchemaIds.BaseColorFieldId);
        if (string.IsNullOrWhiteSpace(colorValue)) {
            red = 255;
            green = 255;
            blue = 255;
            alpha = 255;
            return;
        }

        ParseColor(colorValue, out red, out green, out blue, out alpha);
    }

    /// <summary>
    /// Resolves the roughness value for one PS2 material.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Cooked roughness value.</returns>
    static float ResolveRoughness(PlatformMaterialCookRequest request) {
        if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.UnlitTextured, StringComparison.OrdinalIgnoreCase)) {
            return 1.0f;
        } else if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.SimpleLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadOptionalFloatField(request.FieldValues, Ps2MaterialSchemaIds.RoughnessFieldId, 0.6f);
        } else if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadOptionalFloatField(request.FieldValues, Ps2MaterialSchemaIds.RoughnessFieldId, 0.35f);
        } else if (IsStandardShaderSchema(request.SchemaId)) {
            return string.Equals(request.SelectedGraphicsProfileId, "ps2-showcase-forward", StringComparison.OrdinalIgnoreCase) ? 0.35f : 0.6f;
        }

        throw new InvalidOperationException($"PS2 material schema '{request.SchemaId}' is not supported.");
    }

    /// <summary>
    /// Resolves the specular strength value for one PS2 material.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Cooked specular strength value.</returns>
    static float ResolveSpecularStrength(PlatformMaterialCookRequest request) {
        if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.UnlitTextured, StringComparison.OrdinalIgnoreCase)) {
            return 0.0f;
        } else if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.SimpleLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadOptionalFloatField(request.FieldValues, Ps2MaterialSchemaIds.SpecularStrengthFieldId, 0.25f);
        } else if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadOptionalFloatField(request.FieldValues, Ps2MaterialSchemaIds.SpecularStrengthFieldId, 0.65f);
        } else if (IsStandardShaderSchema(request.SchemaId)) {
            return string.Equals(request.SelectedGraphicsProfileId, "ps2-showcase-forward", StringComparison.OrdinalIgnoreCase) ? 0.65f : 0.25f;
        }

        throw new InvalidOperationException($"PS2 material schema '{request.SchemaId}' is not supported.");
    }

    /// <summary>
    /// Resolves the emissive strength value for one PS2 material.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Cooked emissive strength value.</returns>
    static float ResolveEmissiveStrength(PlatformMaterialCookRequest request) {
        if (string.Equals(request.SchemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return ReadOptionalFloatField(request.FieldValues, Ps2MaterialSchemaIds.EmissiveStrengthFieldId, 0.0f);
        } else if (IsStandardShaderSchema(request.SchemaId)) {
            return 0.0f;
        }

        return 0.0f;
    }

    /// <summary>
    /// Resolves the cooked texture path from PS2-specific or generic standard-shader field names.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Cooked texture relative path or an empty string when the material is untextured.</returns>
    static string ResolveTextureRelativePath(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (IsStandardShaderSchema(request.SchemaId)) {
            return ReadOptionalField(request.FieldValues, StandardShaderTextureFieldId);
        }

        return ReadOptionalField(request.FieldValues, Ps2MaterialSchemaIds.TextureRelativePathFieldId);
    }

    /// <summary>
    /// Resolves the cooked double-sided flag from PS2-specific or generic standard-shader defaults.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>True when the cooked material should render both faces.</returns>
    static bool ResolveDoubleSided(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (IsStandardShaderSchema(request.SchemaId)) {
            return false;
        }

        return ReadRequiredBooleanField(request.FieldValues, Ps2MaterialSchemaIds.DoubleSidedFieldId);
    }

    /// <summary>
    /// Resolves the cooked cast-shadows flag from PS2-specific or generic standard-shader field names.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>True when the cooked material should cast shadows.</returns>
    static bool ResolveCastShadows(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }
        if (IsStandardShaderSchema(request.SchemaId)) {
            return ReadOptionalBooleanField(request.FieldValues, StandardShaderCastShadowsFieldId, true);
        }

        return ReadRequiredBooleanField(request.FieldValues, Ps2MaterialSchemaIds.CastShadowsFieldId);
    }

    /// <summary>
    /// Returns whether the supplied schema id should use the generic standard-shader compatibility path.
    /// </summary>
    /// <param name="schemaId">Schema identifier selected for the material.</param>
    /// <returns>True when the schema uses the generic standard-shader compatibility path.</returns>
    static bool IsStandardShaderSchema(string schemaId) {
        return string.Equals(schemaId, StandardShaderSchemaId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses one serialized hex color into byte channels consumed by the cooked PS2 material.
    /// </summary>
    /// <param name="value">Serialized color string in `#RRGGBB` or `#RRGGBBAA` form.</param>
    /// <returns>Parsed RGBA color.</returns>
    static void ParseColor(
        string value,
        out byte red,
        out byte green,
        out byte blue,
        out byte alpha) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException("PS2 material base color must be provided.");
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span[0] != '#') {
            throw new InvalidOperationException($"PS2 material base color '{value}' must start with '#'.");
        } else if (span.Length != 7 && span.Length != 9) {
            throw new InvalidOperationException($"PS2 material base color '{value}' must use #RRGGBB or #RRGGBBAA format.");
        }

        red = ParseColorByte(span.Slice(1, 2), value, "red");
        green = ParseColorByte(span.Slice(3, 2), value, "green");
        blue = ParseColorByte(span.Slice(5, 2), value, "blue");
        alpha = span.Length == 9
            ? ParseColorByte(span.Slice(7, 2), value, "alpha")
            : (byte)255;
    }

    /// <summary>
    /// Parses one serialized color byte from a two-character hex span.
    /// </summary>
    /// <param name="value">Two-character hex span.</param>
    /// <param name="originalValue">Original serialized color string used for diagnostics.</param>
    /// <param name="channelName">Human-readable channel label used in failure messages.</param>
    /// <returns>Parsed byte value.</returns>
    static byte ParseColorByte(ReadOnlySpan<char> value, string originalValue, string channelName) {
        if (!byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte parsedValue)) {
            throw new InvalidOperationException($"PS2 material base color '{originalValue}' contains an invalid {channelName} channel.");
        }

        return parsedValue;
    }

    /// <summary>
    /// Reads one required serialized field value from the request field map.
    /// </summary>
    /// <param name="fieldValues">Serialized field values keyed by field identifier.</param>
    /// <param name="fieldId">Field identifier to resolve.</param>
    /// <returns>Resolved non-blank serialized field value.</returns>
    static string ReadRequiredField(IReadOnlyDictionary<string, string> fieldValues, string fieldId) {
        if (fieldValues == null) {
            throw new ArgumentNullException(nameof(fieldValues));
        } else if (string.IsNullOrWhiteSpace(fieldId)) {
            throw new ArgumentException("Field id must be provided.", nameof(fieldId));
        }

        string value;
        if (!fieldValues.TryGetValue(fieldId, out value) || string.IsNullOrWhiteSpace(value)) {
            throw new InvalidOperationException($"Missing required PS2 material field '{fieldId}'.");
        }

        return value;
    }

    /// <summary>
    /// Reads one optional serialized field value from the request field map.
    /// </summary>
    /// <param name="fieldValues">Serialized field values keyed by field identifier.</param>
    /// <param name="fieldId">Field identifier to resolve.</param>
    /// <returns>Resolved serialized field value or an empty string when the field is absent.</returns>
    static string ReadOptionalField(IReadOnlyDictionary<string, string> fieldValues, string fieldId) {
        if (fieldValues == null) {
            throw new ArgumentNullException(nameof(fieldValues));
        } else if (string.IsNullOrWhiteSpace(fieldId)) {
            throw new ArgumentException("Field id must be provided.", nameof(fieldId));
        }

        string value;
        if (!fieldValues.TryGetValue(fieldId, out value) || string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        return value;
    }

    /// <summary>
    /// Reads one required serialized boolean field value from the request field map.
    /// </summary>
    /// <param name="fieldValues">Serialized field values keyed by field identifier.</param>
    /// <param name="fieldId">Field identifier to resolve.</param>
    /// <returns>Parsed boolean field value.</returns>
    static bool ReadRequiredBooleanField(IReadOnlyDictionary<string, string> fieldValues, string fieldId) {
        string value = ReadRequiredField(fieldValues, fieldId);
        bool parsedValue;
        if (!bool.TryParse(value, out parsedValue)) {
            throw new InvalidOperationException($"PS2 material field '{fieldId}' must be a boolean value.");
        }

        return parsedValue;
    }

    /// <summary>
    /// Reads one optional serialized boolean field value from the request field map.
    /// </summary>
    /// <param name="fieldValues">Serialized field values keyed by field identifier.</param>
    /// <param name="fieldId">Field identifier to resolve.</param>
    /// <param name="defaultValue">Fallback value used when the field is absent.</param>
    /// <returns>Parsed boolean field value.</returns>
    static bool ReadOptionalBooleanField(IReadOnlyDictionary<string, string> fieldValues, string fieldId, bool defaultValue) {
        string value = ReadOptionalField(fieldValues, fieldId);
        if (string.IsNullOrWhiteSpace(value)) {
            return defaultValue;
        }

        bool parsedValue;
        if (!bool.TryParse(value, out parsedValue)) {
            throw new InvalidOperationException($"PS2 material field '{fieldId}' must be a boolean value.");
        }

        return parsedValue;
    }

    /// <summary>
    /// Reads one optional serialized float field value from the request field map.
    /// </summary>
    /// <param name="fieldValues">Serialized field values keyed by builder-defined field identifier.</param>
    /// <param name="fieldId">Field identifier to resolve.</param>
    /// <param name="defaultValue">Fallback value used when the field is absent.</param>
    /// <returns>Parsed float field value or the supplied default.</returns>
    static float ReadOptionalFloatField(IReadOnlyDictionary<string, string> fieldValues, string fieldId, float defaultValue) {
        if (fieldValues == null) {
            throw new ArgumentNullException(nameof(fieldValues));
        } else if (string.IsNullOrWhiteSpace(fieldId)) {
            throw new ArgumentException("Field id must be provided.", nameof(fieldId));
        }

        string value;
        if (!fieldValues.TryGetValue(fieldId, out value) || string.IsNullOrWhiteSpace(value)) {
            return defaultValue;
        }

        float parsedValue;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue)) {
            throw new InvalidOperationException($"PS2 material field '{fieldId}' must be a numeric value.");
        }

        return parsedValue;
    }
}
