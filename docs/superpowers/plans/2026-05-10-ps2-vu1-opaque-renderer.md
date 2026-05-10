# PS2 VU1 Opaque Renderer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the CPU triangle submission path for PS2 opaque rendering with a `VU1 + VIF` path that supports opaque untextured and opaque textured draws using PS2-native qword-aligned packed geometry.

**Architecture:** Keep `Ps2RenderManager3D` as the public renderer entry point, but add a new internal VU opaque path beside the current CPU fallback. Builder-side cooking emits PS2-native packed mesh payloads in qword-aligned `vector4`-friendly layout, and runtime-side rendering routes opaque batches through dedicated VIF packet builders, VU microprogram selection, and GIF state encoding.

**Tech Stack:** C#, .NET 9, C++20, PS2SDK, dmaKit, gsKit bootstrap, Docker-based PS2 native build, xUnit.

---

## File Structure

### Existing files to modify

- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PlatformAssetBuilder.cs`
  - teach the builder to stage and preserve PS2 packed mesh payloads
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2PlatformAssetBuilderTests.cs`
  - add builder regressions for packed mesh outputs
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`
  - add native source assertions for the VU path
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp`
  - route opaque rendering through the VU path
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
  - integrate batch building and VU dispatch
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.hpp`
  - expose packed geometry accessors
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.cpp`
  - load packed PS2 geometry payloads
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile`
  - compile and link new VU runtime files

### New builder/runtime files

- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PackedMeshLayout.cs`
  - defines the qword-aligned cooked mesh layout contract
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PackedMeshCooker.cs`
  - converts raw model assets into VU-ready packed mesh payloads
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.hpp`
  - runtime representation for packed PS2 mesh sections and streams
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.cpp`
  - packed mesh loader from cooked PS2 payload bytes
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatch.hpp`
  - one opaque draw batch description
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatchBuilder.hpp`
  - groups proxies into compatible opaque batches
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatchBuilder.cpp`
  - batch assembly implementation
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramKind.hpp`
  - identifies untextured vs textured opaque microprogram kinds
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramRegistry.hpp`
  - VU1 microprogram lookup interface
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramRegistry.cpp`
  - microprogram registration and selection
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuGifStateEncoder.hpp`
  - opaque GS/GIF state encoding helpers
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuGifStateEncoder.cpp`
  - GIF state packet assembly
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp`
  - VIF packet assembly for constants and packed geometry
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
  - VIF packet implementation

### Existing docs to keep aligned

- Reference: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\docs\superpowers\specs\2026-05-10-ps2-vu1-opaque-renderer-design.md`

---

### Task 1: Define the PS2 packed mesh contract in the builder

**Files:**
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PackedMeshLayout.cs`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PackedMeshCooker.cs`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder\Ps2PlatformAssetBuilder.cs`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2PlatformAssetBuilderTests.cs`

- [ ] **Step 1: Write the failing builder test for qword-aligned VU packed meshes**

```csharp
[Fact]
public async Task BuildAsync_WhenSceneContainsOpaqueCube_ProducesVuPackedMeshArtifact() {
    PlatformBuildRequest request = CreateRequestWithCookedCubeScene();
    Ps2PlatformAssetBuilder builder = new Ps2PlatformAssetBuilder(new FakePs2NativeBuildExecutor());

    PlatformBuildReport report = await builder.BuildAsync(
        request,
        new NullPlatformBuildProgressReporter(),
        new CollectingPlatformBuildDiagnosticReporter(),
        CancellationToken.None);

    Assert.True(report.Succeeded);

    string packedMeshPath = Path.Combine(request.WorkingRoot, "ps2-staging", "cooked", "engine", "models", "cube.vup");
    Assert.True(File.Exists(packedMeshPath));

    byte[] bytes = File.ReadAllBytes(packedMeshPath);
    Assert.True(bytes.Length > 0);
    Assert.Equal(0, bytes.Length % 16);
}
```

- [ ] **Step 2: Run the builder test and verify it fails**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~BuildAsync_WhenSceneContainsOpaqueCube_ProducesVuPackedMeshArtifact
```

Expected: `FAIL` because no packed VU mesh artifact exists yet.

- [ ] **Step 3: Add the packed mesh layout type**

