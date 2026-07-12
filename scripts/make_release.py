#!/usr/bin/env python3
"""Build release ZIP for CUTarkovMedicalMod / CUTarkovWeaponMod.

Usage:
  python scripts/make_release.py --mod medical --version 0.3.0
  python scripts/make_release.py --mod weapon --version 0.1.0.0
"""
import argparse
import os
import shutil
import sys
import zipfile


def find_game_path():
    """Read vars.targets to find BaseGamePath."""
    targets = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "vars.targets")
    if not os.path.exists(targets):
        return None
    with open(targets, "r", encoding="utf-8") as f:
        content = f.read()
    # Look for lines like: <BaseGamePath Condition="...">PATH</BaseGamePath>
    # or <BaseGamePath>PATH</BaseGamePath>
    import re
    matches = re.findall(r'<BaseGamePath[^>]*>([^<]+)</BaseGamePath>', content)
    for m in matches:
        m = m.strip().replace("/", "\\")
        # Skip env var references
        if "$(" in m:
            continue
        if os.path.isdir(m):
            return m
    return None


def main():
    parser = argparse.ArgumentParser(description="Build release ZIP")
    parser.add_argument("--mod", required=True, choices=["medical", "weapon"])
    parser.add_argument("--version", required=True)
    parser.add_argument("--gamepath", default=None)
    args = parser.parse_args()

    game_path = args.gamepath or find_game_path()
    if not game_path or not os.path.isdir(game_path):
        print(f"Error: Game path not found: {game_path}")
        sys.exit(1)

    if args.mod == "medical":
        mod_name = "CUTarkovMedicalMod"
        dll_name = "CUTarkovMedicalMod.dll"
    else:
        mod_name = "CUTarkovWeaponMod"
        dll_name = "CUTarkovWeaponMod.dll"

    plugin_dir = os.path.join(game_path, "BepInEx", "plugins", mod_name)
    if not os.path.isdir(plugin_dir):
        print(f"Error: Plugin directory not found: {plugin_dir}")
        sys.exit(1)

    dll_path = os.path.join(plugin_dir, dll_name)
    if not os.path.exists(dll_path):
        print(f"Error: DLL not found: {dll_path}")
        sys.exit(1)

    # Staging directory
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    staging = os.path.join(repo_root, ".staging")
    if os.path.exists(staging):
        shutil.rmtree(staging)
    os.makedirs(staging)

    # Copy DLL
    shutil.copy2(dll_path, os.path.join(staging, dll_name))

    # Copy Framework/Assets
    assets_src = os.path.join(plugin_dir, "Framework", "Assets")
    assets_dst = os.path.join(staging, "Framework", "Assets")
    if os.path.isdir(assets_src):
        os.makedirs(assets_dst, exist_ok=True)
        for item in os.listdir(assets_src):
            s = os.path.join(assets_src, item)
            d = os.path.join(assets_dst, item)
            if os.path.isfile(s):
                shutil.copy2(s, d)

    # Copy Lang
    lang_src = os.path.join(plugin_dir, "Lang")
    lang_dst = os.path.join(staging, "Lang")
    if os.path.isdir(lang_src):
        os.makedirs(lang_dst, exist_ok=True)
        for item in os.listdir(lang_src):
            s = os.path.join(lang_src, item)
            d = os.path.join(lang_dst, item)
            if os.path.isfile(s):
                shutil.copy2(s, d)

    # Create ZIP
    release_dir = os.path.join(repo_root, "Release")
    os.makedirs(release_dir, exist_ok=True)
    zip_name = f"{mod_name}_v{args.version}.zip"
    zip_path = os.path.join(release_dir, zip_name)

    if os.path.exists(zip_path):
        os.remove(zip_path)

    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, dirs, files in os.walk(staging):
            for f in files:
                full = os.path.join(root, f)
                rel = os.path.relpath(full, staging)
                zf.write(full, rel)

    # Cleanup staging
    shutil.rmtree(staging)

    size_mb = os.path.getsize(zip_path) / (1024 * 1024)
    print(f"\nRelease package created:")
    print(f"  Path: {zip_path}")
    print(f"  Size: {size_mb:.1f} MB")
    print(f"  Version: {args.version}")


if __name__ == "__main__":
    main()
