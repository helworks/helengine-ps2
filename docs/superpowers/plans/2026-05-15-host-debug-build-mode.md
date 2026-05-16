# Host Debug Build Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a first-class cross-platform `host-debug` build mode to the editor/build graph, with PS2 as the first platform adapter and a working load-only startup-scene runner as the first milestone.

**Architecture:** The work starts in `helengine` by introducing a generic host-debug build mode, capability metadata, and shared runner contracts in the editor/build graph. After the shared mode exists, `helengine-ps2` implements the first host adapter by reusing packaged PS2 outputs, mapping `cdrom0:` to host files, and running the real generated-core plus PS2 runtime contracts through a host bootstrap that stops after startup scene and cooked asset resolution.

**Tech Stack:** C#, `helengine.editor`, `helengine.baseplatform`, `helengine.editor.tests`, C++ host runner code in `helengine-ps2`, packaged PS2 export artifacts, RTK `dotnet test`, RTK `dotnet build`

---

## File Map

- `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\PlatformDefinition.cs`
  - owns shared platform metadata and will expose host-debug capability data
- `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions`
  - will gain host-debug enums/contracts used by multiple native platforms
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphRunner.cs`
  - owns build-mode execution and will add `host-debug` orchestration
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorBuildQueueItemFactory.cs`
  - will expose host-debug as a selectable build/run mode
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildSelectionModel.cs`
  - will surface per-platform host-debug capability metadata to the UI and build graph
- `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project`
  - will gain tests for host-debug mode selection and build-graph execution
- `C:\dev\helworks\helengine-ps2\builder\Ps2PlatformDefinitionFactory.cs`
  - will declare PS2 host-debug support and runner metadata
- `C:\dev\helworks\helengine-ps2\builder\Ps2PlatformAssetBuilder.cs`
  - will publish any host-debug artifact metadata required by the runner
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\`
  - new PS2 host-debug runner target
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\main.cpp`
  - host-debug runner entrypoint
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostDebugSession.hpp`
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostDebugSession.cpp`
  - own one-shot PS2 startup-scene host execution
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostFileSystem.hpp`
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostFileSystem.cpp`
  - own `cdrom0:` to packaged host-path mapping
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostRenderManager3D.hpp`
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostRenderManager3D.cpp`
  - own the host hardware-boundary adapter around the PS2 render-manager runtime contract
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostRenderManager2D.hpp`
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostRenderManager2D.cpp`
  - own a minimal 2D host stub
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostInputBackend.hpp`
- `C:\dev\helworks\helengine-ps2\tools\ps2-host-debugger\Ps2HostInputBackend.cpp`
  - own a minimal input stub
- `C:\dev\helworks\helengine-ps2\Makefile`
  - will gain a host-debug target for the new runner

### Task 1: Add Shared Host-Debug Build Mode Metadata In helengine

**Files:**
- Create: `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\PlatformHostDebugRunnerKind.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\PlatformHostDebugCapability.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.baseplatform\Definitions\PlatformDefinition.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformBuildSelectionModelTests.cs`

- [ ] **Step 1: Write the failing platform-capability tests**

```csharp
[Fact]
public void PlatformDefinition_WhenHostDebugCapabilityIsProvided_ExposesItToSelectionModels() {
    PlatformDefinition definition = new(
        "ps2",
        "PlayStation 2",
        [],
        [],
        [],
        [],
        [],
        new PlatformHostDebugCapability(
            true,
            PlatformHostDebugRunnerKind.NativeExecutable,
            true,
            true,
            false,
            "ps2-host-debugger"));

    Assert.True(definition.HostDebugCapability.SupportsHostDebug);
    Assert.Equal("ps2-host-debugger", definition.HostDebugCapability.RunnerId);
}
```

- [ ] **Step 2: Run the test to verify the host-debug contract does not exist yet**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorPlatformBuildSelectionModelTests`
Expected: FAIL with missing `PlatformHostDebugCapability`, missing `PlatformHostDebugRunnerKind`, or missing `HostDebugCapability` members on `PlatformDefinition`

- [ ] **Step 3: Add the shared host-debug metadata types**

```csharp
namespace helengine.baseplatform.Definitions;

/// <summary>
/// Describes how one platform's host-debug runner is launched from the editor build graph.
/// </summary>
public enum PlatformHostDebugRunnerKind {
    None = 0,
    NativeExecutable = 1
}
```

