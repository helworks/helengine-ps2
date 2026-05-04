using helengine.baseplatform.Definitions;
using helengine.baseplatform.Profiles;

namespace helengine.ps2.builder;

public static class Ps2PlatformDefinitionFactory {
    public static PlatformDefinition Create() {
        return new PlatformDefinition(
            "ps2",
            "PlayStation 2",
            [
                new PlatformBuildProfileDefinition(
                    "ps2-default",
                    "PS2 Default",
                    "Standard PS2 player build",
                    "gs-kit",
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
                    "gs-kit",
                    "GSKit",
                    "GSKit framebuffer backend",
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
                            [])
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
