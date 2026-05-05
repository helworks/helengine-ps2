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
    /// Cooks one PS2 material request into a serialized PS2 runtime material payload.
    /// </summary>
    /// <param name="request">Builder-owned material translation request.</param>
    /// <returns>Serialized PS2 material asset plus any referenced shader dependencies.</returns>
    public PlatformMaterialCookResult Cook(PlatformMaterialCookRequest request) {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        Ps2MaterialAlphaMode alphaMode = ResolveAlphaMode(ReadRequiredField(request.FieldValues, Ps2MaterialSchemaIds.AlphaModeFieldId));
        Ps2MaterialAsset cookedAsset = new Ps2MaterialAsset {
            Id = request.MaterialAssetId,
            RendererFamilyId = request.SelectedGraphicsProfileId,
            LightingMode = ResolveLightingMode(request.SchemaId),
            AlphaMode = alphaMode,
            RenderClass = ResolveRenderClass(alphaMode),
            TextureRelativePath = ReadOptionalField(request.FieldValues, Ps2MaterialSchemaIds.TextureRelativePathFieldId),
            DoubleSided = ReadRequiredBooleanField(request.FieldValues, Ps2MaterialSchemaIds.DoubleSidedFieldId),
            CastShadows = ReadRequiredBooleanField(request.FieldValues, Ps2MaterialSchemaIds.CastShadowsFieldId),
            UseVertexColor = ResolveUseVertexColor(ReadRequiredField(request.FieldValues, Ps2MaterialSchemaIds.VertexColorModeFieldId)),
            ExpensiveModeAllowed = ResolveExpensiveModeAllowed(request),
            Roughness = ResolveRoughness(request),
            SpecularStrength = ResolveSpecularStrength(request),
            EmissiveStrength = ResolveEmissiveStrength(request)
        };

        return new PlatformMaterialCookResult(helengine.files.AssetSerializer.SerializeToBytes(cookedAsset), Array.Empty<string>());
    }

    /// <summary>
    /// Resolves the lighting mode for one PS2 material schema identifier.
    /// </summary>
    /// <param name="schemaId">Schema identifier selected for the material.</param>
    /// <returns>Lighting mode consumed by the PS2 runtime.</returns>
    static Ps2MaterialLightingMode ResolveLightingMode(string schemaId) {
        if (string.Equals(schemaId, Ps2MaterialSchemaIds.UnlitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.Unlit;
        } else if (string.Equals(schemaId, Ps2MaterialSchemaIds.SimpleLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.SimpleLit;
        } else if (string.Equals(schemaId, Ps2MaterialSchemaIds.ShowcaseLitTextured, StringComparison.OrdinalIgnoreCase)) {
            return Ps2MaterialLightingMode.ShowcaseLit;
        }

        throw new InvalidOperationException($"PS2 material schema '{schemaId}' is not supported.");
    }

    /// <summary>
    /// Resolves the alpha mode for one serialized schema field value.
    /// </summary>
    /// <param name="alphaModeValue">Serialized alpha mode value.</param>
    /// <returns>PS2 alpha mode consumed by the runtime.</returns>
    static Ps2MaterialAlphaMode ResolveAlphaMode(string alphaModeValue) {
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
    /// <param name="vertexColorModeValue">Serialized vertex-color mode value.</param>
    /// <returns>True when vertex color should affect the final color.</returns>
    static bool ResolveUseVertexColor(string vertexColorModeValue) {
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
        }

        return false;
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
        }

        return 0.0f;
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