```csharp
namespace helengine.baseplatform.Definitions;

/// <summary>
/// Describes whether one platform supports the editor host-debug build mode and how the runner behaves.
/// </summary>
public sealed class PlatformHostDebugCapability {
    /// <summary>
    /// Initializes one host-debug capability description.
    /// </summary>
    public PlatformHostDebugCapability(
        bool supportsHostDebug,
        PlatformHostDebugRunnerKind runnerKind,
        bool requiresPackagedExportArtifacts,
        bool supportsSingleStepSceneLoad,
        bool supportsSingleStepDraw,
        string runnerId) {
        SupportsHostDebug = supportsHostDebug;
        RunnerKind = runnerKind;
        RequiresPackagedExportArtifacts = requiresPackagedExportArtifacts;
        SupportsSingleStepSceneLoad = supportsSingleStepSceneLoad;
        SupportsSingleStepDraw = supportsSingleStepDraw;
        RunnerId = runnerId ?? throw new ArgumentNullException(nameof(runnerId));
    }

    /// <summary>
    /// Gets whether the platform supports the host-debug build mode.
    /// </summary>
    public bool SupportsHostDebug { get; }

    /// <summary>
    /// Gets the launch strategy used by the host-debug runner.
    /// </summary>
    public PlatformHostDebugRunnerKind RunnerKind { get; }

    /// <summary>
    /// Gets whether the runner requires the normal packaged export artifacts.
    /// </summary>
    public bool RequiresPackagedExportArtifacts { get; }

    /// <summary>
    /// Gets whether the runner supports a load-only startup-scene mode.
    /// </summary>
    public bool SupportsSingleStepSceneLoad { get; }

    /// <summary>
    /// Gets whether the runner supports a one-shot draw mode.
    /// </summary>
    public bool SupportsSingleStepDraw { get; }

    /// <summary>
    /// Gets the stable runner identifier published by the platform.
    /// </summary>
    public string RunnerId { get; }
}
```

- [ ] **Step 4: Extend `PlatformDefinition` with host-debug capability metadata**

```csharp
public PlatformDefinition(
    string id,
    string displayName,
    PlatformBuildProfileDefinition[] buildProfiles,
    PlatformGraphicsProfileDefinition[] graphicsProfiles,
    PlatformAssetRequirementDefinition[] assetRequirements,
    PlatformMaterialSchemaDefinition[] materialSchemas,
    PlatformMediaProfileDefinition[] mediaProfiles,
    PlatformHostDebugCapability hostDebugCapability) {
    Id = id;
    DisplayName = displayName;
    BuildProfiles = buildProfiles;
    GraphicsProfiles = graphicsProfiles;
    AssetRequirements = assetRequirements;
    MaterialSchemas = materialSchemas;
    MediaProfiles = mediaProfiles;
    HostDebugCapability = hostDebugCapability ?? throw new ArgumentNullException(nameof(hostDebugCapability));
}

/// <summary>
/// Gets the host-debug capability metadata published by the platform builder.
/// </summary>
public PlatformHostDebugCapability HostDebugCapability { get; }
```

- [ ] **Step 5: Run the narrow tests again**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorPlatformBuildSelectionModelTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.baseplatform/Definitions engine/helengine.editor.tests/managers/project
git -C C:\dev\helworks\helengine commit -m "feat: add platform host-debug capability metadata"
```

### Task 2: Add Host-Debug Mode To The Editor Build Graph

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphRunner.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorBuildQueueItemFactory.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorBuildQueueItemDocument.cs`
- Test: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformBuildGraphRunnerTests.cs`

- [ ] **Step 1: Write the failing build-graph tests for host-debug mode**

```csharp
[Fact]
public void RunBuild_WhenHostDebugModeIsSelected_StillWritesRuntimeArtifactsBeforeRunnerLaunch() {
    EditorPlatformBuildSelectionModel selectionModel = BuildSelectionModelWithHostDebugSupport();

    RunHostDebugBuild(selectionModel);

    Assert.Contains("WriteRuntimeGraphicsRendererManifestSource", executedPhases);
    Assert.Contains("PackageContainerArtifacts", executedPhases);
    Assert.Contains("LaunchHostDebugRunner", executedPhases);
}
```

- [ ] **Step 2: Run the tests to verify host-debug mode is not wired yet**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorPlatformBuildGraphRunnerTests`
Expected: FAIL with missing host-debug build mode, missing runner phase, or missing queue-item state

