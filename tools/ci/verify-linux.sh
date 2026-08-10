#!/usr/bin/env bash
#
# linux-x64 verification: build, full test suite, and the Tier 0 spike.
#
# This is not ceremony. The run that first executed this script found a heap-corrupting ownership
# bug that had been passing green on Windows for the whole of the project's history:
# ISession::loadModule* returns a borrowed pointer, and releasing it over-releases. glibc aborts
# on it; the Windows heap did not. See docs/findings.md §9 and SlangModule's constructor.
#
# Prerequisites:
#   pwsh tools/fetch-native.ps1 -Rid linux-x64      # stage the payload (runs fine from Windows)
#   a .NET 10 SDK on this machine
#
# Under WSL from Windows:
#   wsl -d Ubuntu -- bash /mnt/e/path/to/PrismSlang/tools/ci/verify-linux.sh

set -uo pipefail

DOTNET="${DOTNET:-${DOTNET_ROOT:-$HOME/.dotnet}/dotnet}"
if [[ ! -x "$DOTNET" ]]; then
    DOTNET="$(command -v dotnet || true)"
fi
if [[ -z "$DOTNET" || ! -x "$DOTNET" ]]; then
    echo "no dotnet found. Install with:" >&2
    echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0" >&2
    exit 1
fi
export DOTNET_ROOT="$(dirname "$DOTNET")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root" || exit 1

rid_dir="$root/runtimes/linux-x64/native"
if [[ ! -d "$rid_dir" ]]; then
    echo "no linux-x64 payload at $rid_dir" >&2
    echo "run: pwsh tools/fetch-native.ps1 -Rid linux-x64" >&2
    exit 1
fi

status=0

echo "===== BUILD ====="
if ! "$DOTNET" build PrismSlang.slnx -v q --nologo; then status=1; fi

echo "===== TESTS ====="
# Same allocator settings as the linux CI job, so a local run and CI find the same class of bug —
# freed memory is overwritten rather than left intact. See the comment on ci.yml's Test step for
# why MALLOC_CHECK_ is not here with it.
export MALLOC_PERTURB_=165

# A test-host crash is the signal this script exists to catch, so grep for it explicitly:
# the runner can report "Passed!" for the tests that completed before the process died.
out="$("$DOTNET" test PrismSlang.slnx --no-build --nologo -v q 2>&1)"
printf '%s\n' "$out" | grep -E 'Passed!|Failed!|crashed|Aborted'
if printf '%s\n' "$out" | grep -qE 'crashed|Aborted|Failed!'; then status=1; fi

echo "===== SPIKE ====="
if ! "$DOTNET" run --project samples/PrismSlang.Smoke --no-build; then status=1; fi

echo
if [[ $status -eq 0 ]]; then echo "linux-x64: OK"; else echo "linux-x64: FAILED"; fi
exit $status
