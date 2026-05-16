# Host Debug Build Mode Design

## Summary

This document defines a first-class cross-platform `host-debug` build mode for helengine. The goal is to run packaged platform runtimes on Windows or Linux with real native debugging tools while preserving the platform's generated code, cooked asset contracts, and runtime object flow.

PS2 is the first implementation target, but the design is intentionally generic. This is not a PS2-only workaround, and it is not a fake renderer path. It is a host execution mode for native platform runtimes that are difficult to debug directly on-device or in emulators.

## Problem

Several native platforms are expensive to debug through their real device runtime:

- emulators provide weak stack traces and poor memory diagnostics
- on-device debugging is slow or unavailable
- native contract bugs often require excessive ad-hoc logging
- runtime corruption and allocation faults are difficult to isolate without host tooling

Recent PS2 debugging exposed this clearly:

- runtime scene loading and cooked material resolution faults were difficult to diagnose in PCSX2
- generated-core and platform-runtime contract mismatches were expensive to validate
- apparent feature failures could actually be bad dispatch or malformed packaged data

The engine needs a host-side debug execution mode that reuses packaged runtime artifacts rather than approximating them.

## Goals

- Add a real `host-debug` build mode to the editor build pipeline.
- Preserve packaged platform runtime behavior as closely as possible.
- Reuse the real generated-core output for the selected platform build.
- Reuse the real cooked/exported assets for the selected platform build.
- Keep platform-specific runtime code intact wherever host execution does not require replacement.
- Support real debugger workflows on Windows and Linux.
- Support stronger runtime diagnostics later, including ASan and UBSan on Linux.
- Make the feature generic enough for multiple native platforms.

## Non-Goals

- Do not replace platform runtime code with a generic fake renderer.
- Do not require the host debug path to match target hardware performance.
- Do not attempt to simulate low-level hardware behavior perfectly.
- Do not redesign platform cooking or generated-core production for this feature.
- Do not require all platforms to support host-debug immediately.

## Design Principles

### Platform fidelity over convenience

The host debug build should execute the same generated-core, packaged assets, cooked runtime contracts, and runtime object flow as the real platform build. Only the hardware boundary should be swapped.

### Generic editor feature, platform-specific adapters

The build mode belongs to the editor and build graph, not to one platform repository. Platforms should opt into the mode by providing host-debug adapters and metadata.

### Reuse packaged outputs

The host debug runner should consume the real packaged/exported output tree from the selected platform build, not authored editor assets.

### Fail loudly

If a platform cannot express a required host-debug contract, the build should fail clearly. This feature should not rely on best-effort approximations or silent fallback behavior.

## User Experience

The editor/build pipeline should expose `host-debug` as a build/run option for platforms that support it.

Examples:

- `Build`
- `Build And Run`
- `Host Debug Build`
- `Host Debug Run`

For PS2, `Host Debug Run` should:

1. run the normal PS2 export/build path
2. generate the real PS2 generated-core output
3. package the real PS2 cooked assets/disc tree
4. build the PS2 host-debug runner
5. launch the runner against the packaged output

## Architecture

### Build mode

Add a new editor/build mode:

- `runtime`
- `host-debug`

`runtime` means the existing platform-native output.

`host-debug` means:

- generate the same platform runtime artifacts
- produce or reuse the same packaged outputs
- run them through a host adapter layer instead of the target hardware runtime boundary

### Shared ownership

#### helengine.editor

Owns:

- build-mode selection
- build-graph integration
- host-debug workspace creation
- runner invocation
- host-debug capability discovery

#### platform builder

Owns:

- whether the platform supports `host-debug`
- host-debug runner metadata
- platform-specific host-debug capability declarations
- any extra packaged outputs needed by the host runner

#### host-debug runtime infrastructure

Owns:

- packaged runtime mount abstractions
- host file-system access for packaged roots
- shared logging/diagnostic behavior
- shared runner bootstrap conventions

#### platform host-debug adapter

Owns:

- platform path semantics
- platform runtime class reuse
- hardware-boundary replacements
- platform-specific validation logic

## Shared Contracts

The shared contract should stay small and semantic. Suggested concepts:

### Platform host-debug capability

Describes whether a platform supports host-debug and what kind of runner it requires.

Suggested fields:

