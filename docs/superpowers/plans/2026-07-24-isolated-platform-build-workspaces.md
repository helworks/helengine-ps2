# Isolated Platform Build Workspaces Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every platform build invocation own all writable intermediate paths so concurrent builds never lock or overwrite one another.

**Architecture:** Extend the common editor build-graph workspace with explicit generated-project and platform-artifact roots derived from the invocation identity. Pass those roots through `PlatformBuildRequest`; platform builders derive all staging, native outputs, logs, and package scratch data from the supplied roots. PS2 adopts the contract by building from a prepared native workspace rather than the repository `build` directory.

**Tech Stack:** C#/.NET 9 editor build graph, platform plugin API, PS2 Docker/Make native build, xUnit.

---

## File Structure

- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphWorkspace.cs`: expose distinct generated-project and platform-artifact roots.
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphWorkspaceFactory.cs`: allocate collision-free roots per queue invocation and platform.
- `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphRunner.cs`: create, stage, and clean only the invocation-specific roots; pass them to platform builders.
- `C:\dev\helworks\helengine\engine\helengine.platforms\PlatformBuildRequest.cs`: carry generated-project and platform-artifact roots to every platform plugin.
- `builder\Ps2PlatformAssetBuilder.cs`: create PS2 staging/native paths under `PlatformArtifactRootPath`.
- `builder\Ps2BuildWorkspace.cs`: represent a prepared PS2 native workspace and its private artifact paths.
- `builder\Ps2NativeBuildExecutor.cs`: mount and build the prepared PS2 native workspace, not the repository build directory.
- `builder.tests\Ps2PlatformAssetBuilderTests.cs`, `builder.tests\Ps2NativeBuildExecutorTests.cs`: verify PS2 paths stay inside its artifact root.
- Editor build-graph tests beside the workspace factory/runner: verify unique roots for parallel requests and platforms.

### Task 1: Define the common isolated workspace paths

- [ ] **Step 1: Write failing workspace tests**

Add tests creating two workspaces with the same platform/queue identity and two workspaces for different platforms. Assert `GeneratedProjectRootPath` and `PlatformArtifactRootPath` differ between invocations and are descendants of `ExecutionRootPath`.

- [ ] **Step 2: Run tests to verify failure**

Run the editor workspace test project. Expected: compile failure because the two new workspace properties do not exist.

- [ ] **Step 3: Implement workspace properties**

In `EditorPlatformBuildGraphWorkspace`, add:

```csharp
GeneratedProjectRootPath = Path.Combine(ExecutionRootPath, "generated-project");
PlatformArtifactRootPath = Path.Combine(ExecutionRootPath, "platform-artifacts");
```

Update the factory to include a new `Guid.NewGuid().ToString("N")` invocation segment after the platform and queue segments before constructing `ExecutionRootPath`.

- [ ] **Step 4: Run tests to verify success**

Run the focused workspace tests. Expected: all pass and every writable root is unique.

- [ ] **Step 5: Commit**

```bash
git add C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphWorkspace.cs C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphWorkspaceFactory.cs
git commit -m "feat: isolate platform build workspace roots"
```

### Task 2: Propagate isolated roots through the platform build request

- [ ] **Step 1: Write failing request/runner tests**

Assert the runner passes `workspace.GeneratedProjectRootPath` and `workspace.PlatformArtifactRootPath` to the platform builder request, and creates both directories before invoking the builder.

- [ ] **Step 2: Run tests to verify failure**

Run the focused editor build-graph runner tests. Expected: request properties are missing.

- [ ] **Step 3: Implement request propagation**

Add required `GeneratedProjectRootPath` and `PlatformArtifactRootPath` properties to `PlatformBuildRequest`. In `EditorPlatformBuildGraphRunner`, create those directories with the existing workspace directories and populate the new request properties. Route generated project-script `obj`/`bin` output beneath `GeneratedProjectRootPath`.

- [ ] **Step 4: Run tests to verify success**

Run the focused runner and platform request tests. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add C:\dev\helworks\helengine\engine\helengine.platforms\PlatformBuildRequest.cs C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPlatformBuildGraphRunner.cs
git commit -m "feat: pass isolated roots to platform builders"
```

### Task 3: Make PS2 native artifacts private to its build request

- [ ] **Step 1: Write failing PS2 path tests**

In `Ps2PlatformAssetBuilderTests`, construct a request with `PlatformArtifactRootPath = <temp>\\artifacts` and assert `Ps2BuildWorkspace.NativeExecutablePath` is `<temp>\\artifacts\\native\\build\\helengine_ps2.elf` and `StagingRootPath` is `<temp>\\artifacts\\staging`.

- [ ] **Step 2: Run tests to verify failure**

Run `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2PlatformAssetBuilder"`. Expected: the builder still resolves `repositoryRoot\\build`.

