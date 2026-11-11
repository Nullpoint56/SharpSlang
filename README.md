# SharpSlang

C# bindings for the [Slang](https://github.com/shader-slang/slang) shader compiler: compilation and
Slang's full reflection API. They are pure managed P/Invoke over a pinned Slang build. Opt-in
pipeline packages bake shaders into packages a game reads without the compiler, and project their
layouts onto [NRI](https://github.com/NVIDIA-RTX/NRI) through
[SharpNRI](https://github.com/Nullpoint56/SharpNRI).

An independent project, not affiliated with the Slang authors. MIT licensed. **Beta**: the API may
still change between releases.

## Quick start

```csharp
using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
using var module  = session.LoadModule("standard_surface");
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

byte[] spirv = program.GetEntryPointCodeBytes(0, targetIndex: 0);
```

Cook with SharpSlang.Pipeline.Bake, and read the result in the game with SharpSlang.Pipeline:

```csharp
ShaderPackageBaker.Bake(program, module, targetIndex: 0)
    .WithNriProjection(new NriProjectionOptions { UnboundedArrayCap = 1024 })
    .WriteToFile("cooked/standard_surface.pslb");

var package = ShaderPackage.ReadFromFile("cooked/standard_surface.pslb");   // no compiler needed
var nri     = package.ReadNriProjection();                                  // SharpNRI descs
```

[The guide](docs/guide.md) covers compiling, reflection, baking, the NRI projection, D3D12, lifetime,
threads, and a table of the traps.

## Install

```
dotnet add package SharpSlang.Pipeline.Bake --prerelease
```

| Package | What it is | Where it runs |
|---|---|---|
| `SharpSlang` | the binding and Slang's native library | wherever you compile shaders |
| `SharpSlang.Descs` | Slang's enums and structs, no native code | anywhere |
| `SharpSlang.Pipeline.Bake` | the reflection snapshot and the baker, on top of `SharpSlang` | your asset pipeline |
| `SharpSlang.Pipeline` | reads baked packages; no native dependency | the shipped game |
| `SharpSlang.Pipeline.NRI` | NRI pipeline layouts and shader descs, through `SharpNRI.Descs`; no native dependency | cooker or game |
| `SharpSlang.Dxc` | build targets that copy Microsoft's dxc from `Microsoft.Direct3D.DXC`, so Slang can emit DXIL | an asset pipeline targeting D3D12 (win-x64) |

Versions follow Slang: the major is Slang's version as `YYYYRRP`, so `2026210.x.y` is Slang v2026.21.

## Platforms

| Platform | Notes |
|---|---|
| Windows x64 | SPIR-V and, with `SharpSlang.Dxc`, DXIL |
| Linux x64, Linux arm64 | glibc 2.34 or newer |
| macOS arm64 | the native library is a zip on each release, not in the packages |

.NET 11. Slang's ABI is not stable across versions, so one release pins one Slang release
(currently v2026.21). It is built from source, with the patches in [patches/](patches/README.md).

## What is tested

The suite has **1223 tests**, run against oracles rather than values typed by hand:
- a C++ probe that checks every struct and all 279 `DllImport` entry points against `slang.h`;
- the Khronos and D3D12 validation layers, judging real NRI pipelines built from a 33-shader corpus;
- Slang's own reflection tests;
- a pixel-exact render on Vulkan and D3D12;
- each compile target's own tool.

It also dispatches 56 shaders from Slang's `tests/compute`. Every Slang defect found is recorded in
[docs/upstream.md](docs/upstream.md).

## Building from source

```powershell
git submodule update --init --recursive
pwsh tools/build-native.ps1 -Rid win-x64
dotnet build SharpSlang.slnx -c Release
dotnet test --solution SharpSlang.slnx -c Release --no-build
pwsh tools/check.ps1
```

Requirements per platform, the build options and how the repository is organised are in
[AGENTS.md](AGENTS.md).

## Licence

MIT ([LICENSE](LICENSE)). The `SharpSlang` package redistributes Slang's binaries
(`Apache-2.0 WITH LLVM-exception`) and the components built into them. Their licences are copied
beside your application under `licenses/SharpSlang/<rid>/`, and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists them.
