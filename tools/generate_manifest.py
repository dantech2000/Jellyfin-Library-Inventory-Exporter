#!/usr/bin/env python3
"""Generate the Jellyfin plugin repository manifest.

Every plugin zip becomes one catalog version. The version and targetAbi come from the
meta.json packaged inside the zip, so one release can carry a build per Jellyfin line.

    generate_manifest.py build.yaml manifest.json                            # GitHub releases
    generate_manifest.py build.yaml manifest.json --local DIR --base-url URL # local zips
"""
import argparse
import hashlib
import io
import json
import os
import re
import sys
import urllib.request
import zipfile
from datetime import datetime, timezone

# Releases before 0.1.4 shipped without meta.json. All of them targeted Jellyfin 10.11.
LEGACY_TARGET_ABI = "10.11.0.0"


def read_build(path):
    data = {}
    current = None
    collecting_block = False
    with open(path, "r", encoding="utf-8") as handle:
        for raw in handle:
            line = raw.rstrip("\n")
            if not line or line == "---":
                continue
            if collecting_block and line.startswith("  "):
                data[current] += line.strip() + " "
                continue
            collecting_block = False
            if ":" in line:
                key, value = line.split(":", 1)
                value = value.strip().strip('"')
                if value == ">":
                    current = key
                    data[key] = ""
                    collecting_block = True
                else:
                    current = key
                    data[key] = value
            elif current:
                data[current] += line.strip() + " "
    return data


def github_json(url):
    token = os.environ.get("GITHUB_TOKEN")
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json"})
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def download_asset(asset):
    token = os.environ.get("GITHUB_TOKEN")
    if token and asset.get("url"):
        request = urllib.request.Request(asset["url"], headers={"Accept": "application/octet-stream"})
        request.add_header("Authorization", f"Bearer {token}")
    else:
        request = urllib.request.Request(asset["browser_download_url"])
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def package_identity(zip_bytes, fallback_version):
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        if "meta.json" in archive.namelist():
            meta = json.loads(archive.read("meta.json").decode("utf-8"))
            return meta["version"], meta["targetAbi"]
    return fallback_version, LEGACY_TARGET_ABI


def version_entry(build, zip_bytes, fallback_version, source_url, timestamp, changelog):
    version, target_abi = package_identity(zip_bytes, fallback_version)
    return {
        "version": version,
        "changelog": changelog,
        "targetAbi": target_abi,
        "sourceUrl": source_url,
        "checksum": hashlib.md5(zip_bytes).hexdigest(),
        "timestamp": timestamp,
        "repositoryName": build.get("repositoryName", build["name"]),
        "repositoryUrl": build.get("repositoryUrl", ""),
    }


def github_versions(build):
    repo = os.environ["GITHUB_REPOSITORY"]
    versions = []
    for release in github_json(f"https://api.github.com/repos/{repo}/releases?per_page=100"):
        if release.get("draft") or release.get("prerelease"):
            continue
        tag_version = re.sub(r"^v", "", release["tag_name"])
        changelog = release.get("body") or build.get("changelog", "")
        assets = {asset["name"]: asset for asset in release["assets"]}
        for name, asset in assets.items():
            if not name.endswith(".zip"):
                continue
            zip_bytes = download_asset(asset)
            entry = version_entry(build, zip_bytes, tag_version, asset["browser_download_url"], release["published_at"], changelog)
            md5_asset = assets.get(name + ".md5")
            if md5_asset:
                published = download_asset(md5_asset).decode("utf-8").strip().split()[0].lower()
                if published != entry["checksum"]:
                    sys.exit(f"{release['tag_name']}/{name}: published MD5 {published} does not match the archive MD5 {entry['checksum']}")
            versions.append(entry)
    return versions


def local_versions(build, directory, base_url):
    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    changelog = build.get("changelog", "").strip()
    versions = []
    for name in sorted(os.listdir(directory)):
        if not name.endswith(".zip"):
            continue
        with open(os.path.join(directory, name), "rb") as handle:
            zip_bytes = handle.read()
        versions.append(version_entry(build, zip_bytes, build["version"], f"{base_url.rstrip('/')}/{name}", timestamp, changelog))
    return versions


def version_key(entry):
    return tuple(int(part) for part in entry["version"].split("."))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("build_yaml")
    parser.add_argument("output")
    parser.add_argument("--local", metavar="DIR", help="read plugin zips from DIR instead of GitHub releases")
    parser.add_argument("--base-url", help="URL that serves the zips from DIR (required with --local)")
    args = parser.parse_args()
    if args.local and not args.base_url:
        parser.error("--local requires --base-url")

    build = read_build(args.build_yaml)
    versions = local_versions(build, args.local, args.base_url) if args.local else github_versions(build)
    versions.sort(key=version_key, reverse=True)
    manifest = [{
        "guid": build["guid"],
        "name": build["name"],
        "description": build["description"].strip(),
        "overview": build["overview"],
        "owner": build["owner"],
        "category": build["category"],
        "imageUrl": build.get("imageUrl", ""),
        "versions": versions,
    }]

    output_directory = os.path.dirname(args.output)
    if output_directory:
        os.makedirs(output_directory, exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
