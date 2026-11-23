# Experimental JIT / Harmony backend (Dopamine + StikDebug)

This branch exists to investigate a **dynamic Harmony backend**, independently of
`feature/steam-workshop-browser`. It is **not** currently a JIT-enabled
launcher. The existing static weaver remains the only mod preparation backend.

## Target hardware

11-inch iPad Pro 1st generation (2018, A12X) or 2nd generation (2020,
A12Z), arm64/arm64e hardware, running iPadOS 17 and Dopamine. The user also
has StikDebug. The exact iPad model and iPadOS patch version are not yet
known, and no runtime behavior on that iPad has been verified.

## Current diagnostic

The Mods screen contains an **Experimental: check JIT** button. It
explicitly asks the running .NET runtime whether it supports compiled
dynamic code and, only if both runtime feature flags are true, attempts to
create and invoke a tiny `DynamicMethod` returning 42. Results are displayed
and written to the Godot log. No JIT probe runs during ordinary startup.

**A passed probe is necessary but not sufficient** for dynamic mods:
it does not test native writable/executable pages, MonoMod detours,
Harmony patch generation, Godot initialization, AOT/JIT interoperability,
or Steam/desktop APIs. Debugger or jailbreak presence by itself is **not**
proof that the packaged Mono runtime is JIT-capable. A failed probe does
not disable Workshop browsing or existing static mod preparation.

## Runtime facts and required engineering

- `tools/mono/build_mono.py` currently sets `UseMonoRuntime=true` and
  `RunAOTCompilation=true` for packaged libraries. The game host and
  game assembly use an interpreter; enabling debugger JIT entitlement
  cannot add a JIT compiler that is absent from the build.
- The bundled Mono runtime and its architecture must be audited, then a
  separate signed iOS runtime variant must be built with JIT-capable Mono,
  using permitted runtime executable memory on the actual device.
- Next verify reflection-emit and Harmony/MonoMod dynamic-method detouring
  against a self-contained dummy assembly; **do not** begin by patching game
  internals or loading arbitrary user mods.
- Only after that succeeds should a dynamic mod loader be integrated,
  honouring manifest dependencies and loading the selected game assembly
  before the original `ModManager` runs. The current
  `BuiltInMods.AssemblyFileExists` rejects code not precompiled in the
  bundle: this path must change *only* in the verified dynamic backend.
- Android's modified Mono and Harmony compatibility work is a reference,
  not a drop-in iOS binary. Its Android-native libraries cannot be copied
  into an iOS IPA.
- Keep the original JIT-free backend working. If the dynamic backend is
  unavailable, do **not** silently treat every unmodified desktop mod as
  compatible.

## CI and versioning

This experimental branch is **not** included in the Workshop branch's
automatic full IPA push trigger. Run the IPA workflow explicitly with
`feature/jit-harmony-experimental` if you want to test this diagnostic
on-device. Reuse the existing bundle identifier to preserve the
installation's data and increment the iOS build number when packaging
an actual replacement IPA.

## Existing CI issue inherited from Workshop branch

The source branch's Build IPA #23 failed its new BaseLib P/Invoke
regression test during `ModWeaver.Tests`: the narrow native-import
rewriter did not accept the test fixture. That issue is independent of
the JIT diagnostic and needs to be resolved before claiming this branch
passes its full test suite.
