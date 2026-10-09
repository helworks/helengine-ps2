# Native PS2 GS texture formats

Per-platform texture and font-atlas settings accept `PS2.PSM*` identifiers.
The catalog covers all thirteen GS pixel-storage modes, with three palette
storage choices for each indexed mode. There are 23 native selections.

| Identifier | GS code | Host transfer bits | GS storage bits |
| --- | --- | --- | --- |
| `PS2.PSMCT32` | `0x00` | 32 | 32 |
| `PS2.PSMCT24` | `0x01` | 24 | 32 |
| `PS2.PSMCT16` | `0x02` | 16 | 16 |
| `PS2.PSMCT16S` | `0x0A` | 16 | 16 |
| `PS2.PSMT8` | `0x13` | 8 | 8 |
| `PS2.PSMT4` | `0x14` | 4 | 4 |
| `PS2.PSMT8H` | `0x1B` | 8 | 32 |
| `PS2.PSMT4HL` | `0x24` | 4 | 32 |
| `PS2.PSMT4HH` | `0x2C` | 4 | 32 |
| `PS2.PSMZ32` | `0x30` | 32 | 32 |
| `PS2.PSMZ24` | `0x31` | 24 | 32 |
| `PS2.PSMZ16` | `0x32` | 16 | 16 |
| `PS2.PSMZ16S` | `0x3A` | 16 | 16 |

The five indexed identifiers default to a CT32 palette. Their `.CLUT16` and
`.CLUT16S` variants select the corresponding native palette storage. Both
four-bit and eight-bit indices use a full 16-entry or 256-entry palette.
Palette entries are reordered for CSM1 before transfer.

Existing `Rgba32`, `Indexed8` and `Indexed4` settings retain their meanings.
Default textures remain RGBA32/A8 with a 512-pixel resolution cap; font atlases
remain Indexed8/A8 with the existing quantizer and the same resolution cap.
An explicit zero cap skips resizing. Resulting native dimensions must fit
within 1024 pixels per axis.

## Asset and transfer contract

The existing `Ps2TextureAsset` and little-endian HELE format 2/version 2 remain
the runtime contract. Existing logical format and pixel-storage identifiers
0/1/2 continue to mean CT32/T8/T4. Additional modes append identifiers 3-12;
these logical identifiers are distinct from the hardware GS codes above.
No extra native texture header is inserted into the pixel payload.

`PixelData` contains compact, row-major host-transfer texels. CT24 and Z24 use
three bytes per texel. High-bit indexed modes use compact eight-bit or four-bit
indices; the GS places those indices into the selected bits of its 32-bit
storage words. Four-bit transfers put the first pixel in the low nibble and
continue across row boundaries. Runtime staging zero-pads the final DMA
qword, while serialized lengths describe only the actual pixel payload.

The native upload selects the requested DPSM. The GS performs the native
memory mapping, including the different 16S and Z layouts. Z textures store
raw packed bits and sample those bits through the GS color interpretation;
the cooker does not substitute a normalized luminance depth image.

## Alpha and allocation

RGBA32 and CT32 palettes convert source alpha from 0-255 to the GS blending
range 0-128. CT24/Z24 are opaque. CT16/16S and their depth or palette variants
support opaque or binary alpha. Binding configures TEXA for the selected
storage and alpha policy, including the appropriate TA0/TA1 values.

Texture allocations use complete 8192-byte GS pages and page-aligned bases.
CT32/24, Z32/24 and high-bit indexed modes use 64x32 pages; CT16/16S and
Z16/16S use 64x64 pages; T8 uses 128x64 pages; T4 uses 128x128 pages.
An indexed texture also reserves a separate page for its palette. The editor
uses the same allocation contract for VRAM estimates, rather than the smaller
host-transfer payload size.

The 2D runtime can update RGBA32 texture regions from a source buffer with an
explicit row stride. It patches the retained CT32 pixels and reuploads to the
same VRAM allocation. Cooked textures retain their selected alpha policy;
anonymous runtime font textures retain their original coverage bytes.
Region updates reject other native storage modes instead of treating their
packed or indexed data as RGBA32.

## Reference and validation scope

Hardware codes are defined by
[PS2SDK gs_psm.h](https://ps2dev.github.io/ps2sdk/gs__psm_8h_source.html).
The GS transfer and storage interpretations are checked against the local
PCSX2 reference in `C:/dev/helworks/reference/ps2/pcsx2`, especially
`GSRegs.h`, `GSLocalMemory.h`, `GSLocalMemory.cpp`, `GSTables.cpp` and
`GSClut.cpp`. The existing SDK behavior is documented by
[gsKit gsTexture.c](https://github.com/ps2dev/gsKit/blob/master/ee/gs/src/gsTexture.c).

Byte-layout tests, native parser/upload checks and EE cross compilation do not
establish visual correctness in PCSX2 or on hardware. Test artifacts remain in
`C:/dev/helworks/builds/helengine-ps2/texture-formats`.
