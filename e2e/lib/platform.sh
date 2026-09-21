# Sourced by run.sh and perf.sh.

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
