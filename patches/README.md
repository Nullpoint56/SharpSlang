# patches/

Changes to Slang applied on top of the pinned tag before the native build. There are none yet.

## Format

Each patch is a `git diff` of `slang/` against the tag `Directory.Build.props` pins (`SlangVersion`),
with a prose header above the first `diff --git` line saying what it changes and why, in Slang's own
terms. `git apply` ignores everything before the first `diff --git`, so the header travels with the
diff.

Named `NNNN-short-description.patch` and applied in name order by `tools/build-native.ps1`. A patch
that is already applied is skipped: the script runs `git apply --check --reverse -C0` first, and a
patch whose reverse applies with no context lines is taken as applied. `-C0` because a later patch
may change a line inside an earlier one's context, which would make a normal reverse check fail on
a tree where both are applied. So `slang/` normally sits with every patch applied and
`git -C slang status` shows the patched files as modified; nothing is ever committed inside it.

Every patch applies cleanly in order onto the tag, and each also applies alone. A patch to a file an
earlier patch touches is cut with the earlier ones applied. Each is dropped at the first Slang tag
that carries its fix.

## Cutting one

```
git -C slang diff -- <paths> > patches/NNNN-short-description.patch
```

then write the header above the diff. Patches follow Slang's coding standard (`slang/CLAUDE.md`,
`slang/CONTRIBUTING.md`, `extras/formatting.sh`), make no semantic change to an existing call, and
keep comments in Slang's voice.

## Moving the pin

Check out the new tag in `slang/` (`git -C slang fetch --tags`, `git -C slang checkout <tag>`,
`git -C slang submodule update --init --recursive`), stage the pointer with
`git update-index --cacheinfo 160000,<commit>,slang`, set `SlangVersion`, and re-run
`tools/build-native.ps1`, which refuses a checkout that is not at the pinned tag and rebuilds from a
fresh tree when the commit, the patches or the options change.
