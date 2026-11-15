# Release notes

One file per tag, `v<Version>.md` (for example `v2026210.0.0-beta.1.md`), written in the pull
request that bumps the version. No release goes out without its file.

Notes are compact lists of what happened, never why or how. Only these sections, in this order,
and only when non-empty:

1. **Added**
2. **Fixed**
3. **Breaking changes**
4. **Verified**: where the release was verified — the hardware, the compile targets and devices,
   and the commit measured, linked to the coverage report at the tag — plus what CI ran on the
   tagged commit.

Each bullet is one terse line. API changes are listed mechanically, from the public API diff; the
notes do not repeat Slang's changelog, attribute changes to sources, or explain motives. The release
body adds the generated parts: the Slang release and the dxc package version bound, the API changes
per package, and a link per package and version.

```markdown
## Fixed
- SharpSlang.Dxc declares .NET 11 (the package page showed no framework)

## Breaking changes
- Package versions follow Slang: every package's major is the Slang version as YYYYRRP

## Verified
- <GPU>, SPIR-V on Vulkan and DXIL on D3D12: <commit> (coverage)
- WARP (D3D12), lavapipe (Vulkan): CI on the tagged commit
```