```csharp
namespace helengine.ps2.builder;

/// <summary>
/// Defines the first-milestone VU1 packed mesh layout used by cooked PS2 geometry.
/// </summary>
public static class Ps2PackedMeshLayout {
    /// <summary>
    /// Stable file extension for packed PS2 VU mesh payloads.
    /// </summary>
    public const string PackedMeshExtension = ".vup";

    /// <summary>
    /// Stable payload version for the first qword-aligned VU packed mesh format.
    /// </summary>
    public const byte Version = 1;

    /// <summary>
    /// Size of one packed qword in bytes.
    /// </summary>
    public const int QwordSize = 16;
}
```

- [ ] **Step 4: Add the packed mesh cooker**

```csharp
namespace helengine.ps2.builder;

/// <summary>
/// Converts raw model assets into the first-milestone PS2 VU1 packed mesh payload format.
/// </summary>
public sealed class Ps2PackedMeshCooker {
    /// <summary>
    /// Builds one qword-aligned packed mesh payload from a raw model asset.
    /// </summary>
    /// <param name="modelAsset">Raw model asset to convert.</param>
    /// <returns>Packed PS2 mesh payload bytes.</returns>
    public byte[] Cook(ModelAsset modelAsset) {
        if (modelAsset == null) {
            throw new ArgumentNullException(nameof(modelAsset));
        }

        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(Ps2PackedMeshLayout.Version);
        WritePackedVertexBlocks(writer, modelAsset);
        WritePackedIndexBlocks(writer, modelAsset);
        PadToQwordBoundary(writer);

        return stream.ToArray();
    }
}
```

- [ ] **Step 5: Wire the packed mesh cooker into the PS2 asset builder**

```csharp
readonly Ps2PackedMeshCooker PackedMeshCooker;

public Ps2PlatformAssetBuilder() {
    NativeBuildExecutor = new Ps2NativeBuildExecutor();
    MaterialCooker = new Ps2MaterialCooker();
    PackedMeshCooker = new Ps2PackedMeshCooker();
    DiscLayoutWriter = new Ps2DiscLayoutWriter();
    RuntimeAssetPathManifestWriter = new Ps2RuntimeAssetPathManifestWriter();
    CookedAssetPathRewriter = new Ps2CookedAssetPathRewriter();
    Descriptor = new PlatformBuilderDescriptor(
        "helengine.ps2.builder",
        "1.0.0",
        "ps2",
        new EngineCompatibilityRange("1.0.0", "999.0.0"),
        new ManifestCompatibilityRange(1, 3),
        ["ps2"],
        ["ps2"]);
    Definition = Ps2PlatformDefinitionFactory.Create();
}
```

- [ ] **Step 6: Rerun the builder test and verify it passes**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~BuildAsync_WhenSceneContainsOpaqueCube_ProducesVuPackedMeshArtifact
```

Expected: `PASS`

- [ ] **Step 7: Commit**

```bash
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core add builder/Ps2PackedMeshLayout.cs builder/Ps2PackedMeshCooker.cs builder/Ps2PlatformAssetBuilder.cs builder.tests/Ps2PlatformAssetBuilderTests.cs
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core commit -m "feat: cook ps2 vu packed meshes"
```

### Task 2: Add runtime packed-model representation and loader

**Files:**
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuPackedModel.cpp`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.hpp`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.cpp`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Add the failing source-level native test for packed model loading**

```csharp
[Fact]
public void Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading() {
    string header = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.hpp");
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RuntimeModel.cpp");

    Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuPackedModel.hpp\"", header, StringComparison.Ordinal);
    Assert.Contains("const Ps2VuPackedModel* GetVuPackedModel() const;", header, StringComparison.Ordinal);
    Assert.Contains("VuPackedModel = new Ps2VuPackedModel();", source, StringComparison.Ordinal);
    Assert.Contains("VuPackedModel->LoadFromPackedBytes(", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading
```

Expected: `FAIL` because the packed-model type does not exist yet.

- [ ] **Step 3: Add the packed runtime model type**

