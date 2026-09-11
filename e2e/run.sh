#!/usr/bin/env bash
# Runs the Docker + Playwright E2E suite against one or both Jellyfin server lines.
#
#   e2e/run.sh 12       Jellyfin 12.0
#   e2e/run.sh 10.11    Jellyfin 10.11
#   e2e/run.sh all      both lines, one after the other (default)
#
# KEEP=1 leaves the stack running after the tests so you can open Jellyfin in a browser.
# JELLYFIN_PLATFORM=linux/amd64 runs the Jellyfin containers as amd64. JELLYFIN_PLATFORM=native
# turns off the automatic choice below.
set -euo pipefail

cd "$(dirname "$0")"

# .NET crashes with an illegal instruction (SIGILL) in arm64 Linux VMs on CPUs that have SME but no SVE,
# such as the Apple M4: https://github.com/dotnet/runtime/issues/122608. There, run Jellyfin as amd64.
choose_platform() {
    if [ -n "${JELLYFIN_PLATFORM:-}" ]; then
        return
    fi

    local features
    features="$(docker run --rm alpine:3 grep -m1 '^Features' /proc/cpuinfo 2>/dev/null || true)"
    if [[ "$features" == *" sme"* && "$features" != *" sve"* ]]; then
        export JELLYFIN_PLATFORM=linux/amd64
        echo "Docker's CPU has SME but no SVE (for example an Apple M4), where .NET can crash in arm64 containers."
        echo "Running the Jellyfin containers as linux/amd64. Set JELLYFIN_PLATFORM=native to override."
    fi
}

run_line() {
    local line="$1"
    local env_file="env/$line.env"
    if [ ! -f "$env_file" ]; then
        echo "Unknown Jellyfin line '$line'. Use 10.11, 12, or all." >&2
        return 2
    fi

    local project="jlie-e2e-${line//./-}"
    local compose=(docker compose --project-name "$project" --env-file "$env_file" --file compose.yaml)
    if [ -n "${JELLYFIN_PLATFORM:-}" ] && [ "$JELLYFIN_PLATFORM" != "native" ]; then
        compose+=(--file compose.platform.yaml)
    fi

    local status=0
    echo "==> Jellyfin $line"
    "${compose[@]}" down --volumes --remove-orphans
    "${compose[@]}" build
    "${compose[@]}" run --rm playwright || status=$?

    if [ "$status" -ne 0 ]; then
        mkdir -p "playwright-report/$line"
        "${compose[@]}" logs --no-color > "playwright-report/$line/docker-compose.log" 2>&1 || true
        echo "Jellyfin $line failed. Server logs: e2e/playwright-report/$line/docker-compose.log" >&2
    fi

    if [ "${KEEP:-0}" = "1" ]; then
        # shellcheck disable=SC1090
        source "$env_file"
        echo "Stack '$project' is still running: http://localhost:$JELLYFIN_HOST_PORT (admin / e2e-admin-password)"
        echo "Stop it with: ${compose[*]} down -v   (run from e2e/)"
    else
        "${compose[@]}" down --volumes --remove-orphans
    fi
    return "$status"
}

choose_platform

case "${1:-all}" in
    all)
        status=0
        run_line 10.11 || status=$?
        run_line 12 || status=$?
        exit "$status"
        ;;
    *)
        run_line "$1"
        ;;
esac
