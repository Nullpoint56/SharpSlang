#!/usr/bin/env bash
# Do the runners cook the same asset? Three rules, because the answer turned out to be "mostly".
#
# Every other oracle in this suite runs inside one process on one machine — agreement, the
# validation layers, the allocators, the API approval. All of them can be green on four runners that
# disagree with each other, and for a cook farm that is the failure that matters.
#
# The first run of this check found two things at once. The layout — the half PrismSlang produces —
# is identical everywhere. The module is identical between *operating systems* (win-x64 and
# linux-x64 agree byte for byte) and differs between *architectures*, for exactly the three corpus
# shaders that use SV_VertexID. That is Slang's codegen, not ours, and it is not something this
# repository can fix. §9.33.
#
# So one flat "everything must match" is the wrong shape: it would be permanently red and would stop
# reporting anything. The rules that are actually true:
#
#   1. layout-digests.txt must be identical on every leg.            fatal
#   2. spirv-digests.txt must be identical within an architecture.   fatal
#   3. across architectures, the set of differing shaders must be
#      exactly the expected list — it may not grow, and it may not
#      silently shrink either.                                       fatal
#
# Rule 3 is an exception list rather than a golden file, and the difference matters. A golden file
# records expected *values* and goes stale on every Slang bump; this records expected
# *disagreements*, checked in both directions, so upstream fixing one is a failing test rather than
# silence.
#
# usage: compare-digests.sh <directory> [expected-leg-count]
set -u

dir="${1:?usage: compare-digests.sh <directory> [expected-leg-count]}"
expected="${2:-4}"
expected_list="$(dirname "$0")/cross-arch-differences.txt"

status=0

# The instrument before the measurement. spirv-worddiff.py is what anyone reaching for an
# explanation of a disagreement below will run, and it has been wrong twice: once reporting only
# half of a difference, once calling a swap of two operands a renumbering — the one direction a tool
# built to justify "cosmetic" must never fail in (§9.38). Five hand-built modules, five verdicts,
# two seconds. A tool nobody checks is a tool nobody should quote.
worddiff="$(dirname "$0")/spirv-worddiff.py"
if command -v python3 >/dev/null 2>&1 && [ -f "$worddiff" ]; then
    echo "=== spirv-worddiff self-test"
    # Captured rather than piped. `python3 … | sed …` under `set -u` with no pipefail reports sed's
    # exit code, so a failing self-test would print BAD lines and pass — the exact shape of defect
    # the rest of this file exists to catch.
    if ! selftest_out="$(python3 "$worddiff" --selftest 2>&1)"; then
        status=1
        echo "::error::spirv-worddiff.py failed its own self-test; do not trust its verdicts."
    fi
    printf '%s\n' "$selftest_out" | sed 's/^/    /'
else
    echo "=== spirv-worddiff self-test: skipped, no python3"
fi

# The leg is the artifact directory, which is the first path component under $dir. Not the parent of
# the file: upload-artifact preserves the path the file was found at, so every leg's digests sit in
# .../bin/Release/net10.0/ and using `dirname` labelled all four of them "net10.0" — which is how
# the first real disagreement got reported as "net10.0 disagrees with net10.0".
leg_of() { printf '%s\n' "${1#"$dir"/}" | cut -d/ -f1 | sed 's/^spirv-digests-//'; }
arch_of() { case "$1" in *-arm64) echo arm64 ;; *-x64) echo x64 ;; *) echo unknown ;; esac; }

