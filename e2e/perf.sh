#!/usr/bin/env bash
# Runs the performance suite: seeds a large library, scans it, and measures exports.
#
#   e2e/perf.sh 12              Jellyfin 12.0, small profile (about 2,750 items)
#   e2e/perf.sh 10.11 medium    Jellyfin 10.11, about 14,300 items
#   e2e/perf.sh 12 large        about 49,500 items
#
# Results go to e2e/perf-results/<line>/: <profile>.json, <profile>.md, and <profile>-memory.csv.
# KEEP=1 leaves the stack running. REUSE=1 then keeps its Jellyfin database, so the next run of the same
# line and profile rebuilds the plugin but skips the library scan. JELLYFIN_PLATFORM works as in run.sh.
set -euo pipefail

cd "$(dirname "$0")"
# shellcheck source=lib/platform.sh
source lib/platform.sh

line="${1:-}"
profile="${2:-small}"
env_file="env/$line.env"
profile_file="perf/profiles/$profile.env"
if [ ! -f "$env_file" ] || [ ! -f "$profile_file" ]; then
    echo "Usage: e2e/perf.sh <10.11|12> [small|medium|large]" >&2
    exit 2
fi

# Export the line and profile settings for compose variable interpolation.
set -a
# shellcheck disable=SC1090
source "$env_file"
# shellcheck disable=SC1090
source "$profile_file"
set +a
# Its own host port (8931 or 8932), so a performance stack never collides with a functional run.
export JELLYFIN_HOST_PORT=$((JELLYFIN_HOST_PORT + 20))

choose_platform
project="jlie-perf-${line//./-}"
compose=(docker compose --project-name "$project" --env-file "$env_file" --file compose.yaml --file compose.perf.yaml)
if [ -n "${JELLYFIN_PLATFORM:-}" ] && [ "$JELLYFIN_PLATFORM" != "native" ]; then
    compose+=(--file compose.platform.yaml)
fi

results="perf-results/$line"
mkdir -p "$results"
rm -f "$results/$profile.json" "$results/$profile.md" "$results/$profile-memory.csv" "$results/$profile-jellyfin.log"

# Samples the resident memory of the Jellyfin process (PID 1 in its container) once per second.
sample_memory() {
    local container="$1" out="$2" rss
    echo "epoch_ms,rss_kb" > "$out"
    while rss="$(docker exec "$container" awk '/^VmRSS/ { print $2 }' /proc/1/status 2>/dev/null)"; do
        echo "$(date +%s)000,$rss" >> "$out"
        sleep 1
    done
}

echo "==> Jellyfin $line, $profile profile"
if [ "${REUSE:-0}" = "1" ]; then
    echo "Reusing the Jellyfin database of the running stack"
else
    "${compose[@]}" down --volumes --remove-orphans
fi
"${compose[@]}" build
started=$SECONDS
"${compose[@]}" up --detach --wait jellyfin
echo "Seeded the library and started Jellyfin in $((SECONDS - started))s"

sample_memory "$("${compose[@]}" ps --quiet jellyfin)" "$results/$profile-memory.csv" &
sampler=$!
status=0
"${compose[@]}" run --rm playwright || status=$?
kill "$sampler" 2>/dev/null || true
wait "$sampler" 2>/dev/null || true

if [ -f "$results/$profile.json" ]; then
    python3 perf/summarize.py "$results/$profile.json" "$results/$profile-memory.csv" "$results/$profile.md" || status=$?
    cat "$results/$profile.md"
    if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
        cat "$results/$profile.md" >> "$GITHUB_STEP_SUMMARY"
    fi
fi

if [ "$status" -ne 0 ]; then
    "${compose[@]}" logs --no-color jellyfin > "$results/$profile-jellyfin.log" 2>&1 || true
    echo "The performance suite failed. Jellyfin log: e2e/$results/$profile-jellyfin.log" >&2
fi

if [ "${KEEP:-0}" = "1" ]; then
    echo "Stack '$project' is still running: http://localhost:$JELLYFIN_HOST_PORT (admin / e2e-admin-password)"
else
    "${compose[@]}" down --volumes --remove-orphans
fi
exit "$status"
