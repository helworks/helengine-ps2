using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;

namespace helengine.ps2.builder;

/// <summary>
/// Creates the typed platform metadata exposed by the PS2 builder.
/// </summary>
public static class Ps2PlatformDefinitionFactory {
    /// <summary>
    /// Creates the PS2 platform definition with renderer-family-aware graphics profiles and material schemas.
    /// </summary>
    /// <returns>Typed PS2 platform metadata consumed by the editor and builder tests.</returns>
    public static PlatformDefinition Create() {
        return new PlatformDefinition(
            "ps2",
            "PlayStation 2",
            [
                new PlatformBuildProfileDefinition(
                    "ps2-default",
                    "PS2 Default",
                    "Standard PS2 player build",
                    "ps2-standard-forward",
                    "default",
                    [
                        new PlatformSettingDefinition(
                            "texture-scale-percent",
                            "Texture Scale Percent",
                            PlatformSettingKind.Text,
                            "100",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "shader-variant-pruning",
                            "Shader Variant Pruning",
                            PlatformSettingKind.Boolean,
                            "true",
                            true,
                            [])
                    ])
            ],
            [
                new PlatformGraphicsProfileDefinition(
                    "ps2-standard-forward",
                    "PS2 Standard Forward",
                    "Standard PS2 forward renderer for gameplay scenes.",
                    [
                        new PlatformSettingDefinition(
                            "default-width",
                            "Default Width",
                            PlatformSettingKind.Text,
                            "640",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "default-height",
                            "Default Height",
                            PlatformSettingKind.Text,
                            "448",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "vsync-enabled",
                            "VSync Enabled",
                            PlatformSettingKind.Boolean,
                            "true",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "fullscreen-enabled",
                            "Fullscreen Enabled",
                            PlatformSettingKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "depth-handler-mode",
                            "Depth Handler Mode",
                            PlatformSettingKind.Choice,
                            "hardware",
                            true,
                            ["hardware", "software"])
                    ]),
                new PlatformGraphicsProfileDefinition(
                    "ps2-showcase-forward",
                    "PS2 Showcase Forward",
                    "Expensive PS2 forward renderer for tiny showcase scenes.",
                    [
                        new PlatformSettingDefinition(
                            "default-width",
                            "Default Width",
                            PlatformSettingKind.Text,
                            "640",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "default-height",
                            "Default Height",
                            PlatformSettingKind.Text,
                            "448",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "vsync-enabled",
                            "VSync Enabled",
                            PlatformSettingKind.Boolean,
                            "true",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "fullscreen-enabled",
                            "Fullscreen Enabled",
                            PlatformSettingKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "depth-handler-mode",
                            "Depth Handler Mode",
                            PlatformSettingKind.Choice,
                            "hardware",
                            true,
                            ["hardware", "software"])
                    ])
            ],
            [
                new PlatformAssetRequirementDefinition(
                    "scene",
                    "Scene",
                    true,
                    ["helen"]),
                new PlatformAssetRequirementDefinition(
                    "texture",
                    "Texture",
                    true,
                    ["png", "tga", "jpg"]),
                new PlatformAssetRequirementDefinition(
                    "font",
                    "Font",
                    false,
                    ["font.asset", "ttf", "otf"])
            ],
            [
                new PlatformMaterialSchemaDefinition(
                    "ps2-unlit-textured",
                    "PS2 Unlit Textured",
                    ["ps2-standard-forward", "ps2-showcase-forward"],
                    [
                        new PlatformMaterialFieldDefinition(
                            "texture-relative-path",
                            "Texture",
                            PlatformMaterialFieldKind.Text,
                            string.Empty,
                            false,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "alpha-mode",
                            "Alpha Mode",
                            PlatformMaterialFieldKind.Choice,
                            "opaque",
                            true,
                            ["opaque", "alpha-test", "alpha-blend", "additive"]),
                        new PlatformMaterialFieldDefinition(
                            "double-sided",
                            "Double Sided",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.CastShadowsFieldId,
                            "Cast Shadows",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "vertex-color-mode",
                            "Vertex Color",
                            PlatformMaterialFieldKind.Choice,
                            "multiply",
                            true,
                            ["multiply", "ignore"])
                    ]),
                new PlatformMaterialSchemaDefinition(
                    "ps2-simple-lit-textured",
                    "PS2 Simple Lit Textured",
                    ["ps2-standard-forward", "ps2-showcase-forward"],
                    [
                        new PlatformMaterialFieldDefinition(
                            "texture-relative-path",
                            "Texture",
                            PlatformMaterialFieldKind.Text,
                            string.Empty,
                            false,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "alpha-mode",
                            "Alpha Mode",
                            PlatformMaterialFieldKind.Choice,
                            "opaque",
                            true,
                            ["opaque", "alpha-test", "alpha-blend", "additive"]),
                        new PlatformMaterialFieldDefinition(
                            "double-sided",
                            "Double Sided",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.CastShadowsFieldId,
                            "Cast Shadows",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "vertex-color-mode",
                            "Vertex Color",
                            PlatformMaterialFieldKind.Choice,
                            "multiply",
                            true,
                            ["multiply", "ignore"]),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.BaseColorFieldId,
                            "Base Color",
                            PlatformMaterialFieldKind.Color,
                            "#ffffff",
                            false,
                            [])
                    ]),
                new PlatformMaterialSchemaDefinition(
                    "ps2-showcase-lit-textured",
                    "PS2 Showcase Lit Textured",
                    ["ps2-showcase-forward"],
                    [
                        new PlatformMaterialFieldDefinition(
                            "texture-relative-path",
                            "Texture",
                            PlatformMaterialFieldKind.Text,
                            string.Empty,
                            false,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "alpha-mode",
                            "Alpha Mode",
                            PlatformMaterialFieldKind.Choice,
                            "opaque",
                            true,
                            ["opaque", "alpha-test", "alpha-blend", "additive"]),
                        new PlatformMaterialFieldDefinition(
                            "double-sided",
                            "Double Sided",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.CastShadowsFieldId,
                            "Cast Shadows",
                            PlatformMaterialFieldKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "vertex-color-mode",
                            "Vertex Color",
                            PlatformMaterialFieldKind.Choice,
                            "multiply",
                            true,
                            ["multiply", "ignore"]),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.BaseColorFieldId,
                            "Base Color",
                            PlatformMaterialFieldKind.Color,
                            "#ffffff",
                            false,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.RoughnessFieldId,
                            "Roughness",
                            PlatformMaterialFieldKind.Number,
                            "0.35",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.SpecularStrengthFieldId,
                            "Specular Strength",
                            PlatformMaterialFieldKind.Number,
                            "0.65",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            "expensive-mode-allowed",
                            "Expensive Mode",
                            PlatformMaterialFieldKind.Boolean,
                            "true",
                            true,
                            []),
                        new PlatformMaterialFieldDefinition(
                            Ps2MaterialSchemaIds.EmissiveStrengthFieldId,
                            "Emissive Strength",
                            PlatformMaterialFieldKind.Number,
                            "0.0",
                            true,
                            [])
                    ])
            ],
            [
                new PlatformComponentCompatibilityDefinition(
                    "helengine.meshcomponent",
                    PlatformComponentCompatibilityKind.Transform,
                    "Mesh components are normalized during packaging.",
                    string.Empty),
                new PlatformComponentCompatibilityDefinition(
                    "helengine.cameracomponent",
                    PlatformComponentCompatibilityKind.Transform,
                    "Camera components are normalized during packaging.",
                    string.Empty),
                new PlatformComponentCompatibilityDefinition(
                    "helengine.fpscomponent",
                    PlatformComponentCompatibilityKind.Transform,
                    "Font references are rewritten during packaging.",
                    string.Empty),
                new PlatformComponentCompatibilityDefinition(
                    "helengine.textcomponent",
                    PlatformComponentCompatibilityKind.Transform,
                    "Font references are rewritten during packaging.",
                    string.Empty)
            ],
            [
                new PlatformCodegenProfileDefinition(
                    "default",
                    "Default",
                    "PS2 C# to C++ codegen profile",
                    PlatformCodegenLanguage.Cpp,
                    PlatformSerializationEndianness.LittleEndian,
                    [
                        new PlatformSettingDefinition(
                            "write-conversion-report",
                            "Write Conversion Report",
                            PlatformSettingKind.Boolean,
                            "true",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "include-project-defined-preprocessor-symbols",
                            "Include Project Symbols",
                            PlatformSettingKind.Boolean,
                            "false",
                            true,
                            []),
                        new PlatformSettingDefinition(
                            "load-native-runtime-metadata",
                            "Load Native Runtime Metadata",
                            PlatformSettingKind.Boolean,
                            "true",
                            true,
                            [])
                    ])
            ],
            [
                new PlatformStorageProfileDefinition(
                    "disc-layout",
                    "Disc Layout",
                    PlatformStorageProfileKind.DiscLayout,
                    "ps2-disc-layout",
                    allowContainerSegmentation: true)
            ],
            [
                new PlatformMediaProfileDefinition(
                    "ps2-install-tree",
                    "PS2 Install Tree",
                    PlatformMediaLayoutKind.InstallTree,
                    allowPhysicalDuplication: true,
                    preferLocalityOverDeduplication: true)
            ]);
    }
}
