# PrismSlang — Guide

How to use it. [findings.md](findings.md) is why it is shaped this way; this is what to type.

Every code sample here is the real API — the signatures were checked against the source, not
recalled — and the traps flagged along the way are the ones that actually bit somebody.

---

## Contents

- [The two halves](#the-two-halves)
- [Compiling](#compiling)
- [Reflecting](#reflecting)
- [Filling a uniform buffer](#filling-a-uniform-buffer)
- [Projecting to Vulkan](#projecting-to-vulkan)
- [Specialization constants](#specialization-constants)
- [Compute and dispatch](#compute-and-dispatch)
- [Baking and loading](#baking-and-loading)
- [Serving shaders from your own filesystem](#serving-shaders-from-your-own-filesystem)
- [Diagnostics](#diagnostics)
- [Reading a shader's functions](#reading-a-shaders-functions)
- [Laying out a generic before you use it](#laying-out-a-generic-before-you-use-it)
- [Which bindings the code actually reads](#which-bindings-the-code-actually-reads)
- [Caching parsed modules](#caching-parsed-modules)
- [The payload has to be the pinned one](#the-payload-has-to-be-the-pinned-one)
- [Lifetime](#lifetime)
- [Threads](#threads)
- [Traps worth knowing](#traps-worth-knowing)

---

## The two halves

Reference `PrismSlang` in the tool that cooks shaders. Reference `PrismSlang.Runtime` — and
`PrismSlang.Vulkan` if you want the pipeline projection — in the game that loads them. The runtime
half has no native dependency, so a shipped build carries no compiler.

```xml
<!-- asset pipeline -->
<PackageReference Include="PrismSlang" Version="0.1.0-alpha" />
<PackageReference Include="PrismSlang.Vulkan" Version="0.1.0-alpha" />

<!-- game -->
<PackageReference Include="PrismSlang.Runtime" Version="0.1.0-alpha" />
<PackageReference Include="PrismSlang.Vulkan" Version="0.1.0-alpha" />
```

`ShaderPackage` — the container, its reader, and the staleness checks — is in the runtime half.
`ShaderPackageBaker`, which is the only thing that needs a compiler, is in `PrismSlang`.

## Compiling

```csharp
using PrismSlang;
using PrismSlang.Spirv;

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

**Pin the SPIR-V version.** `SpirvVersion` defaults to unset, which means Slang picks — measured as
1.5. A device running Vulkan 1.1 accepts at most SPIR-V 1.3, and above that `vkCreateShaderModule`
fails with nothing useful to say. `VulkanEnvironment.MaxSpirvVersion(VulkanApiVersion.V1_2)` maps
the relationship. With a version pinned, `TargetOptions.VerifyEmittedSpirv` (on by default) parses
the emitted header and throws if Slang produced something else.

## Reflecting

```csharp
using PrismSlang.Reflection;

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
"I do not know" with a number near `ulong.MaxValue`, not with a null
([§9.12](findings.md#912-first-hunt-with-the-harness-when-slang-does-not-know-and-when-it-dies)),
and a field after an array whose length is a specialization constant has no offset until the
pipeline is created.

## Projecting to Vulkan

```csharp
using PrismSlang.Vulkan;

VulkanPipelineLayout vk = layout.ToVulkan();
```

Everything casts directly to a Vulkan binding's types — the enums mirror the spec's numeric values:

```csharp
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

`IsUnbounded` means the shader declared `Texture2D t[]` and said nothing about how many. Slang's own
answer for that is `0xFFFFFFFF`, which is not a count and is normalised to 0 here so it cannot reach
a driver. You choose the cap and declare it with
`VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT`.

Also on the layout: `PushConstants`, `VertexInputs`, `FragmentOutputs` (which sizes
`VkPipelineColorBlendStateCreateInfo::attachmentCount`), `Stages`, and `SpecializationConstants`.
`VulkanBinding.InputAttachmentIndex` carries the subpass index for a `SubpassInput`, which is a
different number from the binding and you need both.

`StageFlags` is **conservative** — every stage in the program is reported on every binding, because
Slang's global-scope reflection does not attribute a parameter to entry points. It over-permits
rather than omitting a stage that reads the binding; narrow it per pipeline if you care.

## Specialization constants

Both sides agree on `constant_id` by reading it, rather than hardcoding it twice:

```csharp
VulkanSpecializationInfo info = layout.Pack(new Dictionary<string, object>
{
    ["kCascadeCount"] = 4,
    ["kSoftShadows"]  = true,
}, includeDefaults: true);

// info.MapEntries -> VkSpecializationMapEntry[], info.Data -> the packed buffer
```

The value's type must match the constant's — `Pack` refuses a `double` for an `int` by name and
type rather than truncating. Watch for `c.IsFloatingPoint ? c.DefaultFloat : c.DefaultInt`, which
unifies to `double` before boxing and hands `Pack` the wrong type whichever branch is taken.

A name the shader does not declare throws, rather than silently leaving the shader on its default —
a material referring to a removed constant means the two have drifted.

## Compute and dispatch

```csharp
var stage = vk.Stage("computeMain")!;
var (x, y, z) = stage.WorkgroupSize!.GroupCountFor(width, height);   // ceil-divided
```

`GroupCountFor` refuses rather than dividing when the workgroup size is **specialized** —
`[numthreads(kGroupX, 1, 1)]` with a specialization constant means Slang reports `0` for that
dimension. To dispatch one of those, resolve it against the values you are about to supply:

```csharp
var declared = SpirvModule.Read(spirv).EntryPoints[0].WorkgroupSize!;
var resolved = VulkanWorkgroupSize.From(declared, new Dictionary<uint, uint> { [0] = 128 });
resolved.GroupCountFor(1920);
```

`From(size, null)` and `From(size, emptyDictionary)` mean different things on purpose: null is "I
have not decided", any dictionary is "these are the values", and an absent constant keeps the
shader's default because that is what Vulkan does with one.

## Baking and loading

In the asset pipeline:

```csharp
using PrismSlang.Baking;

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
whatever the function was called. It is read from the module, never derived
([§9.8 A](findings.md#98-four-findings-from-the-first-consumer)).

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
recoverable ([§9.20](findings.md#920-the-last-two-gaps-one-of-which-crashes)).

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
SlangGlobalSession.PinnedBuildTag    // what these bindings expect, e.g. "2026.14.1"
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
  — and the difference was invisible on Windows and Linux until a macOS runner faulted on it
  ([§9.25](findings.md#925-the-macos-leg-found-an-ownership-bug-in-this-binding)).
- **Double dispose is a no-op**, including concurrently.
- **Use after dispose throws** `ObjectDisposedException` rather than dereferencing.
- **Forgetting to dispose leaks** until process exit — it does not corrupt. There is deliberately no
  finalizer: the GC finalizes in arbitrary order, and Slang's objects form an ownership hierarchy it
  knows nothing about, so a finalizer could release a module into a session whose allocator has gone
  ([§9.1](findings.md#91-two-bugs-the-cross-platform-run-found)).
- A `ProgramLayout` outlives all of it. That is the point of a snapshot.

## Threads

**A `SlangGlobalSession` and everything made from it belong to one thread at a time.** This is
Slang's rule, not a limitation of the binding — `slang.h` says a global session "is currently *not*
thread-safe" and that it "and the objects created from it should be externally synchronized when
shared across threads".

Sharing one across threads without a lock is an access violation inside `loadModuleFromSource`, on
every run measured. That is the good failure mode: a cooker that gets this wrong stops rather than
shipping. It is pinned by
`Sharing_one_global_session_across_threads_is_never_silently_wrong`, which asserts the thing that
would actually hurt — it may die, but it must never return success with bytes that differ from a
serial compile.

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
`The_back_end_may_be_parallelised_once_the_front_end_has_finished`
([§9.28](findings.md#928-the-primary-use-case-had-no-coverage-at-all)).

### Two things that are process-global

- **`SlangDiagnostics.Policy` and `SlangDiagnostics.Handler` are static.** Slang's diagnostics are
  per-call with no session to hang a policy off, so redirecting them is necessarily process-wide.
  Set them once at startup. Changing either while other threads are compiling changes it for them
  too.
- **A `ProgramLayout` is immutable managed data** and is safe to read from any number of threads,
  with no lock and no relationship to the objects it came from. That is what the snapshot is for.

## Traps worth knowing

Each of these was a real defect, found by measurement. They are handled now; the list is so you
recognise the shape if you meet a cousin of one.

| | |
|---|---|
| `pName` is `main` | Slang names every per-entry-point module `main`. Use `BakedEntryPoint.ModuleEntryPointName`, never the Slang function name. |
| A vertex matrix spans locations | `float4x4` takes four. Locations come from the offsets Slang reports, never from counting fields. |
| A `float3x3` is 48 bytes | Rows on a 16-byte stride. Use `MatrixLayout.Stride`. |
| `Mode` vs the decoration | Slang says `ROW_MAJOR` where the module says `ColMajor`. Same bytes, opposite vocabulary. |
| "Unknown" is a big number | `CategorySize.Unknown` is `ulong.MaxValue - 1`. Check `IsKnown` before arithmetic. |
| A specialized workgroup size is `0` | Check `ThreadGroupSize.IsSpecialized`; do not divide by it. |
| `double3`/`double4` vertex attributes | Slang assigns one location where Vulkan requires two. The projection refuses it — the module is invalid SPIR-V, confirmed by the validation layers ([§9.17](findings.md#917-the-fourth-oracle-and-the-question-it-settled)). |
| A spec-constant-sized resource array | `Texture2D t[kCount]` never reflects: on Linux it **ends the process** inside Slang's reflection, on Windows it surfaces as a catchable `SEHException`. Not detectable in advance; also unusable on Vulkan, where `descriptorCount` is fixed before specialization exists. Do not write one. |
| An `AppendStructuredBuffer` costs two descriptors | Slang emits a separate storage buffer for the count, reported as a binding named `counter` and absent from `Parameters`. Build the descriptor set layout from `DescriptorSets`, and pair counters with `CounterBinding` — with both an append and a consume buffer, the two counters have identical names ([§9.20](findings.md#920-the-last-two-gaps-one-of-which-crashes)). |
| A descriptor with no name | Loose global uniforms (`uniform float4 gTint;` at file scope) become a constant buffer nobody declared, so its range has a null `Name`. It is not anonymous, just undeclared — check `DescriptorRange.IsImplicitGlobalBuffer`, and `ProgramLayout.GlobalConstantBufferSize` for its size ([§9.7](findings.md#97-remaining)). |
| `GetEntryPointIdentityHash` is not a cache key | It is stable and follows the source, but is **identical across optimization levels** while the emitted code is not — so keying a pipeline cache on it serves optimised code for an unoptimised request with nothing to notice. Use it to answer "same shader"; hash `GetEntryPointCodeBytes` to answer "same code". |
| An unbounded array that is not the last binding | `VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT` is legal only on a set's highest-numbered binding, and Slang does not arrange for that. Check `VulkanBinding.Binding` against the set before reaching for it; `PARTIALLY_BOUND` with a fixed cap is legal anywhere ([§9.18](findings.md#918-what-the-layers-said-about-the-whole-corpus)). |
| The `sample` interpolation modifier | Slang emits `OpDecorate … Sample` without declaring `OpCapability SampleRateShading`, which spirv-val rejects as malformed. No device feature changes it. |
| `ParameterBlocksUseRegisterSpaces` | Byte-for-byte inert on SPIR-V. A `ParameterBlock` takes a space of its own either way. |

Reflection reports what Slang says. Where the two disagreed, the emitted module won — that is what
[findings.md](findings.md) is a record of.
