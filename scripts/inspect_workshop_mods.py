#!/usr/bin/env python3
"""Inspect an existing Workshop mod directory for the future stock Mono mod loader.

This does NOT execute mods, activate JIT, or modify game files.
"""
import argparse
import json
from pathlib import Path

def inspect_mods(root: Path) -> dict:
    if not root.is_dir():
        raise ValueError(f"mod directory does not exist: {root}")
    mods = []
    for folder in sorted(p for p in root.iterdir() if p.is_dir() and not p.is_symlink()):
        files = [p for p in folder.rglob("*") if p.is_file() and not p.is_symlink()]
        manifests = [p.relative_to(folder).as_posix() for p in files if p.name.lower() in ("manifest.json", "mod.json")]
        dlls = [p.relative_to(folder).as_posix() for p in files if p.suffix.lower() == ".dll"]
        pcks = [p.relative_to(folder).as_posix() for p in files if p.suffix.lower() == ".pck"]
        if not (manifests or dlls or pcks):
            continue
        mods.append({"folder": folder.name, "manifests": sorted(manifests),
                     "managed_assemblies": sorted(dlls), "resource_packs": sorted(pcks)})
    return {"schema": 1, "mods": mods}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mods_dir", type=Path, help="Existing launcher-installed mod directory")
    parser.add_argument("--output", type=Path, help="Write JSON inventory; stdout otherwise")
    args = parser.parse_args()
    result = json.dumps(inspect_mods(args.mods_dir), indent=2) + "\n"
    if args.output:
        args.output.write_text(result, encoding="utf-8")
    else:
        print(result, end="")

if __name__ == "__main__":
    main()
