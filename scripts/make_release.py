#!/usr/bin/env python3
"""Build release zip for CU Tarkov mods.

Usage:
  python scripts/make_release.py --mod medical --version 0.2.5
  python scripts/make_release.py --mod weapon --version 1.0.2.0
"""
import argparse
import os
import shutil
import sys
import zipfile
from pathlib import Path

# Repo root = parent of scripts/
REPO_ROOT = Path(__file__).resolve().parent.parent

MOD_CONFIG = {
    "medical": {
        "name": "CUTarkovMedicalMod",
        "dll": "CUTarkovMedicalMod.dll",
        "plugin_dir": "CUTarkovMedicalMod",
    },
    "weapon": {
        "name": "CUTarkovWeaponMod",
        "dll": "CUTarkovWeaponMod.dll",
        "plugin_dir": "CUTarkovWeaponMod",
    },
}

EXCLUDE_EXTS = {".pdb", ".xml"}
EXCLUDE_FILES = {".staging", ".update_prepare.bat", ".update_apply.bat"}


def read_game_path():
    """Read BaseGamePath from vars.targets."""
    vt = REPO_ROOT / "vars.targets"
    if not vt.exists():
        return None
    for line in vt.read_text(encoding="utf-8").splitlines():
        if "<BaseGamePath>" in line:
            start = line.index(">") + 1
            end = line.rindex("<")
            return line[start:end].strip()
    return None


def package(mod_key, version, game_path):
    cfg = MOD_CONFIG[mod_key]
    plugin_dir = Path(game_path) / "BepInEx" / "plugins" / cfg["plugin_dir"]

    if not plugin_dir.exists():
        print(f"ERROR: Plugin directory not found: {plugin_dir}", file=sys.stderr)
        sys.exit(1)

    dll_path = plugin_dir / cfg["dll"]
    if not dll_path.exists():
        print(f"ERROR: DLL not found: {dll_path}", file=sys.stderr)
        sys.exit(1)

    release_dir = REPO_ROOT / "Release"
    release_dir.mkdir(exist_ok=True)

    zip_name = f"{cfg['name']}_v{version}.zip"
    zip_path = release_dir / zip_name

    # Remove old zip
    if zip_path.exists():
        zip_path.unlink()

    file_count = 0
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        # DLL at root
        zf.write(dll_path, cfg["dll"])
        file_count += 1

        # Framework/Assets/ at root
        assets_dir = plugin_dir / "Framework" / "Assets"
        if assets_dir.exists():
            for f in assets_dir.rglob("*"):
                if f.is_file():
                    ext = f.suffix.lower()
                    if ext in EXCLUDE_EXTS:
                        continue
                    arcname = f.relative_to(plugin_dir)
                    zf.write(f, str(arcname))
                    file_count += 1

        # Lang/ at root
        lang_dir = plugin_dir / "Lang"
        if lang_dir.exists():
            for f in lang_dir.iterdir():
                if f.is_file() and f.suffix.lower() not in EXCLUDE_EXTS:
                    zf.write(f, f"Lang/{f.name}")
                    file_count += 1

    size_mb = zip_path.stat().st_size / (1024 * 1024)
    print(f"Created: {zip_path}")
    print(f"  Files: {file_count}")
    print(f"  Size:  {size_mb:.2f} MB")


def main():
    parser = argparse.ArgumentParser(description="Package CU Tarkov mod release zip")
    parser.add_argument("--mod", required=True, choices=["medical", "weapon"])
    parser.add_argument("--version", required=True)
    parser.add_argument("--gamepath", default=None)
    args = parser.parse_args()

    game_path = args.gamepath or read_game_path()
    if not game_path:
        print("ERROR: Could not determine game path. Use --gamepath or set BaseGamePath in vars.targets", file=sys.stderr)
        sys.exit(1)

    package(args.mod, args.version, game_path)


if __name__ == "__main__":
    main()
