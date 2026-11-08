# SharpSlang

**C# bindings over the [Slang](https://github.com/shader-slang/slang) shader compiler — compilation
and full reflection.**

Slang compiles a modern shading language to SPIR-V and can describe exactly what the result needs
bound to it. SharpSlang exposes both from C#: compile a shader, read back its descriptor sets, push
constants, vertex inputs and buffer layouts, and hand a renderer the pipeline state that goes with
it.

Pure managed P/Invoke. No C++/CLI and no native source in this repository — the payload is Slang's
own prebuilt release, pinned to a version and fetched by a script.

---

## Status

`2026210.0.0-beta.1`, against Slang `v2026.21`, on .NET 11. Every package's major is the Slang
version as `YYYYRRP`: 2026.21 is 2026210.

Beta because nothing has exercised it but a test suite — thorough, but not a shipping game hitting
the reflection surface with shaders nobody anticipated, and leaving beta waits on that. Expect breaking
changes until then.

## What you get

Six packages. Which ones you want depends on where the code runs and what it talks to.

### `SharpSlang` and `SharpSlang.Pipeline.Bake` — compile and cook, in your asset pipeline

`SharpSlang` is the binding: the compiler, and Slang's reflection API one class per object. It
carries the native Slang payload, so it is the one you keep out of a shipped game, and it names no
pipeline package. `SharpSlang.Pipeline.Bake` is the cooker on top of its public API: the
reflection snapshot (`Reflect()`) and the baker. Reference the cooker; the binding comes with it.

```csharp
using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
using var module  = session.LoadModule("standard_surface");
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

var spirv  = program.GetEntryPointCodeBytes(0, targetIndex: 0);
var layout = program.Reflect(targetIndex: 0);     // descriptor sets, push constants, vertex inputs, byte layouts
```

Slang's standard modules (`import slang.neural`, `slang.numerics`, `slang.functional`,
`experimental.workgraph`) come with it, beside the compiler in every kind of output; Slang declares
them experimental, so a session that imports one sets `SessionOptions.ExperimentalFeatures`.

You also get specialization constants, generics and type conformances, the declaration tree, the
byte layout of a uniform buffer down to matrix and array strides, `import`/`#include` served from
your own filesystem (an engine VFS or a pack file), and a bake step that writes the SPIR-V and the
layout into one file with content hashes over the whole import graph:

```csharp
ShaderPackage package = ShaderPackageBaker.Bake(program, module, targetIndex: 0)
    .WithNriProjection(new NriProjectionOptions { UnboundedArrayCap = 1024 });   // optional: NRI descs too
package.WriteToFile("cooked/standard_surface.pslb");

if (!package.IsCurrent(SlangGlobalSession.BuildTag))    // for the build system, next time round
    Rebuild(package.StaleDependencies());
```

### `SharpSlang.Pipeline` — read baked shaders, in the shipped game

No native dependency at all. A game references this and carries no compiler: it brings
`SharpSlang.Descs`, Slang's enums and structs generated from `slang.h` as plain types, and nothing
native.

```csharp
var package = ShaderPackage.ReadFromFile("cooked/standard_surface.pslb");

foreach (var entry in package.EntryPoints)
{
    var code = entry.Code;                      // SPIR-V, ready for vkCreateShaderModule
    var name = entry.ModuleEntryPointName;      // what pName needs — Slang calls them all "main"
}

ProgramLayout layout = package.Layout;          // the reflection snapshot, as baked
```

`AssemblySplitTests` builds a consumer referencing only this and `SharpSlang.Pipeline.NRI`, and
checks the output carries no `slang-compiler`, no `slang-glslang`, no `NRI` library and no
`runtimes/` tree.

### `SharpSlang.Pipeline.NRI` — turn reflection into an NRI pipeline layout

