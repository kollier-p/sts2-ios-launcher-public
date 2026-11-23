# Experimental native Mono JIT backend

This directory vendors a **prototype**, not a working backend of STS2 Mobile.

The three upstream files below are copied from
TheUnknownThing/StS2-Launcher-iOS (main branch, inspected 2026-11-23):

- `upstream-mono-ios-jit.patch` — pinned Mono **9.0.20** runtime patch.
- `MonoJitMemory.h` and `MonoJitMemory.mm` — iOS RX/RW code aliasing.

Original project: https://github.com/TheUnknownThing/StS2-Launcher-iOS
Original MIT licence: `LICENSE.upstream.txt`.

Copyright (c) 2026 Eky. Used under the MIT licence.

**Not yet integrated with the IPA.** Our app uses the **.NET 10.0.12**
Mono iOS runtime. The upstream patch and source checksums target .NET
**9.0.20**, and those runtimes are not binary-interchangeable. We must
port the code and executable-memory integration to Mono 10, build and
link a matching iOS runtime, preserve our Godot/GameHost integration,
and test Harmony/MonoMod on device before enabling dynamic Workshop mods.

Do not copy the Mono 9 archive over the Mono 10 archive. Do not disable
native-runtime SHA checks or change AOT mode without verifying that the
archive actually has the ARM64 code generator. A passing GitHub IPA build
alone cannot prove JIT support on a Dopamine device.

The `tools/mono/audit_jit_runtime.py` CI diagnostic inspects the
currently packaged Mono 10 archive for JIT compiler symbols. It is a
first evidence-gathering step, not a capability declaration.
