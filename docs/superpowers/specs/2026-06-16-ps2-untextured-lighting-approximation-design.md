# PS2 Untextured Lighting Approximation Design

**Goal**

Reduce the PS2 untextured VU path cost in `Colored Cubes` by replacing the expensive per-triangle specular calculation with a cheaper approximation, while preserving the current diffuse-lit look.

**Current Evidence**

- Runtime baseline in PCSX2 is stable again.
- `Colored Cubes` currently reports approximately:
  - `Upd 30`
  - `Rdr 30`
  - `Set 18.0`
  - `Prep 2.6`
  - `Emit 12.9`
  - `Enc 0.3`
  - `Tpl 9.9`
  - `Wt 2.3`
  - `Hit 192`
- This isolates the main hotspot to the lighting work inside the untextured VU emit path, not packet assembly.

**Root Cause**

The shared PS2 untextured VU path still resolves flat triangle color per triangle in `Ps2VuVifPacketBuilder.cpp`. The expensive part is the specular term in `ResolveTexturedVertexColor(const Ps2VuLightingConstants&, ...)`, which currently uses `std::pow(ndotl, specularPower)` inside the hot loop.

**Approved Approach**

Keep the current diffuse lighting behavior and material color response, but replace the per-triangle specular calculation with a cheaper approximation in the PS2 untextured VU path only.

This approximation must:

- preserve the general lit appearance
- avoid moving heavy work into `Set`
- avoid introducing new runtime layout fields in hot classes
- avoid changing the VU packet contract
- remain isolated to PS2 untextured opaque lighting

**Non-Goals**

- no broad material-system rewrite
- no generated-core edits
- no change to textured PS2 lighting
- no attempt to perfectly match the old specular curve

**Expected Visual Impact**

- diffuse shading should stay effectively the same
- specular highlights may become broader, softer, or less sharp
- matte surfaces should change little
- simple scenes such as `Colored Cubes` should remain visually acceptable

**Implementation Boundary**

Primary file:

- `src/platform/ps2/rendering/vu/Ps2VuVifPacketBuilder.cpp`

Likely test file:

- `builder.tests/Ps2VuUntexturedPathSourceTests.cs`

**Verification**

Success means all of the following are true:

- the hot per-triangle resolver no longer uses the expensive specular power path
- `Colored Cubes` still renders correctly in PCSX2
- `Tpl` drops materially from the current `~9.9 ms`
- overall runtime does not regress through `Set`
- no new menu boot/runtime instability is introduced

**Risk**

The main risk is trading one hotspot for another, as happened with the rejected lookup-table experiment that moved heavy work into `Set`. Any new approximation must stay inside the hot loop and remain cheaper overall than the current `pow` path.
