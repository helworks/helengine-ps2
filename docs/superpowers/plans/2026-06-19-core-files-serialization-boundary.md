# Core Files Serialization Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the `helengine.files`/`helengine.core` serialization boundary, remove duplicate writer ownership, and migrate generic-compatible built-in components off bespoke packaged payload and runtime deserializer paths.

**Architecture:** The implementation keeps `helengine.core` read-only for binary payload handling and moves all writer-side primitives and helpers into `helengine.files`. Built-in components that already fit the reflected persistence contract will load through the same automatic/generic runtime payload path used by ordinary components, while only `MeshComponent`, `CameraComponent`, `SceneMapComponent`, and `StaticMeshCollider3DComponent` remain bespoke under the current generic-system limits.

**Tech Stack:** C#, .NET 9, Helengine editor/runtime scene persistence, PS2 build pipeline, C++ codegen validation, xUnit

---

### Task 1: Freeze The Cleanup Inventory And Guardrails

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\docs\superpowers\specs\2026-06-19-core-files-serialization-boundary-design.md`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\RuntimeComponentDeserializerSourceAuditTests.cs`

- [ ] **Step 1: Add the final "generic now vs bespoke now" matrix to the spec if any drift remains**

Confirm the approved spec still explicitly lists:

```text
Generic now:
DirectionalLightComponent, AmbientLightComponent, PointLightComponent, SpotLightComponent,
TextComponent, SpriteComponent, RoundedRectComponent, FPSComponent, DebugComponent,
RigidBody3DComponent, BoxCollider3DComponent, SphereCollider3DComponent,
CapsuleCollider3DComponent, KinematicMotion3DComponent, CharacterController3DComponent

Keep bespoke now:
MeshComponent, CameraComponent, SceneMapComponent, StaticMeshCollider3DComponent
```

- [ ] **Step 2: Tighten the generated-core regression test to match the approved target**

Update `EditorGeneratedCoreRegenerationServiceTests.cs` so the test that currently asserts built-in generic deserializers are not generated only keeps the four approved bespoke built-ins excluded from generated deserializer output.

Use assertions shaped like:

```csharp
Assert.DoesNotContain("GeneratedRuntimeMeshComponentDeserializer", registrationSource, StringComparison.Ordinal);
Assert.DoesNotContain("GeneratedRuntimeCameraComponentDeserializer", registrationSource, StringComparison.Ordinal);
Assert.DoesNotContain("GeneratedRuntimeSceneMapComponentDeserializer", registrationSource, StringComparison.Ordinal);
Assert.DoesNotContain("GeneratedRuntimeStaticMeshCollider3DComponentDeserializer", registrationSource, StringComparison.Ordinal);
Assert.Contains("GeneratedRuntimeDirectionalLightComponentDeserializer", registrationSource, StringComparison.Ordinal);
Assert.Contains("GeneratedRuntimeTextComponentDeserializer", registrationSource, StringComparison.Ordinal);
```

- [ ] **Step 3: Replace source-audit assumptions that still expect removable bespoke deserializers**

Adjust `RuntimeComponentDeserializerSourceAuditTests.cs` so it only audits the files that are intentionally staying bespoke after the cleanup, instead of pinning ownership-cleanup snippets in files that should be deleted.