- [ ] **Step 3: Implement PS2 workspace derivation**

Change `Ps2PlatformAssetBuilder.CreateWorkspace` to derive native, staging, disc scratch, and log paths from `request.PlatformArtifactRootPath`. Add explicit corresponding properties to `Ps2BuildWorkspace`; retain `OutputRootPath` only for final exports.

- [ ] **Step 4: Run tests to verify success**

Run the focused PS2 builder tests. Expected: every writable PS2 path is inside `PlatformArtifactRootPath`.

- [ ] **Step 5: Commit**

```bash
git add builder/Ps2PlatformAssetBuilder.cs builder/Ps2BuildWorkspace.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
git commit -m "feat(ps2): isolate native build artifacts"
```

### Task 4: Build PS2 from the private native workspace

- [ ] **Step 1: Write failing native-executor tests**

Assert `Ps2NativeBuildExecutor` creates/mounts the private native workspace and that its Docker `make` working directory and output ELF path are under `PlatformArtifactRootPath`, not `RepositoryRootPath\\build`.

- [ ] **Step 2: Run tests to verify failure**

Run `dotnet test builder.tests/helengine.ps2.builder.tests.csproj --filter "FullyQualifiedName~Ps2NativeBuildExecutor"`. Expected: command arguments mount the repository as writable `/workspace`.

- [ ] **Step 3: Implement native workspace preparation**

Copy the required PS2 source tree, Makefile, and Dockerfile into `workspace.NativeWorkspaceRootPath`. Mount that path as `/workspace`; mount generated core at `/generated-core`; run `make` there. Package only from the private staging/disc roots.

- [ ] **Step 4: Run tests to verify success**

Run the focused native executor tests. Expected: Docker arguments use the private workspace and no writable repository build path.

- [ ] **Step 5: Commit**

```bash
git add builder/Ps2NativeBuildExecutor.cs builder/Ps2BuildWorkspace.cs builder.tests/Ps2NativeBuildExecutorTests.cs
git commit -m "feat(ps2): build native artifacts in private workspace"
```

### Task 5: Verify concurrency and cleanup boundaries

- [ ] **Step 1: Write failing concurrency tests**

Create two requests with the same project/platform and assert their generated project and platform artifact roots differ. Write the same `scene.tools.dll` name to both roots in parallel and assert both files exist with independent contents. Add a cleanup test asserting a workspace cleanup cannot delete the final output root.

- [ ] **Step 2: Run tests to verify failure**

Run the focused workspace/runner tests. Expected: current cleanup or intermediate-path behavior allows a collision or lacks the required path boundary.

- [ ] **Step 3: Implement guarded cleanup**

Make cleanup accept only `ExecutionRootPath` and validate it is beneath the project platform-isolation root before recursive deletion. Do not delete `OutputRootPath` as part of workspace reset.

- [ ] **Step 4: Run verification**

Run the focused editor workspace tests and `dotnet test builder.tests/helengine.ps2.builder.tests.csproj`. Expected: all pass; parallel writes do not collide.

- [ ] **Step 5: Commit**

```bash
git add C:\dev\helworks\helengine\engine\helengine.editor\managers\project builder.tests
git commit -m "test: verify isolated platform build concurrency"
```

### Task 6: End-to-end parallel build validation

- [ ] **Step 1: Run two platform builds concurrently**

Launch two build requests for the same demo project with different platform IDs and output directories. Confirm each report names a distinct execution root, generated project root, and platform artifact root.

- [ ] **Step 2: Run two PS2 builds concurrently**

Launch two PS2 build requests with distinct output directories. Confirm both complete without a generated-DLL lock and each ISO exists at its requested output path.

- [ ] **Step 3: Commit final verification adjustments**

```bash
git add docs/superpowers/specs/2026-07-24-isolated-platform-build-workspaces-design.md docs/superpowers/plans/2026-07-24-isolated-platform-build-workspaces.md
git commit -m "docs: record isolated platform build validation"
```