```cpp
namespace helengine::ps2 {
    /// <summary>
    /// Stores qword-aligned packed PS2 geometry used by the VU1 opaque render path.
    /// </summary>
    class Ps2VuPackedModel final {
    public:
        void LoadFromPackedBytes(const std::uint8_t* bytes, std::size_t length);
        const std::vector<std::uint8_t>& GetPackedVertexBytes() const;
        const std::vector<std::uint8_t>& GetPackedIndexBytes() const;

    private:
        std::vector<std::uint8_t> PackedVertexBytes;
        std::vector<std::uint8_t> PackedIndexBytes;
    };
}
```

- [ ] **Step 4: Extend `Ps2RuntimeModel` with the packed fast-path member**

```cpp
class Ps2RuntimeModel final : public ::RuntimeModel {
public:
    Ps2RuntimeModel();
    void LoadFromRaw(::ModelAsset* modelAsset);
    const Ps2VuPackedModel* GetVuPackedModel() const;

private:
    Ps2VuPackedModel* VuPackedModel;
};
```

- [ ] **Step 5: Load the packed representation while preserving the legacy path**

```cpp
void Ps2RuntimeModel::LoadFromRaw(::ModelAsset* modelAsset) {
    if (modelAsset == nullptr) {
        throw std::invalid_argument("PS2 raw model data is required.");
    }

    LoadLegacyVectors(modelAsset);

    delete VuPackedModel;
    VuPackedModel = new Ps2VuPackedModel();
    VuPackedModel->LoadFromPackedBytes(modelAsset->Ps2PackedMeshBytes->Data, static_cast<std::size_t>(modelAsset->Ps2PackedMeshBytes->Length));
}
```