- [ ] **Step 3: Add a host-debug mode to the build queue and build-graph execution**

```csharp
public enum EditorBuildExecutionMode {
    Runtime = 0,
    HostDebug = 1
}
```

```csharp
if (queueItem.ExecutionMode == EditorBuildExecutionMode.HostDebug) {
    WriteRuntimeGraphicsRendererManifestSource(workspace.GeneratedCoreRootPath, selectionModel);
    RunPlatformBuild(selectionModel, workspace);
    LaunchHostDebugRunner(selectionModel, workspace);
    return;
}
```

- [ ] **Step 4: Add a dedicated runner-launch phase that validates platform capability**

```csharp
void LaunchHostDebugRunner(EditorPlatformBuildSelectionModel selectionModel, EditorPlatformBuildGraphWorkspace workspace) {
    if (selectionModel == null) {
        throw new ArgumentNullException(nameof(selectionModel));
    }
    if (workspace == null) {
        throw new ArgumentNullException(nameof(workspace));
    }

    PlatformHostDebugCapability capability = selectionModel.PlatformDefinition.HostDebugCapability;
    if (capability == null || !capability.SupportsHostDebug) {
        throw new InvalidOperationException($"Platform '{selectionModel.PlatformDefinition.Id}' does not support host-debug.");
    }

    if (!capability.RequiresPackagedExportArtifacts) {
        throw new InvalidOperationException("Host-debug currently requires packaged export artifacts.");
    }

    RunHostDebugRunner(selectionModel, workspace, capability);
}
```

- [ ] **Step 5: Run the targeted build-graph tests again**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorPlatformBuildGraphRunnerTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.editor/managers/project engine/helengine.editor.tests/managers/project
git -C C:\dev\helworks\helengine commit -m "feat: add host-debug build graph mode"
```

### Task 3: Publish PS2 Host-Debug Metadata

**Files:**
- Modify: `builder\Ps2PlatformDefinitionFactory.cs`
- Modify: `builder\Ps2PlatformAssetBuilder.cs`
- Test: `builder.tests\Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing PS2 metadata tests**

```csharp
[Fact]
public void CreateDefinition_WhenPs2PlatformIsDescribed_DeclaresHostDebugSupport() {
    PlatformDefinition definition = Ps2PlatformDefinitionFactory.Create();

    Assert.True(definition.HostDebugCapability.SupportsHostDebug);
    Assert.Equal("ps2-host-debugger", definition.HostDebugCapability.RunnerId);
    Assert.True(definition.HostDebugCapability.SupportsSingleStepSceneLoad);
    Assert.False(definition.HostDebugCapability.SupportsSingleStepDraw);
}
```

- [ ] **Step 2: Run the narrow PS2 metadata tests to verify host-debug support is not declared yet**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter CreateDefinition_WhenPs2PlatformIsDescribed_DeclaresHostDebugSupport`
Expected: FAIL with missing `HostDebugCapability` or unmet assertions

- [ ] **Step 3: Add PS2 host-debug capability metadata**

```csharp
new PlatformHostDebugCapability(
    true,
    PlatformHostDebugRunnerKind.NativeExecutable,
    true,
    true,
    false,
    "ps2-host-debugger")
