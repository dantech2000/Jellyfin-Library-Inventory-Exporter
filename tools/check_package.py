#!/usr/bin/env python3
"""Check the meta.json inside a Jellyfin plugin package.

    check_package.py <build.yaml> <zip> <expected-targetAbi>

Fails when meta.json is missing, when its guid is missing or differs from the guid in
build.yaml (Jellyfin then logs "The manifest ID 00000000-... did not match the package
info ID"), or when its targetAbi is not the one this build is for.
"""
import json
import os
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from generate_manifest import read_build  # noqa: E402


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    build_path, zip_path, expected_abi = sys.argv[1:]
    expected_guid = read_build(build_path)["guid"]

    with zipfile.ZipFile(zip_path) as archive:
        if "meta.json" not in archive.namelist():
            sys.exit(f"{zip_path}: no meta.json")
        meta = json.loads(archive.read("meta.json").decode("utf-8"))

    errors = []
    if meta.get("guid") != expected_guid:
        errors.append(f"guid is {meta.get('guid')!r}, build.yaml has {expected_guid!r}")
    if meta.get("targetAbi") != expected_abi:
        errors.append(f"targetAbi is {meta.get('targetAbi')!r}, expected {expected_abi!r}")
    if errors:
        sys.exit("\n".join(f"{zip_path}: {error}" for error in errors))
    print(f"{zip_path}: guid={meta['guid']} targetAbi={meta['targetAbi']} version={meta['version']}")


if __name__ == "__main__":
    main()
