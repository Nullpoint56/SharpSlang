#!/usr/bin/env bash
#
# Documentation checks. Cheap enough to run on every markdown-only change, which is the point:
# those changes skip the build matrix, so this is what stands in for it.
#
# Two properties, both of which this repository has already got wrong once:
#
#   1. Every internal link resolves — the file exists and the anchor matches a real heading.
#      Anchors are generated from heading text, so renaming a heading silently breaks every link
#      to it and nothing complains until a reader clicks.
#
#   2. Every § reference in the source resolves to a heading in design.md or findings.md.
#      The two documents share one numbering — §1-§8 in design, §9.x in findings — and ~48 code
#      comments cite a bare section number relying on that. Splitting the documents was safe only
#      because the numbers are unique; this is what keeps that true rather than assuming it.
#
# Usage:  bash tools/ci/check-docs.sh

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

problems=$(mktemp)
trap 'rm -f "$problems"' EXIT

DOCS=(README.md docs/design.md docs/findings.md docs/guide.md)

# GitHub's anchor rule, near enough: lowercase, drop anything that is not alphanumeric, space or
# hyphen, then spaces to hyphens.
# The trailing newline matters: anchors_of feeds these to `grep -qxF`, and without it every anchor
# in a document concatenates into one line that matches nothing. That was this script's first bug.
anchor_of() {
    printf '%s\n' "$1" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9 -]//g; s/ /-/g'
}

# Every heading in a file, as its anchor, one per line.
anchors_of() {
    grep -E '^#{1,6} ' "$1" | sed 's/^#\{1,6\} *//' | while IFS= read -r heading; do
        anchor_of "$heading"
    done
}

# Whether a file has a heading with this anchor.
#
# Written with a variable rather than `anchors_of "$1" | grep -qxF "$2"`, which is the obvious form
# and is wrong under `set -o pipefail`: `grep -q` exits the moment it matches, the producer takes a
# SIGPIPE, and pipefail reports the pipeline as failed *because* the match succeeded. It only
# appeared to work for a document's last heading, where the producer had already finished writing.
# This script's second bug, and a better one than its first.
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
            # Collapse one level of ../ so docs/../README.md resolves.
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

# ---------------------------------------------------------------- 2. § references from the source

echo "==> section references"

sections=$(mktemp)
trap 'rm -f "$problems" "$sections"' EXIT
{
    grep -hoE '^#{2,3} [0-9]+(\.[0-9]+)?' docs/design.md docs/findings.md | sed 's/^#* *//'
} | sort -u > "$sections"

if [ ! -s "$sections" ]; then
    echo "found no numbered headings in docs/ - the extraction is broken, not the docs" >> "$problems"
fi

# Cited from code, scripts and project files. Excludes the docs themselves: a § inside findings.md
# is prose about a neighbouring section and is covered by the link check where it is a link.
cited=$(grep -rhoE '§ ?[0-9]+(\.[0-9]+)?' \
            --include='*.cs' --include='*.csproj' --include='*.props' \
            --include='*.ps1' --include='*.sh' --include='*.yml' . 2>/dev/null \
        | sed 's/§ *//' | sort -u)

for section in $cited; do
    grep -qxF "$section" "$sections" \
        || echo "code cites §$section, which is not a heading in design.md or findings.md" >> "$problems"
done

# ---------------------------------------------------------------- verdict

if [ -s "$problems" ]; then
    echo
    echo "documentation problems:"
    sed 's/^/    /' "$problems"
    count=$(wc -l < "$problems")
    echo
    echo "::error::$count documentation problem(s)"
    exit 1
fi

echo
echo "docs OK - $(wc -l < "$sections") numbered sections, all links and § references resolve"
