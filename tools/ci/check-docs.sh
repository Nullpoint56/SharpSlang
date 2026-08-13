#!/usr/bin/env bash
#
# Two things a reader would notice and no build would: a link that goes nowhere, and a solution
# entry pointing at a file that is not there. Both have happened here — renaming a document left
# thirteen source references updated and the .slnx entry dangling, and deleting one left a link in
# design.md pointing at nothing.
#
# It used to check more. A §-reference validator existed because two documents shared one numbering
# and ~170 code comments cited it; one of those documents is gone and the citations with it. What is
# left is small enough to read.
#
#   1. Every internal link resolves — the file exists and the anchor matches a real heading.
#   2. Every § a code comment cites is a heading in design.md, whose numbers are cited by bare
#      number and therefore must not move.
#   3. Every file the solution lists exists, and every document and script is listed.
#
# What none of this sees is whether a document is *true*. DocumentedFactsTests covers the countable
# part of that; the rest is reading.
#
# Usage:  bash tools/ci/check-docs.sh

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

problems=$(mktemp)
sections=$(mktemp)
trap 'rm -f "$problems" "$sections"' EXIT

DOCS=(README.md THIRD-PARTY-NOTICES.md docs/design.md docs/guide.md)

# GitHub's anchor rule, near enough: lowercase, drop anything that is not alphanumeric, space or
# hyphen, then spaces to hyphens.
anchor_of() {
    printf '%s\n' "$1" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9 -]//g; s/ /-/g'
}

anchors_of() {
    grep -E '^#{1,6} ' "$1" | sed 's/^#\{1,6\} *//' | while IFS= read -r heading; do
        anchor_of "$heading"
    done
}

# Written with a variable rather than piping into `grep -q`, which is the obvious form and is wrong
# under `set -o pipefail`: grep exits on the first match, the producer takes a SIGPIPE, and the
# pipeline reports failure *because* the match succeeded.
has_anchor() {
    local anchors
    anchors=$(anchors_of "$1")
    printf '%s\n' "$anchors" | grep -qxF "$2"
}

# ---------------------------------------------------------------- 1. internal links

echo "==> internal links"
for src in "${DOCS[@]}"; do
    [ -f "$src" ] || { echo "missing document: $src" >> "$problems"; continue; }
    dir=$(dirname "$src")

    grep -oE '\]\([^)]+\)' "$src" | sed 's/^](//; s/)$//' | while IFS= read -r link; do
        case "$link" in http://*|https://*|mailto:*) continue;; esac

        file="${link%%#*}"
        anchor=""
        case "$link" in *#*) anchor="${link#*#}";; esac

        if [ -n "$file" ]; then
            target="$dir/$file"
            target="${target#./}"
            while [[ "$target" == */../* ]]; do
                target=$(printf '%s' "$target" | sed 's|[^/]*/\.\./||')
            done
            if [ ! -f "$target" ]; then
                echo "$src -> $link : no such file ($target)" >> "$problems"
                continue
            fi
        else
            target="$src"
        fi

        if [ -n "$anchor" ] && ! has_anchor "$target" "$anchor"; then
            echo "$src -> $link : no heading with that anchor in $target" >> "$problems"
        fi
    done
done

# ---------------------------------------------------------------- 2. § references

echo "==> section references"

grep -hoE '^#{2,3} [0-9]+(\.[0-9]+)?' docs/design.md | sed 's/^#* *//' | sort -u > "$sections"

if [ ! -s "$sections" ]; then
    echo "found no numbered headings in design.md - the extraction is broken, not the doc" >> "$problems"
fi

# obj/ is excluded and must stay excluded: fetch-native.ps1 stages Slang's own release there,
# user-guide markdown included, and scanning it would have this script grading upstream's prose.
cited=$(grep -rhoE '§ ?[0-9]+(\.[0-9]+)?' \
            --include='*.cs' --include='*.csproj' --include='*.props' \
            --include='*.ps1' --include='*.sh' --include='*.yml' --include='*.md' \
            --exclude-dir=obj --exclude-dir=bin --exclude-dir=runtimes . 2>/dev/null \
        | sed 's/§ *//' | sort -u)

for section in $cited; do
    grep -qxF "$section" "$sections" \
        || echo "code cites §$section, which is not a heading in design.md" >> "$problems"
done

# ---------------------------------------------------------------- 3. solution file references

echo "==> solution references"

for solution in *.slnx; do
    [ -f "$solution" ] || continue

    grep -oE '(File|Project) Path="[^"]+"' "$solution" \
        | sed 's/.*Path="//; s/"$//; s|\\|/|g' \
        | while IFS= read -r referenced; do
            [ -f "$referenced" ] \
                || echo "$solution references $referenced, which does not exist" >> "$problems"
        done
done

# The reverse, for the directories the solution mirrors. A document nobody can find from the IDE is
# most of the way to a document nobody reads.
for expected in docs/*.md tools/*.ps1 tools/ci/*.sh README.md LICENSE THIRD-PARTY-NOTICES.md; do
    [ -f "$expected" ] || continue
    grep -qF "Path=\"$expected\"" ./*.slnx 2>/dev/null \
        || echo "$expected exists but no .slnx lists it" >> "$problems"
done

# ---------------------------------------------------------------- verdict

if [ -s "$problems" ]; then
    echo
    echo "documentation problems:"
    sed 's/^/    /' "$problems"
    echo
    echo "::error::$(wc -l < "$problems") documentation problem(s)"
    exit 1
fi

echo
echo "OK - links, § references and solution entries all resolve"
