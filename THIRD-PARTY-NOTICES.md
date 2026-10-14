# Third-party notices

PrismSlang's own source is MIT — see [LICENSE](LICENSE). One of the four packages redistributes
binaries it did not write, and this file says which, under what terms, and where the licence texts
are.

## What is redistributed, and by which package

| Package | Bundles |
|---|---|
| `PrismSlang.Runtime` | nothing. MIT only. |
| `PrismSlang.Vulkan` | nothing. MIT only. Its enum values are transcribed from the Vulkan specification's `vk.xml`; values are facts, not expression. |
| `PrismSlang.NRI` | nothing. MIT only. It references the `PrismNRI.Descs` package (MIT) for NRI's descriptor types and redistributes no NRI binary. |
| **`PrismSlang`** | binaries from the [Slang](https://github.com/shader-slang/slang) project, one set per runtime identifier |

Under `runtimes/<rid>/native/` in the `PrismSlang` package, taken unmodified from Slang's own
prebuilt release for that platform:

- `slang-compiler` — the compiler itself
- `slang-glslang` — Slang's glslang/SPIRV-Tools bundle, which is where `spirv-opt` lives
- `slang-standard-module-<version>` — the precompiled standard library

The version is pinned in `Directory.Build.props` and staged by `tools/fetch-native.ps1`, which
downloads the release archive from `shader-slang/slang` and copies these files without altering
them. No Slang source is present in this repository, and nothing here is a derivative work of it —
PrismSlang calls the published C API.

## Slang

**`Apache-2.0 WITH LLVM-exception`**, as declared by the `LICENSE` file in Slang's own distribution.

That distribution also carries a `LICENSES/` directory covering the third-party code vendored into
Slang itself, under these SPDX identifiers:

`Apache-2.0` · `BSL-1.0` · `CC-BY-4.0` · `LLVM-exception` · `LicenseRef-UOI-NCSA` · `MIT` ·
`Unlicense`

Both are staged alongside the payload and ship inside the `PrismSlang` package, at
`runtimes/<rid>/LICENSE.txt` and `runtimes/<rid>/LICENSES/`. They sit outside `native/`, so NuGet
does not copy them into a consumer's output directory — they travel with the package rather than
with the build. The publish workflow refuses to push a package where any RID is missing them.

Upstream: <https://github.com/shader-slang/slang> — the release matching the pinned version carries
the same texts.

## If you build from source

`runtimes/` is gitignored and populated by `tools/fetch-native.ps1`. Until you run it there are no
third-party binaries in your working tree at all, and this file describes nothing that is present.