- [ ] **Step 4: Run the smallest regeneration test slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorGeneratedCoreRegenerationServiceTests -v minimal`

Expected: the updated expectations fail before implementation and identify exactly which built-ins still route through hand-authored runtime deserializers.

### Task 2: Move Writer Ownership Out Of Helengine Core

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\serialization\EngineBinaryReader.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\serialization\BinaryReaderLE.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\serialization\BinaryReaderBE.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\serialization\BinarySerializationExtensions.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\serialization\EngineBinaryWriter.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\serialization\BinaryWriterLE.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\serialization\BinaryWriterBE.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.files\serialization\EngineBinaryWriter.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.files\serialization\BinaryWriterLE.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.files\serialization\BinaryWriterBE.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.files\helengine.files.csproj`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\helengine.core.csproj`
- Test: `C:\dev\helworks\helengine\managed\helengine.ps2\helengine.ps2.csproj`

- [ ] **Step 1: Write the failing codegen repro command into the task notes and keep it as the ownership gate**

The direct failure to clear is:

```text
Generated C++ type name collision for 'EngineBinaryWriter' between
'helengine.EngineBinaryWriter' and 'helengine.files.EngineBinaryWriter'
```

Use this exact command later as the validation gate:

```powershell
rtk proxy powershell.exe -NoProfile -Command "& 'C:\dev\helworks\csharpcodegen\codegen\bin\Debug\net9.0\codegen.exe' 'C:\dev\helworks\helengine\managed\helengine.ps2\helengine.ps2.csproj' 'C:\tmp\ps2-codegen-repro-out'"
```

- [ ] **Step 2: Remove writer factories from `helengine.core` while keeping reader factories intact**

`EngineBinaryReader.Create(...)` must remain in `helengine.core`.

The deleted core writer surface should be replaced by `helengine.files` ownership shaped like:

```csharp
namespace helengine.files {
    public abstract class EngineBinaryWriter : IDisposable {
        public static EngineBinaryWriter Create(Stream stream, EngineBinaryEndianness endianness, bool leaveOpen = true) {
            return endianness == EngineBinaryEndianness.LittleEndian
                ? new BinaryWriterLE(stream, leaveOpen)
                : new BinaryWriterBE(stream, leaveOpen);
        }
    }
}
```

- [ ] **Step 3: Split mixed read/write extension methods by ownership**

Keep only read helpers in `helengine.core\serialization\BinarySerializationExtensions.cs`.
Move write helpers into a files-side extension class such as:

```csharp
namespace helengine.files {
    public static class BinarySerializationWriteExtensions {
        public static void WriteFloat4(this EngineBinaryWriter writer, float4 value) { /* moved body */ }
        public static void WriteSceneEntityReference(this EngineBinaryWriter writer, SceneEntityReference reference) { /* moved body */ }
    }
}
```

- [ ] **Step 4: Update compile references so writer call sites resolve only through `helengine.files`**

Fix all write-side consumers to use the files-side namespace and project reference. Do not leave aliases or compatibility wrappers in `helengine.core`.

- [ ] **Step 5: Run the direct codegen repro**

Run: the command from Step 1.

Expected: the duplicate `EngineBinaryWriter` collision no longer appears. Any remaining failures should be downstream persistence issues, not type-name duplication.

### Task 3: Remove Mixed Write-Side Payload Code From Helengine Core

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\MeshComponentScenePayloadSerializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\LightComponentScenePayloadSerializer.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.files\scene\MeshComponentRuntimePayloadWriter.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.files\scene\CameraComponentRuntimePayloadWriter.cs`
- Create: `C:\dev\helworks\helengine\engine\helengine.files\scene\SceneMapComponentRuntimePayloadWriter.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\SceneComponentPackagingTransformService.cs`

- [ ] **Step 1: Replace mixed read/write payload classes with read-only core classes and files-side writers**

Do not leave methods like `Write(...)` in `helengine.core`.

Use a split shaped like:

```csharp
// helengine.core
public static class MeshComponentScenePayloadReader {
    public static void Read(EngineBinaryReader reader, out SceneAssetReference modelReference, out SceneAssetReference[] materialReferences, out byte renderOrder3D) { /* existing read body */ }
}

// helengine.files
public static class MeshComponentRuntimePayloadWriter {
    public static void Write(EngineBinaryWriter writer, SceneAssetReference modelReference, SceneAssetReference[] materialReferences, byte renderOrder3D) { /* existing write body */ }
}
```

- [ ] **Step 2: Do the same split for the camera and scene-map packaged writers**

`SceneComponentPackagingTransformService.cs` currently writes camera and scene-map runtime payloads inline. Move those write shapes into files-side writer classes so the editor/build pipeline depends on `helengine.files` for emission.

- [ ] **Step 3: Delete the light payload serializer instead of moving it**

`LightComponentScenePayloadSerializer.cs` should not be migrated to `helengine.files`; the approved architecture is to eliminate the bespoke light runtime payload shape entirely.

- [ ] **Step 4: Update packaging callers to use files-side runtime writers**

