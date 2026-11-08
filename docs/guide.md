# SharpSlang — Guide

How to use it: what to type, and what Slang does that will surprise you.

Every code sample here is the real API — the signatures were checked against the source, not
recalled — and the traps flagged along the way are the ones that actually bit somebody.

---

## Contents

- [The two halves](#the-two-halves)
- [Referencing SharpSlang from source](#referencing-sharpslang-from-source)
- [Compiling](#compiling)
- [Reflecting](#reflecting)
- [Filling a uniform buffer](#filling-a-uniform-buffer)
- [Projecting to NRI](#projecting-to-nri)
- [Targeting D3D12](#targeting-d3d12)
- [Specialization constants](#specialization-constants)
- [Compute and dispatch](#compute-and-dispatch)
- [Baking and loading](#baking-and-loading)
- [Serving shaders from your own filesystem](#serving-shaders-from-your-own-filesystem)
- [Diagnostics](#diagnostics)
- [Reading a shader's functions](#reading-a-shaders-functions)
- [Linking with options](#linking-with-options)
- [Laying out a generic before you use it](#laying-out-a-generic-before-you-use-it)
- [Which bindings the code actually reads](#which-bindings-the-code-actually-reads)
- [Caching parsed modules](#caching-parsed-modules)
- [The payload has to be the pinned one](#the-payload-has-to-be-the-pinned-one)
- [Lifetime](#lifetime)
- [Threads](#threads)
- [What allocates](#what-allocates)
- [Traps worth knowing](#traps-worth-knowing)

---

## The two halves

Reference `SharpSlang` in the tool that cooks shaders. Reference `SharpSlang.Pipeline` — and
`SharpSlang.Pipeline.NRI` if you want the pipeline projection — in the game that loads them. The
runtime half has no native dependency, so a shipped build carries no compiler.

```xml
<!-- asset pipeline -->
<PackageReference Include="SharpSlang" Version="0.1.0-alpha" />
<PackageReference Include="SharpSlang.Pipeline.NRI" Version="0.1.0-alpha" />

<!-- game -->
<PackageReference Include="SharpSlang.Pipeline" Version="0.1.0-alpha" />
<PackageReference Include="SharpSlang.Pipeline.NRI" Version="0.1.0-alpha" />
```

`ShaderPackage` — the container, its reader, and the staleness checks — is in the runtime half.
`ShaderPackageBaker`, which is the only thing that needs a compiler, is in `SharpSlang`.

## Referencing SharpSlang from source

An engine developed alongside SharpSlang can reference its projects instead of its packages: a
sibling checkout and `ProjectReference`s, so a change on one side is visible on the other without
a pack in between.

```xml
<!-- the cooker; ../SharpSlang is the checkout -->
<ProjectReference Include="..\SharpSlang\dotnet\SharpSlang.csproj" />
<ProjectReference Include="..\SharpSlang\pipeline-nri\SharpSlang.Pipeline.NRI.csproj" />
```

**Stage the payload in the checkout.** Run `pwsh tools/fetch-native.ps1` there once per Slang pin.
The reference to `dotnet/SharpSlang.csproj` then copies the host RID's payload flat into the
engine's output, beside its own assemblies.

**DXIL takes the targets by hand.** A package's `build/` files do not flow through a project
reference, so referencing `dxc/SharpSlang.Dxc.csproj` would copy no dxc. The cooker does
what the package does: reference Microsoft's package at SharpSlang's `DxcPackageVersion` and import
the targets file, as `tests/SharpSlang.Tests/SharpSlang.Tests.csproj` does.

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Direct3D.DXC" Version="[1.9.2609.5]" PrivateAssets="all" />
</ItemGroup>
<Import Project="..\SharpSlang\dxc\build\SharpSlang.Dxc.targets" />
```

**Both solutions build into SharpSlang's `obj/` and `bin/`.** The engine builds SharpSlang's
projects in place, so each solution's restore rewrites the other's. With the same target framework
on both sides that is harmless.

**Start it from anywhere.** As its own executable or as `dotnet engine.dll`, from any directory.
Slang loads `slang-glslang` and `dxcompiler` itself, by name, and Windows looks for a name beside
the executable and in the working directory rather than beside the compiler; so on Windows
`SlangGlobalSession.Create` first loads them from where the compiler was loaded from, and dxc from
the application's directory as well. On Linux the payload's own `$ORIGIN` search path does the
same, measured; on macOS its `@loader_path` rpaths should, and the osx-arm64 test leg measures it.

## Compiling

```csharp
using SharpSlang;
using SharpSlang.Spirv;

using var global = SlangGlobalSession.Create();

using var session = global.CreateSession(new SessionOptions
{
    Targets      = [new TargetOptions { SpirvVersion = SpirvVersion.V1_4 }],
    SearchPaths  = ["assets/shaders"],
    PreprocessorMacros = new Dictionary<string, string> { ["MAX_LIGHTS"] = "64" },
});

using var module  = session.LoadModule("standard_surface");   // resolves via SearchPaths
using var vertex  = module.FindEntryPoint("vertexMain");
using var pixel   = module.FindEntryPoint("fragmentMain");
using var program = session.Link(module, vertex, pixel);

byte[] vertexSpirv = program.GetEntryPointCodeBytes(0);
byte[] pixelSpirv  = program.GetEntryPointCodeBytes(1);
```

`LoadModuleFromSource(name, path, source)` compiles a string instead, for generated shaders.

**A function with no `[shader]` attribute needs its stage.** `FindEntryPoint(name)` only sees
functions Slang already knows the stage of, which means ones carrying `[shader("vertex")]` and
friends. A plain `float4 main() : SV_Target` is invisible to it, and to `Module.EntryPointCount`,
because nothing in the declaration says which stage it is for — the semantic does not, and neither
does the name. Supply it:

```csharp
using var pixel = module.FindEntryPoint("main", SlangStage.SLANG_STAGE_FRAGMENT);
```

This is what `slangc -stage fragment -entry main` does. Slang type-checks the function against the
stage at that point rather than at link time, so a mismatch is reported with diagnostics naming the
shader. Most HLSL written before the attribute existed is in this shape.

**Pin the SPIR-V version.** `SpirvVersion` defaults to unset, which means Slang picks — measured as
1.5. A device running Vulkan 1.1 accepts at most SPIR-V 1.3, and above that `vkCreateShaderModule`
fails with nothing useful to say. The Vulkan specification fixes the ceiling per API version: 1.0
for Vulkan 1.0, 1.3 for 1.1, 1.5 for 1.2, 1.6 from 1.3. With a version pinned, `TargetOptions.VerifyEmittedSpirv` (on by default) parses
the emitted header and throws if Slang produced something else.

## Reflecting

```csharp
using SharpSlang.Reflection;

ProgramLayout layout = program.Reflect();
```

`ProgramLayout` is an **immutable managed snapshot**, not a proxy. It stays valid after the program,
session and global session are all disposed, and it serialises to JSON. That is deliberate: Slang's
reflection pointers are interior pointers owned by the object that produced them, so a proxy would
become a use-after-free presenting as corrupt bindings rather than a clean crash
([§4.4](design.md#44-reflection-must-be-projected-to-a-managed-snapshot-not-proxied)).

```csharp
foreach (var p in layout.Parameters)
    Console.WriteLine($"{p.Name}: set {p.DescriptorSet}, {p.Type.Kind}");

foreach (var e in layout.EntryPoints)
    Console.WriteLine($"{e.Name}: {e.Stage} {e.ThreadGroupSize}");

foreach (var c in layout.SpecializationConstants)
    Console.WriteLine($"{c.Name} = constant_id {c.ConstantId}, default {c.DefaultInt}");
```

`layout.SubObjectRanges` is the one view connecting a descriptor space back to the parameter that
owns it — a `ParameterBlock` takes a space of its own and nothing else in the reflection says whose
it is. Empty for a shader whose parameters are all bound directly.

## Filling a uniform buffer

The part most likely to go wrong silently. A `ConstantBuffer<T>` reflects as a `ContainerLayout`
whose `ElementType` is the struct:

```csharp
var block = (StructLayout)((ContainerLayout)layout.Parameter("gMaterial")!.Type).ElementType;

foreach (var field in block.Fields)
{
    if (!field.IsUniformOffsetKnown) continue;      // see the note below
    Console.WriteLine($"{field.Name} @ {field.UniformOffset}");
}
```

**A matrix is not its components packed together.** A `float3x3` occupies 48 bytes, not 36: three
rows on a 16-byte stride. A row-major `float4x2` is 64 bytes for eight floats.

```csharp
var m = (MatrixLayout)block.Field("normalMatrix")!.Type;
m.Stride;                          // 16 — bytes between rows (or columns, under COLUMN_MAJOR)
m.ComponentOffset(row: 2, column: 1); // does the arithmetic for you, either major axis
```

`MatrixLayout.Mode` is stated in Slang's terms, and **the emitted module decorates the opposite
word** — a session set to `ROW_MAJOR` emits members decorated `ColMajor`. That is not a
contradiction: HLSL writes rows-by-columns while SPIR-V's `OpTypeMatrix` is a list of columns, so
the same bytes get opposite names. Read `Mode` and `Stride` together and ignore the decoration.

Arrays have `ElementStride` — a `float2[3]` in a constant buffer strides 16, not 8. A
`StructuredBuffer<T>` reports `ResourceLayout.ElementType` and `ElementStride` for T.

**Check `IsUniformOffsetKnown` / `IsUniformSizeKnown`** before doing arithmetic. Slang answers
"I do not know" with a number near `ulong.MaxValue`, not with a null,
and a field after an array whose length is a specialization constant has no offset until the
pipeline is created.

## Projecting to NRI

```csharp
using SharpSlang.Pipeline.NRI;

NriPipelineProjection nri = layout.ToNri(new NriProjectionOptions { UnboundedArrayCap = 1024 });
using var pipelineLayout = device.CreatePipelineLayout(nri.Layout);
```

`nri.Layout` is a SharpNRI `NriPipelineLayoutDesc`, filled in from the snapshot: one `NriDescriptorSetDesc` per register space the shader uses, one
`DescriptorRangeDesc` per binding, one `RootConstantDesc` per push-constant block, and
`ShaderStages` set to every stage the program has. `SharpSlang.Pipeline.NRI` references `SharpNRI.Descs`
only — the descs as plain types, no `NRI.dll` — so this runs wherever `SharpSlang.Pipeline` does.

What NRI's API is indexed by, and the desc cannot carry, is on the projection beside it:

```csharp
var (set, range) = nri.Range("albedo")!.Value;        // which set, which range, by parameter name
var descriptorSet = pool.AllocateDescriptorSet(pipelineLayout, set.SetIndex);
descriptorSet.Update(range.RangeIndex, 0, albedoView);

var push = nri.RootConstants[0];                       // Index for cmd.SetRootConstants, Desc.size
cmd.SetRootConstants(push.Index, in myPushData);

foreach (var a in nri.VertexAttributes)                // Location, Format and D3D semantic filled
    attributes.Add(a.Desc with { StreamIndex = 0, Offset = myOffsetFor(a.Name) });

var shader = package.EntryPoints[0].ToNriShaderDesc(); // stage, bytes, ModuleEntryPointName
```

**Per frame, keep the integers and your own storage.** Look names up once, at load, and keep the
set and range indices. Every frame, reset the pool and allocate each projected set into an array you
own with SharpNRI's span overload; `NriDescriptorSet` is a struct, so the array holds the sets
themselves and none of this allocates:

```csharp
// Once, at load: the integers NRI is indexed by, and somewhere for the sets to live.
var (set, range) = nri.Range("albedo")!.Value;
uint albedoSet = set.SetIndex, albedoRange = range.RangeIndex;
var sets = new NriDescriptorSet[nri.DescriptorSets.Count];  // SetIndex is the position: index by it

// Every frame, once the GPU has finished with what this pool held:
pool.Reset();
for (var i = 0; i < sets.Length; i++)
    pool.AllocateDescriptorSets(pipelineLayout, nri.DescriptorSets[i].SetIndex, sets.AsSpan(i, 1));
sets[albedoSet].Update(albedoRange, 0, albedoView);
for (var i = 0; i < sets.Length; i++)
    cmd.SetDescriptorSet(nri.DescriptorSets[i].SetIndex, sets[i]);
```

Two things undo that, both measured: the array-returning `AllocateDescriptorSets(layout, setIndex,
count)`, which allocates its array on every call, and a `foreach` over `DescriptorSets` or `Ranges`,
which boxes an enumerator, hence the indexed loops. [What allocates](#what-allocates) has the frame
they were measured in.

**`SetIndex` is not the register space.** NRI's descriptor API is indexed by position in the desc's
`DescriptorSets` array; the SPIR-V is indexed by `set = N`. They coincide until a shader skips a
space — `[[vk::binding(0, 2)]]` with nothing in space 1 — and then set index 1 is register space
2. The projection reports both; NRI's Vulkan backend places each set at its register space and
fills the gap with an empty layout, so the module binds where it says.

**An unbounded array needs a cap.** `Texture2D t[];` is a runtime-sized array, and NRI's
`DescriptorRangeDesc.descriptorNum` has no spelling for "unbounded" — whatever number goes there is
the size of the binding the device creates. `ToNri` refuses the program unless
`UnboundedArrayCap` is set; the range comes back `ARRAY | PARTIALLY_BOUND` with that count. Add
`VARIABLE_SIZED_ARRAY` yourself only if the range is the last binding of its set, which Slang does
not arrange for.

**`ARRAY` is on every array range**, and it is load-bearing: NRI's Vulkan backend reads a range
without it as `descriptorNum` consecutive bindings of one descriptor each — the D3D register-range
reading — where Slang's SPIR-V declares one binding of `descriptorNum` descriptors.

**The device must have zero `VkBindingOffsets`.** NRI's Vulkan backend adds the per-register-class
shifts to every binding it builds, assuming the shader compiler was told the same shifts. Slang was
not. State the device's offsets in `NriProjectionOptions.VkBindingOffsets` and the projection refuses
a non-zero set rather than let the mismatch surface as a wrong texture at draw time.

What NRI has no word for is refused with the construct named: a
combined texture sampler (`Sampler2D` — declare a `Texture2D` and a `SamplerState`), an inline
uniform block, a 64-bit or three-component half vertex attribute (`Format` has none), a second
push-constant block. NRI also has no specialization constants: a shader that declares them is
specialized at link time, and a compute stage whose group size is a specialization constant reports
`ThreadGroupSize.IsSpecialized` with nothing to resolve it against.

Both NRI backends judge this: `NriDeviceTests` builds every corpus shader into real NRI pipeline
layouts and pipelines on a validated Vulkan device from SPIR-V and on a validated D3D12 device from
DXIL, and dispatches compute work bound through the projection on each. The `STRUCTURED_BUFFER` /
`STORAGE_STRUCTURED_BUFFER` and `BUFFER` / `STORAGE_BUFFER` splits are what tell an SRV from a UAV
on D3D12; on Vulkan both halves of each pair are the same descriptor type.

## Targeting D3D12

The same shader, cooked twice: one session per target, one baked package per target, and the same
`ToNri` call on each. What changes is below.

```csharp
using var dxil = global.CreateSession(new SessionOptions
{
    Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_DXIL }],   // sm_6_5 by default
});
```

**dxc has to be there.** Slang emits DXIL by handing HLSL to dxc, and ships none. Reference
`SharpSlang.Dxc`, which stages `dxcompiler.dll` and `dxil.dll` in the application's output
for `win-x64`; `CreateSession` refuses a DXIL target without it, naming the package, so the failure
is at session creation rather than inside the first compile.

**Registers are per class.** A DXIL layout reports `b0`, `t0`, `s0` and `u0` as four bindings at
index 0 in the categories `CONSTANT_BUFFER`, `SHADER_RESOURCE`, `SAMPLER_STATE` and
`UNORDERED_ACCESS`, where a SPIR-V layout numbers every descriptor of a set consecutively. The
projection passes each index through as `baseRegisterIndex` and NRI's D3D12 backend sorts them by
descriptor type, which is what makes the same projection code correct on both. `[[vk::binding]]`
is ignored on DXIL: measured, a texture declared `[[vk::binding(3, 2)]]` lands at the next free
`t` register of space 0.

**Push constants need a name.** On a SPIR-V target `[vk::push_constant]` makes the block root
constants on its own. On DXIL Slang drops the attribute and lays the block out as an ordinary
constant buffer, and reflection keeps no trace of the attribute on either target. So the host names
the block:

```csharp
var options = new NriProjectionOptions { RootConstantParameters = ["push"] };
var vk  = spirvPackage.Layout.ToNri(options);   // "push" is already root constants: a no-op
var d3d = dxilPackage.Layout.ToNri(options);    // "push" leaves the descriptor set and becomes
                                                // RootConstantDesc at its b register
```

One list serves both builds. Naming a constant buffer that is not a push-constant block on a
SPIR-V layout is refused, because the SPIR-V reads it through its descriptor and root constants
there would be a push-constant range the shader never reads. On DXIL, NRI's D3D12 backend binds
root constants at `b<registerIndex>` of `RootRegisterSpace`, which the projection sets to the
block's space, so the shader needs no change.

**A thread-group size cannot be a specialization constant.** D3D12 has none, and Slang lowers the
other uses of one to members of a constant buffer at `b0` that the host fills. `numthreads` has no
such fallback: `[numthreads(kGroupX, 1, 1)]` fails the compile with `E55205`, "specialization
constants not supported for numthreads". A DXIL build needs a literal size.

**What a DXIL package does not carry.** `BakedEntryPoint.SpirvVersion` is `Unspecified` and
`WorkgroupSize` is null, because there is no SPIR-V module to read them from; `ToNriShaderDesc`
does not need either. The thread-group size still comes from reflection on `ProjectedStage`.
`ModuleEntryPointName` is `main`, which D3D12 ignores: a DXIL container has one entry point.

## Specialization constants

The snapshot lists every specialization constant with the id the module decorates it with, its
scalar type, its size and the default the shader declared, so a host reads `constant_id` rather
than hardcoding it twice:

```csharp
foreach (var c in layout.SpecializationConstants)
{
    // c.Name, c.ConstantId, c.ScalarType, c.ByteSize, c.DefaultInt or c.DefaultFloat
}
```

NRI has no specialization constants, so on NRI a shader that declares them is specialized at link
time. The ids are what a host on another API packs its values against.

## Compute and dispatch

```csharp
var size = nri.Stage("computeMain")!.ThreadGroupSize!.Value;     // [numthreads(x, y, z)]
var groups = (width + (uint)size.X - 1) / (uint)size.X;           // ceil-divided
```

Check `IsSpecialized` first: `[numthreads(kGroupX, 1, 1)]` with a specialization constant makes
Slang report `0` for that dimension, and dividing by it is the bug. The module knows what
reflection does not — the constant id and the declared default — so resolve it from there:

```csharp
var declared = SpirvModule.Read(spirv).EntryPoints[0].WorkgroupSize!;
var (x, y, z) = declared.Resolve(new Dictionary<uint, uint> { [0] = 128 });
```

A constant the dictionary does not supply keeps the shader's default, because that is what Vulkan
does with one.

## Baking and loading

In the asset pipeline:

```csharp
using SharpSlang.Baking;

ShaderPackage package = ShaderPackageBaker.Bake(program, module);   // module, for its imports
package.WriteToFile("cooked/standard_surface.pslb");
```

In the game, with no compiler present:

```csharp
var package = ShaderPackage.ReadFromFile("cooked/standard_surface.pslb");

foreach (var e in package.EntryPoints)
{
    var stage = new VkPipelineShaderStageCreateInfo
    {
        stage  = (VkShaderStageFlags)VulkanProjection.ToStage(e.Stage),
        module = CreateShaderModule(e.Code),
        pName  = e.ModuleEntryPointName,    // NOT e.Name — see below
    };
}
```

**`Name` and `ModuleEntryPointName` are different things.** `Name` is the Slang function, for
looking a shader up. `ModuleEntryPointName` is what the entry point is called *inside the SPIR-V*,
which is what `pName` needs — and Slang names every per-entry-point module `main` by default,
whatever the function was called. It is read from the module, never derived.

Staleness, for a build system:

```csharp
if (!package.IsCurrent(SlangGlobalSession.BuildTag))
    foreach (var path in package.StaleDependencies())
        Console.WriteLine($"rebuild: {path} changed");
```

`Bake(program, module)` records a content hash for the shader **and everything it imports or
includes**. Prefer it over the string overload for anything loaded from disk: hashing only the named
file leaves a bake claiming to be current after a shared lighting module changed, and the symptom is
a shader running the version before last. Content hashes, not timestamps, so a checkout does not
rebuild the project.

`ReadFrom` has exactly one failure mode — `InvalidDataException` — whatever is wrong with the bytes.
`Bake` reads its own layout back before returning, and throws `InvalidOperationException` if the
package could not be loaded again. That is a defect in SharpSlang to report, not an error in the
shader.

## Serving shaders from your own filesystem

```csharp
sealed class PackFileProvider(AssetPack pack) : ISlangFileProvider
{
    public byte[]? TryRead(string path) => pack.TryGetEntry(path);   // null means "not found"
}

using var fs = new SlangFileSystem(new PackFileProvider(pack));
using var session = global.CreateSession(new SessionOptions
{
    Targets = [new TargetOptions()],
    FileSystem = fs,
    SearchPaths = ["/shaders"],
});
```

Returning null is a miss, and Slang continues searching. Throwing is safe — the callback boundary
catches everything and turns it into a failure code, because an exception unwinding into native
code is undefined behaviour. Dispose the filesystem *after* the session: Slang holds the pointer for
the session's lifetime.

For tests and generated shaders there is `InMemoryFileProvider`, which needs no implementation:

```csharp
using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>
{
    ["/shaders/lighting.slang"] = generatedSource,
}));
```

## Diagnostics

An error-level diagnostic on a **successful** compile is fatal by default, and that default is
load-bearing rather than pedantic: with `slang-glslang` absent, `getEntryPointCode` succeeds, emits
~23% larger unoptimised SPIR-V, and reports an error — once per global session, so shaders 2..n of
a batch are silently oversized with nothing said at all.

```csharp
SlangDiagnostics.Policy  = SlangDiagnosticPolicy.Report;      // default: ThrowOnError
SlangDiagnostics.Handler = (operation, severity, text) =>
    log.Write(severity >= SlangDiagnosticSeverity.Error ? Level.Error : Level.Warn, text);
```

The severity is handed to you already classified. Slang has no structured severity — it comes from
parsing message prefixes — so deriving it again means reimplementing `SlangDiagnostics.Classify`.

Because that diagnostic fires only once per session, there is a second, ordering-independent
mechanism: `GlobalSessionOptions.RequireSpirvOptimizer` (on by default) checks for the downstream
compiler at session creation, before a single shader is compiled.

## Reading a shader's functions

The declaration tree says what a module offers; `FunctionSignature` says what each one looks like,
which is what a material system needs to check a candidate against a hook.

```csharp
foreach (var d in module.ReflectDeclarations().Descendants(DeclarationKind.Func))
    Console.WriteLine(d.Function);      // vector<float,3> shade(Surface s, out float occlusion)

var fn = program.FindFunction("shade");                 // by name, across the linked program
var m  = program.FindFunction(surfaceType, "tinted");   // a method on a type
```

Two things to know before matching on strings:

- **Type names are Slang's canonical spelling.** `float3` is `vector<float,3>`, and
  `StructuredBuffer<float4>` is `StructuredBuffer<vector<float,4>, DefaultDataLayout>`.
- **Overloads are only visible by name.** The declaration tree gives two sibling nodes with real
  signatures; `FindFunction` gives one function with `IsOverloaded` set, `Overloads` populated, and
  its *own* signature empty. Check `IsOverloaded` before reading `Parameters`.

To pick between overloads, hand Slang the call's argument types:

```csharp
var chosen = program.ResolveOverload("blend", program.FindType("Lambert")!, floatType);
```

Types and their tags come off the same objects: `SlangType.Fields` lists a struct's members
structurally (no layout, so it works for a type no shader parameter uses), `SlangType.FindAttribute`
looks one up by name, and `TypeLayout.Attributes` enumerates them where the type has a layout.
`UserAttribute.ArgumentTypes` says what each argument was *declared* as, next to the probed value.

## Linking with options

`Link` takes an optional `LinkOptions`, carrying the three things that can only be decided at link
time.

```csharp
using var program = session.Link(module, [vertex, fragment], new LinkOptions
{
    SpecializationArguments = [lambertType],   // concrete types for the program's generics
    TypeConformances        = [lambert],       // implementations reachable by dynamic dispatch
    CompilerOptions         = new LinkTimeOptions
    {
        Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL,
    },
});
```

**Type conformances** are how a shader dispatching through an interface reaches an implementation
the module never named:

```csharp
var material = module.RequireType("IMaterial");           // FindType returns null instead
using var lambert  = session.CreateTypeConformance(module.RequireType("Lambert"),  material, 0);
using var emissive = session.CreateTypeConformance(module.RequireType("Emissive"), material, 1);
```

The trailing id is the value written into the shader's dynamic-dispatch table, so a host selecting
an implementation by number can pin it; omit it and Slang assigns one. Registering a type that does
not implement the interface throws rather than linking something that cannot dispatch. Without the
conformance the implementation is not in the program at all, however visible it looks in the source.

**`CompilerOptions` has exactly two typed properties**, and that is not an oversight. Slang accepts
the whole `CompilerOptionName` range at `linkWithOptions` and silently ignores most of it, so a
typed property for an ignored option would read as a feature and do nothing. Measured by diffing
emitted SPIR-V:

| Option | At link time |
|---|---|
| `Optimization` | works — None 2,052 B, default 1,868, Maximal 1,668 |
| `FloatingPointMode` | works — Precise adds 36 B; Fast *is* the default |
| `DebugInformation` | no effect; it is a session/target setting |
| `VulkanUseEntryPointName` | no effect at link time; works on the session or target |
| `Capability` | no effect — `spirv_1_6` still emits 1.5. The profile is the only version knob |

The three that do nothing are reachable through `RawEntries` if a future release honours them, and a
test asserts a raw entry and its typed equivalent emit identical bytes, so the escape hatch cannot
rot.

The main use is one session producing debug and release variants of the same shader, rather than a
session per configuration.

## Laying out a generic before you use it

"What would `Wrapper<Lambert>` lay out as" — asked without authoring a shader that declares one,
which is what a material system needs after it picks an implementation.

```csharp
var specialized = program.SpecializeGeneric("Wrapper", GenericArgument.OfType(lambert));
// specialized.Name       -> "Wrapper<Lambert>"
// specialized.Arguments  -> [T = Lambert]
// specialized.Layout     -> field offsets, sizes, strides — a snapshot, so it outlives the program

var cascades = program.SpecializeGeneric("Cascades", GenericArgument.OfInt(4));   // Cascades<4>
```

Value generics are only reachable this way: `LinkOptions.SpecializationArguments` specializes the
*program* and reports type parameters exclusively.

**Argument kinds are checked before Slang sees them, and that is not politeness.** Given a value
where a type parameter belongs, `spReflection_specializeGeneric` access-violates and ends the
process on every platform. `SpecializeGeneric` refuses the combination with a message naming what
was expected. A generic mixing type *and* value parameters can still be given them in the wrong
order — the counts match, and Slang reports the two lists separately so the interleaving is not
recoverable.

## Which bindings the code actually reads

Reflection reports the shader's interface, which includes parameters the emitted code never touches.
`IMetadata` is the only thing that knows the difference, and the answer differs per stage.

```csharp
using var metadata = program.GetEntryPointMetadata(entryPointIndex);
foreach (var b in metadata.UnusedBindings(layout))
    log.Info($"{b.Category} set {b.Space} binding {b.Index} is declared but never read");
```

A binding reported unused still has to appear in the descriptor set layout — the SPIR-V declares it
either way. What you save is the descriptor write and the resource behind it.

## Caching parsed modules

For a library where many entry points import the same modules, serialize the shared ones once.

```csharp
File.WriteAllBytes(cache, module.Serialize().ToArray());

// Later, in a fresh session:
if (session.IsBinaryModuleUpToDate(sourcePath, ir))
    session.LoadModuleFromIRBlob("mylib", sourcePath, ir);
```

**Ask `IsBinaryModuleUpToDate` from a session that has not loaded the module.** It is answered from
the session's own file-system view, so load-serialize-ask returns true unconditionally — even for a
path with no file behind it. `ReadModuleInfo(ir)` reports the compiler build that wrote the blob,
and a blob is only loadable by that build.

## The payload has to be the pinned one

`SlangGlobalSession.Create` refuses a `slang-compiler` that is not the release these bindings were
generated from, and throws `SlangVersionMismatchException` naming both versions. Slang states its
ABI is not stable between releases, and this layer calls COM vtables by slot ordinal taken from one
specific header — a payload with a reordered interface links, calls, and calls the wrong function.

The realistic way to hit it is undramatic: `runtimes/` is gitignored, so bumping `$(SlangVersion)`
without re-running `fetch-native.ps1` leaves the previous release staged and everything builds.

```csharp
SlangGlobalSession.PinnedBuildTag    // what these bindings expect, e.g. "2026.21"
SlangGlobalSession.BuildTag          // what the loaded library reports

// Deliberately running something else:
using var global = SlangGlobalSession.Create(
    new GlobalSessionOptions { RequirePinnedSlangVersion = false });
```

## Lifetime

Dispose everything, in any order:

```csharp
using var global  = SlangGlobalSession.Create();
using var session = global.CreateSession(options);
using var module  = session.LoadModule("surface");
```

- **Disposal order does not matter.** Each wrapper takes a native reference on the object above it,
  so disposing a session while a module from it is still alive is safe. Slang does not guarantee
  this on its own — `ISession` retains `IGlobalSession`, but `IModule` does *not* retain `ISession`
  — and the difference was invisible on Windows and Linux until a macOS runner faulted on it.
- **Double dispose is a no-op**, including concurrently.
- **Use after dispose throws** `ObjectDisposedException` rather than dereferencing.
- **Forgetting to dispose leaks** until process exit — it does not corrupt. There is deliberately no
  finalizer: the GC finalizes in arbitrary order, and Slang's objects form an ownership hierarchy it
  knows nothing about, so a finalizer could release a module into a session whose allocator has gone.
- A `ProgramLayout` outlives all of it. That is the point of a snapshot.

## Threads

**A `SlangGlobalSession` and everything made from it belong to one thread at a time.** This is
Slang's rule, not a limitation of the binding — `slang.h` says a global session "is currently *not*
thread-safe" and that it "and the objects created from it should be externally synchronized when
shared across threads".

Sharing one across threads without a lock has been measured doing four different things: an access
violation inside `loadModuleFromSource`, a clean `SlangException` out of one thread while the others
carried on, once nothing at all, and once **five of eight threads returning success with SPIR-V
that differed from a serial compile** (1,492 bytes against 1,436, on a GitHub-hosted Windows runner
with Slang v2026.21).

**Do not count on the crash to tell you.** The access violation stops the cook, which is the outcome
you want. The `SlangException` says `load module 'x' failed: SLANG_FAIL`, and that is
indistinguishable from a shader that genuinely does not compile: a cooker that logs a per-shader
failure and keeps going will blame the file, ship the rest of the batch, and blame a different file
next time. Getting this wrong does not reliably announce itself.

The last outcome is the unrecoverable one: the shader compiles, nothing is reported, and different
bytes ship. So there is no safe way to share a global session without a lock, and no test pins any
outcome of doing it, because no outcome holds on every run. Use one of the two patterns below.

### Cooking on all your cores

**One global session per worker thread.** `slang.h`: "Distinct global sessions may be used from
different threads in parallel." No locks anywhere, and each thread's output is byte-identical to a
serial compile.

```csharp
var shaders = new ConcurrentQueue<string>(work);

Parallel.For(0, Environment.ProcessorCount, _ =>
{
    // One per worker, created once and reused for every shader that worker takes.
    using var global = SlangGlobalSession.Create();
    using var session = global.CreateSession(options);

    while (shaders.TryDequeue(out var name))
    {
        using var module = session.LoadModule(name);
        // ... link, GetTargetCode, write the cooked asset
    }
});
```

The cost is that a global session amortizes Slang's standard-library load and this pays for it once
per worker — **about 80 ms each**, measured on a developer machine. Sixteen workers is a little over
a second, once, which is nothing against a cook of any size. Create it *outside* the work loop and
this is the pattern to use.

### If you cannot afford a global session per thread

Serialize the front end and parallelize the back end. `slang.h` marks this **experimental**: once a
component type "has been fully specialized and linked", `getTargetCode`, `getEntryPointCode`,
`getTargetMetadata`, `getEntryPointMetadata` and `getResultAsFileSystem` support "concurrent backend
code generation, including from multiple threads compiling different linked `IComponentType`
instances", while "front end operations such as loading modules, specialization, and linking still
require external synchronization".

```csharp
var programs = new List<LinkedProgram>();
lock (gate)                                     // front end: one at a time
{
    foreach (var name in work)
        programs.Add(Link(session, session.LoadModule(name)));
}

Parallel.ForEach(programs, p =>                 // back end: parallel
{
    using var code = p.GetTargetCode();
    Write(code.ToArray());
});
```

Both patterns are measured on every platform in CI, byte-compared against a serial baseline, by
`Threads_with_their_own_global_session_agree_with_a_serial_compile` and
`The_back_end_may_be_parallelised_once_the_front_end_has_finished`.

### Two things that are process-global

- **`SlangDiagnostics.Policy` and `SlangDiagnostics.Handler` are static.** Slang's diagnostics are
  per-call with no session to hang a policy off, so redirecting them is necessarily process-wide.
  Set them once at startup. Changing either while other threads are compiling changes it for them
  too.
- **A `ProgramLayout` is immutable managed data** and is safe to read from any number of threads,
  with no lock and no relationship to the objects it came from. That is what the snapshot is for.

## What allocates

Two halves, and they allocate differently on purpose.

**Once per shader, in the cooker or at load:** compiling, reflecting, baking, reading a package and
projecting it. All of it builds trees of records and lists of descs, and all of it is garbage you
pay once. Measured on a two-stage shader with five descriptors: reading a package parses about
100 KB, and `ToNri` allocates about 5 KB.

**After load, nothing.** Every property and indexer on a projection, and every lookup on it —
`Range(name)`, `Set(space)` and `Stage(name)` — allocates zero
bytes per call, and `AllocationTests` measures that with `GC.GetAllocatedBytesForCurrentThread` so
it stays zero. Cache the results anyway: a lookup by name is still a string comparison per range,
and what a renderer wants per draw is the set index and the range index, which are plain integers
on the projected records.

The same holds on Vulkan and on D3D12. The NRI projection is one object whichever backend consumes
it, and the DXIL build differs only in what was baked.

**A whole frame, measured.** SharpNRI measures its own per-frame calls at zero on NRI's NONE
backend, and the lookups above are zero one at a time, but the frame an engine writes is the two
together, and `AllocationTests` measures that as well: a compute frame driven purely through an
`NriPipelineProjection`, with the sets allocated into an array it owns by the span overload and
bound by `SetIndex`, every range written by `RangeIndex`, three ranges found by name, root constants
set by `Index` from a struct, the group count read from `ThreadGroupSize`, then submit and wait. It
allocates 0 bytes over 300 frames after one warm-up frame. Planted in it, the array-returning
`AllocateDescriptorSets` cost 40 bytes per set per frame and a `foreach` over `DescriptorSets` and
`Ranges` 40 per enumerator it boxed; the one-buffer `queue.Submit(cmd, fence, value)` costs nothing,
like the span form. It runs where the NRI device tests do, on win-x64 and linux-x64.

## Traps worth knowing

Each of these was a real defect, found by measurement, and is handled now; the table is so you
recognise the shape if you meet a cousin of one. Symptom first, because that is what you will see.
The section column says where the guide covers it.

| Symptom | Cause | Section |
|---|---|---|
| `vkCreateGraphicsPipelines` cannot find the entry point by its function name | Slang names the entry point of every per-entry-point module `main`. Use `BakedEntryPoint.ModuleEntryPointName`, which is read from the module, never the Slang function name. | [Baking and loading](#baking-and-loading) |
| Vertex attributes after a matrix read garbage | A `float4x4` input takes four locations. Locations come from the offsets Slang reports, never from counting fields. | [Projecting to NRI](#projecting-to-nri) |
| A `float3x3` in a constant buffer reads skewed | It is 48 bytes, not 36: rows on a 16-byte stride. Use `MatrixLayout.Stride` and `ComponentOffset`. | [Filling a uniform buffer](#filling-a-uniform-buffer) |
| The module says `ColMajor` where reflection says `ROW_MAJOR` | Same bytes, opposite vocabulary: HLSL writes rows by columns, SPIR-V's `OpTypeMatrix` is a list of columns. Read `Mode` and `Stride`; ignore the decoration. | [Filling a uniform buffer](#filling-a-uniform-buffer) |
| An offset or size is about 18 quintillion | `CategorySize.Unknown` is `ulong.MaxValue - 1`: Slang's "I do not know", for anything sized by a specialization constant. Check `IsKnown` / `IsUniformOffsetKnown` before arithmetic. | [Filling a uniform buffer](#filling-a-uniform-buffer) |
| A workgroup size of `0`, or a divide by zero dispatching | The dimension is a specialization constant. Check `ThreadGroupSize.IsSpecialized`, and resolve it against the values you supply. | [Compute and dispatch](#compute-and-dispatch) |
| A `double3` or `double4` vertex input is refused | Slang assigns it one location where Vulkan requires two, so the module is invalid SPIR-V (the validation layers confirm it), and NRI has no 64-bit vertex format. Use `float`. | [Projecting to NRI](#projecting-to-nri) |
| Reflecting a shader kills the cooker on Linux, or throws `SEHException` on Windows | A resource array sized by a specialization constant (`Texture2D t[kCount]`). Slang's reflection throws a C++ exception through its C API; nothing distinguishes the shader beforehand. It cannot work on Vulkan anyway, where `descriptorCount` is fixed before specialization exists. Do not write one. | [Reflecting](#reflecting) |
| A buffer the shader reads has no binding | Its block holds only arrays sized by a specialization constant, and Slang places it nowhere. The projection refuses the shader with the parameter named. Give the array a fixed size. | [Filling a uniform buffer](#filling-a-uniform-buffer) |
| The descriptor set layout is short two storage buffers | An `AppendStructuredBuffer` or `ConsumeStructuredBuffer` costs two descriptors: Slang adds a counter buffer, a binding named `counter` that is absent from `Parameters`. Build layouts from `DescriptorSets`, and pair counters with `CounterBinding`: with both kinds in one shader the two counters have the same name. | [Reflecting](#reflecting) |
| A descriptor range with a null name | Loose global uniforms (`uniform float4 gTint;` at file scope) become a constant buffer nobody declared. Check `DescriptorRange.IsImplicitGlobalBuffer`, and `ProgramLayout.GlobalConstantBufferSize` for its size. | [Reflecting](#reflecting) |
| Two option sets emit identical code under different `GetEntryPointIdentityHash` values | The hash follows the source and the compile options, not the code: naming the default optimisation level explicitly changes it. Fine as a conservative cache key; hash `GetEntryPointCodeBytes` to answer "same code". | [Linking with options](#linking-with-options) |
| A variable-count descriptor binding is refused by the device | `VARIABLE_SIZED_ARRAY` (`VARIABLE_DESCRIPTOR_COUNT` on Vulkan) is legal only on a set's highest-numbered binding, and Slang does not put the unbounded array last. `PARTIALLY_BOUND` with a fixed cap is legal anywhere. | [Projecting to NRI](#projecting-to-nri) |
| A shader using `sample` interpolation is refused | Since Slang 2026.21 the module declares `SampleRateShading`, so the device must enable `sampleRateShading` (2026.14.1 emitted a module spirv-val rejected). | [Projecting to NRI](#projecting-to-nri) |
| `ParameterBlocksUseRegisterSpaces` changes nothing | It is byte-for-byte inert on SPIR-V: a `ParameterBlock` takes a space of its own either way. | [Reflecting](#reflecting) |
| A cook fails a different shader each time with `SLANG_FAIL`, and each passes alone; or ships different bytes with no symptom at all | One global session shared across threads. It sometimes fails as `SLANG_FAIL` on one module instead of crashing, which a per-shader error log blames on the file, and has been measured returning success with different SPIR-V. Give each thread its own global session, or lock. | [Threads](#threads) |
| Uniform offsets differ between the Vulkan and the D3D12 build of one shader | Offsets are per target: std140 over-aligns a struct member and rounds its size up where D3D packing does neither (byte 32 on Vulkan, 24 on D3D, same source). Reflect against the target you run on; a baked layout belongs to its target. | [Targeting D3D12](#targeting-d3d12) |
| `FindEntryPoint(name)` finds nothing and `EntryPointCount` is 0 | The function has no `[shader("...")]` attribute, so Slang does not know its stage. Use the overload taking a `SlangStage`. | [Compiling](#compiling) |
| Shaders 2..n of a batch are about 23 % larger, and nothing was said | `slang-glslang` (where `spirv-opt` lives) is missing; Slang reports it once per global session, as an error on a successful compile. `RequireSpirvOptimizer` (on by default) refuses at session creation instead. | [Diagnostics](#diagnostics) |
| `vkCreateShaderModule` fails with nothing useful on an older device | The SPIR-V version defaults to 1.5 when unset; Vulkan 1.1 accepts at most 1.3. Pin `SpirvVersion`. | [Compiling](#compiling) |
| `IsBinaryModuleUpToDate` says current after the source changed | It answers from the asking session's file cache. Ask from a session that has not loaded the module. | [Caching parsed modules](#caching-parsed-modules) |
| A link option has no effect | `linkWithOptions` accepts every option and ignores most; only `Optimization` and `FloatingPointMode` take effect there. | [Linking with options](#linking-with-options) |
| A function found by name appears to take no parameters | It is overloaded: `FindFunction` returns one function with `IsOverloaded` set and an empty signature. Check `IsOverloaded` before `Parameters`. | [Reading a shader's functions](#reading-a-shaders-functions) |
| `SlangVersionMismatchException` after a version bump | The previous release is still staged in `runtimes/` (git-ignored). Fetch the pinned payload again. | [The payload has to be the pinned one](#the-payload-has-to-be-the-pinned-one) |
| The same shader cooked on x64 and on ARM64 hashes differently | x64 and ARM64 emit byte-identical SPIR-V within an architecture and differ on shaders using `SV_VertexID`: Slang creates the two builtins it lowers to in the opposite order, so the modules are equivalent and never compare equal. Windows and Linux agree exactly. Cook on one architecture, or content-address per architecture. | [Baking and loading](#baking-and-loading) |
| A hash over a baked layout differs between platforms | It does not: `ProgramLayout.ToJson` writes `\n` on every platform on purpose, so the layout is portable. The SPIR-V beside it carries the row above. | [Baking and loading](#baking-and-loading) |

Reflection reports what Slang says. Where reflection and the emitted module disagreed, the module
won, and the rows above are what came of it.
