#!/usr/bin/env python3
"""Zip a published plugin directory into a Jellyfin plugin package and its .md5 file.

    package_plugin.py <publish-dir> <out-dir>

The archive name comes from the resolved meta.json, so each Jellyfin server line
(one per target framework) gets its own package.
"""
import hashlib
import json
import os
import sys
import zipfile

PACKAGE_NAME = "Jellyfin.Plugin.LibraryInventoryExporter"


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    publish_dir, out_dir = sys.argv[1], sys.argv[2]

    with open(os.path.join(publish_dir, "meta.json"), encoding="utf-8") as handle:
        meta = json.load(handle)
    if "@" in meta["version"] or "@" in meta["targetAbi"]:
        sys.exit(f"{publish_dir}/meta.json still contains template tokens")

    os.makedirs(out_dir, exist_ok=True)
    zip_path = os.path.join(out_dir, f"{PACKAGE_NAME}_{meta['version']}.zip")
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for directory, subdirectories, files in os.walk(publish_dir):
            subdirectories.sort()
            for name in sorted(files):
                path = os.path.join(directory, name)
                archive.write(path, os.path.relpath(path, publish_dir))

    with open(zip_path, "rb") as handle:
        checksum = hashlib.md5(handle.read()).hexdigest()
    with open(zip_path + ".md5", "w", encoding="utf-8") as handle:
        handle.write(checksum + "\n")
    print(f"{zip_path} targetAbi={meta['targetAbi']} md5={checksum}")


if __name__ == "__main__":
    main()
