# Third-party notices

PrismSlang's own source is MIT — see [LICENSE](LICENSE). One of the five packages redistributes
binaries it did not write, and this file says which, under what terms, and where the licence texts
are. A second one depends on a Microsoft package that carries binaries, and this file says why that
is a dependency rather than a redistribution.

## What is redistributed, and by which package

| Package | Bundles |
|---|---|
| `PrismSlang.Runtime` | nothing. MIT only. |
| `PrismSlang.Vulkan` | nothing. MIT only. Its enum values are transcribed from the Vulkan specification's `vk.xml`; values are facts, not expression. |
| `PrismSlang.NRI` | nothing. MIT only. It references the `PrismNRI.Descs` package (MIT) for NRI's descriptor types and redistributes no NRI binary. |
| **`PrismSlang`** | binaries from the [Slang](https://github.com/shader-slang/slang) project, one set per runtime identifier |
| `PrismSlang.Native.Dxc` | nothing. MIT only. It depends on Microsoft's `Microsoft.Direct3D.DXC` package and copies `dxcompiler.dll` and `dxil.dll` out of it beside the compiler; see below. Opt-in: nothing else depends on it. |

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

## DirectX Shader Compiler

`PrismSlang.Native.Dxc` redistributes nothing. It is a package with no binary in it: a dependency
on Microsoft's own [`Microsoft.Direct3D.DXC`](https://www.nuget.org/packages/Microsoft.Direct3D.DXC)
package from nuget.org, pinned exactly to the version in `Directory.Build.props`
(`DxcPackageVersion`), and a `build/PrismSlang.Native.Dxc.targets` that copies two files out of
that package's `build/native/bin/x64/` into the consumer's output, beside `slang-compiler.dll`:

- `dxcompiler.dll` — the compiler Slang loads by name when asked for DXIL
- `dxil.dll` — the DXIL validator that signs the result

So `dxcompiler.dll` and `dxil.dll` reach a consumer from Microsoft's package, restored from
nuget.org under the terms that package carries — `LICENSE-LLVM.txt` (the LLVM Release License,
University of Illinois/NCSA, DXC's declared licence) and `LICENSE.txt` (the Microsoft Software
License Terms for the DirectX Shader Compiler) — and never from a PrismSlang package or this
repository. That is the point of the shape. The DirectXShaderCompiler GitHub release carries the
same binaries with Microsoft's terms beside them, and those terms permit distributing the covered
code only inside an application that adds significant primary functionality and forbid providing
it "as a stand-alone offering", which a payload-only NuGet package arguably is. Microsoft's own
package on nuget.org is Microsoft providing it, and a consumer restoring that package takes its
terms directly.

PrismSlang never calls dxc itself: Slang loads it as a downstream compiler when a session targets
DXIL, and PrismSlang only puts it where Slang looks. `SlangGlobalSession.CreateSession` refuses a
DXIL target when dxc is not loadable, naming the package.

The publish workflow refuses to push a `PrismSlang.Native.Dxc` package that carries anything
under `runtimes/`, that lacks the targets file, or whose dependency on `Microsoft.Direct3D.DXC` is
not the pinned version.

## If you build from source

`runtimes/` is gitignored and populated by `tools/fetch-native.ps1`. Until you run it there are no
third-party binaries in your working tree at all, and this file describes nothing that is present.
dxc arrives through NuGet restore, into the package cache, as any other package does.
