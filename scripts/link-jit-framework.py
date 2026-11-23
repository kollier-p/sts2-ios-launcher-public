#!/usr/bin/env python3
"""Link the isolated patched Mono JIT host into an iOS ARM64 framework.

Run scripts/build-jit-runtime.py first. This does not modify the existing
launcher or produce an IPA. The runtime's managed assemblies must be staged
separately before the host can start a game.
"""
from __future__ import annotations

import argparse
import plistlib
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
JIT = ROOT / "native/jit"
WORK = ROOT / ".work/jit"
LIB = WORK / "build/mono/mini"
PACK = WORK / "runtime-pack/runtimes/ios-arm64"
OUT = WORK / "framework/sts2.framework"
LIBRARIES = (
    "libmonosgen-2.0.a",
    "libmono-component-marshal-ilgen-static.a",
    "libmono-component-debugger-stub-static.a",
    "libmono-component-hot_reload-stub-static.a",
    "libmono-component-diagnostics_tracing-stub-static.a",
)

def command(*args: object) -> str:
    return subprocess.check_output([str(a) for a in args], text=True).strip()

def build() -> Path:
    sdk = command("xcrun", "--sdk", "iphoneos", "--show-sdk-path")
    include = PACK / "native/include/mono-2.0"
    if not include.is_dir():
        raise SystemExit(f"Missing Mono runtime pack headers: {include}")
    missing = [str(LIB / name) for name in LIBRARIES if not (LIB / name).is_file()]
    if missing:
        raise SystemExit("Missing patched Mono libraries; run scripts/build-jit-runtime.py first:\n" + "\n".join(missing))
    OUT.mkdir(parents=True, exist_ok=True)
    binary = OUT / "sts2"
    args = [
        "xcrun", "clang++", "-dynamiclib", "-arch", "arm64", "-isysroot", sdk,
        "-miphoneos-version-min=16.0", "-std=c++17", "-fobjc-arc", "-O2",
        "-I" + str(include),
        str(JIT / "RuntimeHost.mm"), str(JIT / "MonoJitMemory.mm"),
        *["-Wl,-force_load," + str(LIB / name) for name in LIBRARIES],
        "-framework", "Foundation", "-framework", "Security", "-lz", "-liconv",
        "-Wl,-install_name,@rpath/sts2.framework/sts2", "-o", str(binary),
    ]
    subprocess.run(args, check=True)
    (OUT / "Info.plist").write_bytes(plistlib.dumps({
        "CFBundleExecutable": "sts2",
        "CFBundleIdentifier": "org.sts2.jit.runtime",
        "CFBundleName": "sts2",
        "CFBundlePackageType": "FMWK",
        "CFBundleShortVersionString": "9.0.20",
        "CFBundleVersion": "1",
        "MinimumOSVersion": "16.0",
    }))
    print("Linked experimental Mono JIT framework:", OUT)
    return binary

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    build()

if __name__ == "__main__":
    main()
