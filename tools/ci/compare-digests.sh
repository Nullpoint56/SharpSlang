#!/usr/bin/env bash
# Do the four runners cook the same asset?
#
# Every other oracle in this suite runs inside one process on one machine — agreement, the
# validation layers, the allocators, the API approval. All four can be green on four runners that
# disagree with each other, and for a cook farm that is the failure that matters: if a developer's
# Windows box and a Linux runner emit different bytes for the same shader, content hashes stop
# matching, incremental cooks redo everything, and the asset that ships is not the asset that was
# tested.
#
# Deliberately not a golden file in the tree. A golden answers "did the output change", which is the
# wrong question — it goes stale on every Slang bump and gets regenerated whenever it is
# inconvenient. Comparing the legs to each other answers "do the build machines agree", which needs
# no maintenance and cannot rot.
#
# It found one on the first run it was pointed at, before ever reaching CI: all 28 SPIR-V modules
# matched between win-x64 and linux-x64 and all 28 layouts differed, because the layout JSON that
# goes into a shader package was written with Environment.NewLine (§9.30).
#
# usage: compare-digests.sh <directory> [expected-leg-count]
set -u

dir="${1:?usage: compare-digests.sh <directory> [expected-leg-count]}"
expected="${2:-4}"

mapfile -t files < <(find "$dir" -name 'spirv-digests.txt' | sort)

# The safeguard that keeps this from being §9.18's driver. A comparison that finds nothing to
# compare must fail: "all legs agreed" and "no leg reported" are the same green otherwise, and the
# second one is what a renamed artifact or a job that died early actually produces.
if [ "${#files[@]}" -lt "$expected" ]; then
  echo "::error::found ${#files[@]} digest file(s) under $dir, expected $expected."
  echo "A leg did not upload its digests — which is not the same as the legs agreeing."
  find "$dir" -type f | sed 's/^/    /'
  exit 1
fi

echo "=== ${#files[@]} legs reported"
for f in "${files[@]}"; do
  # The artifact directory is named for the RID, so the parent is the leg.
  echo "    $(basename "$(dirname "$f")")  $(wc -l < "$f") shaders"
done

reference="${files[0]}"
status=0

for f in "${files[@]:1}"; do
  if ! diff -q "$reference" "$f" >/dev/null; then
    echo
    echo "::error::$(basename "$(dirname "$f")") disagrees with $(basename "$(dirname "$reference")")"
    # Printed in full rather than summarised: which shader and which half of it — the module or
    # the layout — is the entire diagnosis, and it costs a few lines.
    diff "$reference" "$f" | sed 's/^/    /'
    status=1
  fi
done

if [ "$status" -ne 0 ]; then
  echo
  echo "The same source compiled to different artifacts on different machines. A cooked asset now"
  echo "depends on which runner cooked it. docs/findings.md §9.30 has the one this check found."
  exit 1
fi

echo
echo "all ${#files[@]} legs agree, shader for shader, on both the module and the layout"
