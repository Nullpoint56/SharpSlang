#!/usr/bin/env bash
#
# Things a reader would notice and no build would: a link that goes nowhere, a comment citing a
# heading that is not there, and a solution entry pointing at a file that is not there. All have
# happened here: renaming a document left thirteen source references updated and the .slnx entry
# dangling, deleting one left a link pointing at nothing, and deleting another left sentences in
# code comments with the citation cut out of the middle.
#
#   1. Every internal link in the documents resolves: the file exists and the anchor matches a
#      real heading.
#   2. Every AGENTS.md#<anchor> a code comment cites is a heading in AGENTS.md, so renaming a
#      heading cannot silently orphan the comments that cite it. Code comments cite AGENTS.md this
#      way and nothing else; a bare section number (the old design.md form) has nothing to resolve
#      against and fails.
#   3. Every file the solution lists exists, and every document and script is listed.
#
# What none of this sees is whether a document is *true*. DocumentedFactsTests and the phrase pins
# beside the tests that measure each figure cover the countable part of that; the rest is reading.
#
# Usage:  bash tools/ci/check-docs.sh

set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."

problems=$(mktemp)
trap 'rm -f "$problems"' EXIT

DOCS=(README.md THIRD-PARTY-NOTICES.md AGENTS.md docs/guide.md docs/upstream.md docs/releases/README.md)

# The source files whose comments cite documents. obj/ is excluded and must stay excluded:
# fetch-native.ps1 stages Slang's own release there, user-guide markdown included, and scanning it
# would have this script grading upstream's prose. So are slang/ (the submodule, checked out for a
# native build) and _build/ (its build trees): Slang's own documents cite their own numbered
# sections (measured). tools/ci/*.txt and *.py are scanned too: their comments carry citations like
# any other.
cited_in_code() {
    grep -rhoE "$1" \
        --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' \
        --include='*.ps1' --include='*.sh' --include='*.yml' --include='*.md' \
        --include='*.txt' --include='*.py' \
        --exclude-dir=obj --exclude-dir=bin --exclude-dir=runtimes --exclude-dir=.git \
        --exclude-dir=slang --exclude-dir=_build . 2>/dev/null
}

# GitHub's anchor rule, near enough: lowercase, drop anything that is not alphanumeric, space or
# hyphen, then spaces to hyphens. Applied to every heading of a file in one pipeline, not a process
# per heading: the guide's traps table links a section from every row, and a loop per heading per
# link took minutes on Windows, where starting a process is slow.
anchors_of() {
    grep -E '^#{1,6} ' "$1" | sed 's/^#\{1,6\} *//' | tr '[:upper:]' '[:lower:]' \
        | sed 's/[^a-z0-9 -]//g; s/ /-/g'
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

# ---------------------------------------------------------------- 2. AGENTS.md anchors

echo "==> AGENTS.md anchors"

cited_in_code 'AGENTS\.md#[a-z0-9-]+' | sed 's/^AGENTS\.md#//' | sort -u | while IFS= read -r anchor; do
    has_anchor AGENTS.md "$anchor" \
        || echo "code cites AGENTS.md#$anchor, which is not a heading in AGENTS.md" >> "$problems"
done

cited_in_code '§ ?[0-9]+(\.[0-9]+)?' | sort -u | while IFS= read -r section; do
    echo "something cites $section, a section number with no document behind it; cite an AGENTS.md#<anchor> instead" >> "$problems"
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
for expected in docs/*.md docs/releases/*.md tools/*.ps1 tools/ci/*.sh README.md AGENTS.md CLAUDE.md LICENSE THIRD-PARTY-NOTICES.md; do
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
echo "OK - links, AGENTS.md anchors and solution entries all resolve"
