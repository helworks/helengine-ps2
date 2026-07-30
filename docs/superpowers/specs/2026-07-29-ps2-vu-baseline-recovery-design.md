# PS2 VU Baseline Recovery Design

## Goal

Restore the complete compile contract for committed renderer snapshot `29acaa0` without changing its VIF/VU packet behavior.

## Design

`Ps2VuVifPacketBuilder.cpp` already owns the recovered packet-cache behavior. Its paired header must declare the same textured VU method signature and the two private members it uses: the direct GIF word staging vector and `Ps2VuTexturedPacketCache`.

The current boot overlay calls newer telemetry APIs. Small zero-valued compatibility getters and the VIF-drain setter remain in `Ps2RenderManager3D`; they do not affect packet construction or submission.

## Validation

Add a source-contract test for the builder/header pairing, verify it fails before the declaration repair and passes afterward, then run a full PS2 package build. The ISO must identify itself as B238.
