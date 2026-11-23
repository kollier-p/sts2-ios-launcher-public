# Native JIT source import — Stage 2 (in progress)

The files in this directory are copied from
[TheUnknownThing/StS2-Launcher-iOS](https://github.com/TheUnknownThing/StS2-Launcher-iOS/tree/main/ios/jit)
under its MIT license. The upstream project preserves original launcher attribution
and further dependency notices in its `LICENSE` and `THIRD_PARTY_LICENSES.md`.
No game binaries or third-party runtime binaries are included here.

## Imported sources

- `MonoJitMemory.h`: API for registering and translating executable code regions.
- `MonoJitMemory.mm`: RX/RW alias registry used by the patched Mono JIT.
- `RuntimeHost.mm`: Godot managed-entry bridge, executable-memory probe,
  Mono initialization and managed entrypoint dispatch.

These files are **not linked into the existing launcher**. In particular,
`RuntimeHost.mm` depends on upstream's patched Mono 9.0.20 libraries and
headers and the custom runtime pack. The existing launcher links a different
Mono 10.0.12 library-builder host. Linking both together is unsafe.

## Next build tasks

1. Vendor/pin the upstream Mono 9.0.20 source patch set and build scripts,
   retaining checksum guards and license notices.
2. Add a separate macOS/iOS JIT framework build target and its dependency
   graph; do not replace the existing `Build IPA` workflow yet.
3. Export Godot shell with compatible managed ABI and link the native
   bridge to the pinned Mono runtime.
4. Stage BCL and managed game assemblies from the existing on-device
   Steam installer, rather than assuming game content exists on CI runner.
5. Wire the JIT startup gate to the launcher's game-ready state and test
   A12X/iPadOS 17.0.x JIT activation on device.

## Current verification

Only source retrieval and GitHub commits have been performed. No native
compilation or IPA test has succeeded for these imported sources.
