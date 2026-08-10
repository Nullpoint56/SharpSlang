#!/usr/bin/env bash
#
# Documentation checks. Cheap enough to run on every markdown-only change, which is the point:
# those changes skip the build matrix, so this is what stands in for it. It also runs in the main
# job, because two of the three properties below can be broken by a change that touches no markdown
# at all.
#
# Four properties. Every one of them has already been broken here at least once:
#
#   1. Every internal link resolves — the file exists and the anchor matches a real heading.
#      Anchors are generated from heading text, so renaming a heading silently breaks every link
#      to it and nothing complains until a reader clicks.
#
#   2. Every § reference resolves to a heading in design.md or findings.md — from the source *and*
#      from the documents themselves. The two documents share one numbering — §1-§8 in design,
#      §9.x in findings — and ~48 code comments cite a bare section number relying on that.
#      The documents were excluded from this check at first, on the theory that a § in prose is
#      about a neighbour and needs no verifying. Including them found a citation of section 5.4 in
#      design.md, where section 5 has no numbered subsections at all and never has.
#      (Written without the sigil on purpose: this file is scanned too, so a comment about a bad
#      reference, spelled the normal way, is a bad reference.)
#
#   3. Every file the solution lists exists. Renaming binding-surface.md to design.md updated
#      thirteen source references and left the one in PrismSlang.slnx pointing at nothing — a
#      dangling entry the IDE shows as a missing file and no build ever complains about. The
#      solution's non-project files are the docs and the scripts, so they belong in this check.
#
#   4. Every test class the documents name exists. §7's risk table names the test or CI job that
#      enforces each mitigation, rather than citing a section of this document — a row that cited
#      §4.3 in support of a claim §4.3 overturns is what prompted the change. Naming a control
#      only helps if the control is real, so this checks that it is.
#
# What none of this can see: whether a cited section actually *supports* the claim citing it. This
# validates the citation graph's edges, not its arguments. Four of the defects in findings.md were
# of exactly that shape — a comment asserting something the code did not do — and the only reason
# property 4 exists is that naming an artefact instead of a section moves part of that class from
# unverifiable prose into a grep.
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

# Cited from code, scripts, project files — and from the documents. A § in prose was assumed not to
# need checking, because it points at a neighbour and a reader would notice. Nobody noticed 5.4.
#
# obj/ is excluded and must stay excluded: fetch-native.ps1 stages Slang's own release there,
# user-guide markdown included, and scanning it would have this script grading upstream's prose.
cited=$(grep -rhoE '§ ?[0-9]+(\.[0-9]+)?' \
            --include='*.cs' --include='*.csproj' --include='*.props' \
            --include='*.ps1' --include='*.sh' --include='*.yml' \
            --include='*.md' \
            --exclude-dir=obj --exclude-dir=bin --exclude-dir=runtimes . 2>/dev/null \
        | sed 's/§ *//' | sort -u)

for section in $cited; do
    grep -qxF "$section" "$sections" \
        || echo "code cites §$section, which is not a heading in design.md or findings.md" >> "$problems"
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

# The reverse, for the two directories the solution is supposed to mirror. A document nobody can
# find from the IDE is most of the way to a document nobody reads, and adding one is exactly when
# updating the solution gets forgotten.
for expected in docs/*.md tools/*.ps1 tools/ci/*.sh README.md; do
    [ -f "$expected" ] || continue
    grep -qF "Path=\"$expected\"" ./*.slnx 2>/dev/null \
        || echo "$expected exists but no .slnx lists it" >> "$problems"
done

# ---------------------------------------------------------------- 4. named controls exist

echo "==> named tests"

# §7 names the test class or CI job that enforces each mitigation instead of citing a section of
# design.md, after a row spent its life citing the section that refuted it. That only helps if the
# names are real, so: every `SomethingTests` and every `Method_named_like_this` the documents put in
# backticks must be declared under tests/.
#
# Deliberately narrow. Both patterns match this repo's own conventions and nothing else — a class
# suffixed Tests, and xunit's sentence-shaped method names. Widening it to "any backticked
# identifier" would start grading upstream's vocabulary, and a check with false positives gets
# muted, which is worse than not having it.
named=$(grep -rhoE '`([A-Za-z0-9]+Tests|[A-Z][a-z0-9]*(_[a-z0-9]+)+)`' "${DOCS[@]}" 2>/dev/null \
        | tr -d '`' | sort -u)

for name in $named; do
    grep -rqE "(class|void) +$name\b" tests/ --include='*.cs' \
        || echo "docs name \`$name\`, which is not declared anywhere under tests/" >> "$problems"
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
echo "OK - $(wc -l < "$sections") numbered sections; links, § references and solution entries all resolve"
