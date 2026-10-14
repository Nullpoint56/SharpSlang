# PrismSlang

**C# bindings over the [Slang](https://github.com/shader-slang/slang) shader compiler — compilation
and full reflection.**

Slang compiles a modern shading language to SPIR-V and can describe exactly what the result needs
bound to it. PrismSlang exposes both from C#: compile a shader, read back its descriptor sets, push
constants, vertex inputs and buffer layouts, and hand a renderer the pipeline state that goes with
it.

Pure managed P/Invoke. No C++/CLI and no native source in this repository — the payload is Slang's
own prebuilt release, pinned to a version and fetched by a script.

> **This is [PrismEngine](https://github.com/Nullpoint56)'s binding first.** It serves one Vulkan
> renderer that is itself under development, and Prism's needs decide the calls. It is packaged and
> documented to be reusable — but nobody else has used it yet.

---

## Status

`0.1.0-alpha`, against Slang `v2026.14.1`, on .NET 11.

Alpha because nothing has exercised it but a test suite — thorough, but not a shipping game hitting
the reflection surface with shaders nobody anticipated. PrismEngine will do that slowly, and 1.0
waits on it. Expect breaking changes until then.

## What you get

Three packages. Which one you want depends on where the code runs.

### `PrismSlang` — compile, in your asset pipeline

The compiler and the whole reflection surface. Carries the native Slang payload, so this is the one
you keep out of a shipped game.

```csharp
using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
using var module  = session.LoadModule("standard_surface");
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

var spirv  = program.GetEntryPointCodeBytes(0);
var layout = program.Reflect();     // descriptor sets, push constants, vertex inputs, byte layouts
```

You also get specialization constants, generics and type conformances, the declaration tree, the
byte layout of a uniform buffer down to matrix and array strides, `import`/`#include` served from
your own filesystem (an engine VFS or a pack file), and a bake step that writes the SPIR-V and the
layout into one file with content hashes over the whole import graph:

```csharp
ShaderPackage package = ShaderPackageBaker.Bake(program, module);
package.WriteToFile("cooked/standard_surface.pslb");

if (!package.IsCurrent(SlangGlobalSession.BuildTag))    // for the build system, next time round
    Rebuild(package.StaleDependencies());
```

### `PrismSlang.Runtime` — read baked shaders, in the shipped game

No native dependency at all. A game references this and carries no compiler.

```csharp
var package = ShaderPackage.ReadFromFile("cooked/standard_surface.pslb");

foreach (var entry in package.EntryPoints)
{
    var code = entry.Code;                      // SPIR-V, ready for vkCreateShaderModule
    var name = entry.ModuleEntryPointName;      // what pName needs — Slang calls them all "main"
}

ProgramLayout layout = package.Layout;          // the reflection snapshot, as baked
```

`AssemblySplitTests` builds a consumer referencing only this and `PrismSlang.Vulkan`, and checks the
output carries no `slang-compiler`, no `slang-glslang` and no `runtimes/` tree.

### `PrismSlang.Vulkan` — turn reflection into Vulkan pipeline state

Also no native dependency. Projects a reflection snapshot onto the structures Vulkan wants. The
enums mirror the specification's numeric values, so they cast straight across and this needs no
Vulkan binding of its own.

```csharp
VulkanPipelineLayout vk = layout.ToVulkan();

foreach (var set in vk.DescriptorSets)
foreach (var b in set.Bindings)
{
    var binding = new VkDescriptorSetLayoutBinding
    {
        binding         = b.Binding,
        descriptorType  = (VkDescriptorType)b.DescriptorType,
        descriptorCount = b.IsUnbounded ? myBindlessCap : b.DescriptorCount,
        stageFlags      = (VkShaderStageFlags)b.StageFlags,
    };
}
```

Also on the layout: `PushConstants`, `VertexInputs`, `FragmentOutputs`, `Stages`,
`SpecializationConstants`, and dispatch geometry for compute.

## Using it

Packages are published to **GitHub Packages** on a `v*` tag. Add the feed, then take what you need:

```bash
dotnet nuget add source https://nuget.pkg.github.com/Nullpoint56/index.json \
    --name prismslang --username <you> --password <a PAT with read:packages>

dotnet add package PrismSlang            # asset pipeline
dotnet add package PrismSlang.Runtime    # shipped game
dotnet add package PrismSlang.Vulkan     # renderer
```

The usual shape of an integration is: cook offline with `PrismSlang`, bake a `.pslb` per shader,
ship those, and at run time read them with `PrismSlang.Runtime` and project with
`PrismSlang.Vulkan`. **[docs/guide.md](docs/guide.md)** walks through each step, plus lifetime,
threading, diagnostics, and the traps worth knowing before you meet them.

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
| `win-x64` | `windows-latest` | |
| `linux-x64` | `ubuntu-latest` | also runs the Vulkan validation layers via lavapipe |
| `linux-arm64` | `ubuntu-24.04-arm` | the only leg exercising ARM64 calling conventions |
| `osx-arm64` | `macos-14` | the only leg on a non-glibc allocator and loader |

All four run the full suite on every push, and all four ship in the `PrismSlang` package. If you
content-address cooked assets, note that x64 and ARM64 emit byte-identical SPIR-V within an
architecture and differ on shaders using `SV_VertexID`; the modules are equivalent but will never
compare equal. [The traps table](docs/guide.md#traps-worth-knowing) has the detail.

.NET 11 and C# 15. The Slang version is pinned in `Directory.Build.props`, and that pin drives both
the payload fetch and the binding regeneration, because Slang's ABI is not stable across versions.

## Building from source

```bash
pwsh tools/fetch-native.ps1 -Rid win-x64 -Profile Spirv   # stage the pinned Slang payload
dotnet build PrismSlang.slnx -c Release
dotnet test  PrismSlang.slnx -c Release
dotnet run --project samples/PrismSlang.Smoke -c Release  # source -> SPIR-V, end to end
```

`runtimes/` is gitignored, so the fetch is the first step on a fresh clone. `-Profile Spirv` stages
`slang-compiler` and `slang-glslang`; the latter is where `spirv-opt` lives, and without it every
shader compiles about 23% larger while Slang mentions it exactly once per session.

A C++ compiler is optional but wanted. Six tests generate a probe, compile it against the pinned
`slang.h`, and compare struct sizes, field offsets, bitfield packing and every function signature —
all 278 `DllImport`s and all 231 vtable methods — against what the header actually declares. It is
the only check in the suite that can see a layout error, and the only one that can see a wrong
parameter list on a vtable slot nothing has ever called. They skip with instructions where no
compiler is found; CI has one on every runner.

**542 tests** run on all four platforms each push, against oracles rather than expected values typed
by hand: the emitted SPIR-V, Slang's own alternate route to each fact, a vk.xml-generated enum
mirror, the Khronos validation layers building real pipelines on a headless device from a
**32-shader corpus**, and Slang's own test suite — 31 shaders whose expected reflection the Slang
team wrote, compared against both `spReflection_ToJson` and this package's projection. It also
**dispatches 56 shaders** from Slang's `tests/compute` on a real device: buffers, storage images,
sampled textures and samplers bound at the set and binding index reflection reports, and the
readback compared with the words Slang's own tests record. `NativeExportTests` resolves all 278 `DllImport` entry points against the
loaded library.

## Documentation

| | |
|---|---|
| **[guide.md](docs/guide.md)** | how to use it — compiling, reflecting, projecting, baking, lifetime, threads, and the traps |
| **[design.md](docs/design.md)** | how it is built and why — the layering, the package split, the scope boundary, the risks |

Most of the difficulty in binding Slang is not in the signatures. `loadModule` returns a borrowed
pointer where every other call returns an owned one; `getEntryPointCode` names every module `main`;
a specialization constant used as an array length makes reflection end the process on Linux and
throw on Windows. The guide's [traps table](docs/guide.md#traps-worth-knowing) is the short version
of what those cost to find out.

## How it was built

`dotnet/Native/` is statically generated by ClangSharp from Slang's headers.

Everything else — the rest of `PrismSlang`, `PrismSlang.Runtime`, `PrismSlang.Vulkan`, the tests and
these documents — was written with AI assistance.

## What it uses

| | |
|---|---|
| [Slang](https://github.com/shader-slang/slang) | the compiler this binds. Its prebuilt release is the native payload |
| [ClangSharp](https://github.com/dotnet/ClangSharp) | generates `dotnet/Native/` from Slang's headers |
| [.NET 11](https://github.com/dotnet/runtime) | the runtime, and the P/Invoke and `[UnmanagedCallersOnly]` machinery |

Test-only, none of it shipped in a package:

| | |
|---|---|
| [xUnit](https://github.com/xunit/xunit) | the test framework |
| [PublicApiGenerator](https://github.com/PublicApiGenerator/PublicApiGenerator) | renders each package's public surface for approval testing |
| [Vortice.Vulkan](https://github.com/amerkoleci/Vortice.Vulkan) | `vk.xml`-generated Vulkan bindings, used as the oracle for the mirrored enum values and to build real pipelines and dispatch real compute work in the device tests |
| [Mesa lavapipe](https://gitlab.freedesktop.org/mesa/mesa) | a software Vulkan device, so those tests run on a CI runner with no GPU |
| [Vulkan-ValidationLayers](https://github.com/KhronosGroup/Vulkan-ValidationLayers) | judges the projected pipeline state against the specification |
| [SPIRV-Tools](https://github.com/KhronosGroup/SPIRV-Tools) | `spirv-dis` and `spirv-val`, for reading emitted modules by hand |
| [LLVM/clang](https://github.com/llvm/llvm-project) | compiles a generated probe against the pinned `slang.h`, so struct layouts and function signatures are checked against a C++ compiler rather than against the generator that produced them |
| Slang's own [`tests/reflection` and `tests/compute`](https://github.com/shader-slang/slang/tree/master/tests) | fetched at the pinned tag. The only expected values in this repository that the Slang team wrote rather than this repository deriving |

`spirv-opt` and `glslang` are used too, but through Slang: they are inside the `slang-glslang`
library its release ships, not separate dependencies of this repository.

## Licence

[MIT](LICENSE) for everything in this repository.

The `PrismSlang` package additionally redistributes Slang's own prebuilt binaries, which are
`Apache-2.0 WITH LLVM-exception`. Their licence texts ship inside the package under
`runtimes/<rid>/`, and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) says what is bundled and
under what terms. `PrismSlang.Runtime` and `PrismSlang.Vulkan` bundle nothing and are MIT alone.
