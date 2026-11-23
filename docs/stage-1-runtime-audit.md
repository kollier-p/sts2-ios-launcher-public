# Stage 1 — Mono JIT / Harmony integration audit

Audit date: 2026-11-23. This is a **source-level audit**, not a successful port or device test.

## Repositories and source evidence

- **Base:** `kollier-p/sts2-ios-launcher-public`, branch `feature/mono-jit-harmony`, derived from `feature/steam-workshop-browser`.
- **Runtime reference:** `TheUnknownThing/StS2-Launcher-iOS`, main branch.
- Base source examined: `README.md`, `.github/workflows/build-ipa.yml`, `setup.sh`, `scripts/build-mono.sh`, `scripts/test.sh`, `tools/setup_ios.py`, `tools/mono/build_mono.py`, `tools/mono/mono_library.py`, `tools/dependencies.json`.
- Reference source examined: `docs/architecture.md`, `docs/jit.md`, `docs/mods.md`, `ios/project.godot`, `ios/jit/RuntimeHost.mm`, `scripts/ios/build.py`, `src/STS2Jit/Entry.cs`, `src/STS2Jit/STS2Jit.csproj`, `src/STS2JitPrepare/Program.cs`, `LICENSE`, `THIRD_PARTY_LICENSES.md`.

## Component comparison

| Concern | Existing iOS launcher | JIT reference | Required integration |
| --- | --- | --- | --- |
| Runtime | iOS Mono **10.0.12** library-builder framework with AOT/interpreter mode and game-assembly mounting | Patched Mono **9.0.20** JIT, native `RuntimeHost.mm` and executable RW/RX aliases | **Replace or isolate** managed runtime initialization. Cannot simply link two competing Mono hosts. |
| .NET build | SDK **10.0.401**, pinned Xcode **27.2**, GodotSharp **4.5.1** | SDK **9.0.318**, Godot **4.5.1**, tested Xcode **26.3** | Pin a consistent runtime/BCL and build toolchain; check managed assembly compatibility. |
| Game input | Steam depot download **on device**, with game version **0.113.0** pinned and 0.111.0 previous profile | Prepares macOS ARM64 Steam game on Mac; tested game **0.111.0** | Build a **versioned on-device content adapter**. Do not assume upstream 0.111 hooks match 0.113. |
| Assembly strategy | Generated/patched game host with Mono library builder and app sandbox game files | Original game/mod assemblies loaded by JIT; locally adapts pinned CoreLib and Harmony | Preserve game and mod IL where patchable; move JIT-specific dependency transforms to verified copies. |
| Mod discovery | Workshop downloader and existing ModApi/GameAdapters components; exact output path still requires tracing | `Entry.cs` patches `ModManager.Initialize` and `ReadModsInDirRecursive` to scan `OS.GetUserDataDir()/mods` | Map Workshop install root into stock loader with correct dependency handling. |
| Harmony | Not established in current iOS AOT/interpreter runtime | `STS2JitPrepare` adapts Harmony 2.4.2.0; `STS2JitSupport` and native `sts2_jit_patch` support JIT region writes | Port native writable alias support and verified dependency adapter, with patch smoke tests. |
| Game content | On-device processing and prepared PCKs | `pck_patch_sentry` + `pck_add_cs`; Godot launches `--main-pack user://StS2.pck` | Decide whether to retain current mount or adopt upstream main-pack handoff; don't combine both blindly. |
| Cloud saves | Existing explicit Steam Cloud sync | Download/import; no upload or automatic sync | Retain existing sync behavior and modded-save isolation. |
| Build | `Build IPA` workflow runs on `xcode-27` and invokes `scripts/build-mono.sh --unsigned` | `scripts/ios/build.py` exports JIT Godot shell and links custom native framework | Introduce a **separate JIT build target** initially; don't silently relabel baseline IPA as JIT-capable. |

## Proposed integration contracts (to implement in Stage 2)

**Runtime content contract (version 1):**
- `game_root`: existing sandbox-installed game content; stable across launcher upgrades.
- `assemblies_root`: unchanged, user-owned game managed assemblies.
- `pack_path`: validated game PCK path.
- `native_root`: packaged licensed iOS-native dependencies.
- `mods_root`: Workshop-managed folder tree, with manifests, DLLs, PCKs and dependencies intact.
- `save_root`: existing launcher save root; preserve vanilla/modded separation.
- `game_version` and `assembly_sha256`: validation inputs for hook/adapter compatibility.
- Runtime host must not read Steam credentials; launch handoff contains paths only.

**Startup sequence:** existing launcher UI -> Steam-owned content validation -> JIT permission gate -> native Mono JIT initialization -> managed game assembly load -> Harmony mobile hooks -> stock mod-loader redirection -> game startup.

**Failure policy:** refuse JIT game launch on unknown assembly hashes, missing content, missing JIT permission, failed native runtime initialization or failed mandatory patches; keep launcher and saves usable. Log diagnostics without Steam tokens.

## Stage 1 acceptance checklist

- [x] Identify and compare build/runtime versions.
- [x] Locate existing build, test and CI entry points.
- [x] Locate upstream native runtime, Harmony adapter and mod redirection implementation.
- [x] Identify version mismatch (0.113.0 vs upstream 0.111.0).
- [x] Define runtime content and failure-handling interfaces.
- [x] Identify licensing boundaries and physical-device test limitations.
- [ ] **Stage 2:** implement and compile a JIT native runtime build target.
- [ ] **Stage 3:** validate Harmony patches on device.

## Licensing and safety

Preserve MIT notices from imported source and relevant third-party licenses. Do not commit game assemblies, assets, Steam tokens, saves, FMOD/Spine proprietary binaries or signing credentials. A GitHub-hosted macOS build cannot validate Dopamine-specific JIT behavior on A12X/iPadOS 17.0.x.

## Key design decision

The base's Mono 10 library-builder and upstream's patched Mono 9 JIT runtime are **not interchangeable**. Stage 2 must implement a separate, explicit JIT target with its own pinned runtime and host ABI before switching the production IPA workflow. Avoid mixing Mono 9 runtime binaries with Mono 10 BCL/framework outputs without a proven compatibility layer.
