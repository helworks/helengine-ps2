## Title

Restore write/read serialization ownership between `helengine.files` and `helengine.core`, remove unnecessary bespoke built-in component persistence, and collapse generic-compatible components onto the shared reflected persistence system.

## Context

The current codebase has drifted away from its intended ownership model:

- `helengine.files` is supposed to own write-side persistence.
- `helengine.core` is supposed to own read-side/runtime reconstruction only.

That boundary is currently broken in several ways:

- `helengine.core` contains writer-side primitives such as `EngineBinaryWriter`, `BinaryWriterLE`, and `BinaryWriterBE`.
- `helengine.files` contains a second writer-side stack with the same conceptual responsibility.
- `helengine.core` contains mixed read/write payload serializers, especially for built-in scene components such as lights and other built-in runtime components.
- the updated C++ codegen now fails when both `helengine.EngineBinaryWriter` and `helengine.files.EngineBinaryWriter` appear in the same generated closure.

The PS2 build failure exposed the architectural problem, but the problem itself is not PS2-specific. The correct fix is to restore the project boundary instead of adding a codegen workaround.

The follow-up audit shows a second layer of architectural debt: built-in components that are already structurally compatible with the generic reflected persistence system still use hand-authored packaged payload rewrites and hand-authored runtime deserializers.

## Goals

- Make `helengine.files` the only owner of write-side binary persistence.
- Make `helengine.core` read-only with respect to binary payload handling.
- Remove bespoke light payload persistence and route light components through the generic component persistence/deserialization pipeline.
- Remove other bespoke built-in component persistence paths whose state is already representable by the generic reflected persistence system.
- Audit all explicit component persistence descriptors and payload serializers to identify which ones no longer need to exist because the generic reflected component system can handle them.
- Intentionally drop legacy support for old light payload formats.

## Non-Goals

- Preserve compatibility with old serialized light payload bytes.
- Introduce a third shared serialization project.
- Add name-remapping or codegen-only collision workarounds.
- Perform unrelated scene serialization refactors beyond the boundary cleanup and genericization audit.
- Expand the generic persistence system to support dictionaries, getter-only collections, abstract runtime objects, or non-constructible nested payload types unless that becomes necessary for a clearly justified component.

## Desired End State

### Ownership

`helengine.files` owns:

- `EngineBinaryWriter`
- `BinaryWriterLE`
- `BinaryWriterBE`
- write-oriented binary helper/extension methods
- editor/build/export-time component payload writers
- editor/build/export-time asset/package writers

`helengine.core` owns:

- `EngineBinaryReader`
- `BinaryReaderLE`
- `BinaryReaderBE`
- read-oriented binary helper/extension methods
- runtime payload readers/deserializers
- runtime-facing model types

`helengine.core` must not contain any class or method whose responsibility is to emit packaged binary payload bytes.

### Component persistence direction

Built-in scene components should use the same generic persistence model whenever their state is representable through the reflected component system. A bespoke component payload path should exist only when the generic path cannot correctly represent the component's data or lifecycle needs.

For light components specifically, the current state does not justify a bespoke path:

- the persisted state is ordinary scalar/vector/enum data
- the generic reflected component persistence path already supports those value types
- `LightType` is getter-only and should remain derived from the concrete component type instead of being serialized

Therefore lights should move to the generic path and the bespoke light payload serializer/deserializer path should be removed.

The same standard should be applied to all current built-in component persistence paths, not just lights.

## Persistence Audit Findings

### Generic-compatible built-in components

These built-in components are already structurally representable through the current generic reflected persistence system because their authored state is composed of writable public members with supported scalar, vector, enum, array, and supported asset-reference-backed types:

- `DirectionalLightComponent`
- `AmbientLightComponent`
- `PointLightComponent`
- `SpotLightComponent`
- `TextComponent`
- `SpriteComponent`
- `RoundedRectComponent`
- `FPSComponent`
- `DebugComponent`
- `RigidBody3DComponent`
- `BoxCollider3DComponent`
- `SphereCollider3DComponent`
- `CapsuleCollider3DComponent`
- `KinematicMotion3DComponent`
- `CharacterController3DComponent`

These components should not keep permanent bespoke packaged payload formats or permanent hand-authored runtime deserializers once the cleanup is complete.

### Components that remain legitimately bespoke with the current generic system

These components currently still justify explicit persistence logic because the generic system cannot represent them correctly without additional feature work:

- `MeshComponent`
  - `Materials` is getter-only and runtime reconstruction depends on `SetMaterials`, not direct reflected assignment.
- `SceneMapComponent`
  - `Mappings` is a getter-only `Dictionary<string, string>` and dictionaries are not currently supported by the generic reflected persistence system.
- `CameraComponent`
  - `CameraClearSettings` is a struct, `RenderTarget` is an abstract runtime object, and the packaged camera layer mask is normalized for runtime-specific constraints.