Change `SceneComponentPackagingTransformService` to depend on files-side writer helpers for the remaining bespoke components only.

- [ ] **Step 5: Run the smallest compile-focused test slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter RuntimeSceneLoadServiceTests -v minimal`

Expected: any failures now point to light/genericization registration gaps, not missing writer ownership.

### Task 4: Genericize Light Components End-To-End

**Files:**
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeDirectionalLightComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeAmbientLightComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimePointLightComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeSpotLightComponentDeserializer.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeComponentRegistry.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\SceneComponentPackagingTransformService.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.editor\serialization\scene\LightComponentTaggedFieldEncoding.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\RuntimeSceneLoadServiceTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorGeneratedCoreRegenerationServiceTests.cs`

- [ ] **Step 1: Stop treating lights as bespoke packaged payloads**

Remove the explicit light rewrite branches in `SceneComponentPackagingTransformService.cs` so light records are rewritten through the existing automatic-component path instead of:

```csharp
writer.WriteByte(LightComponentScenePayloadSerializer.CurrentVersion);
LightComponentScenePayloadSerializer.WriteDirectionalLight(writer, lightComponent);
```

- [ ] **Step 2: Let generated/generic runtime deserializers own light reconstruction**

Remove light registration from `RuntimeComponentRegistry.cs` and let generated runtime deserializers cover:

```csharp
DirectionalLightComponent
AmbientLightComponent
PointLightComponent
SpotLightComponent
```

- [ ] **Step 3: Delete the unused tagged-field helper if the build confirms no remaining references**

`LightComponentTaggedFieldEncoding.cs` should be removed entirely once references are gone.

- [ ] **Step 4: Replace bespoke runtime light tests with generic runtime expectations**

Update `RuntimeSceneLoadServiceTests.cs` to build light records using the automatic runtime payload format and assert the resulting components preserve `Color`, `Intensity`, `ShadowsEnabled`, `ShadowMapMode`, `ShadowStrength`, and type-specific members like `Range` and `ShadowDistance`.

Use a test payload shape like:

```csharp
writer.WriteByte(AutomaticScriptComponentRuntimeDeserializer.CurrentVersion);
writer.WriteInt32(schema.Members.Count);
// ordered reflected member values...
```

- [ ] **Step 5: Run the light-focused test slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeSceneLoadServiceTests|EditorGeneratedCoreRegenerationServiceTests" -v minimal`

Expected: light components load through generic runtime deserializers and no bespoke light deserializer files are required.

### Task 5: Genericize UI Built-Ins That Already Fit The Reflected Model

**Files:**
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeTextComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeSpriteComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeRoundedRectComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeFPSComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeDebugComponentDeserializer.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeComponentRegistry.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\SceneComponentPackagingTransformService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\RuntimeSceneLoadServiceTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\AutomaticScriptComponentPersistenceDescriptorTests.cs`

- [ ] **Step 1: Remove explicit rewrite branches for generic-compatible UI built-ins**

Delete the bespoke `RewriteTextComponentRecord`, `RewriteSpriteComponentRecord`, `RewriteRoundedRectComponentRecord`, `RewriteFPSComponentRecord`, and `RewriteDebugComponentRecord` routing where the component state is already representable by automatic persistence.

- [ ] **Step 2: Keep asset-backed member behavior through automatic component save-state rewriting**

Verify the generic path uses existing save-state rewriting for:

```csharp
FontAsset
RuntimeTexture
```

and that `TextComponent.Font`, `TextComponent.Texture`, `SpriteComponent.Texture`, `FPSComponent.Font`, and `DebugComponent.Font` still package correctly through automatic asset-reference handling.

- [ ] **Step 3: Remove bespoke runtime registry entries for the UI built-ins**

Delete the five registrations from `RuntimeComponentRegistry.cs` and rely on generated runtime deserializers.

- [ ] **Step 4: Add one focused runtime load test per behavior family**

Add or update tests that cover:

```csharp
TextComponent: text, font, fontScale, alignment, selectionEnabled
SpriteComponent: texture, sourceRect, size, color
RoundedRectComponent: corners, fill/border colors, radius, thickness
FPSComponent / DebugComponent: font, padding, refresh interval, render order
```

- [ ] **Step 5: Run the UI persistence slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "RuntimeSceneLoadServiceTests|AutomaticScriptComponentPersistenceDescriptorTests" -v minimal`

