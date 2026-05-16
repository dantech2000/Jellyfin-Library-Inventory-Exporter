#!/usr/bin/env python3
import hashlib
import json
import os
import re
import sys
import urllib.request


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


def github_asset_text(asset):
    token = os.environ.get("GITHUB_TOKEN")
    if token and asset.get("url"):
        request = urllib.request.Request(asset["url"], headers={"Accept": "application/octet-stream"})
        request.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.read().decode("utf-8")
    with urllib.request.urlopen(asset["browser_download_url"], timeout=30) as response:
        return response.read().decode("utf-8")


def main():
    build = read_build(sys.argv[1])
    output = sys.argv[2]
    repo = os.environ["GITHUB_REPOSITORY"]
    releases = github_json(f"https://api.github.com/repos/{repo}/releases")
    versions = []
    for release in releases:
        if release.get("draft") or release.get("prerelease"):
            continue
        tag = release["tag_name"]
        version = re.sub(r"^v", "", tag)
        zip_asset = next((a for a in release["assets"] if a["name"].endswith(".zip")), None)
        md5_asset = next((a for a in release["assets"] if a["name"].endswith(".zip.md5")), None)
        if not zip_asset:
            continue
        checksum = ""
        if md5_asset:
            checksum = github_asset_text(md5_asset).strip().split()[0]
        versions.append({
            "version": version,
            "changelog": release.get("body") or build.get("changelog", ""),
            "targetAbi": build["targetAbi"],
            "sourceUrl": zip_asset["browser_download_url"],
            "checksum": checksum or hashlib.md5(zip_asset["browser_download_url"].encode("utf-8")).hexdigest(),
            "timestamp": release["published_at"],
        })
    manifest = [{
        "guid": build["guid"],
        "name": build["name"],
        "description": build["description"].strip(),
        "overview": build["overview"],
        "owner": build["owner"],
        "category": build["category"],
        "versions": versions,
    }]
    os.makedirs(os.path.dirname(output), exist_ok=True)
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
