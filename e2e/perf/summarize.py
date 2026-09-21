#!/usr/bin/env python3
"""Adds Jellyfin memory use to a performance result and writes a Markdown summary.

    summarize.py <result.json> <memory.csv> <summary.md>

memory.csv holds one resident-memory sample per second of the Jellyfin process (see perf.sh).
"""
import csv
import json
import statistics
import sys


def read_memory(path):
    samples = []
    try:
        with open(path, encoding="utf-8") as handle:
            for row in csv.DictReader(handle):
                if row.get("rss_kb"):
                    samples.append((int(row["epoch_ms"]), int(row["rss_kb"]) / 1024))
    except FileNotFoundError:
        pass
    return samples


def add_memory(run, samples):
    before = [mb for t, mb in samples if run["startedAt"] - 15_000 <= t < run["startedAt"]]
    during = [mb for t, mb in samples if run["startedAt"] <= t <= run["finishedAt"] + 1_000]
    run["memory"] = {
        "beforeMb": round(statistics.median(before), 1) if before else None,
        "peakMb": round(max(during), 1) if during else None,
    }
    if before and during:
        run["memory"]["growthMb"] = round(max(during) - statistics.median(before), 1)


def cell(value, suffix=""):
    return "n/a" if value is None else f"{value:,}{suffix}"


def summary(result):
    seed = result.get("seed", {})
    if result.get("scanReused"):
        scan = f"Jellyfin had indexed {result.get('indexedItems', 0):,} of them in an earlier run (REUSE=1)."
    else:
        scan = f"Jellyfin indexed {result.get('indexedItems', 0):,} of them in {result.get('scanSeconds', 0):,.0f} s."
    lines = [
        f"### Jellyfin {result.get('jellyfinVersion')}, {result.get('profile')} profile",
        "",
        f"Plugin {result.get('pluginVersion')}, platform {result.get('platform')}. "
        f"Seeded {result.get('seededItems', 0):,} items: {seed.get('movies', 0):,} movies, {seed.get('series', 0):,} series, "
        f"{seed.get('episodes', 0):,} episodes, {seed.get('albums', 0):,} albums, and {seed.get('tracks', 0):,} songs. "
        f"{scan} {result.get('users', 0)} users.",
        "",
        "| Export | Items | Time | Items/s | Slowest stage | Archive | Peak memory | Growth | Status p95 | Jellyfin p95 | Progress updates | Longest pause | Progress consistent |",
        "| --- | ---: | ---: | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |",
    ]
    for run in result.get("exports", []):
        memory = run.get("memory", {})
        progress = run["progress"]
        consistent = "yes" if progress["monotonic"] and progress["countsConsistent"] else "no"
        stages = progress.get("stageSeconds") or {}
        slowest = max(stages.items(), key=lambda entry: entry[1]) if stages else None
        slowest_cell = f"{slowest[0]} ({slowest[1]:,.1f} s)" if slowest else "n/a"
        lines.append(
            f"| {run['name']} | {run['itemCount']:,} | {run['seconds']:,.1f} s | {run['itemsPerSecond']:,} | {slowest_cell} "
            f"| {run['archiveBytes'] / 1_048_576:,.1f} MB | {cell(memory.get('peakMb'), ' MB')} | {cell(memory.get('growthMb'), ' MB')} "
            f"| {run['statusLatencyMs']['p95']:,} ms | {run['serverLatencyMs']['p95']:,} ms "
            f"| {progress['updates']:,} | {progress['maxGapSeconds']:,} s | {consistent} |"
        )
    return "\n".join(lines) + "\n"


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    result_path, memory_path, summary_path = sys.argv[1:]
    with open(result_path, encoding="utf-8") as handle:
        result = json.load(handle)

    samples = read_memory(memory_path)
    for run in result.get("exports", []):
        add_memory(run, samples)

    with open(result_path, "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2)
        handle.write("\n")
    with open(summary_path, "w", encoding="utf-8") as handle:
        handle.write(summary(result))


if __name__ == "__main__":
    main()
