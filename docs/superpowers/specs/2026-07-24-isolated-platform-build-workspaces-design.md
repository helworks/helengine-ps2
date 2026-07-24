# Isolated Platform Build Workspaces

## Goal

Every platform build executes in its own writable workspace. Concurrent builds must never share generated code, project-script intermediates, native artifacts, staging folders, package outputs, or temporary files.

## Scope

The common editor build-graph workspace layer owns workspace allocation. Each platform builder consumes the allocated paths and must not write build artifacts into a repository root or a shared temporary location.

The final user-requested output directory remains the export destination. It is not reused as an intermediate workspace.

## Workspace Contract

For each `(build request, platform)` pair, the build graph allocates one unique workspace root beneath the build request working root. The root contains these writable areas:

- `generated-dotnet`: generated project scripts and their `obj` and `bin` outputs.
- `generated-core`: generated native source consumed by platform-native compilers.
- `platform`: platform-private native build artifacts, staging data, and package scratch files.
- `logs`: per-build process output and diagnostics.

The workspace identity is unique for every invocation, including two simultaneous builds of the same project and platform. A platform builder receives the workspace identity and resolved paths through the existing platform build request/workspace model rather than constructing paths from a repository root.

## Data Flow

1. The editor build graph creates a unique workspace for each selected platform.
2. Code generation and project-script compilation write only below that workspace.
3. The platform builder receives the generated roots plus a platform-private artifact root.
4. Native compilation and packaging write only below the platform-private artifact root.
5. Packaging copies the finished artifact to the requested output root.
6. Cleanup removes only the unique workspace after the build lifecycle completes; it never removes a requested output root.

## PS2 Adoption

The PS2 builder stops using `helengine-ps2\\build` and `ps2-staging` under the request working root as writable shared locations. Its native executable, Docker-mounted native workspace, staging folder, disc layout, and ISO scratch output are derived from the platform-private artifact root.

The PS2 Docker invocation mounts that unique native workspace instead of the repository root for writable compiler output. The repository source remains read-only input to the workspace preparation step.

## Failure Handling

Build failures retain the workspace and logs for diagnostics. A failed build never deletes another build's workspace or final output. File-lock errors are reported with the isolated path that was locked.

## Validation

Tests must verify:

- two workspace allocations for the same project/platform have different writable roots;
- two platform allocations from one request have different writable roots;
- generated project-script intermediate paths are descendants of their workspace;
- PS2 native executable and staging paths are descendants of its platform-private artifact root;
- parallel platform builds can write identical artifact names without cross-build path collisions;
- cleanup is scoped to the workspace root and never the final output root.

## Compatibility

Platform output layouts and artifact names remain unchanged. Existing callers keep providing the requested output root; the build graph supplies the new isolated intermediate roots internally.
