#!/usr/bin/env python3
"""Build the pinned upstream patched Mono JIT runtime for iOS ARM64.

Separate from the existing Mono 10 library-builder pipeline. Requires macOS,
Xcode, git, CMake and Ninja. Does not package proprietary game files.
"""
from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
JIT = ROOT / "native/jit"
SOURCE = ROOT / ".work/jit/dotnet-runtime"
BUILD = ROOT / ".work/jit/build"
PACK = ROOT / ".work/jit/runtime-pack"
DOWNLOAD = ROOT / ".work/jit/downloads"
VERSION = "9.0.20"
COMMIT = "3879076d9a06ce098d37c3882fb1845a6627335b"
PACK_SHA256 = "ccb189791773d4ca77cee3dd03e28dc2a9b6231b479e1e44e28088a378692b3f"
LIBRARIES = (
    "libmonosgen-2.0.a",
    "libmono-component-marshal-ilgen-static.a",
    "libmono-component-debugger-stub-static.a",
    "libmono-component-hot_reload-stub-static.a",
    "libmono-component-diagnostics_tracing-stub-static.a",
)

def run(*args: object, cwd: Path | None = None) -> None:
    print("+", *(str(a) for a in args), flush=True)
    subprocess.run([str(a) for a in args], cwd=cwd, check=True)

def output(*args: object, cwd: Path | None = None) -> str:
    return subprocess.check_output([str(a) for a in args], cwd=cwd, text=True).strip()

def source() -> None:
    if not SOURCE.exists():
        SOURCE.parent.mkdir(parents=True, exist_ok=True)
        run("git", "clone", "--depth", "1", "--branch", "v" + VERSION,
            "https://github.com/dotnet/runtime.git", SOURCE)
    if output("git", "rev-parse", "HEAD", cwd=SOURCE) != COMMIT:
        raise RuntimeError("Mono runtime commit differs from pinned revision")
    patch = JIT / "mono-ios-jit.patch"
    reverse = subprocess.run(["git", "apply", "--reverse", "--check", str(patch)],
                             cwd=SOURCE, capture_output=True)
    if reverse.returncode:
        run("git", "apply", "--check", patch, cwd=SOURCE)
        run("git", "apply", patch, cwd=SOURCE)
    header = SOURCE / "src/mono/mono/utils/sts2-ios-jit-memory.h"
    original = (JIT / "MonoJitMemory.h").read_bytes()
    if header.exists() and header.read_bytes() != original:
        raise RuntimeError("Runtime JIT memory header differs")
    header.parent.mkdir(parents=True, exist_ok=True)
    header.write_bytes(original)

def runtime_pack() -> None:
    archive = DOWNLOAD / f"microsoft.netcore.app.runtime.mono.ios-arm64.{VERSION}.nupkg"
    DOWNLOAD.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        url = (f"https://api.nuget.org/v3-flatcontainer/"
               f"microsoft.netcore.app.runtime.mono.ios-arm64/{VERSION}/{archive.name}")
        urllib.request.urlretrieve(url, archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != PACK_SHA256:
        raise RuntimeError("Mono iOS runtime pack checksum mismatch")
    if not (PACK / "runtimes/ios-arm64/native/include/mono-2.0").is_dir():
        PACK.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(archive) as bundle:
            bundle.extractall(PACK)

def build() -> None:
    generated = SOURCE / "artifacts/obj"
    generated.mkdir(parents=True, exist_ok=True)
    (generated / "runtime_version.h").write_text("\n".join(
        f"#define Runtime{kind}{part} {value}" for kind, parts in (
            ("File", (("MajorVersion", 9), ("MinorVersion", 0), ("BuildVersion", 20), ("RevisionVersion", 0))),
            ("Product", (("MajorVersion", 9), ("MinorVersion", 0), ("PatchVersion", 20))))
        for part, value in parts) + "\n")
    (generated / "_version.c").write_text(
        f'static char sccsid[] __attribute__((used)) = "@(#)Version {VERSION} @Commit: {COMMIT}";\n')
    run("cmake", "-S", SOURCE / "src/mono", "-B", BUILD, "-G", "Ninja",
        "-DCMAKE_SYSTEM_NAME=iOS", "-DCMAKE_OSX_ARCHITECTURES=arm64",
        "-DCMAKE_OSX_DEPLOYMENT_TARGET=16.0", "-DCMAKE_BUILD_TYPE=Release",
        "-DCLR_CMAKE_HOST_ARCH=arm64", "-DDISABLE_JIT=OFF", "-DDISABLE_AOT=ON",
        "-DDISABLE_REFLECTION_EMIT=OFF", "-DDISABLE_EXECUTABLES=ON",
        "-DDISABLE_EVENTPIPE=ON", "-DDISABLE_DEBUGGER_AGENT=ON",
        "-DDISABLE_LLDB=ON", "-DSTATIC_COMPONENTS=ON",
        "-DDISABLE_LINK_STATIC_COMPONENTS=ON", "-DDISABLE_SHARED_LIBS=ON",
        "-DENABLE_LAZY_GC_THREAD_CREATION=OFF", "-DENABLE_ICALL_EXPORT=ON",
        "-DGC_SUSPEND=preemptive")
    run("cmake", "--build", BUILD, "-j", "8", "--target",
        "monosgen-static", "mono-component-marshal-ilgen-static",
        "mono-component-debugger-stub-static",
        "mono-component-hot_reload-stub-static",
        "mono-component-diagnostics_tracing-stub-static")
    for name in LIBRARIES:
        path = BUILD / "mono/mini" / name
        if not path.is_file():
            raise RuntimeError(f"Missing native JIT library: {path}")
    print("Native JIT static libraries built successfully.")

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Check pinned source and patch only")
    args = parser.parse_args()
    if sys.platform != "darwin":
        raise SystemExit("This build requires a macOS runner with the iOS SDK")
    for tool in ("git", "cmake", "ninja", "xcrun"):
        if shutil.which(tool) is None:
            raise SystemExit(f"Missing required tool: {tool}")
    source()
    if not args.check:
        runtime_pack()
        build()

if __name__ == "__main__":
    main()