```

- [ ] **Step 4: Publish any host-debug output metadata needed by the editor**

```csharp
public sealed class Ps2HostDebugRunnerMetadata {
    /// <summary>
    /// Gets the relative executable path produced by the PS2 host-debug runner target.
    /// </summary>
    public string ExecutableRelativePath { get; init; } = "tools/ps2-host-debugger/bin/ps2-host-debugger.exe";
}
```

- [ ] **Step 5: Run the targeted PS2 tests again**

Run: `rtk dotnet test builder.tests\helengine.ps2.builder.tests.csproj --filter CreateDefinition_WhenPs2PlatformIsDescribed_DeclaresHostDebugSupport`
Expected: PASS, or PASS after updating any unrelated fixture drift in the touched test file

- [ ] **Step 6: Commit**

```bash
git add builder/Ps2PlatformDefinitionFactory.cs builder/Ps2PlatformAssetBuilder.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
git commit -m "feat: declare PS2 host-debug capability"
```

### Task 4: Build The PS2 Load-Only Host Runner

**Files:**
- Create: `tools\ps2-host-debugger\main.cpp`
- Create: `tools\ps2-host-debugger\Ps2HostDebugSession.hpp`
- Create: `tools\ps2-host-debugger\Ps2HostDebugSession.cpp`
- Create: `tools\ps2-host-debugger\Ps2HostFileSystem.hpp`
- Create: `tools\ps2-host-debugger\Ps2HostFileSystem.cpp`
- Create: `tools\ps2-host-debugger\Ps2HostRenderManager3D.hpp`
- Create: `tools\ps2-host-debugger\Ps2HostRenderManager3D.cpp`
- Create: `tools\ps2-host-debugger\Ps2HostRenderManager2D.hpp`
- Create: `tools\ps2-host-debugger\Ps2HostRenderManager2D.cpp`
- Create: `tools\ps2-host-debugger\Ps2HostInputBackend.hpp`
- Create: `tools\ps2-host-debugger\Ps2HostInputBackend.cpp`
- Modify: `Makefile`

- [ ] **Step 1: Write the failing host-runner smoke contract in a minimal readme-like test note**

```text
Invocation:
ps2-host-debugger --export-root C:\dev\helprojs\output\ps2-vu-colored-baseline --mode load-only

Expected:
- initializes Core
- maps cdrom0:\ paths into export-root\disc
- loads startup scene
- resolves cooked runtime materials and models
- exits 0 on success
```

- [ ] **Step 2: Add the runner entrypoint and argument parsing**

```cpp
int main(int argc, char** argv) {
    helengine::ps2::Ps2HostDebugSession session;
    return session.Run(argc, argv);
}
```

- [ ] **Step 3: Add packaged-path host mapping**

```cpp
std::string Ps2HostFileSystem::ResolveHostPath(const std::string& runtimePath) const {
    if (runtimePath.rfind("cdrom0:\\", 0) != 0) {
        throw std::invalid_argument("PS2 host debug requires cdrom0 rooted runtime paths.");
    }

    std::string relativePath = runtimePath.substr(std::string("cdrom0:\\").length());
    std::replace(relativePath.begin(), relativePath.end(), '\\', std::filesystem::path::preferred_separator);
    return (DiscRootPath / relativePath).string();
}
```

- [ ] **Step 4: Add the minimal load-only session bootstrap**

```cpp
int Ps2HostDebugSession::Run(int argc, char** argv) {
    ParseArguments(argc, argv);
    InitializeCore();
    LoadStartupScene();
    return 0;
}
```

- [ ] **Step 5: Add host stubs for render/input managers that preserve PS2 runtime contracts**

```cpp
::RuntimeMaterial* Ps2HostRenderManager3D::BuildMaterialFromCooked(::Ps2MaterialAsset* materialAsset) {
    if (materialAsset == nullptr) {
        throw std::invalid_argument("PS2 cooked material asset is required.");
    }

    ::helengine::ps2::Ps2RuntimeMaterial* runtimeMaterial = new ::helengine::ps2::Ps2RuntimeMaterial();
    runtimeMaterial->LoadFromCooked(materialAsset);
    return runtimeMaterial;
}
```

- [ ] **Step 6: Add a host build target**

```make
ps2-host-debugger:
	$(CXX) $(CXXFLAGS) \
	tools/ps2-host-debugger/main.cpp \
	tools/ps2-host-debugger/Ps2HostDebugSession.cpp \
	tools/ps2-host-debugger/Ps2HostFileSystem.cpp \
	tools/ps2-host-debugger/Ps2HostRenderManager3D.cpp \
	tools/ps2-host-debugger/Ps2HostRenderManager2D.cpp \
	tools/ps2-host-debugger/Ps2HostInputBackend.cpp \
	-o tools/ps2-host-debugger/bin/ps2-host-debugger.exe
```

- [ ] **Step 7: Build the runner**

Run: `make ps2-host-debugger`
Expected: successful native host-runner build output under `tools/ps2-host-debugger/bin`

- [ ] **Step 8: Commit**

```bash
git add Makefile tools/ps2-host-debugger
git commit -m "feat: add PS2 host-debug load-only runner"
```

### Task 5: Wire Editor Host-Debug Launch To The PS2 Runner

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphRunner.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPlatformBuildGraphRunnerTests.cs`

