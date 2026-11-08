# Third-party notices

SharpSlang's own source is MIT — see [LICENSE](LICENSE). One of the five packages redistributes
binaries it did not write, and this file says which, under what terms, and where the licence texts
are. A second one depends on a Microsoft package that carries binaries, and this file says why that
is a dependency rather than a redistribution.

## What is redistributed, and by which package

| Package | Bundles |
|---|---|
| `SharpSlang.Pipeline` | nothing. MIT only. |
| `SharpSlang.Pipeline.NRI` | nothing. MIT only. It references the `SharpNRI.Descs` package (MIT) for NRI's descriptor types and redistributes no NRI binary. |
| **`SharpSlang`** | binaries from the [Slang](https://github.com/shader-slang/slang) project, one set per runtime identifier |
| `SharpSlang.Dxc` | nothing. MIT only. It depends on Microsoft's `Microsoft.Direct3D.DXC` package and copies `dxcompiler.dll` and `dxil.dll` out of it into the application's output; see below. Opt-in: nothing else depends on it. |

Under `runtimes/<rid>/native/` in the `SharpSlang` package, taken unmodified from Slang's own
prebuilt release for that platform:

- `slang-compiler` — the compiler itself
- `slang-glslang` — Slang's glslang/SPIRV-Tools bundle, which is where `spirv-opt` lives
- `slang-standard-module-<version>` — the precompiled standard library

The version is pinned in `Directory.Build.props` and staged by `tools/fetch-native.ps1`, which
downloads the release archive from `shader-slang/slang` and copies these files without altering
them. No Slang source is present in this repository, and nothing here is a derivative work of it —
SharpSlang calls the published C API.

## Slang

**`Apache-2.0 WITH LLVM-exception`**, as declared by the `LICENSE` file in Slang's own distribution.

That distribution also carries a `LICENSES/` directory covering the third-party code vendored into
Slang itself, under these SPDX identifiers:

`Apache-2.0` · `BSL-1.0` · `CC-BY-4.0` · `LLVM-exception` · `LicenseRef-UOI-NCSA` · `MIT` ·
`Unlicense`

Slang links some of that vendored code statically into the libraries above, and the licences of
cmark, glslang, lz4, miniz, SPIRV-Tools and unordered_dense (and mimalloc, on Windows) ask for
their notices in a binary redistribution. Slang's release carries them in `third-party-notices/`;
they are staged as `runtimes/<rid>/third-party-notices/<name>.txt`.

All three are staged alongside the payload and ship inside the `SharpSlang` package, at
`runtimes/<rid>/LICENSE.txt`, `runtimes/<rid>/LICENSES/` and `runtimes/<rid>/third-party-notices/`. They sit outside `native/`, so NuGet
does not copy them into a consumer's output directory — they travel with the package rather than
with the build. The publish workflow refuses to push a package where any RID is missing them.

Upstream: <https://github.com/shader-slang/slang> — the release matching the pinned version carries
the same texts.

## DirectX Shader Compiler

`SharpSlang.Dxc` redistributes nothing. It is a package with no binary in it: a dependency
on Microsoft's own [`Microsoft.Direct3D.DXC`](https://www.nuget.org/packages/Microsoft.Direct3D.DXC)
package from nuget.org, pinned exactly to the version in `Directory.Build.props`
(`DxcPackageVersion`), and a `build/SharpSlang.Dxc.targets` that copies two files out of
that package's `build/native/bin/x64/` into the root of the consumer's output — beside
`slang-compiler.dll` when the output is flat, one `runtimes/win-x64/native/` away from it in a
portable build:

- `dxcompiler.dll` — the compiler Slang loads by name when asked for DXIL. The pinned version
  also validates and signs what it compiles, with the validator built into it.
- `dxil.dll` — Microsoft's stand-alone DXIL validator. The pinned dxc never loads it: its
  release notes say the compiler always uses its internal validator from 1.8.2505 on, and its
  output was measured byte for byte the same with `dxil.dll` deleted. It is copied for a dxc that
  still signs through it, as releases before 1.8.2502 did; that release's notes record the
  validator's hash being open-sourced.

So `dxcompiler.dll` and `dxil.dll` reach a consumer from Microsoft's package, restored from
nuget.org under the terms that package carries — `LICENSE-LLVM.txt` (the LLVM Release License,
University of Illinois/NCSA, DXC's declared licence) and `LICENSE-MS.txt` (the Microsoft Software
License Terms for the DirectX Shader Compiler), with `LICENCE-MIT.txt` for one header the package
also carries — and never from a SharpSlang package or this repository. That is the point of the
shape. The DirectXShaderCompiler GitHub release carries the same binaries with Microsoft's terms
beside them, and those terms permit distributing the covered code only inside an application that
adds significant primary functionality and forbid providing it "as a stand-alone offering", which
a payload-only NuGet package arguably is. Microsoft's own package on nuget.org is Microsoft
providing it, and a consumer restoring that package takes its terms directly.

SharpSlang never calls dxc itself: Slang loads it as a downstream compiler when a session targets
DXIL, and SharpSlang only copies it into the output and loads it from there before Slang asks for
it by name. `SlangGlobalSession.CreateSession` refuses a DXIL target when dxc is not loadable,
naming the package.

The publish workflow refuses to push a `SharpSlang.Dxc` package that carries anything
under `runtimes/`, that lacks the targets file, or whose dependency on `Microsoft.Direct3D.DXC` is
not the pinned version.

## If you build from source

`runtimes/` is gitignored and populated by `tools/fetch-native.ps1`. Until you run it there are no
third-party binaries in your working tree at all, and this file describes nothing that is present.
dxc arrives through NuGet restore, into the package cache, as any other package does.
