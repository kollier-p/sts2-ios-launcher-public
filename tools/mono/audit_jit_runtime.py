#!/usr/bin/env python3
"""Inspect the pinned iOS Mono archive for native JIT backend symbols.

This is a build-time diagnostic, NOT proof that iOS allows JIT memory, and
not a replacement for a runtime DynamicMethod/Harmony test on the device.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

# mono_jit_* API names alone are insufficient: the AOT/interpreter also
# exports them. An architecture-specific code emitter is more informative.
SYMBOLS = (
    "mono_jit_compile_method",
    "mono_compile_method",
    "mono_arch_compile_method",
    "mono_arch_emit_prolog",
    "mono_arch_emit_epilog",
    "mono_arch_emit_call",
    "mono_jit_set_aot_mode",
    "mono_interp_transform_method",
)


def defined_symbols(nm_output: str) -> set[str]:
    # Each archive member has its own section; ignore undefined references.
    result = set()
    for line in nm_output.splitlines():
        parts = line.split()
        if len(parts) >= 2 and len(parts[-2]) == 1 and parts[-2] not in ("U", "u"):
            if re.fullmatch(r"[_A-Za-z][_A-Za-z0-9.$]*", parts[-1]):
                result.add(parts[-1].lstrip("_"))
    return result


def inspect_archive(archive: Path) -> dict[str, bool]:
    result = subprocess.run(["nm", "-g", str(archive)], capture_output=True, text=True, check=True)
    symbols = defined_symbols(result.stdout)
    return {name: name in symbols for name in SYMBOLS}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--require-backend", action="store_true",
                        help="fail early unless ARM64 JIT compiler symbols are present")
    args = parser.parse_args()
    if not args.archive.is_file():
        parser.error(f"Missing Mono archive: {args.archive}")
    found = inspect_archive(args.archive)
    print("iOS Mono archive JIT symbol audit (not a runtime JIT test):", flush=True)
    for name, exists in found.items():
        print(f"  {name}: {'DEFINED' if exists else 'missing'}", flush=True)
    backend = found["mono_arch_compile_method"] and found["mono_arch_emit_prolog"]
    print("ARM64 native JIT compiler symbols: " + ("present (runtime validation needed)" if backend
          else "absent; the pinned runtime cannot be switched to JIT by changing the AOT mode"),
          flush=True)
    return 0 if backend or not args.require_backend else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, subprocess.CalledProcessError) as error:
        sys.exit(f"Mono JIT audit failed: {error}")