- `SupportsHostDebug`
- `HostDebugRunnerKind`
- `RequiresPackagedExportArtifacts`
- `SupportsSingleStepSceneLoad`
- `SupportsSingleStepDraw`

### Packaged runtime mount

Maps packaged platform paths to host files without changing packaged runtime semantics.

Examples:

- PS2 `cdrom0:\...`
- other platforms may map rooted asset paths differently

### Host hardware boundary adapter

Provides the minimum replacement layer for:

- file access
- input
- renderer device submission boundaries
- optional platform host services

The adapter should avoid rewriting higher-level runtime logic.

## PS2 First Implementation

PS2 should be the first platform adapter because it has immediate debugging pain and strong value from host tooling.

### PS2 host-debug runner scope

Initial PS2 host-debug runs should preserve:

- generated-core from the real export
- packaged startup scene
- packaged cooked materials and models
- PS2 runtime material/model classes
- PS2 runtime asset path behavior
- PS2 renderer-side scene extraction and planning where possible

Initial PS2 host-debug runs should replace:

- `cdrom0:` file access with host file mapping
- `gsKit`
- DMA/VIF/GIF submission
- pad input
- PS2 boot-loop console plumbing

### PS2 first milestone

The first PS2 host-debug runner only needs to:

1. initialize core
2. install host render/input shims
3. load the packaged startup scene
4. resolve cooked runtime assets
5. build runtime materials and models
6. exit

This is enough to debug startup scene load, cooked material resolution, and packaged contract faults.

### PS2 second milestone

Add one-shot frame execution:

- run one `Draw()` pass
- allow proxy rebuild and frame planning
- stop before actual PS2 hardware submission

This enables debugging many renderer contract failures without requiring emulator-only logging.

## Generic Runner Model

The long-term shape should be a host runner abstraction rather than platform-specific one-off executables.

Suggested conceptual structure:

- shared host-debug runner bootstrap
- shared packaged runtime mount layer
- platform-specific runtime adapter module
- platform-specific runner executable or target

The runner should accept:

- packaged output root
- generated-core root or manifest path
- startup scene path
- optional mode flags such as `--load-only` or `--draw-once`

## Integration With Existing Exports

Host-debug should build on top of the normal platform export flow instead of bypassing it.

Required invariant:

- if a runtime bug reproduces on device because of packaged data, generated-core shape, or cooked runtime contracts, it should also reproduce in host-debug unless the issue depends on the low-level hardware boundary itself

This keeps host-debug relevant for real runtime failures.

## Debugging Benefits

Host-debug should unlock:

- real call stacks
- debugger breakpoints
- watch expressions
- native object inspection
- validation around string lengths, object layout, and vtables
- host memory tooling
- ASan and UBSan on Linux later

For PS2 specifically, this would dramatically reduce reliance on emulator-only console logging for runtime contract issues.

## Risks

### False confidence from host-only behavior

Some issues will still depend on the real hardware boundary. Host-debug must be treated as a high-fidelity runtime validation mode, not a full replacement for device or emulator verification.

### Over-generalized abstraction

If the shared contract becomes too broad or too renderer-specific, it will become hard to maintain across platforms. Keep the shared surface small and semantic.

### Platform adapter drift

If host-debug adapters diverge from real platform runtime code, the feature loses value. Shared runtime code should be reused wherever possible.

## Rollout Plan

### Phase 1

- add generic `host-debug` build-mode model to the editor/build graph
- add host-debug capability metadata to platform definitions/builders
- implement PS2 packaged runtime mount and host runner bootstrap
- support `load-only` startup-scene execution

### Phase 2

- support PS2 one-shot draw execution without hardware submission
- add validation and diagnostics around packaged runtime contracts
- add editor workflow support for `Host Debug Build` and `Host Debug Run`

### Phase 3

- extract reusable shared host-debug infrastructure
- onboard additional native platforms that benefit from the same model
- add Linux sanitizer workflows where practical

## Success Criteria

The design is successful when:

- PS2 runtime startup issues can be reproduced under a desktop debugger
- the runner uses the real packaged export and generated-core artifacts
- the host-debug feature is integrated into the editor build pipeline
- platform implementations inherit a shared host-debug build model instead of inventing custom tools
- the feature remains semantically generic and not PS2-branded in the editor/build architecture