- `StaticMeshCollider3DComponent`
  - `CollisionData` is a read-only cooked object with no parameterless-constructor shape compatible with the current generic nested-object rules.

These bespoke paths may be revisited later, but they are not in the obvious "remove now" bucket.

### Candidate persistence files that should disappear after this cleanup

The audit identifies these files as likely removable once built-in components are routed through the generic system:

- `engine/helengine.core/scene/LightComponentScenePayloadSerializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeDirectionalLightComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeAmbientLightComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimePointLightComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeSpotLightComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeTextComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeSpriteComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeRoundedRectComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeFPSComponentDeserializer.cs`
- `engine/helengine.core/scene/runtime/RuntimeDebugComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeRigidBody3DComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeBoxCollider3DComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeSphereCollider3DComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeCapsuleCollider3DComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeKinematicMotion3DComponentDeserializer.cs`
- `engine/helengine.physics/RuntimeCharacterController3DComponentDeserializer.cs`

The audit also found that `engine/helengine.editor/serialization/scene/LightComponentTaggedFieldEncoding.cs` appears unused and should be validated for outright deletion as part of the implementation.

## Architectural Decisions

### 1. No shared writer capability in `helengine.core`

The design intentionally rejects a shared low-level serialization library. Even if technically cleaner than duplication, it would still expose write capability to `helengine.core`, which conflicts with the architectural rule the user wants restored.

### 2. Remove duplication by removing the wrong owner

The duplicate writer stack should not be reconciled by renaming one copy. The copy in `helengine.core` is architecturally invalid and should be removed or migrated out.

### 3. Prefer generic component persistence by default

The generic reflected component persistence system is now mature enough to handle common built-in components whose state is composed of supported member types. Explicit descriptors should remain only where they provide necessary behavior that generic reflection cannot provide.

### 4. Built-in runtime deserializers are not special by default

Hand-authored built-in runtime deserializers are not inherently preferable to generated or generic runtime deserializers. If a built-in component can be reconstructed from the same generic reflected runtime payload used by ordinary components, it should use that path.

### 5. No legacy light compatibility shim

Old bespoke light payload formats are intentionally unsupported after this cleanup. The runtime and editor should only read the generic light component representation going forward.

## Scope Breakdown

### A. Writer ownership cleanup

Work items:

- remove writer-side binary classes from `helengine.core`
- move any writer-only binary extension methods out of `helengine.core`
- ensure all writer-side call sites resolve through `helengine.files`
- keep reader-side binary classes and helpers in `helengine.core`

Expected outcome:

- the codegen closure sees only one `EngineBinaryWriter` family
- write ownership is unambiguous

### B. Mixed serializer cleanup

Work items:

- identify all `helengine.core` classes that both read and write payloads
- split them by ownership
- keep read-side logic in `helengine.core`
- move write-side logic into `helengine.files`, or remove it entirely if the component should use the generic reflected system

Expected outcome:

- `helengine.core` has no mixed read/write persistence utilities

### C. Light component genericization

Work items:

- remove bespoke light write path from `helengine.core`
- remove bespoke runtime light payload deserializers that depend on light-specific byte payload formats
- ensure editor scene save/load for light components goes through the generic reflected component persistence model
- ensure runtime generated component deserializers can materialize light components from the generic payload

Expected outcome:

- light components behave like ordinary components in persistence
- no light-specific payload serializer is needed

### D. Genericize other built-in components that no longer need bespoke runtime payloads

Work items:

- route generic-compatible built-in UI/runtime components through the generic reflected packaging/runtime path
- route generic-compatible built-in physics components through the generic reflected packaging/runtime path
- remove hand-authored packaged payload rewrites and hand-authored runtime deserializers for components whose state is already representable by the generic system

Expected outcome:

- generic-compatible built-ins no longer carry permanent hand-authored runtime payload formats
- the runtime registry becomes smaller and more uniform

### E. Persistence audit

Work items:

- audit explicit component persistence descriptors in the editor
- audit bespoke runtime component deserializers in `helengine.core`
- audit bespoke runtime component deserializers in `helengine.physics`
- audit bespoke scene payload serializers in `helengine.core`
- classify each explicit persistence file into one of:
  - required bespoke path
  - replaceable by generic reflected persistence now
  - dead/obsolete after cleanup

Expected outcome:

- a concrete list of persistence files that can be removed or collapsed into the generic system
- fewer permanent bespoke persistence types

This audit is part of the implementation scope, not an optional follow-up.

## File Families To Review

The implementation should explicitly track and classify at least these families:

- `helengine.core/serialization/*Writer*`
- `helengine.core/serialization/BinarySerializationExtensions.cs` for writer-only methods
- `helengine.core/scene/*PayloadSerializer*.cs`
- `helengine.core/scene/runtime/*Deserializer*.cs`
- `helengine.physics/*Runtime*Deserializer*.cs`
- `helengine.editor/serialization/scene/*PersistenceDescriptor*.cs`
- any built-in editor descriptor that exists only because the generic reflected component path did not exist or was weaker when it was introduced

This review should produce a written implementation-time inventory of:

- files removed
- files kept bespoke and why
- files migrated to generic persistence and why

## Migration Strategy

### Phase 1: restore low-level ownership

- remove `EngineBinaryWriter` and concrete writer types from `helengine.core`
- move or rewrite any writer-facing extension methods so `helengine.files` is the only writer-side owner
- update direct references accordingly

This phase should resolve the codegen name collision in principle, but the implementation should continue through the rest of the cleanup before claiming success.

### Phase 2: move or delete write-side component serializers in `helengine.core`

- find remaining `EngineBinaryWriter` references under `helengine.core`
- for each reference:
  - migrate it to `helengine.files` if it still represents a necessary bespoke write path
  - delete it if the target component should move to generic persistence

### Phase 3: genericize lights

- remove light-specific persistence descriptors or serializers if present
- ensure light components are accepted by the automatic/generic component persistence pipeline
- remove runtime light deserializers that only exist to interpret the bespoke light payload

### Phase 4: genericize other obvious built-in components

- move generic-compatible built-in UI/runtime components off bespoke packaged payloads
- move generic-compatible built-in physics components off bespoke packaged payloads
- shrink runtime component registration to keep only the truly bespoke built-ins

### Phase 5: persistence audit and collapse

- audit other explicit descriptors and runtime deserializers
- convert the obvious generic-compatible cases
- leave only the truly bespoke descriptors in place

### Phase 6: verification

- rerun direct `csharpcodegen` for `managed/helengine.ps2`
- rerun targeted persistence tests
- rerun the full PS2 city build

## Acceptance Criteria

The work is complete when all of the following are true:

- `helengine.core` no longer defines `EngineBinaryWriter`, `BinaryWriterLE`, or `BinaryWriterBE`
- `helengine.core` contains no writer-side binary serialization API surface
- light components no longer depend on bespoke binary payload serializers/deserializers
- the generic component system persists and reconstructs lights
- the generic component system also persists and reconstructs the other generic-compatible built-in components identified by the audit
- only `MeshComponent`, `CameraComponent`, `SceneMapComponent`, and `StaticMeshCollider3DComponent` remain bespoke unless the implementation explicitly expands generic-system capability enough to remove more
- the implementation produces an explicit audit of persistence files that were removed, retained, or genericized
- direct `csharpcodegen` for `managed/helengine.ps2` succeeds
- the full PS2 build for `C:\dev\helprojs\city\output` succeeds

## Risks

### Hidden write dependencies in `helengine.core`

There may be writer-side helpers or serializers outside the obvious binary writer classes. The implementation must search for all `EngineBinaryWriter` usage under `helengine.core` before removing types.

### Built-in descriptors with non-obvious editor behavior

Some explicit component descriptors may preserve editor-time behavior rather than raw data shape. The persistence audit should not blindly delete them without confirming that the generic path preserves the same authored state.

### Generic-system feature limits can turn false positives into churn

Some components look generic at first glance but still depend on unsupported shapes such as getter-only collections, structs, abstract runtime objects, or non-constructible nested payload types. The implementation should remove bespoke code only after confirming the generic system can fully round-trip the component.

### Runtime registration assumptions

The runtime component registry currently mixes generated generic deserializers with hand-authored built-in deserializers. Removing bespoke built-in deserializers requires checking registration behavior so built-in components continue to load through the generic/generated path.

## Testing Strategy

Minimum validation:

- targeted tests for binary reader/writer ownership after the move
- targeted tests for editor scene save/load of light components through the generic system
- targeted tests for runtime scene loading of light components through generic deserializers
- targeted tests for at least one UI built-in and one physics built-in that move from bespoke to generic runtime payloads
- targeted tests that the remaining bespoke components are still intentionally routed through their explicit paths
- direct repro of the `csharpcodegen` command that currently fails on `managed/helengine.ps2`
- full PS2 city build to `C:\dev\helprojs\city\output`

The implementation should run the smallest validation set that proves each step, but it must finish with both the direct codegen repro and the end-to-end PS2 build.

## Recommendation

Implement the cleanup as a single architectural slice with these priorities:

1. restore `files`/`core` serialization ownership
2. remove bespoke light persistence entirely
3. remove other obviously unnecessary bespoke built-in persistence while already in the same area
4. keep only the bespoke paths that are justified by current generic-system limits

This avoids doing a temporary collision fix that would immediately be invalidated by the intended architecture.