compare_all() {   # <filename> <human name>
    local file="$1" what="$2"
    local -a files
    mapfile -t files < <(find "$dir" -name "$file" | sort)

    # A comparison that finds nothing to compare must fail: "all legs agreed" and "no leg reported"
    # are the same green otherwise, and the second is what a renamed artifact produces.
    if [ "${#files[@]}" -lt "$expected" ]; then
        echo "::error::found ${#files[@]} $file under $dir, expected $expected."
        echo "A leg did not upload its digests — which is not the same as the legs agreeing."
        find "$dir" -type f | sed 's/^/    /'
        status=1
        return
    fi

    echo "=== $what: ${#files[@]} legs"
    local reference="${files[0]}"
    for f in "${files[@]:1}"; do
        if ! diff -q "$reference" "$f" >/dev/null; then
            echo "::error::$what differs: $(leg_of "$f") vs $(leg_of "$reference")"
            diff "$reference" "$f" | sed 's/^/    /'
            status=1
        fi
    done
    [ "$status" -eq 0 ] && echo "    all agree"
}

# ---------------------------------------------------------------- 1. the layout, everywhere
compare_all layout-digests.txt "layout"

# ---------------------------------------------------------------- 2 and 3. the module
mapfile -t modules < <(find "$dir" -name spirv-digests.txt | sort)

if [ "${#modules[@]}" -lt "$expected" ]; then
    echo "::error::found ${#modules[@]} spirv-digests.txt under $dir, expected $expected."
    find "$dir" -type f | sed 's/^/    /'
    exit 1
fi

echo "=== module: ${#modules[@]} legs"
for f in "${modules[@]}"; do echo "    $(leg_of "$f")  [$(arch_of "$(leg_of "$f")")]"; done

# Within an architecture: identical, no exceptions.
for arch in x64 arm64; do
    reference=""
    agreed=1
    for f in "${modules[@]}"; do
        [ "$(arch_of "$(leg_of "$f")")" = "$arch" ] || continue
        if [ -z "$reference" ]; then reference="$f"; continue; fi
        if ! diff -q "$reference" "$f" >/dev/null; then
            echo "::error::two $arch legs disagree: $(leg_of "$f") vs $(leg_of "$reference")."
            echo "The same architecture on two operating systems must produce the same module."
            diff "$reference" "$f" | sed 's/^/    /'
            agreed=0
            status=1
        fi
    done
    # Only claim agreement when there was agreement. A log that reports a disagreement and then
    # says the same legs agree is worse than one that says nothing.
    [ -n "$reference" ] && [ "$agreed" -eq 1 ] && echo "    $arch legs agree with each other"
done

# Across architectures: exactly the expected set of shaders may differ.
x64_file=""; arm_file=""
for f in "${modules[@]}"; do
    case "$(arch_of "$(leg_of "$f")")" in
        x64)   [ -z "$x64_file" ] && x64_file="$f" ;;
        arm64) [ -z "$arm_file" ] && arm_file="$f" ;;
    esac
done

if [ -n "$x64_file" ] && [ -n "$arm_file" ]; then
    # First field of every line that differs, which is the shader name.
    differing="$(diff "$x64_file" "$arm_file" | grep -E '^[<>]' | awk '{print $2}' | sort -u)"
    expected_names="$(grep -vE '^\s*(#|$)' "$expected_list" | awk '{print $1}' | sort -u)"

    unexpected="$(comm -23 <(printf '%s\n' "$differing") <(printf '%s\n' "$expected_names"))"
    healed="$(comm -13 <(printf '%s\n' "$differing") <(printf '%s\n' "$expected_names"))"

    if [ -n "$unexpected" ]; then
        echo "::error::new shaders differ between x64 and arm64:"
        printf '%s\n' "$unexpected" | sed 's/^/    /'
        echo "Add them to $expected_list with a reason, or find out why they moved."
        status=1
    fi

    if [ -n "$healed" ]; then
        echo "::error::these no longer differ between x64 and arm64:"
        printf '%s\n' "$healed" | sed 's/^/    /'
        echo "Good news, and $expected_list is what needs editing — an exception list that"
        echo "outlives its exception stops being a record and starts being a blindfold."
        status=1
    fi

    if [ -z "$unexpected" ] && [ -z "$healed" ]; then
        count="$(printf '%s\n' "$differing" | grep -c . || true)"
        echo "    x64 and arm64 differ on exactly the $count expected shader(s)"
    fi
fi

exit "$status"
