# Generated Core Capability Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the PS2 generated-core rewrite layer with a cross-platform runtime-generation contract owned by `helengine`, while keeping PS2 export/build behavior working throughout the migration.

**Architecture:** The migration removes obsolete PS2 rewrites first, then introduces a small cross-platform runtime-generation contract in `helengine`, and finally moves the remaining live compatibility branches behind typed generator inputs. `helengine-ps2` becomes a consumer of that contract instead of a mutator of generated C++.

**Tech Stack:** C#, `helengine.core`, `helengine.editor`, `helengine.editor.tests`, `helengine-ps2` builder tests, generated C++ source assertions, RTK `dotnet test`, RTK `dotnet build`

---

## File Map

- `C:\dev\helworks\helengine\engine\helengine.core\content\RuntimeContentManagerConfiguration.cs`
  - owns shared runtime content-processor registration and material processor branch selection
- `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeSceneAssetReferenceResolver.cs`
  - owns runtime packaged-asset resolution, material/model/texture loading behavior, and packaged path policy
- `C:\dev\helworks\helengine\engine\helengine.core\managers\rendering\RenderManager3D.cs`
  - owns the runtime material creation surface, including `BuildMaterialFromCooked`
- `C:\dev\helworks\helengine\engine\helengine.core\managers\rendering\RenderManager2D.cs`
  - owns the texture-release flush runtime surface
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
  - owns generated-core source emission and must consume the shared runtime-generation contract
- `C:\dev\helworks\helengine\engine\helengine.editor.tests\RuntimeContentManagerConfigurationSourceTests.cs`
  - locks generated material processor source shape
- `C:\dev\helworks\helengine\engine\helengine.editor.tests`
  - will need new tests for runtime-generation contract-driven source emission
- `C:\dev\helworks\helengine-ps2\builder\Ps2PlatformDefinitionFactory.cs`
  - will declare the PS2 runtime-generation contract values
- `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
  - currently owns generated-core normalization and will shrink toward zero
- `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
  - currently locks rewrite behavior and will be rewritten to lock fail-fast transitional behavior or deleted
- `C:\dev\helworks\helengine-ps2\builder.tests\Ps2PlatformAssetBuilderTests.cs`
  - should verify PS2 builder metadata passes the right runtime-generation contract

### Task 1: Freeze The Rewrite Inventory

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`

- [ ] **Step 1: Write the failing tests that separate obsolete rewrites from transitional rewrites**

```csharp
[Fact]
public void NormalizeGeneratedCoreSource_WhenMaterialRegistrationAlreadyUsesCookedPs2Contract_LeavesSourceUnchanged() {
    const string source = """
#include "MaterialAsset.hpp"
#include "Ps2MaterialAsset.hpp"
RegisterProcessorIfMissing<Ps2MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::Ps2MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
""";

    string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

    Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource.Replace("\r\n", "\n", StringComparison.Ordinal));
}

