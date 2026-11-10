#!/usr/bin/env python3
"""Validate a draft-1 T360 .tmod metadata-and-assets package."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import sys
import zipfile
from pathlib import PurePosixPath

MAX_ENTRIES = 2000
MAX_TOTAL_SIZE = 64 * 1024 * 1024
ID_PATTERN = re.compile(r"^[a-z0-9][a-z0-9._-]{1,63}$")
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")


class TmodError(ValueError):
    """Raised when a package violates the T360 draft-1 format."""


def _safe_archive_path(name: str) -> PurePosixPath:
    if not name or "\\" in name:
        raise TmodError(f"Invalid archive path: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise TmodError(f"Unsafe archive path: {name!r}")
    if name.endswith("/"):
        raise TmodError(f"Directory entries are not allowed: {name!r}")
    return path


def validate_tmod(path: str) -> dict:
    try:
        archive = zipfile.ZipFile(path, "r")
    except (OSError, zipfile.BadZipFile) as exc:
        raise TmodError(f"Cannot open package: {exc}") from exc

    with archive:
        infos = archive.infolist()
        if not infos or len(infos) > MAX_ENTRIES:
            raise TmodError(f"Archive must contain 1–{MAX_ENTRIES} entries")

        names = set()
        total_size = 0
        for info in infos:
            safe_path = _safe_archive_path(info.filename)
            if info.filename in names:
                raise TmodError(f"Duplicate archive path: {info.filename}")
            names.add(info.filename)
            total_size += info.file_size
            if total_size > MAX_TOTAL_SIZE:
                raise TmodError("Uncompressed package exceeds 64 MiB")
            mode = info.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise TmodError(f"Symbolic links are not allowed: {info.filename}")
            if info.is_dir():
                raise TmodError(f"Directory entries are not allowed: {info.filename}")
            if info.filename != "mod.json" and not safe_path.as_posix().startswith("assets/"):
                raise TmodError(f"Unsupported package entry: {info.filename}")

        if "mod.json" not in names:
            raise TmodError("Missing mod.json")

        try:
            manifest = json.loads(archive.read("mod.json").decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError, KeyError) as exc:
            raise TmodError(f"Invalid mod.json: {exc}") from exc

        if not isinstance(manifest, dict):
            raise TmodError("mod.json must contain a JSON object")
        if manifest.get("format_version") != 1 or isinstance(manifest.get("format_version"), bool):
            raise TmodError("format_version must be integer 1")

        mod_id = manifest.get("id")
        if not isinstance(mod_id, str) or not ID_PATTERN.fullmatch(mod_id):
            raise TmodError("id must match [a-z0-9][a-z0-9._-]{1,63}")

        name = manifest.get("name")
        if not isinstance(name, str) or not 1 <= len(name.strip()) <= 80:
            raise TmodError("name must contain 1–80 non-space characters")

        version = manifest.get("version")
        if not isinstance(version, str) or not 1 <= len(version) <= 32:
            raise TmodError("version must contain 1–32 characters")

        files = manifest.get("files")
        if not isinstance(files, dict):
            raise TmodError("files must be a JSON object")

        listed_assets = set()
        for asset_path, expected_hash in files.items():
            if not isinstance(asset_path, str):
                raise TmodError("Asset paths must be strings")
            safe_path = _safe_archive_path(asset_path)
            if not safe_path.as_posix().startswith("assets/"):
                raise TmodError(f"Manifest may only list assets/: {asset_path}")
            if asset_path not in names:
                raise TmodError(f"Manifest references missing asset: {asset_path}")
            if not isinstance(expected_hash, str) or not SHA256_PATTERN.fullmatch(expected_hash):
                raise TmodError(f"Invalid SHA-256 digest for {asset_path}")
            actual_hash = hashlib.sha256(archive.read(asset_path)).hexdigest()
            if actual_hash != expected_hash:
                raise TmodError(f"SHA-256 mismatch: {asset_path}")
            listed_assets.add(asset_path)

        actual_assets = {entry for entry in names if entry.startswith("assets/")}
        if actual_assets != listed_assets:
            missing = sorted(actual_assets - listed_assets)
            raise TmodError(f"Unlisted asset files: {', '.join(missing)}")

        return {
            "id": mod_id,
            "name": name.strip(),
            "version": version,
            "asset_count": len(listed_assets),
        }


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate a T360 .tmod package")
    parser.add_argument("package", help="Path to the .tmod ZIP package")
    args = parser.parse_args()
    try:
        result = validate_tmod(args.package)
    except TmodError as exc:
        print(f"INVALID: {exc}", file=sys.stderr)
        return 1
    print(
        "VALID: {id} v{version} — {name} ({asset_count} assets)".format(**result)
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
