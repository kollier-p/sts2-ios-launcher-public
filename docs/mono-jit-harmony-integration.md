# Mono JIT / Harmony integration (A12X, iPadOS 17.0.x)

Status: integration planning and compatibility gate; **not a working JIT port yet**.

## Baseline and provenance

- Base: `feature/steam-workshop-browser` (Steam login, on-device game download/processing, Workshop browser/downloader, cloud saves).
- Runtime reference: https://github.com/TheUnknownThing/StS2-Launcher-iOS (MIT-licensed porting code; preserve upstream attribution and third-party notices).
- Target: 2018 iPad Pro 11-inch, A12X, iPadOS 17.0.x, Dopamine rootless jailbreak.
- Keep the existing app bundle identifier and sandbox paths stable to avoid losing saves and downloaded game files.

## Non-negotiable integration boundaries

1. **Steam/game preparation** remains in the existing launcher. Do not bundle proprietary game content, Steam credentials, FMOD/Spine artifacts without license review, or user saves in CI artifacts.
2. **Runtime host** is adapted from upstream's `ios/` and `scripts/ios/build.py`, but must accept the output of the existing on-device content processor instead of assuming the game was prepared on a Mac.
3. **Managed assemblies** must remain compatible with Harmony patch targets and dynamic method generation. Do not claim compatibility based only on assembly loading.
4. **Mod installation** uses the Workshop downloader's existing manifests, dependencies, DLLs and PCKs; feed these into the game's managed loader. Preserve per-mod opt-in and modded save isolation.
5. **JIT** must be verified *inside the app process* on A12X/iPadOS 17.0.x. Dopamine jailbreak presence does not itself prove executable-memory entitlements or Mono JIT operation.
6. **Cloud saves**: preserve existing explicit sync/backup semantics and avoid importing incompatible modded profiles automatically.

## Integration sequence

- [ ] Inventory both repositories' Godot host versions, native plugin ABI, Mono versions, deployment targets and content paths.
- [ ] Define a versioned runtime manifest with game content root, managed assemblies root, native libraries root, mod root and save root.
- [ ] Add runtime startup diagnostics (device, OS, Mono version, JIT probe, assembly loading, patch result), without logging Steam tokens.
- [ ] Port upstream native Mono host and Godot glue as a dedicated module with retained license notices.
- [ ] Adapt on-device game processing to generate the content layout expected by the JIT host.
- [ ] Connect Workshop-installed mods to the managed loader, including dependency ordering and error reporting.
- [ ] Add a minimal test mod that exercises Harmony prefix, postfix and transpiler behavior.
- [ ] Configure macOS GitHub Actions to build the runtime and unsigned IPA; upload logs and IPA only after license/secret checks.
- [ ] Device smoke-test on A12X iPadOS 17.0.x; verify startup, gameplay, Workshop mod installation, Harmony patch execution, save/load and cloud sync.

## Acceptance criteria

1. Existing Steam login downloads and processes a legitimately owned copy on device.
2. Game starts on the target A12X device with JIT active.
3. A known Harmony patch modifies an observed in-game method, not merely loads a DLL.
4. Workshop browsing/downloading and mod dependencies continue to work.
5. Vanilla and modded saves remain separated, and cloud sync remains user-controlled.
6. GitHub Actions publishes an installable unsigned IPA without embedding proprietary game files.

## Risks

Upstream reports testing on M4/iPadOS 18.1, **not A12X/iPadOS 17.0.x**. Godot/Mono native ABI, JIT entitlement behavior and runtime memory protections may differ. GitHub-hosted macOS runners can compile, but cannot validate jailbreak-specific JIT on the physical iPad.
