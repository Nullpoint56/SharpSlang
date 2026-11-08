#!/usr/bin/env bash
#
# linux-x64 verification: build, full test suite, and the Tier 0 spike.
#
# This is not ceremony. The run that first executed this script found a heap-corrupting ownership
# bug that had been passing green on Windows for the whole of the project's history:
# ISession::loadModule* returns a borrowed pointer, and releasing it over-releases. glibc aborts
# on it; the Windows heap did not. See SlangModule's constructor.
#
# Prerequisites:
#   pwsh tools/fetch-native.ps1 -Rid linux-x64      # stage the payload (runs fine from Windows)
#   a .NET 11 SDK on this machine
#
# Under WSL from Windows:
#   wsl -d Ubuntu -- bash /mnt/e/path/to/SharpSlang/tools/ci/verify-linux.sh

set -uo pipefail

DOTNET="${DOTNET:-${DOTNET_ROOT:-$HOME/.dotnet}/dotnet}"
if [[ ! -x "$DOTNET" ]]; then
    DOTNET="$(command -v dotnet || true)"
fi
if [[ -z "$DOTNET" || ! -x "$DOTNET" ]]; then
    echo "no dotnet found. Install with:" >&2
    echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 11.0 --quality preview" >&2
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

# Kept on disk, not only printed. A round of this script reported FAILED, the terminal it ran in
# was clipped to the last few lines, and the evidence was gone before anyone read it — four green
# rounds later there is still no answer as to what it was. Printing a diagnosis only helps if it
# survives the scrollback of whoever was watching, which is the same instrumentation gap
# again, in a third form (AGENTS.md#allocator-poisoning-and-crash-diagnosis). obj/ is ignored, so these cost nothing but the bytes.
logs="$root/obj"
mkdir -p "$logs"

echo "===== BUILD ====="
"$DOTNET" build SharpSlang.slnx -v q --nologo 2>&1 | tee "$logs/verify-build.log"
if [[ ${PIPESTATUS[0]} -ne 0 ]]; then status=1; fi

echo "===== TESTS ====="
# Same allocator settings as the linux CI job, so a local run and CI find the same class of bug —
# freed memory is overwritten rather than left intact. See the comment on ci.yml's Test step for
# why MALLOC_CHECK_ is not here with it.
export MALLOC_PERTURB_=165

# A test-host crash is the signal this script exists to catch. The suite is a
# Microsoft.Testing.Platform application (xunit.v3, global.json), whose summary is "Test run
# summary: Passed!|Failed!" and the totals; a host that dies reports "error: 1" and a non-zero exit
# code with no failed test at all, so the exit code decides, not the words. The TRX and the
# platform's diagnostic log (which names a test that was running when the host died) are kept
# beside the console log.
out="$("$DOTNET" test --solution SharpSlang.slnx --no-build --report-xunit-trx --report-xunit-trx-filename results.trx \
    --results-directory "$logs/verify-test-results" --diagnostic --diagnostic-output-directory "$logs/verify-test-results/diagnostics" 2>&1)"
test_status=$?
printf '%s\n' "$out" > "$logs/verify-tests.log"
printf '%s\n' "$out" | grep -E 'Test run summary|^  (total|failed|succeeded|skipped|error): |crashed|Aborted|exit code'

# The names too, when there are any. Filtering to the summary meant a failure here reported
# "linux-x64: FAILED" and nothing else, and finding out which test it was took four more runs —
# the same instrumentation gap as before, in the script written to catch that class of problem.
if printf '%s\n' "$out" | grep -qE '^\s*failed '; then
    echo "--- failing tests:"
    printf '%s\n' "$out" | grep -E -A3 '^\s*failed ' | head -60
fi

if [[ $test_status -ne 0 ]] || printf '%s\n' "$out" | grep -qE 'crashed|Aborted|Failed!'; then status=1; fi

echo "===== SPIKE ====="
"$DOTNET" run --project samples/SharpSlang.Smoke --no-build 2>&1 | tee "$logs/verify-spike.log"
if [[ ${PIPESTATUS[0]} -ne 0 ]]; then status=1; fi

echo
if [[ $status -eq 0 ]]; then
    echo "linux-x64: OK"
else
    echo "linux-x64: FAILED"
    echo "  full output: obj/verify-build.log, obj/verify-tests.log, obj/verify-spike.log"
fi
exit $status
