# PrismSlang

C# bindings over the [Slang](https://github.com/shader-slang/slang) shader compiler — compilation
and **full reflection** — for [PrismEngine](https://github.com/Nullpoint56)'s renderer.

Pure managed P/Invoke. No C++/CLI, no native code in this repository: the payload is Slang's own
prebuilt release, pinned to a version and fetched by a script.

```csharp
using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
using var module  = session.LoadModule("standard_surface");
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

var layout = program.Reflect().ToVulkan();     // descriptor sets, push constants, vertex inputs
var spirv  = program.GetEntryPointCodeBytes(0);
```

---

## Three packages, and why

| Package | Assembly | Native payload | For |
|---|---|---|---|
| **`PrismSlang.Runtime`** | 93 KB | none | reading baked shaders at run time |
| **`PrismSlang.Vulkan`** | 49 KB | none | reading a reflection snapshot as Vulkan pipeline state |
| **`PrismSlang`** | 158 KB | **36 MB per RID** | compiling shaders in an asset pipeline |

A shipped game reads shaders that were compiled offline; it must not carry a 36 MB compiler to do
it. So the reader half — the package container, the reflection snapshot, the SPIR-V header reader —
lives in `PrismSlang.Runtime` and has **no native dependency at all**. `PrismSlang.Vulkan` depends
on that half rather than on the compiler, so a renderer pays nothing either.

A test asserts this rather than a comment claiming it: `tests/PrismSlang.Runtime.Consumer` is a
project referencing only those two, and `AssemblySplitTests` checks its build output contains no
`slang-compiler`, no `slang-glslang` and no `runtimes/` tree.

`PrismSlang.Vulkan` is separate for the mirrored reason: everything in `PrismSlang` proper is
API-neutral, because Slang's own binding vocabulary spans D3D, Vulkan and Metal. A D3D12 consumer
should never pull Vulkan concepts in.

## What it does

- **Compile** Slang to SPIR-V, with the target environment pinned and the emitted module verified
  against it rather than assumed.
- **Reflect** everything: descriptor sets, push constants, vertex inputs and fragment outputs,
  specialization constants, generics and the declaration tree — and the byte layout of a uniform
  buffer, down to matrix and array strides.
- **Project** that to Vulkan: `VkDescriptorSetLayoutBinding`, `VkPushConstantRange`,
  `VkVertexInputAttributeDescription`, `VkSpecializationInfo`, workgroup sizes and dispatch counts.
  No dependency on any Vulkan binding — the enums mirror the spec's values for a direct cast.
- **Bake** a shader and its layout into one file, so the runtime never touches the compiler, with
  content hashes over the whole import graph so a stale cook is detectable.
- **Host** `import` and `#include` on a managed filesystem — an engine VFS or a pack file — instead
  of loose source on disk.

## What it deliberately does not do

Decide renderer policy. It reports what the shader requires and stops:

- no vertex buffer bindings, strides or input rates — only attribute locations and formats
- no meaning attached to a set index ("set 1 is per-material")
- no descriptor pool sizing or update strategy
- no legacy `spCompileRequest*` API, bytecode VM, coverage tracing, or `slang-rhi`
  ([findings.md §9.2](docs/findings.md#92-what-is-deliberately-not-bound))

Where a mapping is genuinely ambiguous it refuses rather than guessing quietly. Two push-constant
buffers, a vertex input with no attribute format, a buffer Slang placed nowhere — each throws with
a message naming the shader construct that caused it.

## Platforms

| RID | State |
|---|---|
| `win-x64` | verified, in CI |
| `linux-x64` | verified, in CI — including the Vulkan validation layers via lavapipe |
| `osx-arm64` | mapped in `fetch-native.ps1`, **never executed** |

.NET 10, C# 14. Slang is pinned in `Directory.Build.props`; the pin is the single source of truth
for both the payload fetch and the binding regeneration, because Slang's ABI is explicitly not
stable across versions.

## Getting started

```bash
pwsh tools/fetch-native.ps1 -Rid win-x64 -Profile Spirv   # stage the pinned Slang payload
dotnet build PrismSlang.slnx -c Release
dotnet test  PrismSlang.slnx -c Release
dotnet run --project samples/PrismSlang.Smoke -c Release  # source -> SPIR-V, end to end
```

The payload is not committed — `runtimes/` is gitignored — so the fetch is the first step on a fresh
clone. `-Profile Spirv` stages `slang-compiler` and `slang-glslang`; the latter is where `spirv-opt`
lives, and without it every shader compiles ~23% larger while Slang says so exactly once per
session ([§9.8 C](docs/findings.md#98-four-findings-from-the-first-consumer)).

**[docs/guide.md](docs/guide.md)** is the how-to: compiling, reflecting, projecting, baking, and the
traps worth knowing before you hit them.

## Documentation

| | |
|---|---|
| **[guide.md](docs/guide.md)** | how to use it — the task-shaped documentation |
| **[findings.md](docs/findings.md)** | what was measured, and what contradicted a reasonable guess |
| **[design.md](docs/design.md)** | the surface study, written before any of it existed |

`findings.md` is the one worth reading even if you never use this library. Binding Slang is mostly
not a question of signatures — it is that `loadModule` returns a borrowed pointer where every other
call returns an owned one, that `getEntryPointCode` names every module `main`, that a specialization
constant used as an array length makes reflection kill the process. Seventeen sections of that, each
one measured, several of them mistakes this binding made and then caught.

## Testing

384 tests. The interesting ones do not assert against numbers someone typed:

- **`ModuleAgreementTests`** compiles a 27-shader corpus and requires the reflection snapshot and
  the emitted SPIR-V to state *the same set of facts, in both directions* — so a fact one side
  simply lacks is a difference rather than a silence. This is what found most of the defects in
  `findings.md`.
- **`SurfaceCoverageTests`** fails on any value of Slang's enums the corpus neither exercises nor
  explicitly declares untried, because "nobody compiled that" is where every defect had been hiding.
- **`VulkanValidationTests`** builds real `VkPipeline`s from the projection on a headless device
  with the Khronos validation layers loaded — the only oracle in the suite that is not ultimately
  Slang describing itself.
- **`MalformedInputTests`** truncates and corrupts both file readers under a fixed seed.
- **`NativeExportTests`** resolves all 278 `DllImport` entry points against the loaded library.

## Licence

MIT.
