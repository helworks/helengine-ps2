using System.Reflection;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies the remaining generated-core normalization behavior required by the PS2 native build executor.
/// </summary>
public sealed class Ps2NativeBuildExecutorTests {
    /// <summary>
    /// Verifies that the opaque untextured VU program starts from the current xgkick-only baseline before transform work is reintroduced.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_ShouldStartAsKickOnlyBaseline() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("xtop VI02", source, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI03, VI02, 0x00000010", source, StringComparison.Ordinal);
        Assert.Contains("xgkick VI03", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the transform-only opaque VU path writes the XYZ2 ADC lane into packet memory before storing XYZ data.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldWriteAdcWordsIntoXyz2Slots() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("isw.w", source, StringComparison.Ordinal);
        Assert.Contains("22(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("24(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("26(VI02)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF01", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF02", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mfir.w VF03", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the transform-only opaque VU path writes triangle vertices into packet slots using the CPU-facing winding order.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingTransformOnlyPacketPath_ShouldSwapSecondAndThirdVertexStores() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.Contains("sq.xyz VF01, 22(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF03, 24(VI02)", source, StringComparison.Ordinal);
        Assert.Contains("sq.xyz VF02, 26(VI02)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF02, 24(VI02)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq.xyz VF03, 26(VI02)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the compact untextured VU path preserves the host-built flat-color GIF template instead of overwriting RGBAQ slots in the microprogram.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldNotOverwriteTriangleRgbaqSlots() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("sq VF12, 21(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq VF12, 23(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sq VF12, 25(VI00)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the compact untextured VU path keeps flat color host-owned and does not execute VU-side face-normal lighting math.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingCompactUntexturedPath_ShouldAvoidVuLightingMath() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("opmula.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("opmsub.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF16, 30(VI00)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("lq VF19, 33(VI00)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the last visible rotating cube_test VU baseline stays on the earlier transform-only path instead of the later VU clip-flag variant that regressed to a black frame.
    /// </summary>
    [Fact]
    public void Ps2OpaqueDraw3DProgram_WhenUsingLastVisibleCubeTestBaseline_ShouldStayOnTransformOnlyPath() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string programPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "programs",
            "Ps2OpaqueDraw3D.vsm");

        string source = File.ReadAllText(programPath);

        Assert.DoesNotContain("clipw.xyz", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fcand", source, StringComparison.Ordinal);
        Assert.Contains("iaddiu VI04, VI00, 0x00000000", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 native build consumes the editor's generated-core unity translation unit name instead of the retired amalgamated file name.
    /// </summary>
    [Fact]
    public void Ps2NativeBuildExecutor_WhenUsingCurrentEditorGeneratedCoreOutput_ShouldCompileUnityTranslationUnit() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string makefilePath = Path.Combine(repositoryRootPath, "Makefile");

        string source = File.ReadAllText(makefilePath);

        Assert.Contains("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.Contains("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_unity.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$(HOST_GENERATED_CORE_STAGE_ROOT)/helengine_core_amalgamated.cpp", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque untextured VU packet builder does not use CPU near-plane clipping or CPU front-face rejection in the untextured VU path.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedPath_ShouldNotUseCpuTriangleRejectionMarkers() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);
        string untexturedBranch = ExtractSourceRange(
            source,
            "} else if (!textured) {",
            "} else {");

        Assert.DoesNotContain("ClipTriangleAgainstNearPlane(", untexturedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFrontFacingTriangle(", untexturedBranch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque-untextured VU packet template uses an explicit VU-owned header instead of the draw_prim helper packet seam.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldUseVuOwnedPacketHeader() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);

        Assert.Contains("packet2_utils_gif_add_set(gifPacket.get(), 1);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_gs_add_lod(gifPacket.get(), &lod);", source, StringComparison.Ordinal);
        Assert.Contains("packet2_add_2x_s64(", source, StringComparison.Ordinal);
        Assert.Contains("GS_REG_TEST", source, StringComparison.Ordinal);
        Assert.Contains("packet2_utils_gs_add_prim_giftag(gifPacket.get(), &prim, 3u, UntexturedTriangleRegisterList, 2u, 0);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("draw_prim_end(gifPacket.get()->next, 2, UntexturedTriangleRegisterList)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the opaque-untextured VU packet header mirrors the active depth-test state instead of hardcoding all-pass depth behavior.
    /// </summary>
    [Fact]
    public void Ps2VuVifPacketBuilder_WhenBuildingOpaqueUntexturedTemplate_ShouldRespectActiveDepthState() {
        string repositoryRootPath = ResolveRepositoryRoot();
        string builderPath = Path.Combine(
            repositoryRootPath,
            "src",
            "platform",
            "ps2",
            "rendering",
            "vu",
            "Ps2VuVifPacketBuilder.cpp");

        string source = File.ReadAllText(builderPath);

        Assert.Contains("gsGlobal != nullptr && gsGlobal->ZBuffering == GS_SETTING_ON", source, StringComparison.Ordinal);
        Assert.Contains("rendererZTestMethod", source, StringComparison.Ordinal);
        Assert.Contains("rendererDepthTestEnabled", source, StringComparison.Ordinal);
        Assert.Contains("rendererDepthTestEnabled ? DRAW_ENABLE : DRAW_DISABLE", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 generated-core normalization rewrites ScrollComponent size initialization to the current value-type int2 API.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenScrollComponentUsesPointerSizedInt2_RewritesToValueInitialization() {
        const string source = "ScrollComponent::ScrollComponent() : SizeValue(new int2())\n{\n}\n";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("ScrollComponent.cpp", source);

        Assert.Contains("SizeValue(int2())", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeValue(new int2())", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated content-manager material registration is normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenContentManagerUsesRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#include "RuntimeContentManagerConfiguration.hpp"
#include "ContentManager.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "AssetContentProcessor.hpp"
#include "MaterialAsset.hpp"

void RuntimeContentManagerConfiguration::ConfigureSharedAssetContentManager(::ContentManager* contentManager)
{
RegisterProcessorIfMissing<MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("RegisterProcessorIfMissing<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterProcessorIfMissing<MaterialAsset*>", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated scene material resolution is normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesCurrentRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "MaterialAsset.hpp"
#include "ShaderAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);
::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);
this->TrackOwnedMaterial(runtimeMaterial);
this->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);
return runtimeMaterial;}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Load<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the older ownership-guarded raw resolver shape is also normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesLegacyGuardedRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "runtime/finally.hpp"
#include "MaterialAsset.hpp"
#include "ShaderAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
auto __releaseMaterialAssetGuard = he_cpp_make_scope_exit([&]() {
ReleaseTransientMaterialAsset(materialAsset);
});
::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);
auto __releaseShaderAssetGuard = he_cpp_make_scope_exit([&]() {
ReleaseTransientShaderAsset(shaderAsset);
});
::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);
this->TrackOwnedMaterial(runtimeMaterial);
this->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);
return runtimeMaterial;}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Contains("#include \"PlatformMaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Load<PlatformMaterialAsset*>", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager headers are normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#pragma once
class RuntimeMaterial;
class MaterialAsset;
class ShaderAsset;
class RuntimeModel;
class ModelAsset;

class RenderManager3D
{
public:
    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);
    virtual ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) = 0;
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.hpp", source);

        Assert.Contains("class PlatformMaterialAsset;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that raw generated render-manager sources are normalized back onto the cooked-platform material contract required by PS2 exports.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesRawMaterialContract_RewritesToCookedPlatformContract() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source);

        Assert.Contains("BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("platform-owned cooked material creation", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated render-manager headers using the generic cooked-platform-material seam remain unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#pragma once
class RuntimeMaterial;
class MaterialAsset;
class PlatformMaterialAsset;
class ShaderAsset;
class RuntimeModel;
class ModelAsset;

class RenderManager3D
{
public:
    virtual ::RuntimeMaterial* BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset);
    virtual ::RuntimeMaterial* BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset);
    virtual ::RuntimeModel* BuildModelFromRaw(::ModelAsset* data) = 0;
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.hpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated render-manager sources using the generic cooked-platform-material seam remain unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromCooked(::PlatformMaterialAsset* materialAsset)
{
throw new NotSupportedException("This renderer does not support platform-owned cooked material creation.");
}

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated runtime content-manager registration using the generic cooked-platform-material seam remains unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenContentManagerUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RuntimeContentManagerConfiguration.hpp"
#include "ContentManager.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "AssetContentProcessor.hpp"
#include "PlatformMaterialAsset.hpp"

void RuntimeContentManagerConfiguration::ConfigureSharedAssetContentManager(::ContentManager* contentManager)
{
RegisterProcessorIfMissing<PlatformMaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::PlatformMaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that generated scene material resolution using the generic cooked-platform-material seam remains unchanged.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesGenericCookedMaterialContract_LeavesSourceUnchanged() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "runtime/native_exceptions.hpp"
#include "runtime/native_string.hpp"
#include "ModelAsset.hpp"
#include "ContentManager.hpp"
#include "RuntimeModel.hpp"
#include "PlatformMaterialAsset.hpp"
#include "RuntimeMaterial.hpp"
#include "Core.hpp"
#include "RenderManager3D.hpp"
#include "TextureAsset.hpp"
#include "RuntimeTexture.hpp"
#include "RenderManager2D.hpp"
#include "MaterialPropertyBlock.hpp"
#include "FontAsset.hpp"
#include "system/io/path.hpp"
#include "system/io/file.hpp"
#include "RuntimeContentProcessorIds.hpp"
#include "StandardMaterialTextureBindingDefaults.hpp"
#include "SceneAssetReferenceSourceKind.hpp"
#include "ShaderTargetNames.hpp"

::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
    if (reference == nullptr)
    {
throw new ArgumentNullException("reference");
    }
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::PlatformMaterialAsset *materialAsset = this->AssetContentManager->Load<PlatformMaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
return Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource);
    }

    /// <summary>
    /// Verifies that the PS2 generated runtime graphics manifest stays on the simplest supported graphics path.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRuntimeGraphicsManifestEnablesHdrAndHighPostProcess_RewritesToDisabledValues() {
        const string source = """
const HERuntimeGraphicsRendererManifest RuntimeGraphicsRendererManifest =
{
    true,
    HERuntimePostProcessTier::High,
};
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("runtime", "runtime_graphics_renderer_manifest.cpp"), source);

        Assert.Contains("false", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("HERuntimePostProcessTier::Disabled", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated font scaling loops are normalized from managed dictionary accessors onto native std::pair field access.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFontAssetUsesManagedPairAccessors_RewritesToStdPairFields() {
        const string source = """
for (const auto& entry : *Characters)
{
::FontChar glyph = entry.get_Value();
glyph.SourceX = glyph.SourceX * widthScale;
(*scaledCharacters)[entry.get_Key()] = glyph;
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FontAsset.cpp", source);

        Assert.Contains("::FontChar glyph = entry.second;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("(*scaledCharacters)[entry.first] = glyph;", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.get_Value()", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("entry.get_Key()", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated FPS component headers declare the private overlay helper referenced by the translated PS2 source.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFpsComponentHeaderMissesOverlayHelperDeclaration_InsertsDeclaration() {
        const string source = """
private:
    std::string FormatFpsValue(double fps);

    std::string FormatRenderFpsText(double renderFps, double drawMilliseconds);

    void RefreshOverlayActivation();
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FPSComponent.hpp", source);

        Assert.Contains("std::string FormatOverlaySecondaryLine(std::string baseRenderText);", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated FPS component sources call the overlay helper through the component instance so the PS2 native compiler can resolve the private member.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenFpsComponentSourceCallsOverlayHelperUnqualified_RewritesToThisCall() {
        const string source = """
if (this->RenderTextComponent != nullptr)
{
this->RenderTextComponent->set_Text(FormatOverlaySecondaryLine(this->RenderFpsText));
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("FPSComponent.cpp", source);

        Assert.Contains("this->RenderTextComponent->set_Text(this->FormatOverlaySecondaryLine(this->RenderFpsText));", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("set_Text(FormatOverlaySecondaryLine(", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated light-component includes are normalized when the current engine rewrite emits the invalid `LightType::hpp` form.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenLightComponentIncludesUseScopedHeaderToken_RewritesToPlainHeaderName() {
        const string source = """
#include "AmbientLightComponent.hpp"
#include "LightType::hpp"
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("AmbientLightComponent.cpp", source);

        Assert.Contains("#include \"LightType.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LightType::hpp", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that generated PS2 direct-disc file streams become readable owned memory streams without injecting constructor fields that the generated header does not declare.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenPs2FileStreamUsesDirectDiscBuffer_MarksMemoryBufferReadableOwnedAndReadOnlyWithoutInjectingUndefinedFields() {
        const string source = """
FileStream::FileStream(const uint8_t* data, size_t dataLength)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), writable(false) {
}

FileStream::FileStream(const char* path, FileMode mode)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true) {
#if HE_CPP_PLATFORM_PS2
    std::string resolvedPs2ReadPath = FileStreamSupportResolvePs2DiscReadPath(path);
    bool usesPs2DirectRead = FileStreamSupportStartsWithPs2CdromPrefix(resolvedPs2ReadPath) || resolvedPs2ReadPath.find(';') != std::string::npos;
    if (usesPs2DirectRead) {
        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);
        usesMemoryBuffer = true;
        length = memoryBuffer.size();
        return;
    }
#endif
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource(Path.Combine("system", "io", "file-stream.cpp"), source);

        Assert.DoesNotContain("usesMemoryBuffer", normalizedSource, StringComparison.Ordinal);
        Assert.Contains(": file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), writable(false)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains(": file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("ownsMemoryBuffer = true;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("writable = false;", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Invokes the private generated-core normalization entry point so tests can assert the PS2 build-specific source rewrite contract.
    /// </summary>
    /// <param name="fileName">Generated source file name passed to the normalization routine.</param>
    /// <param name="contents">Generated source contents passed to the normalization routine.</param>
    /// <returns>Normalized generated source contents.</returns>
    static string InvokeNormalizeGeneratedCoreSource(string fileName, string contents) {
        MethodInfo normalizeMethod = typeof(Ps2NativeBuildExecutor).GetMethod(
            "NormalizeGeneratedCoreSource",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeGeneratedCoreSource reflection lookup failed.");

        return (string)(normalizeMethod.Invoke(null, [fileName, contents])
            ?? throw new InvalidOperationException("NormalizeGeneratedCoreSource returned null."));
    }

    /// <summary>
    /// Resolves the repository root path from the current test binary location.
    /// </summary>
    /// <returns>Absolute repository root path.</returns>
    static string ResolveRepositoryRoot() {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    /// <summary>
    /// Extracts a contiguous source section bounded by exact start and end markers so tests can reason about a single branch without matching unrelated helper code elsewhere in the file.
    /// </summary>
    /// <param name="source">Full source text.</param>
    /// <param name="startMarker">Exact text where the desired section begins.</param>
    /// <param name="endMarker">Exact text that terminates the desired section.</param>
    /// <returns>The substring between the provided markers, including the start marker and excluding the end marker.</returns>
    static string ExtractSourceRange(string source, string startMarker, string endMarker) {
        int startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0) {
            throw new InvalidOperationException($"Start marker '{startMarker}' was not found.");
        }

        int endIndex = source.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        if (endIndex < 0) {
            throw new InvalidOperationException($"End marker '{endMarker}' was not found.");
        }

        return source[startIndex..endIndex];
    }
}
