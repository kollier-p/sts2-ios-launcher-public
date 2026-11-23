#!/usr/bin/env python3
"""Build experiment: rebuild matching .NET 10 Mono for iOS with ARM64 JIT.

The upstream dual-map patch targets Mono 9.0.20 and is only a reference.
Never reuse its binary or accept a patch with unresolved hunks against .NET 10.
This script deliberately stops at the source porting gate until reviewed.
"""
from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / ".work/jit-native/dotnet-runtime"
BUILD = ROOT / ".work/jit-native/build"
PATCH = ROOT / "experimental/jit/upstream-mono-ios-jit.patch"
HEADER = ROOT / "experimental/jit/MonoJitMemory.h"
VERSION = "10.0.12"
SOURCE_COMMIT = "4271d88e0aebf3d04f188f1334c2220d80555ef6"


def run(*args: object, cwd: Path | None = None) -> None:
    command = [str(x) for x in args]
    print("+ " + " ".join(command), flush=True)
    subprocess.run(command, cwd=cwd, check=True)


def ensure_source() -> None:
    SOURCE.parent.mkdir(parents=True, exist_ok=True)
    if not SOURCE.is_dir():
        run("git", "clone", "--depth", "1", "--branch", "v" + VERSION,
            "https://github.com/dotnet/runtime.git", SOURCE)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=SOURCE,
                                     text=True).strip()
    if commit != SOURCE_COMMIT:
        raise RuntimeError(f"Wrong .NET runtime revision {commit}; expected {SOURCE_COMMIT}.")
    # Stop rather than overwrite any existing source modifications.
    result = subprocess.run(["git", "status", "--porcelain"], cwd=SOURCE,
                            capture_output=True, text=True, check=True)
    if result.stdout.strip():
        raise RuntimeError("Native runtime source checkout has local changes.")


def port_patch() -> None:
    # This was written for Mono 9.0.20; never apply it to Mono 10
    # without checking every source hunk.
    check = subprocess.run(["git", "apply", "--check", str(PATCH)], cwd=SOURCE)
    if check.returncode:
        raise RuntimeError(
            "The MIT-licensed Mono 9.0.20 JIT patch needs a source-level port "
            "to .NET 10.0.12. It was NOT applied. Review failed hunks before "
            "enabling native JIT.")
    run("git", "apply", PATCH, cwd=SOURCE)
    dest = SOURCE / "src/mono/mono/utils/sts2-ios-jit-memory.h"
    dest.write_bytes(HEADER.read_bytes())
    # The bundled-resource lookup uses SIZE_MAX as the "no extension"
    # sentinel. Comparing unsigned extension_offset >= 0 is always true
    # and can pass NULL to strcmp. The pinned interpreter library needed
    # a binary patch for this; fix it in source for the JIT runtime.
    bundled = SOURCE / "src/mono/mono/metadata/bundled-resources.c"
    original = bundled.read_text()
    old = "if (extension_offset >= 0 && bundled_resources_is_known_assembly_extension (extension)) {"
    new = "if (extension != NULL && bundled_resources_is_known_assembly_extension (extension)) {"
    if original.count(old) != 1:
        raise RuntimeError("Mono bundled-resources null extension guard changed; review source")
    bundled.write_text(original.replace(old, new))


def build_native() -> None:
    generated = SOURCE / "artifacts/obj"
    generated.mkdir(parents=True, exist_ok=True)
    (generated / "runtime_version.h").write_text("\n".join(
        f"#define Runtime{kind}{part} {number}"
        for kind, parts in (
            ("File", (("MajorVersion", 10), ("MinorVersion", 0), ("BuildVersion", 12), ("RevisionVersion", 0))),
            ("Product", (("MajorVersion", 10), ("MinorVersion", 0), ("PatchVersion", 12))),
        ) for part, number in parts) + "\n")
    (generated / "_version.c").write_text(
        f'static char sccsid[] __attribute__((used)) = '
        f'"@(#)Version {VERSION} @Commit: {SOURCE_COMMIT}";\n')
    run("cmake", "-S", SOURCE / "src/mono", "-B", BUILD, "-G", "Ninja",
        "-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64",
        "-DCMAKE_OSX_DEPLOYMENT_TARGET=16.0", "-DCMAKE_BUILD_TYPE=Release",
        "-DCLR_CMAKE_HOST_ARCH=arm64", "-DDISABLE_JIT=OFF", "-DDISABLE_AOT=ON",
        "-DDISABLE_REFLECTION_EMIT=OFF", "-DDISABLE_EXECUTABLES=ON",
        "-DDISABLE_EVENTPIPE=ON", "-DDISABLE_DEBUGGER_AGENT=ON",
        "-DDISABLE_LLDB=ON", "-DSTATIC_COMPONENTS=ON",
        "-DDISABLE_LINK_STATIC_COMPONENTS=ON", "-DDISABLE_SHARED_LIBS=ON",
        "-DENABLE_LAZY_GC_THREAD_CREATION=OFF",
        "-DENABLE_ICALL_EXPORT=ON", "-DGC_SUSPEND=preemptive")
    run("cmake", "--build", BUILD, "-j", "8", "--target",
        "monosgen-static", "mono-component-marshal-ilgen-static",
        "mono-component-debugger-stub-static",
        "mono-component-hot_reload-stub-static",
        "mono-component-diagnostics_tracing-stub-static")
    archive = BUILD / "mono/mini/libmonosgen-2.0.a"
    if not archive.is_file():
        raise RuntimeError("The patched Mono 10 JIT archive was not produced.")
    print(f"Native JIT runtime candidate: {archive}", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true",
                        help="stop after verifying the Mono 10 source patch")
    args = parser.parse_args()
    ensure_source()
    port_patch()
    if not args.check_only:
        build_native()


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"iOS Mono JIT port: {error}")