Expected: UI built-ins round-trip through the generic reflected runtime payload path without hand-authored runtime deserializer files.

### Task 6: Genericize Physics Built-Ins That Already Fit The Reflected Model

**Files:**
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeRigidBody3DComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeBoxCollider3DComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeSphereCollider3DComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeCapsuleCollider3DComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeKinematicMotion3DComponentDeserializer.cs`
- Delete: `C:\dev\helworks\helengine\engine\helengine.physics\RuntimeCharacterController3DComponentDeserializer.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\EditorPhysics3DCodegenFeatureSymbolService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\managers\project\SceneComponentPackagingTransformService.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.bepu\BepuRuntimeComponentRegistration.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\managers\project\EditorPhysics3DCodegenFeatureSymbolServiceTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.bepu.tests\BepuAutomaticPhysicsRuntimePayloadTests.cs`

- [ ] **Step 1: Keep the explicit physics feature detection but stop preserving bespoke payloads for generic-compatible types**

`EditorPhysics3DCodegenFeatureSymbolService.cs` should still detect whether those component types exist in scenes, but the runtime payload generation should always use the automatic runtime payload shape for:

```text
RigidBody3DComponent
BoxCollider3DComponent
SphereCollider3DComponent
CapsuleCollider3DComponent
KinematicMotion3DComponent
CharacterController3DComponent
```

- [ ] **Step 2: Remove bespoke fallback preservation in scene packaging**

Delete the branches in `SceneComponentPackagingTransformService.cs` that currently preserve or deserialize those packaged payloads through hand-authored runtime deserializers.

- [ ] **Step 3: Remove BEPU registration for the deleted runtime deserializers**

`BepuRuntimeComponentRegistration.cs` should continue to register only the physics runtime deserializers that remain bespoke, which after this task should be `RuntimeStaticMeshCollider3DComponentDeserializer` only.

- [ ] **Step 4: Update physics tests to expect automatic runtime payloads**

Adjust `EditorPhysics3DCodegenFeatureSymbolServiceTests.cs` and `BepuAutomaticPhysicsRuntimePayloadTests.cs` so they validate the automatic runtime payload header:

```csharp
Assert.Equal(AutomaticScriptComponentRuntimeDeserializer.CurrentVersion, version);
Assert.Equal(expectedMemberCount, memberCount);
```

instead of bespoke physics payload formats.

- [ ] **Step 5: Run the physics slice**

Run:
- `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter EditorPhysics3DCodegenFeatureSymbolServiceTests -v minimal`
- `rtk dotnet test engine\helengine.bepu.tests\helengine.bepu.tests.csproj --filter BepuAutomaticPhysicsRuntimePayloadTests -v minimal`

Expected: generic-compatible physics built-ins now package and load through the reflected runtime payload format.

### Task 7: Preserve And Re-Assert The True Bespoke Paths

**Files:**
- Modify: `C:\dev\helworks\helengine\engine\helengine.core\scene\runtime\RuntimeComponentRegistry.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor\serialization\scene\ComponentPersistenceRegistry.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\ComponentPersistenceRegistryTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\SceneMapComponentPersistenceDescriptorTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\MeshComponentPersistenceDescriptorTests.cs`
- Modify: `C:\dev\helworks\helengine\engine\helengine.editor.tests\serialization\scene\CameraComponentPersistenceDescriptorTests.cs`

- [ ] **Step 1: Make the keep-bespoke set explicit in runtime registration and tests**

After the cleanup, the intentional bespoke runtime set should be:

```text
MeshComponent
CameraComponent
SceneMapComponent
StaticMeshCollider3DComponent
```

Keep this explicit in registry tests so future drift fails fast.

- [ ] **Step 2: Keep editor explicit descriptors only where the generic system cannot represent the component**

`ComponentPersistenceRegistry.cs` should continue to register explicit descriptors only for:

```csharp
new MeshComponentPersistenceDescriptor();
new CameraComponentPersistenceDescriptor();
new SceneMapComponentPersistenceDescriptor();
```

and everything else should fall through to `AutomaticScriptComponentPersistenceDescriptor`.

- [ ] **Step 3: Strengthen tests for the four justified bespoke paths**

Add targeted assertions that prove each one still needs bespoke handling:
- `MeshComponent` requires material-slot/save-state behavior
- `CameraComponent` requires custom payload normalization and unsupported member handling
- `SceneMapComponent` requires dictionary persistence
- `StaticMeshCollider3DComponent` requires cooked collision blob reconstruction

- [ ] **Step 4: Run the bespoke-path test slice**

Run: `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj --filter "ComponentPersistenceRegistryTests|MeshComponentPersistenceDescriptorTests|CameraComponentPersistenceDescriptorTests|SceneMapComponentPersistenceDescriptorTests" -v minimal`

Expected: the four remaining bespoke paths are explicit and justified.

### Task 8: End-To-End Verification And Build Repro

**Files:**
- Modify: `C:\dev\helworks\helengine-ps2\docs\superpowers\specs\2026-06-19-core-files-serialization-boundary-design.md`
- Modify: `C:\dev\helworks\helengine-ps2\docs\superpowers\plans\2026-06-19-core-files-serialization-boundary.md`

- [ ] **Step 1: Write the final removed/kept/genericized inventory into the spec or implementation notes**

Record the final classification as three flat lists:

```text
Removed files
Kept bespoke files
Genericized component families
```

Do not leave this only in commit history.

- [ ] **Step 2: Run the targeted managed/editor/runtime test slices together**

Run:
- `rtk dotnet test engine\helengine.editor.tests\helengine.editor.tests.csproj -v minimal`
- `rtk dotnet test engine\helengine.bepu.tests\helengine.bepu.tests.csproj -v minimal`

Expected: all persistence-related tests pass with no references to the deleted bespoke runtime deserializers.

- [ ] **Step 3: Run the direct PS2 codegen repro**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -Command "& 'C:\dev\helworks\csharpcodegen\codegen\bin\Debug\net9.0\codegen.exe' 'C:\dev\helworks\helengine\managed\helengine.ps2\helengine.ps2.csproj' 'C:\tmp\ps2-codegen-repro-out'"
```