Also no native dependency. Projects a reflection snapshot onto [NRI](https://github.com/NVIDIA-RTX/NRI) through
[SharpNRI](https://github.com/Nullpoint56/SharpNRI). It references `SharpNRI.Descs` — NRI's
descriptor structs and enums as plain types — and nothing with a native library in it, so a cooker
can project on a machine with no driver and a game carries no `NRI.dll` it did not ask for.

```csharp
NriPipelineProjection nri = package.ReadNriProjection()               // stored at bake time,
    ?? layout.ToNri(new NriProjectionOptions { UnboundedArrayCap = 1024 });  // or projected now

using var pipelineLayout = device.CreatePipelineLayout(nri.Layout);   // a SharpNRI NriDevice

var (set, range) = nri.Range("albedo")!.Value;                        // by shader parameter name
var descriptorSet = pool.AllocateDescriptorSet(pipelineLayout, set.SetIndex);
descriptorSet.Update(range.RangeIndex, 0, albedoView);

var shader = package.EntryPoints[0].ToNriShaderDesc();               // stage, bytes, entry name
```

The desc is NRI's; what the projection adds is the bookkeeping NRI's API is indexed by and the
desc cannot carry: the set index and range index each shader parameter became, the root-constant
index of a `[vk::push_constant]` block, vertex attributes with their locations and formats, and the
fragment outputs. Register spaces, `ARRAY` flags, stage masks and the fragment-only rule for input
attachments follow NRI's own Vulkan backend, and `NriDeviceTests` builds every corpus shader into
real NRI pipeline layouts and pipelines on a validated device to check that they do.

The engine projects at bake time: the cooker stores the projection in the package
(`WithNriProjection`) and the game reads the descs back (`ReadNriProjection`) without projecting;
`NriPackageTests` holds what is read back to a fresh projection, field by field, on every corpus
shader for both targets.

Measured against both NRI backends: Vulkan from SPIR-V and D3D12 from DXIL. NRI has no
specialization constants, so a shader that uses them is specialized at link time; NRI's `Format`
has no 64-bit vertex formats, so a `double` attribute is refused rather than guessed.

### `SharpSlang.Dxc` — DXIL, for D3D12

Slang emits DXIL by handing HLSL to dxc, and Slang's own release ships no dxc. This package carries
no binary either: it depends on Microsoft's own
[`Microsoft.Direct3D.DXC`](https://www.nuget.org/packages/Microsoft.Direct3D.DXC) package at a
pinned version and copies `dxcompiler.dll` and `dxil.dll` from it into the application's output on
`win-x64`: beside the compiler in a flat build, one `runtimes\win-x64\native\` away from it in a
portable one, and SharpSlang loads dxc from there before Slang looks for it. Add it to the asset
pipeline that targets D3D12 and nothing else:

```csharp
using var session = global.CreateSession(new SessionOptions
{
    Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL, Profile = "sm_6_5" }],   // name the shader model
});
```

Without it, a session asking for DXIL is refused at creation with the package named, rather than
failing inside the first compile. A Vulkan-only pipeline never needs it.

## Using it

Packages are published on a `v*` tag, twice: to **GitHub Packages**, and as a **GitHub release**
carrying the same `.nupkg` files for anyone without feed access. Add the feed, then take what you
need:

```bash
dotnet nuget add source https://nuget.pkg.github.com/Nullpoint56/index.json \
    --name sharpslang --username <you> --password <a PAT with read:packages>

dotnet add package SharpSlang.Pipeline.Bake   # asset pipeline (brings SharpSlang and the compiler)
dotnet add package SharpSlang.Pipeline    # shipped game
dotnet add package SharpSlang.Pipeline.NRI        # renderer, talking NRI through SharpNRI
dotnet add package SharpSlang.Dxc # asset pipeline, only if it emits DXIL for D3D12
dotnet add package SharpSlang.Descs      # a library that only names Slang's types (the others bring it)
```

`SharpSlang.Pipeline.NRI` depends on `SharpNRI.Descs` from nuget.org, at exactly the version pinned as
`SharpNriDescsPackageVersion` in `Directory.Build.props`. The device that takes its descs is
`SharpNRI`, which a renderer references itself: a release that depends on that same
`SharpNRI.Descs`, or NuGet warns NU1608 and the two disagree about NRI's vocabulary.

The usual shape of an integration is: cook offline with `SharpSlang.Pipeline.Bake`, bake a `.pslb`
per shader with its NRI projection stored, ship those, and at run time read them with
`SharpSlang.Pipeline` and the descs with `SharpSlang.Pipeline.NRI`. **[docs/guide.md](docs/guide.md)** walks through each
step, plus lifetime, threading, diagnostics, and the traps worth knowing before you meet them.

## What it does not decide for you

It reports what the shader requires and stops there:

- no vertex buffer bindings, strides or input rates — only attribute locations and formats
- no meaning attached to a set index ("set 1 is per-material")
- no descriptor pool sizing or update strategy
- no legacy `spCompileRequest*` API, bytecode VM, coverage tracing, or `slang-rhi`

Where a mapping is genuinely ambiguous it throws rather than guessing — two push-constant buffers, a
vertex input with no attribute format, a buffer Slang placed nowhere — and the message names the
shader construct that caused it.

## Platforms

| RID | CI | Notes |
|---|---|---|
| `win-x64` | `windows-latest` | D3D12 on WARP, the pixel test included; DXIL and HLSL judged by dxc |
| `linux-x64` | `ubuntu-24.04` | also runs the NRI Vulkan device tests on lavapipe, under the Khronos validation layer, the pixel test included; SPIR-V, GLSL and WGSL judged by spirv-val, glslang and naga |
| `linux-arm64` | `ubuntu-24.04-arm` | the only leg exercising ARM64 calling conventions; its SPIR-V judged by spirv-val |
| `osx-arm64` | `macos-15` | the only leg on a non-glibc allocator and loader; Metal judged by `xcrun metal`; on demand and before every release |

Windows and both Linux legs run the full suite when CI is started by hand on a branch, which is
the only way a branch is tested, and on master only for code that no branch run has already passed.
The macOS leg runs when a manual run asks for it, and before every release, because a macOS minute
costs ten times a Linux one. All four ship in the `SharpSlang` package. The NRI device tests, which
are every test that builds a pipeline or dispatches a shader, run on `win-x64` and `linux-x64`;
`linux-arm64` skips them because SharpNRI ships no library for it, and macOS by decision. The
projection tests that need no device run everywhere. The Vulkan half needs a Khronos validation layer as new as the
Vulkan headers NRI is built with (the Vulkan SDK's 1.4.363 is; Ubuntu 24.04's 1.3.275 is not, and
the tests skip with its error quoted). If you
content-address cooked assets, note that x64 and ARM64 emit byte-identical SPIR-V within an
architecture and differ on shaders using `SV_VertexID`; the modules are equivalent but will never
compare equal. [The traps table](docs/guide.md#traps-worth-knowing) has the detail.

.NET 11 and C# 15. The Slang version is pinned in `Directory.Build.props`, and that pin drives both
the payload fetch and the binding regeneration, because Slang's ABI is not stable across versions.

## Building from source

```bash
pwsh tools/fetch-native.ps1 -Rid win-x64 -Profile Spirv   # stage the pinned Slang payload
dotnet build SharpSlang.slnx -c Release
dotnet test  SharpSlang.slnx -c Release
pwsh tools/check.ps1                                     # lint: analyzers and formatting, as CI runs it
dotnet run --project samples/SharpSlang.Smoke -c Release  # source -> SPIR-V, end to end
```

`runtimes/` is gitignored, so the fetch is the first step on a fresh clone. `-Profile Spirv` stages
`slang-compiler` and `slang-glslang`; the latter is where `spirv-opt` lives, and without it every
shader compiles about 23% larger while Slang mentions it exactly once per session.

SharpNRI, which the NRI projection and its device tests use, restores from nuget.org like every
other package.

A C++ compiler is optional but wanted. Six tests generate a probe, compile it against the pinned
`slang.h`, and compare struct sizes, field offsets, bitfield packing and every function signature —
all 279 `DllImport`s and all 231 vtable methods — against what the header actually declares. It is
the only check in the suite that can see a layout error, and the only one that can see a wrong
parameter list on a vtable slot nothing has ever called. They skip with instructions where no
compiler is found; CI has one on every runner.

**1187 tests** run on every leg, against oracles rather than expected values typed
by hand: the emitted SPIR-V, Slang's own alternate route to each fact, the Khronos validation
layers and the D3D12 debug layer judging real NRI pipelines built from a **33-shader corpus**, and Slang's own test suite — 31 shaders whose expected reflection the Slang
team wrote, compared against both `spReflection_ToJson` and this package's projection. It also
**dispatches 56 shaders** from Slang's `tests/compute` on a validated NRI Vulkan device: buffers,
storage images, sampled textures and samplers bound at the register space and range the NRI
projection reports, and the readback compared with the words Slang's own tests record. `NativeExportTests` resolves all 279 `DllImport` entry points against the
loaded library.

A vertex and fragment program compiled to SPIR-V and to DXIL, projected, built into an NRI graphics
pipeline and rendered on Vulkan and on D3D12 must match an image computed on the CPU, **pixel for
pixel**. Every corpus shader, and Slang's `tests/compute`, is compiled to **every target** and
judged by that target's own tool: `spirv-val` (SPIR-V), dxc's validator in `dxil.dll` (DXIL),
`glslangValidator` (GLSL), dxc (HLSL), `xcrun metal` (Metal, on macOS), naga (WGSL), NVRTC (CUDA)
and the host's C++ compiler (C++), and each tool is shown broken output once to prove it refuses it. Each tool is found on the machine at run time; a missing one is
a skip that names it, and CI fails a leg where a tool it installs did not run.

## Documentation

| | |
|---|---|
| **[guide.md](docs/guide.md)** | how to use it — compiling, reflecting, projecting, baking, lifetime, threads, and the traps |
| **[AGENTS.md](AGENTS.md)** | how it is built and why, for contributors — the layers, the pin, the decisions, and every measured oddity of Slang's with where it is handled |
| **[upstream.md](docs/upstream.md)** | the Slang defects found here, as report text |

Most of the difficulty in binding Slang is not in the signatures. `loadModule` returns a borrowed
pointer where every other call returns an owned one; `getEntryPointCode` names every module `main`;
a specialization constant used as an array length makes reflection end the process on Linux and
throw on Windows. The guide's [traps table](docs/guide.md#traps-worth-knowing) is the short version
of what those cost to find out.

## How it was built

`dotnet/Native/` is statically generated by ClangSharp from Slang's headers.

Everything else — the rest of `SharpSlang`, `SharpSlang.Pipeline`, `SharpSlang.Pipeline.NRI`,
`SharpSlang.Dxc`, the tests and these documents — was written with AI
assistance.

## What it uses

| | |
|---|---|
| [Slang](https://github.com/shader-slang/slang) | the compiler this binds. Its prebuilt release is the native payload |
| [ClangSharp](https://github.com/dotnet/ClangSharp) | generates `dotnet/Native/` from Slang's headers |
| [.NET 11](https://github.com/dotnet/runtime) | the runtime, and the P/Invoke and `[UnmanagedCallersOnly]` machinery |
| [SharpNRI.Descs](https://www.nuget.org/packages/SharpNRI.Descs) | NRI's descriptor structs and enums as plain types, the one dependency of `SharpSlang.Pipeline.NRI`. No native library |
| [Microsoft.Direct3D.DXC](https://www.nuget.org/packages/Microsoft.Direct3D.DXC) | Microsoft's package of `dxcompiler.dll` and `dxil.dll`, the one dependency of `SharpSlang.Dxc`, which copies them into the application's output. Slang hands it HLSL to emit DXIL |

Test-only, none of it shipped in a package:

| | |
|---|---|
| [xUnit](https://github.com/xunit/xunit) | the test framework |
| [SharpNRI](https://github.com/Nullpoint56/SharpNRI) and `SharpNRI.Testing` | NRI itself, with its validated headless device: builds real pipeline layouts and pipelines out of the NRI projection and dispatches compute work bound through it |
| [PublicApiGenerator](https://github.com/PublicApiGenerator/PublicApiGenerator) | renders each package's public surface for approval testing |
| [Mesa lavapipe](https://gitlab.freedesktop.org/mesa/mesa) | a software Vulkan device, so the NRI Vulkan tests run on a CI runner with no GPU |
| [Vulkan-ValidationLayers](https://github.com/KhronosGroup/Vulkan-ValidationLayers) | judges the pipelines built from the NRI projection against the specification |
| [SPIRV-Tools](https://github.com/KhronosGroup/SPIRV-Tools) | `spirv-val`, which judges every SPIR-V module the corpora compile to; `spirv-dis` for reading them by hand |
| [glslang](https://github.com/KhronosGroup/glslang) | `glslangValidator`, which compiles the GLSL target's output for Vulkan |
| [naga](https://github.com/gfx-rs/wgpu/tree/trunk/naga) | validates the WGSL target's output (`cargo install naga-cli`) |
| dxc's `dxil.dll` and `dxcompiler.dll` | from `Microsoft.Direct3D.DXC`: the DXIL validator, and the compiler for the HLSL target's output |
| NVRTC, `xcrun metal` | the CUDA toolkit's runtime compiler and Xcode's Metal compiler, for the CUDA and Metal targets where installed |
| [LLVM/clang](https://github.com/llvm/llvm-project) | compiles a generated probe against the pinned `slang.h`, so struct layouts and function signatures are checked against a C++ compiler rather than against the generator that produced them |
| Slang's own [`tests/reflection` and `tests/compute`](https://github.com/shader-slang/slang/tree/master/tests) | fetched at the pinned tag. The only expected values in this repository that the Slang team wrote rather than this repository deriving |

`spirv-opt` and `glslang` are used too, but through Slang: they are inside the `slang-glslang`
library its release ships, not separate dependencies of this repository.

## Licence

[MIT](LICENSE) for everything in this repository.

The `SharpSlang` package additionally redistributes Slang's own prebuilt binaries, which are
`Apache-2.0 WITH LLVM-exception`. Their licence texts ship inside the package under
`licenses/<rid>/`, and its build targets copy them beside your application, to
`licenses/SharpSlang/<rid>/` (`SharpSlangCopyLicenses=false` turns that off);
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) says what is bundled and under what terms. `SharpSlang.Pipeline`, `SharpSlang.Pipeline.NRI` and
`SharpSlang.Dxc` bundle nothing and are MIT alone; the last one depends on Microsoft's
`Microsoft.Direct3D.DXC` package, whose terms a consumer takes on by restoring it.