[Fact]
public void NormalizeGeneratedCoreSource_WhenResolverAlreadyUsesBuildMaterialFromCooked_LeavesSourceUnchanged() {
    const string source = """
::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
return Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);
}
""";

    string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

    Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource.Replace("\r\n", "\n", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the narrow rewrite tests to confirm the current test suite still assumes the old rewrite ownership**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2NativeBuildExecutorTests`
Expected: at least one failure or outdated assertion showing the tests still lock material-related rewrite behavior

- [ ] **Step 3: Update `Ps2NativeBuildExecutorTests.cs` so it documents transitional ownership instead of preserving obsolete rewrites**

```csharp
/// <summary>
/// Verifies that PS2 normalization no longer mutates generated sources that already express the cooked-material contract directly.
/// </summary>
[Fact]
public void NormalizeGeneratedCoreSource_WhenGeneratedSourceAlreadyOwnsCookedMaterialContract_DoesNotRewrite() {
    const string source = """
::RuntimeMaterial* RuntimeSceneAssetReferenceResolver::ResolveMaterial(::SceneAssetReference* reference)
{
const std::string fullPath = this->ResolveFileBackedAssetPath(reference);
::Ps2MaterialAsset *materialAsset = this->AssetContentManager->Load<Ps2MaterialAsset*>(fullPath, RuntimeContentProcessorIds::MaterialAsset);
return Core::get_Instance()->get_RenderManager3D()->BuildMaterialFromCooked(materialAsset);
}
""";

    string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeSceneAssetReferenceResolver.cpp", source);

    Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource.Replace("\r\n", "\n", StringComparison.Ordinal));
}
```

- [ ] **Step 4: Add strict transitional assertion helpers in `Ps2NativeBuildExecutor.cs` for any rewrite that survives this task**

```csharp
static string ReplaceExactlyOnce(string contents, string oldValue, string newValue, string description) {
    if (string.IsNullOrEmpty(contents)) {
        throw new InvalidOperationException($"Expected generated source contents for {description}.");
    }

    int matchIndex = contents.IndexOf(oldValue, StringComparison.Ordinal);
    if (matchIndex < 0) {
        throw new InvalidOperationException($"Generated source drifted before applying normalization: {description}");
    }

    if (contents.IndexOf(oldValue, matchIndex + oldValue.Length, StringComparison.Ordinal) >= 0) {
        throw new InvalidOperationException($"Generated source matched more than once during normalization: {description}");
    }

    return contents.Replace(oldValue, newValue, StringComparison.Ordinal);
}
```

- [ ] **Step 5: Run the rewrite test suite again**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2NativeBuildExecutorTests`
Expected: PASS, with material-related tests rewritten around “do not rewrite” or strict transitional assertions

- [ ] **Step 6: Commit**

```bash
git add builder.tests/Ps2NativeBuildExecutorTests.cs builder/Ps2NativeBuildExecutor.cs
git commit -m "test: classify PS2 generated-core rewrites"
```

### Task 2: Introduce The Cross-Platform Runtime Generation Contract

**Files:**
- Create: `C:\dev\helworks\helengine\engine\helengine.core\platform\RuntimeMaterialResolutionMode.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.core\platform\PackagedPathPolicy.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.core\platform\RuntimeGenerationContract.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests`

- [ ] **Step 1: Write failing tests for the new contract types and generator entrypoint plumbing**

```csharp
[Fact]
public void RuntimeGenerationContract_DefaultDesktopContract_UsesRawShaderBackedMaterials() {
    RuntimeGenerationContract contract = RuntimeGenerationContract.CreateDesktopDefault();

    Assert.Equal(RuntimeMaterialResolutionMode.RawShaderBacked, contract.MaterialResolutionMode);
    Assert.False(contract.AllowsRootedPackagedAssetPaths);
}

[Fact]
public void GeneratedCoreRegeneration_WhenPs2ContractIsSupplied_EmitsCookedMaterialBranch() {
    string source = GenerateRuntimeContentManagerConfigurationSource(
        new RuntimeGenerationContract(
            RuntimeMaterialResolutionMode.CookedPlatformOwned,
            supportsRenderManager2DTextureReleaseFlush: false,
            PackagedPathPolicy.RootedOrContentRelative));

    Assert.Contains("new AssetContentProcessor<Ps2MaterialAsset>()", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the new test targets to verify the contract does not exist yet**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter RuntimeGenerationContract`
Expected: FAIL with missing type or missing generator overload errors

- [ ] **Step 3: Add the new cross-platform contract types in `helengine.core`**

```csharp
namespace helengine {
    /// <summary>
    /// Describes how runtime materials are resolved in generated player code.
    /// </summary>
    public enum RuntimeMaterialResolutionMode {
        RawShaderBacked = 0,
        CookedPlatformOwned = 1
    }
}
```

```csharp
namespace helengine {
    /// <summary>
    /// Describes how packaged asset paths may appear inside generated player code.
    /// </summary>
    public enum PackagedPathPolicy {
        ContentRelativeOnly = 0,
        RootedOrContentRelative = 1
    }
}
```

```csharp
namespace helengine {
    /// <summary>
    /// Captures the runtime behaviors that generated player code depends on across platforms.
    /// </summary>
    public sealed class RuntimeGenerationContract {
        /// <summary>
        /// Initializes a new runtime-generation contract.
        /// </summary>
        public RuntimeGenerationContract(
            RuntimeMaterialResolutionMode materialResolutionMode,
            bool supportsRenderManager2DTextureReleaseFlush,
            PackagedPathPolicy packagedPathPolicy) {
            MaterialResolutionMode = materialResolutionMode;
            SupportsRenderManager2DTextureReleaseFlush = supportsRenderManager2DTextureReleaseFlush;
            PackagedPathPolicy = packagedPathPolicy;
        }

        /// <summary>
        /// Gets how generated runtime code should resolve material assets.
        /// </summary>
        public RuntimeMaterialResolutionMode MaterialResolutionMode { get; }

        /// <summary>
        /// Gets whether generated scene-management code may call RenderManager2D texture-release flushing.
        /// </summary>
        public bool SupportsRenderManager2DTextureReleaseFlush { get; }

        /// <summary>
        /// Gets how generated runtime code may resolve packaged file-backed asset paths.
        /// </summary>
        public PackagedPathPolicy PackagedPathPolicy { get; }
    }
}
```

- [ ] **Step 4: Thread the new contract through the generated-core regeneration entrypoints**

```csharp
public string GenerateRuntimeContentManagerConfigurationSource(RuntimeGenerationContract runtimeGenerationContract) {
    if (runtimeGenerationContract == null) {
        throw new ArgumentNullException(nameof(runtimeGenerationContract));
    }

    return runtimeGenerationContract.MaterialResolutionMode == RuntimeMaterialResolutionMode.CookedPlatformOwned
        ? BuildCookedPlatformOwnedRuntimeContentManagerConfigurationSource()
        : BuildRawShaderBackedRuntimeContentManagerConfigurationSource();
}
```

- [ ] **Step 5: Run the targeted editor tests**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeGenerationContract|RuntimeContentManagerConfigurationSourceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.core/platform engine/helengine.editor/managers/project/EditorGeneratedCoreRegenerationService.cs engine/helengine.editor.tests
git -C C:\dev\helworks\helengine commit -m "feat: add cross-platform runtime generation contract"
```

### Task 3: Move Material Resolution And Packaged Path Semantics Into The Generator

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\content\RuntimeContentManagerConfiguration.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeSceneAssetReferenceResolver.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\RuntimeContentManagerConfigurationSourceTests.cs`
- Create or Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\scene\runtime\RuntimeSceneAssetReferenceResolverSourceTests.cs`

- [ ] **Step 1: Write failing source-generation tests for material branch selection and rooted path policy**

```csharp
[Fact]
public void GenerateRuntimeSceneAssetReferenceResolver_WhenContractUsesCookedPlatformOwnedMaterials_EmitsBuildMaterialFromCooked() {
    string source = GenerateRuntimeSceneAssetReferenceResolverSource(
        new RuntimeGenerationContract(
            RuntimeMaterialResolutionMode.CookedPlatformOwned,
            supportsRenderManager2DTextureReleaseFlush: false,
            PackagedPathPolicy.RootedOrContentRelative));

    Assert.Contains("BuildMaterialFromCooked(materialAsset)", source, StringComparison.Ordinal);
    Assert.DoesNotContain("BuildMaterialFromRaw(materialAsset, shaderAsset)", source, StringComparison.Ordinal);
}

[Fact]
public void GenerateRuntimeSceneAssetReferenceResolver_WhenContractAllowsRootedPaths_EmitsRootedPathBranch() {
    string source = GenerateRuntimeSceneAssetReferenceResolverSource(
        new RuntimeGenerationContract(
            RuntimeMaterialResolutionMode.CookedPlatformOwned,
            supportsRenderManager2DTextureReleaseFlush: false,
            PackagedPathPolicy.RootedOrContentRelative));

    Assert.Contains("if (Path::IsPathRooted(reference->get_RelativePath()))", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused generator tests to confirm the current emission path is not yet contract-driven**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeSceneAssetReferenceResolverSourceTests|RuntimeContentManagerConfigurationSourceTests"`
Expected: FAIL because generated source still relies on compile symbols or hard-coded branches instead of the shared contract

- [ ] **Step 3: Refactor the generator to branch on `RuntimeGenerationContract` instead of platform-specific compile symbols**

```csharp
if (runtimeGenerationContract.MaterialResolutionMode == RuntimeMaterialResolutionMode.CookedPlatformOwned) {
    builder.AppendLine("            Ps2MaterialAsset materialAsset = AssetContentManager.Load<Ps2MaterialAsset>(fullPath, RuntimeContentProcessorIds.MaterialAsset);");
    builder.AppendLine("            return Core.Instance.RenderManager3D.BuildMaterialFromCooked(materialAsset);");
} else {
    builder.AppendLine("            MaterialAsset materialAsset = AssetContentManager.Load<MaterialAsset>(fullPath, RuntimeContentProcessorIds.MaterialAsset);");
    builder.AppendLine("            ShaderAsset shaderAsset = AssetContentManager.Load<ShaderAsset>(");
    builder.AppendLine("                ResolveShaderPackagePath(materialAsset.ShaderAssetId),");
    builder.AppendLine("                RuntimeContentProcessorIds.ShaderAsset);");
    builder.AppendLine("            RuntimeMaterial runtimeMaterial = Core.Instance.RenderManager3D.BuildMaterialFromRaw(materialAsset, shaderAsset);");
    builder.AppendLine("            TrackOwnedMaterial(runtimeMaterial);");
    builder.AppendLine("            ApplyMaterialDiffuseTexture(runtimeMaterial, materialAsset, fullPath);");
    builder.AppendLine("            return runtimeMaterial;");
}
```

- [ ] **Step 4: Refactor path resolution to use contract semantics**

```csharp
if (runtimeGenerationContract.PackagedPathPolicy == PackagedPathPolicy.RootedOrContentRelative) {
    builder.AppendLine("            if (Path.IsPathRooted(reference.RelativePath)) {");
    builder.AppendLine("                return Path.GetFullPath(reference.RelativePath);");
    builder.AppendLine("            }");
}
builder.AppendLine("            string fullPath = Path.GetFullPath(Path.Combine(ContentRootPath, reference.RelativePath));");
```

- [ ] **Step 5: Run the focused editor tests again**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeSceneAssetReferenceResolverSourceTests|RuntimeContentManagerConfigurationSourceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.core/content/RuntimeContentManagerConfiguration.cs engine/helengine.core/scene/runtime/RuntimeSceneAssetReferenceResolver.cs engine/helengine.editor/managers/project/EditorGeneratedCoreRegenerationService.cs engine/helengine.editor.tests
git -C C:\dev\helworks\helengine commit -m "feat: generate runtime material and path behavior from shared contract"
```

### Task 4: Move Texture Release Flush And Composition Behavior Behind The Contract

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\SceneManager.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorGeneratedCoreRegenerationService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\SceneManagerTests.cs`
- Create or Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`

- [ ] **Step 1: Write failing tests for generated scene-manager behavior when texture-release flush is unsupported**

```csharp
[Fact]
public void GenerateSceneManagerSource_WhenContractDisablesTextureReleaseFlush_DoesNotEmitFlushReleasedTexturesCall() {
    string source = GenerateSceneManagerSource(
        new RuntimeGenerationContract(
            RuntimeMaterialResolutionMode.CookedPlatformOwned,
            supportsRenderManager2DTextureReleaseFlush: false,
            PackagedPathPolicy.RootedOrContentRelative));

    Assert.DoesNotContain("FlushReleasedTextures();", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the scene-manager generation tests to confirm the behavior still depends on downstream rewriting**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "SceneManagerTests|EditorGeneratedCoreRegenerationServiceTests"`
Expected: FAIL because the generated source still emits the unsupported call or naming pattern

- [ ] **Step 3: Move the scene-manager generation branch into `EditorGeneratedCoreRegenerationService.cs`**

```csharp
if (runtimeGenerationContract.SupportsRenderManager2DTextureReleaseFlush) {
    builder.AppendLine("            Core.Instance.RenderManager2D.FlushReleasedTextures();");
}
```

- [ ] **Step 4: If amalgamation helper collisions still exist, move them into explicit generator shaping instead of post-build renaming**

```csharp
builder.AppendLine("    template <typename T>");
builder.AppendLine("    void DeleteGeneratedArray_SceneManager(Array<T>* values) {");
builder.AppendLine("        if (values == nullptr || values == Array<T>::Empty()) {");
builder.AppendLine("            return;");
builder.AppendLine("        }");
builder.AppendLine("        delete values;");
builder.AppendLine("    }");
```

- [ ] **Step 5: Run the focused tests**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "SceneManagerTests|EditorGeneratedCoreRegenerationServiceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.core/scene/runtime/SceneManager.cs engine/helengine.editor/managers/project/EditorGeneratedCoreRegenerationService.cs engine/helengine.editor.tests
git -C C:\dev\helworks\helengine commit -m "feat: generate scene manager behavior from runtime contract"
```

### Task 5: Teach The PS2 Builder To Declare The Shared Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2PlatformDefinitionFactory.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2PlatformAssetBuilder.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write failing PS2 builder tests for contract declaration**

```csharp
[Fact]
public void Definition_WhenCreated_DeclaresCookedPlatformOwnedRuntimeMaterialResolution() {
    PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();

    Assert.Equal("CookedPlatformOwned", definition.RuntimeGenerationContract.MaterialResolutionMode.ToString());
    Assert.False(definition.RuntimeGenerationContract.SupportsRenderManager2DTextureReleaseFlush);
}
```

- [ ] **Step 2: Run the PS2 builder tests to verify the builder has no shared contract surface yet**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2PlatformAssetBuilderTests`
Expected: FAIL with missing metadata or missing contract assertions

- [ ] **Step 3: Add the runtime-generation contract to the PS2 platform definition**

```csharp
return new PlatformDefinition(
    platformId: "ps2",
    displayName: "PlayStation 2",
    runtimeGenerationContract: new RuntimeGenerationContract(
        RuntimeMaterialResolutionMode.CookedPlatformOwned,
        supportsRenderManager2DTextureReleaseFlush: false,
        PackagedPathPolicy.RootedOrContentRelative));
```

- [ ] **Step 4: Thread the platform contract into the generated-core build request path**

```csharp
EditorGeneratedCoreBuildRequest request = new EditorGeneratedCoreBuildRequest(
    platformDefinition.RuntimeGenerationContract,
    otherExistingArguments);
```

- [ ] **Step 5: Run the PS2 builder tests again**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2PlatformAssetBuilderTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add builder/Ps2PlatformDefinitionFactory.cs builder/Ps2PlatformAssetBuilder.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
git commit -m "feat: declare PS2 runtime generation contract"
```

### Task 6: Delete Obsolete Rewrites And Shrink The Normalization Layer

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs`
- Modify: `C:\dev\helworks\helengine-ps2\builder.tests\Ps2NativeBuildExecutorTests.cs`

- [ ] **Step 1: Write failing tests that prove obsolete material-related rewrites are gone**

```csharp
[Fact]
public void NormalizeGeneratedCoreSource_WhenRuntimeContentManagerConfigurationAlreadyUsesContractOwnedMaterialProcessor_DoesNotInjectPs2MaterialRewrite() {
    const string source = """
#include "MaterialAsset.hpp"
#include "Ps2MaterialAsset.hpp"
RegisterProcessorIfMissing<Ps2MaterialAsset*>(contentManager, RuntimeContentProcessorIds::MaterialAsset, new ::AssetContentProcessor_1<::Ps2MaterialAsset*>(), new Array<std::string>({ MaterialAssetExtension }));
""";

    string normalizedSource = InvokeNormalizeGeneratedCoreSource("RuntimeContentManagerConfiguration.cpp", source);

    Assert.Equal(source.Replace("\r\n", "\n", StringComparison.Ordinal), normalizedSource.Replace("\r\n", "\n", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the PS2 rewrite tests to confirm they still lock obsolete behavior**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2NativeBuildExecutorTests`
Expected: FAIL until the obsolete assertions and rewrite branches are removed

- [ ] **Step 3: Delete the obsolete material, resolver, render-manager, and diagnostics rewrites from `Ps2NativeBuildExecutor.cs`**

```csharp
static void NormalizeGeneratedCoreSources(string generatedCoreRootPath) {
    if (string.IsNullOrWhiteSpace(generatedCoreRootPath)) {
        throw new ArgumentException("Generated core root path must be provided.", nameof(generatedCoreRootPath));
    }

    if (!Directory.Exists(generatedCoreRootPath)) {
        return;
    }

    NormalizeScrollComponentIfNeeded(generatedCoreRootPath);
    NormalizeSceneManagerIfStrictlyRequired(generatedCoreRootPath);
}
```

- [ ] **Step 4: Rewrite or delete tests that only existed to preserve removed rewrites**

```csharp
[Fact]
public void NormalizeGeneratedCoreSource_WhenSceneManagerCompatibilityRewriteNoLongerMatches_Throws() {
    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
        InvokeNormalizeGeneratedCoreSource("SceneManager.cpp", "unexpected source shape"));

    Assert.Contains("Generated source drifted", exception.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 5: Run the PS2 rewrite tests again**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj --filter Ps2NativeBuildExecutorTests`
Expected: PASS, with only transitional strict-assert behavior left

- [ ] **Step 6: Commit**

```bash
git add builder/Ps2NativeBuildExecutor.cs builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "refactor: remove obsolete PS2 generated-core rewrites"
```

### Task 7: Verify End-To-End Export From The New Contract

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\builder\Ps2NativeBuildExecutor.cs` only if any final transitional assertion text or comments need cleanup
- Verify: `C:\dev\helworks\helengine`
- Verify: `C:\dev\helworks\helengine-ps2`

- [ ] **Step 1: Run the `helengine` editor test slices that cover the new contract**

Run: `rtk dotnet test C:\dev\helworks\helengine\engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeContentManagerConfigurationSourceTests|RuntimeSceneAssetReferenceResolverSourceTests|SceneManagerTests|EditorGeneratedCoreRegenerationServiceTests"`
Expected: PASS

- [ ] **Step 2: Run the PS2 builder tests**

Run: `rtk dotnet test builder.tests\\helengine.ps2.builder.tests.csproj`
Expected: PASS

- [ ] **Step 3: Run the PS2 builder project build**

Run: `rtk dotnet build builder\\helengine.ps2.builder.csproj`
Expected: PASS with zero errors

- [ ] **Step 4: Run one real PS2 export smoke test**

Run: `rtk dotnet run --project builder\\helengine.ps2.builder.csproj -- --help`
Expected: PASS for the builder entrypoint, then use the existing editor-driven PS2 export path to produce a fresh ISO without generated-core text rewriting for materials

- [ ] **Step 5: Record the remaining normalization surface and decide whether it is still transitional or deletable**

```text
- ScrollComponent value-type compatibility
- SceneManager generation compatibility, if still required
- no material registration rewrite
- no resolver material rewrite
- no RenderManager3D cooked-material rewrite
- no diagnostics injection
```

- [ ] **Step 6: Commit**

```bash
git add builder/Ps2NativeBuildExecutor.cs
git commit -m "test: verify generated-core capability migration"
```

## Self-Review

- Spec coverage:
  - rewrite inventory classification: covered by Tasks 1 and 6
  - shared cross-platform runtime-generation contract: covered by Task 2
  - contract-driven material/path behavior: covered by Task 3
  - contract-driven scene-manager behavior: covered by Task 4
  - PS2 platform declaration of contract: covered by Task 5
  - verification and rollout discipline: covered by Task 7
- Placeholder scan:
  - removed `TBD`/`TODO` style placeholders
  - each task contains concrete files, commands, and code shapes
- Type consistency:
  - `RuntimeGenerationContract`, `RuntimeMaterialResolutionMode`, and `PackagedPathPolicy` are used consistently across tasks
