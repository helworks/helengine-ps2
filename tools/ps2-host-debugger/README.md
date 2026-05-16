# PS2 Host Debugger Smoke Contract

Invocation:

```text
ps2-host-debugger --export-root C:\dev\helprojs\output\ps2-vu-colored-baseline --mode load-only
```

Expected:

- initializes the host-debug session against one packaged PS2 export root
- maps `cdrom0:\...` runtime paths into `export-root\disc\...`
- loads the packaged startup scene through the native asset serializer
- resolves top-level cooked runtime materials and models through the PS2 runtime objects
- exits `0` on success