Expected: successful completion with no `EngineBinaryWriter` collision and no built-in persistence-related generated-core failures.

- [ ] **Step 4: Run the full city PS2 build**

Run:

```powershell
rtk proxy powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\dev\helworks\helengine\artifacts\build-platform.ps1 -Project C:\dev\helprojs\city\project.heproj -Platform ps2 -Output C:\dev\helprojs\city\output
```

Expected: the build completes successfully and produces a fresh PS2 output under `C:\dev\helprojs\city\output`.

- [ ] **Step 5: Commit in logical slices**

Use at least these commit boundaries:

```bash
git commit -m "refactor: move binary writer ownership to helengine.files"
git commit -m "refactor: route built-in lights and ui components through generic persistence"
git commit -m "refactor: route generic physics components through automatic runtime payloads"
git commit -m "test: lock bespoke persistence boundary"
```

## Self-Review

### Spec Coverage

The plan covers:

- writer ownership restoration in Tasks 2 and 3
- light genericization in Task 4
- wider built-in genericization in Tasks 5 and 6
- the explicit keep-bespoke set in Task 7
- codegen and PS2 build verification in Task 8

No spec requirement is intentionally left unplanned.

### Placeholder Scan

Reviewed for `TODO`, `TBD`, "implement later", and "write tests" placeholders without concrete files or commands. None remain.

### Type Consistency

The plan consistently uses these names across tasks:

- `AutomaticScriptComponentPersistenceDescriptor`
- `AutomaticScriptComponentRuntimeDeserializer`
- `RuntimeComponentRegistry`
- `ComponentPersistenceRegistry`
- `MeshComponent`
- `CameraComponent`
- `SceneMapComponent`
- `StaticMeshCollider3DComponent`

No later task introduces a renamed replacement for those core units.