- [ ] **Step 1: Write the failing launch test**

```csharp
[Fact]
public void RunHostDebugRunner_WhenPs2HostDebugIsSelected_LaunchesThePublishedRunnerAgainstPackagedOutput() {
    EditorPlatformBuildSelectionModel selectionModel = BuildPs2HostDebugSelectionModel();

    LaunchHostDebugRunner(selectionModel, workspace);

    Assert.Equal("ps2-host-debugger", launchedRunnerId);
    Assert.Contains(@"--export-root", launchedArguments);
    Assert.Contains(workspace.OutputRootPath, launchedArguments);
}
```

- [ ] **Step 2: Run the test to verify the editor does not launch a host runner yet**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter RunHostDebugRunner_WhenPs2HostDebugIsSelected_LaunchesThePublishedRunnerAgainstPackagedOutput`
Expected: FAIL

- [ ] **Step 3: Implement the runner launch path**

```csharp
void RunHostDebugRunner(EditorPlatformBuildSelectionModel selectionModel, EditorPlatformBuildGraphWorkspace workspace, PlatformHostDebugCapability capability) {
    string executablePath = ResolveHostDebugRunnerExecutablePath(selectionModel, capability);
    string arguments = $"--export-root \"{workspace.OutputRootPath}\" --mode load-only";
    Process.Start(new ProcessStartInfo(executablePath, arguments) {
        UseShellExecute = false
    });
}
```

- [ ] **Step 4: Run the targeted launch test again**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter RunHostDebugRunner_WhenPs2HostDebugIsSelected_LaunchesThePublishedRunnerAgainstPackagedOutput`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git -C C:\dev\helworks\helengine add engine/helengine.editor/managers/project/EditorPlatformBuildGraphRunner.cs engine/helengine.editor.tests/managers/project/EditorPlatformBuildGraphRunnerTests.cs
git -C C:\dev\helworks\helengine commit -m "feat: launch PS2 host-debug runner from editor"
```

### Task 6: Verify The First PS2 Host-Debug Milestone

**Files:**
- Modify: `tools\ps2-host-debugger\Ps2HostDebugSession.cpp`
- Modify: `tools\ps2-host-debugger\Ps2HostRenderManager3D.cpp`
- Test: manual host-debug smoke verification against `C:\dev\helprojs\output\ps2-vu-colored-baseline`

- [ ] **Step 1: Add success/failure milestone logs**

```cpp
std::cout << "[ps2-host-debug] core initialized" << std::endl;
std::cout << "[ps2-host-debug] startup scene loaded" << std::endl;
std::cout << "[ps2-host-debug] cooked materials resolved" << std::endl;
std::cout << "[ps2-host-debug] cooked models resolved" << std::endl;
```

- [ ] **Step 2: Rebuild the normal PS2 export**

Run: `rtk dotnet run --project helengine.ui\helengine.editor.app\helengine.editor.app.csproj -- --project C:\dev\helprojs\city\project.heproj --build ps2 --output C:\dev\helprojs\output\ps2-vu-colored-baseline`
Expected: successful PS2 export with packaged `disc` tree and generated-core snapshot

- [ ] **Step 3: Build the host runner**

Run: `make ps2-host-debugger`
Expected: PASS

- [ ] **Step 4: Run the host-debug runner against the packaged output**

Run: `tools\ps2-host-debugger\bin\ps2-host-debugger.exe --export-root C:\dev\helprojs\output\ps2-vu-colored-baseline --mode load-only`
Expected: startup scene loads, cooked materials/models resolve, process exits `0`

- [ ] **Step 5: Commit**

```bash
git add tools/ps2-host-debugger
git commit -m "test: verify PS2 host-debug load-only milestone"
```

## Self-Review

- Spec coverage: this plan covers the shared build-mode metadata, editor/build-graph mode, PS2 capability declaration, PS2 load-only host runner, runner launch wiring, and the first verification milestone. The `draw-once` follow-up from the spec is intentionally deferred to a later plan.
- Placeholder scan: no `TODO`, `TBD`, or “similar to previous task” placeholders remain.
- Type consistency: the plan uses a consistent `PlatformHostDebugCapability`, `PlatformHostDebugRunnerKind`, `EditorBuildExecutionMode.HostDebug`, and `ps2-host-debugger` runner id throughout.
