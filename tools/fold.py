#!/usr/bin/env python3
"""Stamp the version through HexiumDist and pack the release zip.

Every zip entry name is written explicitly with forward slashes. This is not
decoration. Compress-Archive - and ZipFile.CreateFromDirectory on some runtimes -
writes backslash-separated entry names, which mod loaders treat as one flat filename:
plugins/Wonderland.dll arrives as a file literally called "plugins\\Wonderland.dll" sitting in
the archive root, and BepInEx never sees a plugins folder at all. The mod then looks
installed and does nothing.

Timestamps are fixed rather than taken from the filesystem so that rebuilding
unchanged sources produces a byte-identical zip. A fold that churns its own hash on
every build cannot be used to answer "is what I deployed what I built?".
"""

import argparse
import hashlib
import json
import pathlib
import shutil
import sys
import zipfile

# Deterministic zip timestamp. 1980-01-01 is the earliest the zip format can encode.
FIXED_DATE = (1980, 1, 1, 0, 0, 0)

PACKAGE_FILES = ["manifest.json", "README.md", "CHANGELOG.md", "LICENSE.md", "icon.png"]


def stamp_manifest(manifest_path: pathlib.Path, version: str) -> None:
    """Write version_number into the manifest, leaving every other key untouched."""
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    if data.get("version_number") == version:
        return
    data["version_number"] = version
    manifest_path.write_text(
        json.dumps(data, indent=4, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"fold: manifest version_number -> {version}")


def stage_dll(built_dll: pathlib.Path, dist: pathlib.Path) -> pathlib.Path:
    staged = dist / "plugins" / built_dll.name
    staged.parent.mkdir(parents=True, exist_ok=True)
    if not staged.exists() or staged.read_bytes() != built_dll.read_bytes():
        shutil.copy2(built_dll, staged)
        print(f"fold: staged {staged.relative_to(dist.parent)}")
    return staged


def pack(dist: pathlib.Path, version: str, staged_dll: pathlib.Path) -> pathlib.Path:
    out = dist / f"Wonderland-v{version}.zip"
    entries = []
    for name in PACKAGE_FILES:
        src = dist / name
        if src.exists():
            entries.append((name, src))
    entries.append(("plugins/" + staged_dll.name, staged_dll))

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for arcname, src in entries:
            assert "\\" not in arcname, f"backslash in entry name: {arcname!r}"
            info = zipfile.ZipInfo(arcname, date_time=FIXED_DATE)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, src.read_bytes())

    digest = hashlib.sha256(staged_dll.read_bytes()).hexdigest()
    print(f"fold: {out.name}  ({len(entries)} entries, dll sha256 {digest[:16]}...)")
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="0.6.0")
    ap.add_argument("--dist", type=pathlib.Path, default=pathlib.Path(__file__).parent.parent / "HexiumDist")
    ap.add_argument("--dll", type=pathlib.Path, default=pathlib.Path(__file__).parent.parent / "bin" / "Release" / "net472" / "Wonderland.dll")
    args = ap.parse_args()

    dist = args.dist.resolve()
    dll = args.dll.resolve()

    if not dll.exists():
        print(f"fold: no built DLL at {dll}", file=sys.stderr)
        return 1
    if not dist.is_dir():
        print(f"fold: no dist folder at {dist}", file=sys.stderr)
        return 1

    stamp_manifest(dist / "manifest.json", args.version)
    staged = stage_dll(dll, dist)
    pack(dist, args.version, staged)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
