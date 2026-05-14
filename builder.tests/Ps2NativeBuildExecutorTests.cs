using System.Reflection;
using helengine.ps2.builder;
using Xunit;

namespace helengine.ps2.builder.tests;

/// <summary>
/// Verifies PS2 native build normalization behavior for generated-core sources before the Docker toolchain compiles them.
/// </summary>
public sealed class Ps2NativeBuildExecutorTests {
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
    /// Verifies that the PS2 generated-core normalization restores the cooked-material virtual hook on the generated render-manager base header.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerHeaderUsesRawMaterialApi_AddsCookedMaterialVirtualHook() {
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

        Assert.Contains("class Ps2MaterialAsset;", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("virtual ::RuntimeMaterial* BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset);", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 generated-core normalization restores the cooked-material fallback implementation on the generated render-manager base source.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenRenderManagerSourceUsesRawMaterialApi_AddsCookedMaterialFallbackImplementation() {
        const string source = """
#include "RenderManager3D.hpp"
#include "runtime/native_exceptions.hpp"

::RuntimeMaterial* RenderManager3D::BuildMaterialFromRaw(::MaterialAsset* materialAsset, ::ShaderAsset* shaderAsset)
{
throw new NotSupportedException("This renderer does not support material creation.");
}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RenderManager3D.cpp", source);

        Assert.Contains("#include \"Ps2MaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("::RuntimeMaterial* RenderManager3D::BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset)", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("throw new NotSupportedException(\"This renderer does not support cooked material creation.\");", normalizedSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the PS2 generated-core normalization rewrites material resolution to load cooked PS2 material assets instead of raw desktop materials.
    /// </summary>
    [Fact]
    public void NormalizeGeneratedCoreSource_WhenResolverUsesRawMaterialApi_RewritesToCookedMaterialResolution() {
        const string source = """
#include "RuntimeSceneAssetReferenceResolver.hpp"
#include "runtime/native_exceptions.hpp"
#include "runtime/native_string.hpp"
#include "ModelAsset.hpp"
#include "ContentManager.hpp"
#include "RuntimeModel.hpp"
#include "MaterialAsset.hpp"
#include "ShaderAsset.hpp"
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
::MaterialAsset *materialAsset = this->AssetContentManager->Load<MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
::ShaderAsset *shaderAsset = this->AssetContentManager->Load<ShaderAsset*>(this->ResolveShaderPackagePath(materialAsset->ShaderAssetId), RuntimeContentProcessorIds::ShaderAsset);
::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromRaw(materialAsset, shaderAsset);
this->ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);
return runtimeMaterial;}
""";

        string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

        Assert.Contains("#include \"Ps2MaterialAsset.hpp\"", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("::RuntimeMaterial *runtimeMaterial = Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);", normalizedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", normalizedSource, StringComparison.Ordinal);
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
}