- [ ] **Step 6: Rerun the source test and verify it passes**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading
```

Expected: `PASS`

- [ ] **Step 7: Commit**

```bash
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core add src/platform/ps2/rendering/Ps2RuntimeModel.hpp src/platform/ps2/rendering/Ps2RuntimeModel.cpp src/platform/ps2/rendering/vu/Ps2VuPackedModel.hpp src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp builder.tests/Ps2NativeBuildInputsTests.cs
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core commit -m "feat: add ps2 vu packed runtime model"
```

### Task 3: Add opaque batch building, program registry, and VIF/GIF packet infrastructure

**Files:**
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatch.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatchBuilder.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuOpaqueBatchBuilder.cpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramKind.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramRegistry.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuProgramRegistry.cpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuGifStateEncoder.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuGifStateEncoder.cpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.hpp`
- Create: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\vu\Ps2VuVifPacketBuilder.cpp`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Add the failing source test for the VU opaque pipeline infrastructure**

```csharp
[Fact]
public void Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure() {
    string rendererHeader = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp");

    Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
    Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp\"", rendererHeader, StringComparison.Ordinal);
    Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp\"", rendererHeader, StringComparison.Ordinal);
    Assert.Contains("#include \"platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp\"", rendererHeader, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure
```

Expected: `FAIL`

- [ ] **Step 3: Add the opaque batch type and batch builder**

```cpp
namespace helengine::ps2 {
    struct Ps2VuOpaqueBatch {
        const Ps2RenderProxy* Proxy;
        const Ps2VuPackedModel* Model;
        const Ps2RuntimeMaterial* Material;
        bool Textured;
    };

    class Ps2VuOpaqueBatchBuilder final {
    public:
        std::vector<Ps2VuOpaqueBatch> Build(const Ps2FramePlan& plan) const;
    };
}
```

- [ ] **Step 4: Add the program kind and registry**

```cpp
namespace helengine::ps2 {
    enum class Ps2VuProgramKind : std::uint8_t {
        OpaqueUntextured = 0,
        OpaqueTextured = 1
    };

    class Ps2VuProgramRegistry final {
    public:
        Ps2VuProgramKind ResolveOpaqueProgram(const Ps2VuOpaqueBatch& batch) const;
    };
}
```

- [ ] **Step 5: Add the VIF and GIF builders**

```cpp
namespace helengine::ps2 {
    class Ps2VuGifStateEncoder final {
    public:
        void EncodeOpaqueState(const Ps2VuOpaqueBatch& batch, GSGLOBAL* gsGlobal) const;
    };

    class Ps2VuVifPacketBuilder final {
    public:
        void Reset();
        void AddOpaqueBatch(const Ps2VuOpaqueBatch& batch, const ::float4x4& world, const ::float4x4& viewProjection);
        const std::vector<std::uint8_t>& GetPacketBytes() const;

    private:
        std::vector<std::uint8_t> PacketBytes;
    };
}
```

- [ ] **Step 6: Rerun the source test and verify it passes**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure
```

Expected: `PASS`

- [ ] **Step 7: Commit**

```bash
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core add src/platform/ps2/rendering/vu/Ps2VuOpaqueBatch.hpp src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp src/platform/ps2/rendering/vu/Ps2VuProgramKind.hpp src/platform/ps2/rendering/vu/Ps2VuProgramRegistry.hpp src/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.hpp src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.hpp src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp builder.tests/Ps2NativeBuildInputsTests.cs
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core commit -m "feat: add ps2 vu opaque packet infrastructure"
```

### Task 4: Route opaque rendering through the VU path and keep CPU fallback

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.hpp`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp`
- Test: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`

- [ ] **Step 1: Add the failing source test for opaque VU routing**

```csharp
[Fact]
public void Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback() {
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\src\platform\ps2\rendering\Ps2RenderManager3D.cpp");

    Assert.Contains("RenderOpaqueWithVuPath(", source, StringComparison.Ordinal);
    Assert.Contains("DrawOpaqueProxyLegacy(", source, StringComparison.Ordinal);
    Assert.Contains("if (UseLegacyCpuOpaquePath)", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback
```

Expected: `FAIL`

- [ ] **Step 3: Split the legacy opaque path from the new VU path**

```cpp
class Ps2RenderManager3D final : public ::RenderManager3D {
private:
    void RenderOpaqueWithVuPath(const Ps2FramePlan& plan, const ::float4x4& view, const ::float4x4& projection);
    void DrawOpaqueProxyLegacy(const Ps2RenderProxy& proxy, const ::float4x4& view, const ::float4x4& projection, const ::float4& viewport, float nearPlaneDistance);
    bool UseLegacyCpuOpaquePath;
};
```

- [ ] **Step 4: Route the opaque pass through batches and packet builders**

```cpp
void Ps2RenderManager3D::RenderOpaqueWithVuPath(const Ps2FramePlan& plan, const ::float4x4& view, const ::float4x4& projection) {
    std::vector<Ps2VuOpaqueBatch> batches = VuOpaqueBatchBuilder.Build(plan);
    for (const Ps2VuOpaqueBatch& batch : batches) {
        VuGifStateEncoder.EncodeOpaqueState(batch, GsGlobal);
        VuVifPacketBuilder.Reset();
        VuVifPacketBuilder.AddOpaqueBatch(batch, ResolveWorldMatrix(*batch.Proxy), projection * view);
        ExecuteVuOpaquePacket(VuProgramRegistry.ResolveOpaqueProgram(batch), VuVifPacketBuilder.GetPacketBytes());
    }
}
```

- [ ] **Step 5: Keep the CPU path as an explicit internal fallback**

```cpp
if (UseLegacyCpuOpaquePath) {
    for (const Ps2RenderProxy* proxy : plan.OpaqueWorld) {
        if (proxy != nullptr) {
            DrawOpaqueProxyLegacy(*proxy, view, projection, viewport, camera->get_NearPlaneDistance());
        }
    }
} else {
    RenderOpaqueWithVuPath(plan, view, projection);
}
```

- [ ] **Step 6: Rerun the source test and verify it passes**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback
```

Expected: `PASS`

- [ ] **Step 7: Commit**

```bash
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core add src/platform/ps2/rendering/Ps2RenderManager3D.hpp src/platform/ps2/rendering/Ps2RenderManager3D.cpp builder.tests/Ps2NativeBuildInputsTests.cs
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core commit -m "feat: route ps2 opaque rendering through vu path"
```

### Task 5: Wire the native build, run end-to-end verification, and measure the three city scenes

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile`
- Modify: `C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\Ps2NativeBuildInputsTests.cs`
- Verify: `C:\dev\helprojs\city\assets\scenes\rendering\cube_test.helen`
- Verify: `C:\dev\helprojs\city\assets\scenes\rendering\colored_cube_grid.helen`
- Verify: `C:\dev\helprojs\city\assets\scenes\rendering\textured_cube_grid.helen`

- [ ] **Step 1: Add the failing native source test for new VU compilation units in the PS2 build**

```csharp
[Fact]
public void Ps2_makefile_compiles_vu_opaque_renderer_units() {
    string source = File.ReadAllText(@"C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\Makefile");

    Assert.Contains("src/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp", source, StringComparison.Ordinal);
    Assert.Contains("src/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp", source, StringComparison.Ordinal);
    Assert.Contains("src/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp", source, StringComparison.Ordinal);
    Assert.Contains("src/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp", source, StringComparison.Ordinal);
    Assert.Contains("src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp", source, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_makefile_compiles_vu_opaque_renderer_units
```

Expected: `FAIL`

- [ ] **Step 3: Update the PS2 Makefile and native source assertions**

```makefile
SOURCES := \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RenderManager3D.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/Ps2RuntimeModel.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuPackedModel.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuOpaqueBatchBuilder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuProgramRegistry.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuGifStateEncoder.cpp \
	$(SOURCE_DIR)/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp
```

- [ ] **Step 4: Rerun the source test and verify it passes**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter FullyQualifiedName~Ps2_makefile_compiles_vu_opaque_renderer_units
```

Expected: `PASS`

- [ ] **Step 5: Run the focused builder/native regression suite**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; rtk dotnet test C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core\builder.tests\helengine.ps2.builder.tests.csproj -c Debug --filter "FullyQualifiedName~BuildAsync_WhenSceneContainsOpaqueCube_ProducesVuPackedMeshArtifact|FullyQualifiedName~Ps2_runtime_model_exposes_vu_packed_geometry_for_fast_path_loading|FullyQualifiedName~Ps2_renderer3d_declares_vu_opaque_batch_and_packet_infrastructure|FullyQualifiedName~Ps2_renderer3d_routes_opaque_draws_through_vu_path_while_retaining_cpu_fallback|FullyQualifiedName~Ps2_makefile_compiles_vu_opaque_renderer_units"
```

Expected: all selected tests `PASS`

- [ ] **Step 6: Build and export the PS2 city scenes for runtime verification**

Run:

```powershell
$env:HELENGINE_ROOT='C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core'; & 'C:\Program Files\dotnet\dotnet.exe' 'C:\dev\helworks\helengine\.worktrees\normalize-camera-viewport-core\helengine.ui\helengine.editor.app\bin\Debug\net9.0-windows\helengine.editor.app.dll' --project 'C:\dev\helprojs\city\project.heproj' --build ps2 --output 'C:\dev\helprojs\output\ps2-vu1-opaque'
```

Expected: `Build completed for platform 'ps2'`

- [ ] **Step 7: Verify runtime correctness and timing on the three city scenes**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "Start-Process 'C:\Program Files\PCSX2\pcsx2-qt.exe' -ArgumentList 'C:\dev\helprojs\output\ps2-vu1-opaque\game.iso'"
```

Expected:

- `cube_test.helen` renders through the VU path
- `colored_cube_grid.helen` renders through the VU path
- `textured_cube_grid.helen` renders through the VU path
- draw timing is materially lower than the current CPU path

- [ ] **Step 8: Commit**

```bash
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core add Makefile builder.tests/Ps2NativeBuildInputsTests.cs
git -C C:\dev\helworks\helengine-ps2\.worktrees\normalize-camera-viewport-core commit -m "feat: wire ps2 vu opaque renderer build"
```

## Self-Review

### Spec coverage

- packed PS2 `vector4`/qword-aligned mesh layout: covered by Task 1 and Task 2
- no runtime `vector3 -> vector4` conversion: covered by Task 1 and Task 2
- parallel VU path beside CPU fallback: covered by Task 4
- opaque untextured + opaque textured only: covered by Task 3 and Task 4
- batch builder, VIF packets, VU registry, GIF state encoding: covered by Task 3
- end-to-end verification on cube, colored grid, textured grid: covered by Task 5

### Placeholder scan

- no `TODO`
- no `TBD`
- no “similar to previous task”
- all tasks list concrete files, commands, and expected outcomes

### Type consistency

- packed builder types consistently use `Ps2PackedMesh*`
- runtime VU types consistently use `Ps2Vu*`
- render-manager opaque fast path consistently uses `RenderOpaqueWithVuPath`
- legacy fallback consistently uses `DrawOpaqueProxyLegacy`
