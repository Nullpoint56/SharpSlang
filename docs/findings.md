# PrismSlang — Findings

An engineering log, kept because most of what is hard about binding Slang is not expressible in a
signature. Slang's headers say what a function takes; they do not say that `loadModule` returns a
borrowed pointer, that `getEntryPointCode` names every module `main`, or that asking an array how
many elements it has can kill the process. Every entry below is something that was measured, and
most of them contradicted a reasonable guess.

It is also the honest record of this binding's own mistakes. Three separate times a comment in this
codebase asserted something the code did not do, written by someone who had just understood the
problem correctly — §9.8, §9.13 and §9.15. Those are here for the same reason the Slang findings
are: the failure mode repeats, and writing it down is the only thing that has ever caught it.

Section numbers continue from [design.md](design.md), which holds §1–§8 — the surface study and the
approach it argued for, written before any of this existed. Numbers are unique across both files,
so a bare `§4.4` or `§9.8` in a code comment is unambiguous.

---

## Index

| | | |
|---|---|---|
| [9. Status](#9-status) | where every layer stands | the table to read first |
| [9.1](#91-two-bugs-the-cross-platform-run-found) | Two bugs the cross-platform run found | a borrowed pointer, and why there is no finalizer |
| [9.2](#92-what-is-deliberately-not-bound) | What is deliberately not bound | ~120 entry points, and why |
| [9.3](#93-what-tier-2-turned-out-to-be) | What Tier 2 turned out to be | three measurements that contradicted the guess |
| [9.4](#94-a-packaging-bug-worth-remembering) | A packaging bug worth remembering | the package that shipped every RID but its own |
| [9.5](#95-link-time-options-only-what-demonstrably-works) | Link-time options | five of seven are silently ignored |
| [9.6](#96-where-value-generics-live) | Where value generics live | program specialisation is type-only |
| [9.7](#97-remaining) | Remaining | the open list |
| [9.8](#98-four-findings-from-the-first-consumer) | Four findings from the first consumer | including a defect in this document |
| [9.9](#99-reading-the-module-back-and-what-that-found) | Reading the module back | five defects behind passing tests |
| [9.10](#910-the-third-round-memory-and-the-pipelines-that-are-not-graphics-pipelines) | Memory, and non-graphics pipelines | matrix strides, subpass inputs, dispatch |
| [9.11](#911-finding-these-without-being-told-a-corpus-and-two-questions-asked-of-it) | A corpus, and two questions asked of it | the harness that replaced guessing where to look |
| [9.12](#912-first-hunt-with-the-harness-when-slang-does-not-know-and-when-it-dies) | When Slang does not know, and when it dies | "unknown" spelled as a number |
| [9.13](#913-what-each-part-should-be-tested-by-and-what-that-turned-up) | What each part should be tested by | the test-strategy map, and three empty rows |
| [9.14](#914-the-mirrored-vulkan-values-four-of-which-were-wrong) | The mirrored Vulkan values | four transcription errors |
| [9.15](#915-the-last-two-rows-lifetime-and-the-callback-boundary) | Lifetime, and the callback boundary | one of five thunks kept its promise |
| [9.16](#916-splitting-the-reader-out-of-the-compiler) | Splitting the reader out of the compiler | so a game ships no compiler |
| [9.17](#917-the-fourth-oracle-and-the-question-it-settled) | The fourth oracle | the validation layers, and what they decided |
| [9.18](#918-what-the-layers-said-about-the-whole-corpus) | The layers, on the whole corpus | three findings, and a gate that excluded everything |
| [9.19](#919-closing-the-reflection-gaps-and-what-three-of-them-turned-out-to-be) | Closing the reflection gaps | signatures, a module cache, and three APIs that under-deliver |
| [9.20](#920-the-last-two-gaps-one-of-which-crashes) | The last two gaps | a descriptor with no parameter, and the fourth crash |
| [9.21](#921-the-four-crashes-what-upstream-says-and-whether-they-are-ours) | The four crashes, checked | a closed audit's residue, and one claim of ours that expired |
| [9.22](#922-the-rest-of-the-reflection-surface-and-what-closing-it-cost) | The rest of the surface | 130 of 172, and forty-two classified by evidence |

### The four oracles

Every finding here came from comparing PrismSlang's answer against something. Which something is
the interesting part, and there turned out to be four, in increasing order of independence:

1. **The emitted module** (§9.9, §9.11). Reflection is a claim; the SPIR-V decorations are what the
   driver reads. Compares as sets, both directions, so a fact one side simply lacks is a difference
   rather than a silence.
2. **Invariants with no oracle** (§9.12). For what the module cannot witness — when Slang drops a
   parameter it also emits no descriptor for it, so both sides agree by being equally silent.
3. **A vk.xml-generated binding** (§9.14). For values copied out of a specification by hand.
4. **The Vulkan validation layers** (§9.17). For the case where reflection and the module agree with
   each other and both are wrong.

---

## 9. Status

All five layers are built and green on **win-x64 and linux-x64**: 384 tests, the Tier 0 spike, and
a full reflection walk on both.

| Layer | State |
|---|---|
| L0 raw | generated; 278 `DllImport`, 383 vtable sites, 27 IIDs, ~5,100 lines. The 11 enums on the reader surface compile into `PrismSlang.Runtime` instead — §9.16 |
| L1 interop | UTF-8, blobs, diagnostics + severity policy, result codes, defaults, **ownership** |
| L2 compilation | `SlangGlobalSession` → `SlangSession` → `SlangModule` → `LinkedProgram` |
| L2 Tier 2 | type lookup, specialisation, type conformances, subtype checks, link-time options |
| L2 target env | pinned SPIR-V version, emitted-module verification, entry-point naming |
| L3 reflection | type tree + bindings + descriptor sets + generics + declarations + spec constants + buffer byte layout (matrix and element strides) |
| L3 SPIR-V | header, `OpEntryPoint` and workgroup-size reader, for facts reflection cannot report |
| L4 hosting | managed `ISlangFileSystem` via `[UnmanagedCallersOnly]` thunks |
| Baking | `ShaderPackage`: SPIR-V + layout + module entry-point name + version + workgroup size + dependency hashes |
| `PrismSlang.Runtime` | the reader half: `ShaderPackage`, `ProgramLayout`, `SpirvModule`. No native dependency, so a shipped game carries no compiler — §9.16 |
| `PrismSlang.Vulkan` | separate package, referencing `PrismSlang.Runtime`: descriptor sets, push constants, vertex inputs, fragment outputs, input attachments, stages and dispatch geometry, `VkSpecializationInfo`, target-env mapping |

| Supporting | State |
|---|---|
| `generate-native.ps1` | self-checking; asserts every interface has an IID |
| `fetch-native.ps1` | win-x64 + linux-x64 verified; osx-arm64 mapped, unrun |
| `tools/ci/verify-linux.sh` | build + tests + spike under WSL |
| `.github/workflows` | CI matrix, regeneration check, package-contents check, publish, lavapipe + validation layers on linux-x64 |
| Tests | `ModuleAgreementTests` diffs a 28-shader corpus against the emitted module as sets, both directions; `SurfaceCoverageTests` fails on any Slang enum value the corpus neither exercises nor declares untried. See §9.11 and §9.13 |

Package sizes tell the story of the split: `PrismSlang` is 34 MB (it carries the compiler for every
RID), `PrismSlang.Vulkan` is **10 KB**. A D3D12 consumer pays nothing for Vulkan.

### 9.1 Two bugs the cross-platform run found

Both were invisible on Windows. This is the entry that justifies [§8](design.md#8-tier-0-spike--done)'s fourth question.

**`ISession::loadModule*` returns a borrowed pointer.** Every other Slang out-parameter carries a
reference for the caller; module loading does not — the session retains its modules, which is what
`getLoadedModuleCount` / `getLoadedModule` enumerate. Releasing it over-releases. glibc aborts with
`free(): corrupted unsorted chunks`; the Windows heap silently tolerated the same corruption, so it
had been green here from the first commit. Bisected by skipping disposal one object at a time, then
fixed by taking our own reference on adoption. `slang.h` documents none of this.

**A finalizer that releases native objects is unsafe here.** `SlangObject` originally had one as a
safety net for a forgotten `Dispose`. But Slang's objects form an ownership hierarchy —
`IGlobalSession` → `ISession` → `IModule` → `IEntryPoint` — and **the GC finalizes in arbitrary
order**, so it can finalize the global session and then release a module into a torn-down
allocator. The finalizer is gone; each wrapper instead holds a managed reference to its owner, so
the owner cannot be collected first. Failing to dispose now leaks rather than corrupts, which is
the right way round: a leak is findable with a profiler, corruption is a crash dump six months
later.

### 9.2 What is deliberately not bound

Tier 4, ~120 entry points: the legacy `spCompileRequest*` API, the bytecode VM, coverage tracing,
cooperative-matrix metadata, and `slang-rhi` in its entirety. Irrelevant to a Vulkan renderer;
binding them would be surface for its own sake.

### 9.3 What Tier 2 turned out to be

Three things were measured rather than assumed, and all three contradicted a reasonable guess:

- **`type_param T : IFoo;` is the global specialisation hook.** `extern struct T : IFoo;` compiles
  and produces *no* specialisation parameter — a silent no-op, which is the worst kind of wrong
  syntax.
- **Interfaces have no declaration kind.** An `interface` comes back as
  `SLANG_DECL_KIND_UNSUPPORTED_FOR_REFLECTION` with its name intact. A gap in Slang, not a
  modelling choice here; confirm candidates via `IsSubType` instead.
- **A generic function is a `Generic` node, not a `Func`.** `applyMaterial<T>` carries its name on
  the generic, with an unnamed function nested inside — so enumerating `Func` alone misses every
  generic in the module.

### 9.4 A packaging bug worth remembering

The published package once contained every RID **except the one it was built on**. The host RID's
files matched both the `Content` glob (flat copy, `Pack="false"`) and the `None` glob (packed), and
`Pack="false"` won. It packed cleanly, restored cleanly, and failed at run time with
`DllNotFoundException`. The globs are now disjoint, and CI asserts the host RID's payload is
present in the `.nupkg`.

### 9.5 Link-time options: only what demonstrably works

`LinkTimeOptions` exposes typed properties for exactly two options, because Slang accepts the whole
`CompilerOptionName` range at `linkWithOptions` and silently ignores most of it. A typed property
for an ignored option reads as a supported feature and does nothing. Measured by diffing emitted
SPIR-V for one shader:

| Option | Effect at link time |
|---|---|
| `Optimization` | **works** — None 2,052 B / default 1,868 / Maximal 1,668 |
| `FloatingPointMode` | **works** — Precise +36 B (Fast matches the default, because Fast *is* the default) |
| `DebugInformation` | none — session/target-level, not link-level |
| `VulkanUseEntryPointName` | none *at link time*; works on the session or target — see §9.8 |
| `Capability` | none — `spirv_1_6` still emits 1.5. The profile is the only version knob |

The ineffective three are reachable via `RawEntries` if a future release honours them. A test
asserts that a raw entry and its typed equivalent produce byte-identical output, so the escape hatch
cannot rot.

> An earlier version of this table said `VulkanUseEntryPointName` had no effect **and** was
> redundant because Slang already emitted the entry point's own name. Both halves were wrong. §9.8.

### 9.6 Where value generics live

`SpecializationParameterKind` has no `Value` member, for the same reason `DeclarationKind` has no
`Interface`: nothing can produce it. Slang's program-level specialisation API is type-only —
`struct Buffer<let N : int>` yields **zero** specialisation parameters, and so does a link-time
constant (`extern static const int kCount;`).

Value parameters are real, but they belong to the generic declaration, so they surface as
`GenericParameter` on `DeclarationNode`: `Buffer` reports `T` (Type, constrained to `IThing`) and
`N` (Value). A consumer reading only `ProgramLayout.SpecializationParameters` would never see `N`,
which is why the declaration tree is the right home for it.

### 9.7 Remaining

Kept current. An entry leaves this list when it is closed, and the closing is recorded wherever it
happened — a "remaining" list that still names finished work is the same defect as a comment
describing code that has moved on, which §9.8, §9.13 and §9.15 are each an instance of.

- **osx-arm64** — RID mapped in `fetch-native.ps1`, never executed. Same class of risk linux-x64
  turned out to carry, so treat it as unverified rather than probably-fine.
- ~~**CI has never fully run.**~~ It runs. Two things it caught that local runs could not, both
  recorded where they were fixed: a hand-edit to generated code that the regeneration check
  rejected, and a step that parsed a console summary whose shape changed when a logger was added to
  it. Nothing is rehearsed-only any more.
- **Per-stage descriptor visibility.** `VulkanProjection` reports every program stage on every
  binding. Conservative and safe, but a renderer wanting tight `stageFlags` still has to narrow
  them itself. Slang's global-scope reflection does not attribute a parameter to entry points, so
  closing this needs a per-entry-point walk rather than more plumbing.
- **The implicit global constant buffer is unnamed.** Loose global uniforms (`float4 gTint;` at
  file scope) become a synthesised buffer that the module calls `globalParams` and reflection
  attributes to no declaration, so its descriptor range has a null name.
  `ProgramLayout.GlobalConstantBufferBinding` identifies it by index; a host binding purely by name
  has to special-case it. Left alone rather than named after the module, because inventing a name
  for something nobody declared is the smaller of two bad options only until someone declares a
  parameter with that name.
- **`getEntryPointHash` is deliberately unbound.** Measured stable and content-sensitive, but
  **identical across `-O0` and `-O3`** while the emitted SPIR-V differs (2,048 vs 1,664 bytes), so
  it is safe as a shader-identity key and unsafe as a pipeline-cache key. Hash the emitted bytes
  instead.

---

### 9.8 Four findings from the first consumer

The engine team integrated PrismSlang and hit four things. Two were real gaps; **one was a defect in
this document that a consumer then built on**, which is the entry worth reading.

#### A. The entry point in the module is called `main`

`PipelineBuilder` hardcoded `"main"` in `VkPipelineShaderStageCreateInfo::pName`, and the report was
that this must be wrong because Slang emits each entry point under its own name — citing §9.5's
claim above and the Tier 0 spike, which printed `vertexMain` and `fragmentMain` next to each
generated module.

The claim was wrong and so was the spike's presentation. Measured by parsing `OpEntryPoint` out of
the emitted modules:

| Path | Name in the module |
|---|---|
| `getEntryPointCode` | **`main`**, whatever the Slang function is called |
| `getEntryPointCode` + `VulkanUseEntryPointName` on the **session** or **target** | the Slang name |
| `getEntryPointCode` + the same option at **link** time | `main` — the option is ignored there |
| `getTargetCode` (whole program) | the Slang names, necessarily: one module, several stages |

The spike had never read a name back. It printed the Slang names it had been *handed*, beside the
bytes, which reads exactly like a report about the bytes. Nothing checked, so the wrong impression
propagated into this document and out to a consumer, where the two errors cancelled: hardcoding
`"main"` was in fact correct, for a reason opposite to the one everyone believed.

The fix is not to pick a side. It is that **nobody derives the name**:

- `LinkedProgram.GetSpirvEntryPoint` reads it from the module.
- `BakedEntryPoint` carries `Name` (Slang, for lookup) and `ModuleEntryPointName` (for `pName`) as
  separate fields, and a test asserts the recorded name matches the accompanying bytes under both
  settings of the option.
- The spike prints both columns.
- `TargetOptions.UseEntryPointNameInSpirv` exposes the option at the level where it works.

#### B. Specialization constants were reflected but unusable

The data was already there — and unfindable. Slang reports a specialization constant as a top-level
shader parameter in category `SPECIALIZATION_CONSTANT`, whose binding **offset is the
`constant_id`**. Verified against the emitted module's `SpecId` decorations for explicit,
non-contiguous (`9`) and Slang-assigned ids alike.

Three traps, all measured:

- **The reported size is `1`.** That is one *slot* in the specialization-constant category, not one
  byte; the uniform size is `0`. Using either would write a one- or zero-byte
  `VkSpecializationMapEntry` for a four-byte int, so `ByteSize` comes from the scalar type. Booleans
  are 4 — Vulkan wants a `VkBool32`.
- **`GetDefaultValueFloat` succeeds on integers.** `uint kFlags = 7` returns `SLANG_OK` from *both*
  default-value getters. Probing in the wrong order silently turns integers into floats, so the
  walker dispatches on the declared scalar type instead.
- **Spec constants collide with descriptors in the parameter list.** They report space 0 and an
  index that is really the constant id, so `[vk::constant_id(0)]` occupies the same
  (space 0, index 0) coordinates as the descriptor at set 0 binding 0 — and they come *first*. This
  was a live bug: `VulkanProjection`'s first-writer-wins name table labelled real descriptors with
  constant names. Found by measuring, not by the complaint.

Surfaced as `ProgramLayout.SpecializationConstants` (neutral) and
`VulkanSpecialization.Pack` / `VulkanPipelineLayout.SpecializationConstants` (map entries plus a
packed buffer). Supplying a name the shader does not declare throws rather than no-ops — a material
referring to a removed constant means the two have drifted, and silently leaving the shader on its
default is the version of that failure nobody can see.

On the related point: program-level specialisation being type-only (§9.6) does push archetype and
feature variants toward generics at cook time. That is the better answer independently, and it
shrinks the variant problem rather than solving it — spec constants are still needed for the axes
that stay dynamic.

#### C. A missing spirv-opt is silent after the first shader

Real, and worse than reported. With `slang-glslang` absent, `getEntryPointCode` **succeeds** and
emits unoptimised SPIR-V, reporting:

```
error[E00100]: failed to load downstream compiler 'spirv-opt'
note[E99996]: failed to load dynamic library 'slang-glslang'
```

Measured on `LinkOptionsTests`' shader: 2,048 bytes without, 1,864 at Slang's default and 1,664 at
maximal with — a 23% regression against optimised output. And the diagnostic fires **once per global
session**: six compiles across two global sessions produced exactly two messages, so shaders 2..n of
every batch are silently oversized with nothing said at all.

That frequency is why two mechanisms were needed, not one:

- `SlangDiagnostics.Policy` defaults to `ThrowOnError`, so an error-severity diagnostic on a
  successful call throws instead of scrolling past. Necessary, but it only catches the first shader.
- `GlobalSessionOptions.RequireSpirvOptimizer` defaults to on and calls
  `checkPassThroughSupport(SPIRV_OPT)` at session creation — deterministic, ordering-independent,
  and before a single shader is compiled. Opt out for a deliberate `-Profile Minimal` deployment.

CI asserts the library is both staged and packed; `DownstreamCompilerTests` asserts it is loadable
*and* that optimised output is actually smaller, so a present-but-inert library fails too.

#### D. The target environment is now pinned and checked

Slang has no `-fspv-target-env`. The SPIR-V version comes from the target profile and nothing else:
every `spirv_1_0`..`spirv_1_6` profile emits exactly the header version it names, and a link-time
`-capability spirv_1_6` is silently ignored. Unset, Slang emits 1.5 — which is where the spike's 1.5
came from: a default, not a decision.

- `TargetOptions.SpirvVersion` names it. Null means "not specified", which is deliberately not the
  same state as 1.5, so an existing caller can pin `Profile = "spirv_1_6"` alone without being told
  it contradicts a version they never chose. Setting both to different versions is rejected at
  session creation rather than resolved by precedence.
- `TargetOptions.VerifyEmittedSpirv` (on by default) checks the emitted header against the request.
- `VulkanEnvironment.MaxSpirvVersion` maps Vulkan version to ceiling. Note 1.4 raises no ceiling
  over 1.3 — both cap at SPIR-V 1.6 — so "the device is 1.4" does not by itself say what to emit.
  Retired DXC's `vulkan1.3` meant SPIR-V 1.6, so matching the old pipeline means asking for 1.6.

Also measured while pinning this: `spirv_1_0`, `1_1` and `1_2` each emit the header version they
name *and* `warning[E50011]: SPIR-V version too old` on a successful compile; `1_3` upward are
clean. Another diagnostic-on-success, and the live test case for the severity classifier that
decides whether one of these fails a build.

---

### 9.9 Reading the module back, and what that found

§9.8's entry-point-name defect came from a claim nobody had checked against the bytes. So the same
question was asked of the rest of the pipeline layout: for a handful of shaders, what does the
emitted module's `DescriptorSet` / `Binding` / `Location` decoration say, and does PrismSlang agree?

It did not, in five places. Four were shipping wrong data behind passing tests, because the tests
only checked the projection against itself.

| | Module says | PrismSlang said |
|---|---|---|
| **Vertex struct with a matrix** | `position`@0 `instanceXform`@1 `uv`@5 `bones`@6 | `position`@0 `uv`@2 `bones`@3, matrix absent |
| **Entry-point `uniform Texture2D`** | set 0 binding 2 | not in the layout at all |
| **`Texture2D t[]`** | one binding | `descriptorCount = 0xFFFFFFFF` |
| **`[vk::push_constant]` + a texture** | binding 0 is `albedo` | binding 0 named `push` |
| **MRT `GBuffer` return** | locations 0, 1, 2 | nothing — no such concept |

**Vertex locations were the serious one.** The projection numbered attributes by counting fields,
one location each. A `float4x4` occupies four, so everything declared after one was reported four
locations too low; and because a matrix had no attribute format it was skipped *while still
advancing the counter by one*, so it vanished entirely. A mesh with a per-instance transform —
ordinary instanced rendering — got no transform and read its UVs from whatever buffer was bound at
location 2. Locations now come from the offset Slang reports for each field, and a matrix becomes
one attribute per location, with the width derived from the location count Slang gave rather than
from an assumption about row- versus column-major.

**Entry-point `uniform` parameters are real descriptors.** They are numbered from zero inside the
entry point's own block, and the block's position in the global numbering is the offset on
`getVarLayout`, per category — with three globals it reports `DESCRIPTOR_TABLE_SLOT off=3`, and the
entry point's two parameters land at 3 and 4. Their `uniform` *values* go somewhere else again: into
an implicit push-constant buffer, whose size is on the element type layout and not on the wrapper,
which reports 0.

**The naming bug was the same bug twice.** §9.8 fixed specialization constants stealing descriptor
names by excluding specialization constants. Push constants do exactly the same thing for exactly
the same reason — they report (space 0, index 0) and sort first — and were still doing it.
Blacklisting kinds as they turn up cannot converge; `OccupiesDescriptorSlot` is now a whitelist.

**Two findings that were not defects**, kept because they cost time to establish:

- A `float depth : SV_Depth` field reports *no* `VARYING_OUTPUT` category at all and is decorated
  `BuiltIn FragDepth`. Presence of the category, not the spelling of the semantic, separates a
  colour attachment from a builtin — the same test that keeps `SV_VertexID` out of the vertex
  inputs. No list of system-value names to maintain.
- Writing `: SV_Target` on a fragment function that returns a struct makes Slang **re-label every
  field**, so `SV_Depth` genuinely becomes `SV_TARGET1` and genuinely gets a location. Reporting
  two colour attachments there is correct. This looked exactly like a bug for a while.

The lasting change is `SpirvDecorations` in the test project and the `PipelineLayoutTests` built on
it: the projection is now asserted against the module rather than against a number typed out by
hand. All five defects would have failed on the first run.

#### Staleness now covers the import graph

`IModule::getDependencyFileCount` reports the shader, everything it `import`s, and everything
`#include`d. `ShaderPackage` hashed only the single source string it was handed, so editing a shared
lighting module left every shader importing it claiming to be current — a shader running the version
before last, which reads as a renderer bug until someone suspects the cooker.

`ShaderPackage.Bake(program, module)` now records each dependency's path and content hash;
`StaleDependencies()` returns *which* files changed, so a build log can say why it rebuilt. Hashes
rather than timestamps, so a checkout does not rebuild the project. A package baked from a string
has nothing to check and `IsCurrent` reports false rather than true, because claiming currency for
something unverifiable is the failure the method exists to prevent.

#### Two things deliberately refused rather than guessed

- **More than one push-constant buffer.** Slang counts push-constant *buffers*, so the offset it
  reports is a buffer index, not a byte offset. They coincide at 0, which is why one range has
  always looked right. Two — an explicit `[vk::push_constant]` block plus entry-point uniforms —
  cannot be laid out into the single block Vulkan wants without inventing offsets, so it throws.
- **A vertex input with no attribute format that still occupies locations.** Silently dropping one
  is how the matrix disappeared.

#### Unbounded arrays

`Texture2D t[]` reports a descriptor count of `0xFFFFFFFF`, which is not a count and must never
reach `VkDescriptorSetLayoutBinding::descriptorCount`. Normalised to 0 with `IsUnbounded` set: the
real cap is a host decision, made with `VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT` against
the device's limits. Fixed-size arrays (`Texture2D t[8]`) were always correct.

---

### 9.10 The third round: memory, and the pipelines that are not graphics pipelines

§9.9 asked the descriptor and location decorations whether reflection agreed with them. This round
asked the same question of two areas it had not covered — where bytes go inside a buffer, and what a
non-graphics pipeline needs — and of the descriptor model again, now that there was a harness for
it. Seven more disagreements, all reported by reflection in a form that looked reasonable.

| | Module says | PrismSlang said |
|---|---|---|
| `float3x3` in a cbuffer | rows 16 bytes apart | size 48, **no stride at all** |
| `StructuredBuffer<Instance>` | `ArrayStride 80`, fields at 0/12/16 | element **size 0, no fields** |
| `SubpassInput` x2 + a texture | bindings 0, 1, 2 | one binding, **at 2** |
| `ParameterBlock<Frame>` | `gFrame.shadow` set 1 binding 1 | set 1 binding 1, **unnamed** |
| `ParameterBlock` inside a block | `gOuter.inner.t` set 1 binding 0 | **no set 1** |
| `[numthreads(kGroupX,1,1)]` | `LocalSizeId` naming spec constant 0 | thread group **`[0, 1, 1]`** |
| `[format("rgba8")] RWTexture2D` | image format `Rgba8` | nothing, anywhere |

#### The matrix stride is the one that corrupts data silently

Reflection reported a `float3x3` as 48 bytes and stopped there. It is 48 because its three rows sit
**16 bytes apart**, not because it holds 48 bytes of matrix — a host writing nine contiguous floats
puts rows 1 and 2 four bytes early and gets a transform that is wrong in a way that looks like bad
maths rather than bad memory. A row-major `float4x2` is 64 bytes for eight floats, which is worse.

Slang will not tell you the stride: `GetStride` on a matrix returns the whole matrix and
`GetElementStride` returns 0. So `MatrixLayout.Stride` is derived — the total divided by the number
of entries along the major axis — and `UniformLayoutTests` asserts the derivation against the
module's `MatrixStride` decoration for 4x4, 3x3, 2x4 and 4x2 under **both** layout modes, because
the non-square shapes are where dividing by the wrong axis still yields a plausible number.
`ComponentOffset(row, column)` applies the rule so a caller never has to.

While measuring it: **the mode Slang reports and the word the module decorates are opposites.** A
session set to `ROW_MAJOR` emits members decorated `ColMajor`, and vice versa. That is not a bug —
HLSL writes rows-by-columns while SPIR-V's `OpTypeMatrix` is a list of columns, so the same bytes get
opposite names — but it is exactly the shape of the entry-point-name confusion in §9.8, so it is
pinned by a test and stated on `MatrixLayout` rather than left for someone to rediscover.

#### A structured buffer had no element layout

`ResourceLayout.ResultType` named the type — `Instance` — and reported a size of 0 with no fields,
because `spReflectionType_GetResourceResultType` hands back a `SlangReflectionType*` and layout
questions can only be asked of a type *layout*. So reflection could tell you an instance buffer held
an `Instance` and nothing about how many bytes that was or where `xform` sat: the buffer could not be
filled. `GetElementTypeLayout` on the resource has the real thing, and returns null on textures,
samplers, byte-address and typed buffers, subpass inputs and acceleration structures — so no shape
test is needed, the resources that have an element layout are exactly the ones that return one.
`ResourceLayout.ElementType` and `ElementStride` now carry it.

#### Two ways to lose a descriptor set

**Subpass inputs.** Slang omits them from *both* its binding-range and its descriptor-set views —
while still consuming their binding indices. With `gA`, `gB`, `gOrdinary` the module decorates
bindings 0, 1 and 2 and reflection offered exactly one range, at index 2. A deferred renderer
building `VkDescriptorSetLayout` from that gets one binding for a shader that reads three. The
parameter list is the half of Slang's reflection that matches the module, so `ReadDescriptorSets`
gained a reconciliation pass: any parameter holding a descriptor-table slot no range covers gets a
range, typed from its resource shape. Only the subpass shape is mapped; anything else lands as
`SLANG_BINDING_TYPE_UNKNOWN`, which the projection refuses loudly. A hole reported as unknown is a
bug report — a hole given a plausible type is this whole exercise repeating.

**Nested parameter blocks.** The walk found a top-level block's space and stopped, so a
`ParameterBlock` declared *inside* one contributed no descriptor set at all. The offsets accumulate
down the chain, and they accumulate through a plain struct too: measured with
`Root { float4 c; Mid mid; Texture2D o; }` and `Mid { float2 pad; ParameterBlock<Leaf> leaf; }`,
`mid` reports a sub-element offset of 1, `leaf` reports 0, and the module puts `gRoot.mid.leaf.t` in
set 1 — so a struct that owns no descriptor set of its own still has to be walked through.
`getSubObjectRangeSpaceOffset`, which looks like the API for exactly this, reports 0 for the same
block.

#### Names, and the fallback that looked dead

`DescriptorRange.Name` was always null; the Vulkan projection recovered names by scanning the
top-level parameter list, which cannot see inside a `ParameterBlock` — so every binding of Slang's
own descriptor-set feature came back unnamed, and a material system binding by name had nothing to
work with. The binding-range view links a leaf variable to a descriptor range through
`getBindingRangeDescriptorSetIndex` and `getBindingRangeFirstDescriptorRangeIndex`, and that works
at global scope and inside a block, so the walk populates `Name` from it.

With that in place the old scan looked redundant, and **the whole suite passed with it disabled**.
It is not redundant: the binding-range view does not attribute an entry point's `uniform`
parameters, so deleting it would have silently dropped the name of every entry-point descriptor. A
probe caught that, not a test — so `An_entry_point_uniform_descriptor_is_still_named` exists now to
make the second attempt fail loudly.

#### A thread group size of zero

`[numthreads(kGroupX, 1, 1)]` with `kGroupX` a specialization constant makes
`getComputeThreadGroupSize` report `[0, 1, 1]`. It genuinely does not know — the value arrives at
pipeline creation — but a host computing `(width + size.X - 1) / size.X` divides by zero, and one
that guards the divide dispatches a wrong group count instead, which is a shader quietly processing
part of its domain.

The module is honest: a fixed size emits `OpExecutionMode LocalSize` with literals, a specialized
one emits `OpExecutionModeId LocalSizeId` naming the constants. So `SpirvModule` reads it, resolving
the ids through `OpSpecConstant` and its `SpecId` decoration to recover **both** the constant id and
the default the shader declared. `ThreadGroupSize.IsSpecialized` flags the reflection-side case, and
`VulkanWorkgroupSize.GroupCountFor` refuses rather than dividing.

> Added later, on checking whether this was known upstream: **the zero is the residue of a fix.**
> This exact call used to access-violate on this exact shader —
> [shader-slang/slang#8738](https://github.com/shader-slang/slang/issues/8738), "AV in
> `spReflectionEntryPoint_getComputeThreadGroupSize` when using specialization constants for
> NumThreads attribute", closed December 2025 — and the fix checks for a specialization constant
> before dereferencing the extent. The crash became a zero. Worth knowing before reading the zero as
> an oversight: it is the deliberate answer to a reported crash, and the gap is that nothing carries
> "unknown" any further than a value that arithmetic accepts.

`VulkanWorkgroupSize.From(size, values)` distinguishes null from empty on purpose: null means
nothing has been decided and the size stays specialized; any dictionary, empty included, says *these
are the values I will supply*, and an absent constant keeps its default because that is what Vulkan
does with one. That distinction came out of a test that failed — the first version had no way to say
"I am deliberately using the defaults".

#### The rest

- **`VulkanPipelineLayout.Stages`.** The layout described a graphics pipeline only. It now carries
  each entry point's stage flag and workgroup size, so a compute pipeline can be built and
  dispatched from it without going back to the neutral layout.
- **`BakedEntryPoint.WorkgroupSize`**, read from the module for the same reason as
  `ModuleEntryPointName`. Package format is now **4**.
- **`[format("rgba8")]`** was reported nowhere — it is a built-in attribute, so it never appeared in
  the user-attribute list either — and an unformatted `RWTexture2D` requires the device's
  `shaderStorageImageWriteWithoutFormat`, which the host had no way to learn. Now on
  `DescriptorRange.ImageFormat` and `VulkanBinding.ImageFormat`.

  The call behind it is guarded, and the guard is load-bearing:
  **`spReflectionTypeLayout_getBindingRangeImageFormat` access-violates** — crashes the process, not
  returns `unknown` — when asked about a `PARAMETER_BLOCK` binding range. Only texture-shaped ranges
  are asked, and the test shader for image formats contains a parameter block so the guard is
  exercised.

#### What the harness looks like now

`SpirvDecorations` grew member offsets, matrix strides, array strides, input attachment indices,
image formats and execution modes. 272 tests; every claim in the two tables above is asserted
against the emitted module's own bytes rather than against a number typed out by hand.

The pattern worth keeping from this round is narrower than "read the module back". Three of the
seven were found by a **probe**, not by a test — the nested parameter block and the
looks-dead-but-is-not fallback among them. A test asserts what someone already thought to ask. A
probe that prints reflection and the module side by side, for a shader nobody has tried yet, is what
turns up the question.

---

### 9.11 Finding these without being told: a corpus, and two questions asked of it

Three rounds of hand-picking a shader, hand-picking a fact and comparing it with the module found
twelve defects. That works and it does not scale, because it only finds what someone thought to ask.
Sorting the twelve by how they failed says what to build instead:

- A few were a **wrong value** — reflection said 2 where the module said 5. A field-by-field
  comparison finds those.
- Most were **missing** — reflection said nothing at all about a subpass input, a nested block, a
  structured buffer's element. A field-by-field comparison cannot find those, because the fields you
  compare are the fields you thought of.

So `ShaderCorpus` is twenty programs covering every stage Slang will compile and every descriptor
kind it will emit, and two tests ask everything of it.

#### `ModuleAgreementTests` — set equality, both directions

Each case is linked whole and emitted with `getTargetCode`, then reduced to a set of facts — every
descriptor's set and binding, every vertex input and fragment output location, every input
attachment index, every specialization id, every struct member's byte offset and matrix stride,
every stage and workgroup size. The same set is derived from `ProgramLayout` and its Vulkan
projection. The two must be equal.

Equal, not "agrees where both speak". A fact nobody thought to assert is still in the set, and a
fact one side simply does not have is a **difference** rather than a silence — which is the whole
point, because silence is how the majority of these hid.

A difference that is understood is declared on the corpus case with its reason, and the assertion is
that the difference is *exactly* the declared set. So a known gap can neither grow quietly nor be
fixed without someone deleting the declaration. There is one, and it is the implicit constant buffer
for loose global uniforms: the module calls it `globalParams`, reflection attributes it to no
declaration. Closing it would mean putting a name chosen by Slang's SPIR-V emitter into the
API-neutral layer, which is worse than the gap.

The harness was checked by breaking things on purpose. Removing the subpass reconciliation loses
four facts and the diff names all four; deriving the matrix stride from the wrong axis reports
`ulTall stride 32` and `ulWide stride 8` against the module's 16 for both — the two non-square
shapes, exactly the ones where the wrong axis still yields a plausible number.

Getting the harness itself right took three iterations, and the second bug in it is worth recording:
reading `FieldLayout.UniformOffset` unconditionally put every varying and every resource field at
offset 0. That is the same defaulting-an-absent-offset mistake §9.9 found in the vertex-input
projection, committed here by the code written to catch it.

#### `SurfaceCoverageTests` — what has never been tried

Agreement proves that what the corpus covers is reported correctly. It says nothing about what the
corpus does not cover, and that is where every defect so far has lived. Subpass inputs were not
wrong in a way anyone could see; they had never been compiled.

So this walks `SlangStage`, `SlangBindingType`, `SlangResourceShape` and `SlangTypeKind` and requires
every value to be either exercised by the corpus or listed with a reason it will not be. It finds no
bugs. It converts "nobody tried that" from invisible into a failing test, which is the only way that
first kind of gap becomes visible before a consumer finds it. The ratchet runs both ways: a value
that becomes exercised must be removed from its list, and a value a future Slang release adds
arrives unexercised and undeclared. Both fail.

Writing the lists surfaced two things worth keeping:

- **`SLANG_BINDING_TYPE_PARAMETER_BLOCK` never reaches a descriptor range on SPIR-V.** A parameter
  block takes a register space of its own, and the descriptor it occupies is reported as
  `CONSTANT_BUFFER` for its implicit uniform buffer; the block itself appears only as a *binding*
  range at global scope, with `descriptorSetIndex == -1`. `VulkanProjection` maps it to
  `UniformBuffer` anyway — that arm is unreachable for this target, and the declaration is the
  record of that rather than a comment nobody will check.
- **`SLANG_TYPE_KIND_MESH_OUTPUT` never reaches the layout.** The mesh case declares
  `out vertices` and `out indices` and Slang reports neither as an entry-point parameter, so a
  renderer sizes mesh output from the shader source rather than from reflection.

#### And it found one on the way in

Writing the corpus meant compiling every stage for the first time, which is itself the point.
`getComputeThreadGroupSize` was read only for `SLANG_STAGE_COMPUTE` — but mesh and amplification
declare `[numthreads]` too, and the module emits `OpExecutionMode LocalSize` for all three. A mesh
shader that said `[numthreads(32,1,1)]` reported no workgroup size at all. Found before either test
was written, by the corpus existing.

297 tests.

---

### 9.12 First hunt with the harness: when Slang does not know, and when it dies

Round four was the first done the intended way — add shaders to the corpus, run, read the diff — and
it behaved differently from the three before it. Eight new cases went in covering per-field matrix
orientation, append/consume buffers, buffer arrays, nested uniform structs, 64-bit scalars, system
values, interpolation modifiers and specialization-sized arrays. **Five of the eight passed on the
first run**, which is itself the result: per-field `row_major` / `column_major` overrides, nested
struct offsets, `double` alignment, unbounded sampler arrays and interpolation modifiers are all
reported correctly, and now have a test saying so rather than nobody's confidence.

The other three all came from one construct.

#### An array whose length is a specialization constant

Slang answers "I do not know" with a number. `SLANG_UNKNOWN_SIZE` is `~size_t(0) - 1`, one below
`SLANG_UNBOUNDED_SIZE`, and it comes back from the size and offset queries as an ordinary `ulong`.
Cast to `uint` it is `0xFFFFFFFE`; used as an offset it points four exabytes past the buffer;
compared against anything it is simply large. Three consequences, all measured:

| Query | Fixed array | Length is a spec constant |
|---|---|---|
| `GetSize(UNIFORM)` on the array | 192 | `0xFFFF_FFFF_FFFF_FFFE` |
| Offset of the next field | 192 | `0xFFFF_FFFF_FFFF_FFFE` |
| `spReflectionType_GetElementCount` | 3 | **access violation** |
| `GetSpecializedElementCount` | 3 | **access violation** |

The element count is the sharp one. The C++ header presents `getElementCount()` as calling
`spReflectionType_GetSpecializedElementCount`, documented to return `SLANG_UNKNOWN_SIZE` "when size
depends on unresolved generic parameters or link-time constants" — so the resolving version is the
one that is supposed to handle this, and it crashes too. The walk now reads the size marker first
and never asks for a count it has been told is unknown. `ArrayLayout.IsSpecializationSized` reports
the case, `CategorySize.IsKnown` and `FieldLayout.IsUniformOffsetKnown` guard the markers.

#### The buffer disappears, and so does the module's descriptor

`ConstantBuffer<Block>` where `Block` holds such an array comes back with **no bindings at all** —
no descriptor range, no push-constant range, no category. So does
`[[vk::push_constant]] ConstantBuffer<Block>`. A renderer building a pipeline layout from that binds
nothing where the shader expects a buffer.

This is the case the agreement harness cannot see, and it is worth being precise about why. The
emitted module drops the descriptor as well, so reflection and the module agree — by being equally
silent — while the shader still reads the thing. §9.11 said the module is the oracle; this is where
that runs out, and the answer is the third technique rather than a better comparison:
`VulkanProjection` now asserts an **invariant** with no oracle at all, that every top-level shader
parameter has a binding of some kind, and refuses by name when one does not. It needs no ground
truth because it needs no comparison.

#### And one that cannot be guarded

A *resource* array sized by a specialization constant — `Texture2D t[kCascades]` — **terminates the
process**. Not an exception: `spReflectionTypeLayout_getBindingRangeCount` on a global scope
containing one exits with no managed stack and no diagnostic, so reflecting such a shader kills the
cooker.

> **Amended, once it could be measured from outside.** That sentence was written on Windows and is
> true on Linux. Slang throws a C++ `Slang::InternalError` out through an `extern "C"` boundary, and
> the two runtimes disagree about what that is: on linux-x64 there is no handler, `std::terminate`
> runs and the process takes SIGABRT — exit 134, *terminate called after throwing an instance of
> 'Slang::InternalError'*. On win-x64 SEH carries it to the CLR, which surfaces a catchable
> `SEHException`, and the cooker loses the shader rather than itself.
>
> Both are now asserted, from a child process, because a test cannot assert the Linux behaviour
> inside the runner it would take down: `tests/PrismSlang.Prober` performs one reflection and exits,
> and `ProberTests` reads the exit code. The control case matters more than the crashing one — a
> prober that cannot find its payload also dies, and it caught exactly that twice while this was
> being written. For an engine this platform split is the operational fact: a Linux cook farm dies
> mid-batch where a Windows workstation reports one failed shader. Same shape as §9.1, and the same
> reason it took a second platform to see.

It is not guarded, because it cannot be detected. Measured side by side, a spec-sized texture array
and a fixed-size one are identical on every query that does not crash — same categories, same
`DESCRIPTOR_TABLE_SLOT=1`, same stride of 0, same alignment, same `genericParamIndex` of -1 — and
the size marker that gives the uniform case away does not exist for a resource, which reports no
uniform footprint at all. There is no question to ask that survives being asked.

The mitigating fact is that the construct cannot work on Vulkan regardless:
`VkDescriptorSetLayoutBinding::descriptorCount` is fixed when the descriptor set layout is created,
which is before any specialization value exists. So it is a shader nobody can use, that kills the
compiler rather than rejecting it. Recorded here, kept out of the corpus, and worth reporting
upstream.

#### Two differences that are the module's fault, not ours

Both declared as corpus gaps rather than fixed, because in both the layout is the more useful of the
two answers:

- **Append and consume buffers are both named `elements` in the module**, after the payload field of
  the struct Slang wraps them in, so the two are indistinguishable by name in the SPIR-V. The layout
  reports the declared names. Sets and bindings agree.
- **The implicit buffer for loose global uniforms** is `globalParams` in the module and unnamed in
  the layout, from §9.11.

#### What the round says about the method

The three rounds before this one found twelve defects at roughly four per round, all by someone
choosing what to look at. This round found three, and all three were in the one construct the corpus
gained that nobody had ever compiled — while five of the eight new areas turned out to be already
correct. That is the shape you want: the yield moves from "wherever we looked" to "wherever we had
not looked", and the answer to *where have we not looked* is now a failing test rather than a guess.

314 tests.

---

### 9.13 What each part should be tested by, and what that turned up

The corpus harness covers one axis very well — does reflection agree with the module — and nothing
else. Asking what the *right* test is for each part of the codebase says where the next holes are,
and three of the answers were missing entirely.

| Part | The test that fits it | State |
|---|---|---|
| L0 raw | every declared entry point resolves in the loaded library | **added** — found two Windows-only bindings |
| L0 raw | generated output matches a fresh run of the generator | CI regeneration check |
| L1 interop | round-trip and lifetime; dispose order, double dispose | **added**, §9.15 |
| L1 diagnostics | severity classification against real compiler text | `DiagnosticPolicyTests` |
| L1 defaults | drift against the header's initialisers | `DefaultsDriftTests` |
| L2 options | each option demonstrably changes the output, or is declared inert | **added** |
| L2 link options | same, at link time | §9.5 |
| L3 reflection | set-equality against the emitted module, both directions | §9.11 |
| L3 reflection | invariants with no oracle, for what the module cannot witness | §9.12 |
| L3 coverage | every Slang enum value exercised or declared untried | §9.11 |
| L3 SPIR-V reader | malformed, truncated and corrupted input | **added** |
| L4 hosting | a managed filesystem serving a whole import tree | `HostingTests` |
| L4 hosting | a provider that throws, and every thunk called hostilely | **added**, §9.15 |
| Baking | round-trip, version rejection, corrupt input | round-trip existed; **corrupt added** |
| Vulkan enums | mirrored values against a vk.xml-generated binding | **added** — found four wrong, §9.14 |
| Vulkan projection | a real VkPipeline on a device with the validation layers | **added**, §9.17 |

Three were missing. All three found something.

#### Every entry point resolves — and two never could, off Windows

A `DllImport` is a promise checked when it is first called and not before. L0 declares 280 of them;
the tests call perhaps a third. An export Slang renames in a future release is not a build error,
not a startup error and not a test failure — it is an `EntryPointNotFoundException` from inside a
cook, naming a function nobody recognises, on whichever machine happens to compile the shader that
reaches it first.

`NativeExportTests` resolves every declaration against the loaded library. It also asserts no export
is bound twice, which is how a generator run goes wrong quietly: two managed methods on one export
both compile, both resolve, and one of them has the wrong signature.

**It found two on its first cross-platform run, and they were Windows-only bindings.** All 280
resolved on win-x64. Under WSL, two did not:

```
?slang_getEmbeddedCoreModule@@YAPEAUISlangBlob@@XZ
?spReflection_GetSession@@YAPEAUISession@slang@@PEAUSlangProgramLayout@@@Z
```

Those are MSVC manglings. Two functions in `slang.h` are not `extern "C"`, so ClangSharp bound them
by name-mangled symbol — which compiles on every platform and can only ever resolve on the one that
produced the mangling. On linux-x64 the symbol carries an Itanium mangling instead, and the call
throws `EntryPointNotFoundException`. Neither function was used, so nothing had ever called them and
nothing had ever noticed.

Both are excluded from generation now, taking L0 to 278 entry points, and a second test rejects any
declaration bound by a `?`- or `_Z`-prefixed entry point so a regeneration cannot bring them back.
That one needs no native library at all: it is a property of the declarations, not of the binary.

#### Malformed input

`SpirvModule` and `ShaderPackage` both parse bytes that came from disk, in a runtime that may have
no compiler to fall back on. `MalformedInputTests` truncates a module at every word boundary, cuts
it at every non-word boundary, corrupts 2,000 individual words under a fixed seed, and forces the
instruction-length words that make a parser loop forever or read off the end.

**`SpirvModule` survived all of it** — every failure an `InvalidDataException`, no hang, no crash.
That is a result worth having written down.

**`ShaderPackage.ReadFrom` did not.** Its documented exception is `InvalidDataException`; a
corrupted byte inside the layout produced a `JsonException` and a truncated file an
`EndOfStreamException`. Neither is in the contract, so neither was being caught, and both surface at
load time in a shipped build. It now has one failure mode whatever goes wrong inside, and length
prefixes are checked against what is left in the stream before being used — `BinaryReader.ReadBytes`
allocates its argument up front, so one flipped byte asking for two gigabytes got two gigabytes
before the read failed.

#### Does every option do something?

§9.5 asked this of the seven link-time options and found five inert. Session and target options —
the ones a consumer actually sets — had never been asked. `OptionEffectTests` requires each property
on `SessionOptions` and `TargetOptions` to have a test named after it proving the output changes, or
an entry in a list of options with no observable effect and the reason. Both directions ratchet: a
new property arrives unaccounted for, and an entry for a property that no longer exists fails.

Most work. One does not, and it is the interesting one.

**`ParameterBlocksUseRegisterSpaces` is byte-for-byte inert on a SPIR-V target.** Setting it produces
identical output and an identical descriptor-set structure, because a `ParameterBlock` introduces a
register space of its own either way.

The property's own documentation said the opposite: that without the flag two blocks are packed into
space 0, that the reflected structure would not be the one the author intended, and that a Vulkan
renderer "almost certainly wants this on" — citing
`ReflectionTests.ParameterBlock_space_assignment_follows_the_target_flag` as proof. That test
exists, and passes, and **its body had already recorded the correction**: "with or without the
target flag — `ParameterBlock` introduces a sub-element register space regardless". Someone measured
this, fixed the assertion, wrote the finding into the body, and left both the test's name and the
property's documentation asserting the belief they had just disproved.

That is §9.8 exactly, in miniature and entirely self-inflicted. A consumer reads the property they
are about to set; they do not read the body of a test in another file. The comment now says what is
true, the test is named for what it asserts, and the inertness has an assertion of its own so a
release that starts honouring the flag fails rather than quietly agreeing with a corrected comment.

#### Still missing when this section was written

All three are now done; the entries are kept because what each turned out to cost is part of the
answer to "what should this be tested by".

- **Adversarial lifetime tests.** The two ownership bugs in §9.1 were found by a cross-platform run,
  not by a test. Dispose order, double dispose, use-after-dispose and forgotten dispose all have
  defined intended behaviour and none of it is asserted. → §9.15, and every rule held.
- **A managed exception escaping an `[UnmanagedCallersOnly]` thunk.** L4's filesystem callbacks are
  called from native code, where an escaping exception is undefined behaviour. Nothing tests what
  happens when a caller's `ISlangFileSystem` throws. → §9.15, where four of the five thunks turned
  out not to be doing what the comment above them said they did.
- **`VulkanEnums` against the specification.** The numeric values are mirrored by hand and nothing
  checks them. Unlike the two above this is a decision before it is a task, because there is no
  offline source of truth in the repository to check against. → §9.14, and four values were wrong.

342 tests at the time of writing.

---

### 9.14 The mirrored Vulkan values, four of which were wrong

`VulkanEnums.cs` copies ~48 numeric values out of the Vulkan specification so the package ships
standalone, and said so:

> The numeric values below are fixed by the Vulkan specification and will not change, so mirroring
> them is safe and keeps the package standalone.

That is an argument about **drift**, and about drift it is correct. It says nothing about
**transcription**. "It cannot change" is not "it was typed correctly", and the two had been treated
as the same sentence — the same shape as every other finding in this document, applied to the
document's own reasoning.

The decision was where to get an oracle. Options were a Vulkan binding referenced by the test
project, a pinned `vk.xml` parsed at test time, dropping the enums for raw `uint`, or recording the
exposure and living with it. The first won: the objection to a Vulkan dependency is about the
*package*, and it says nothing about the test assembly. `PrismSlang.Vulkan` still has no
dependencies; `PrismSlang.Tests` gained `Vortice.Vulkan`, which is generated from the Khronos
`vk.xml` registry, so agreeing with it is agreeing with the registry.

**Four of the 48 were wrong, and they failed on the first run:**

| | Mirrored as | Actually |
|---|---|---|
| `R64Sfloat` | 110 | 112 |
| `R64G64Sfloat` | 113 | 115 |
| `R64G64B64Sfloat` | 116 | 118 |
| `R64G64B64A64Sfloat` | 119 | 121 |

Each is off by exactly two, and the reason is visible once the numbers are laid out: this region of
`VkFormat` runs in UINT/SINT/SFLOAT triples, so 110 is `R64_UINT`, 111 is `R64_SINT` and 112 is
`R64_SFLOAT`. Whoever transcribed them took the *start* of each group. A `double` vertex attribute
was therefore described to the driver as an unsigned integer of the same width — eight bytes read
and reinterpreted, structurally valid, so validation passes and the geometry is simply wrong.

The 32-bit family, which has the identical triple structure, was correct throughout. So was
everything else. It was one contiguous block of four, typed once.

#### And its neighbour

Putting doubles in the corpus to cover the fixed formats turned up something the agreement harness
structurally cannot: Vulkan gives a three- or four-component 64-bit vector **two** consecutive
locations, where every 32-bit type and a narrow 64-bit one takes a single location. Measured with
`float3, double, double2, double4, float2`, Slang assigns 0, 1, 2, 3, 4 and decorates the module the
same way — so the `double4` at 3 and the `float2` at 4 overlap, and reflection and the module are in
perfect agreement about it. A `double3x3` is worse: every row overlaps the next.

That is a third category, after "the module disagrees" and "both are silent". Here **both speak and
both are wrong**, and the only oracle left is the specification.
`VulkanProjection.RequireDistinctLocations` refuses the overlap and names both attributes.

It is worth flagging that this is the one assertion in the Vulkan projection taken from the
specification rather than from a measurement. If the two-location rule somehow does not apply to
vertex inputs, the check is a false refusal on a legal shader. Running the validation layers against
a real device — technique 4 from the list that produced §9.11 — is what would settle it, and this is
the first finding that makes that worth doing rather than merely nice.

> **Settled in §9.17, and the reading was right.** The layers reject the overlapping module with
> `VUID-StandaloneSpirv-OpEntryPoint-08721`, "conflicting input location assignment at location 2" —
> the same location the check names — and they reject it from `vkCreateShaderModule`, before any
> pipeline is built. Slang emits SPIR-V that does not satisfy the specification, so this is not a
> disagreement about how to describe a valid shader.

#### What this says about the other mirrors

There are none left: `SlangImageFormat`, `SlangStage`, `SlangBindingType` and the rest are Slang's
own enums, generated by ClangSharp from the pinned header, so they cannot be mistyped by hand.
`VulkanEnums` was the only table copied by a person, and it was the only one with an error in it.

351 tests.

---

### 9.15 The last two rows: lifetime, and the callback boundary

§9.13's table left three rows empty. §9.14 took one. These are the other two, and unlike every round
before this one **neither found a defect in behaviour** — both found the same thing instead, which
is a comment claiming a guarantee the code only partly kept.

#### Lifetime

Every rule in `SlangObject` exists because of a bug, and none of them was pinned. Both ownership
bugs in §9.1 were found by running the suite on a second platform: glibc aborted on a double release
that the Windows heap had tolerated silently since the first commit. `LifetimeTests` asserts the
four rules that came out of it — dispose is idempotent and safe under contention, use-after-dispose
throws rather than dereferencing, an owner may be disposed before the things it owns, a child keeps
its owner reachable, and abandoning a whole hierarchy to the garbage collector leaks rather than
corrupting.

That last one is the regression test for removing the finalizer: build a full
global-session/session/module/entry-point/program chain, drop every reference without disposing
anything, and force a blocking gen-2 collection with finalizers eight times over. A finalizer
reintroduced anywhere in that chain gets eight chances to release a module into a session whose
allocator has gone.

All of it passed. That is worth stating plainly rather than dressing up as a finding: the ownership
model has been right since §9.1, and it is now held in place by something other than the memory of
having fixed it.

Two of these are memory-safety assertions, which do not fail politely — a regression is an abort,
not a red test. The one that can be mutated cleanly was:
setting `_owner = null` in the constructor makes
`A_child_keeps_its_owner_reachable` fail with "the session was collected while a module from it was
alive", so the reference really is what holds the hierarchy up.

#### The callback boundary

`SlangFileSystem` is the only place in the binding where managed code runs *underneath* native code:
a vtable of `[UnmanagedCallersOnly]` thunks that Slang calls. A managed exception unwinding across
that boundary is undefined behaviour — the runtime does not attempt it, it terminates the process —
and the provider behind those thunks is code this package did not write. An engine VFS reading from
a pack file has every ordinary reason to throw.

The file said so, in a comment above the thunks:

> Every thunk must swallow managed exceptions — letting one unwind across the native frame is
> undefined behaviour, and on Linux it terminates the process outright.

**One of the five did.** `LoadFile` had the catch; `QueryInterface`, `AddRef`, `Release` and
`CastAs` did not. They look like they cannot throw, and they nearly cannot — dereference `self`,
compare GUIDs, touch an interlocked counter. But every one of them begins by resolving a `GCHandle`
out of memory whose lifetime Slang controls, and `GCHandle.FromIntPtr` on a value that is not a live
handle throws. A guard that holds only while nobody makes a mistake is not the guard this boundary
should have. All five have one now.

This is the third time in this document that a comment asserted something the code did not do —
after §9.8's entry-point names and §9.13's `ParameterBlocksUseRegisterSpaces`. In all three the
comment was written by someone who had just understood the problem correctly. Understanding it is
not what makes it true afterwards.

`CallbackBoundaryTests` drives the thunks the way Slang does, through the function pointers read out
of the object Slang is handed: a provider that throws `InvalidOperationException`, `IOException` and
`OutOfMemoryException` in turn, and every slot called with a null `self`, a null out-parameter, an
interface it does not implement, and more releases than references. `OutOfMemoryException` is in the
list on purpose — filtering a catch by exception type is the obvious way to write this guard and the
wrong one.

#### What is left

Nothing on the table from §9.13. The remaining known gaps are the ones in §9.7 and §9.12: osx-arm64
unverified, per-stage descriptor visibility conservative, a resource array sized by a specialization
constant killing the process inside Slang, and `getEntryPointHash` deliberately unbound.

The one thing worth adding that is not a gap in coverage but a change in kind: running the Vulkan
validation layers against a headless device would give a fourth oracle, and §9.14 produced the first
question that needs one — whether a three- or four-component 64-bit vertex attribute really takes
two locations, where Slang and the emitted module agree with each other and disagree with the
specification as read here.

375 tests.

---

### 9.16 Splitting the reader out of the compiler

The second round of engine feedback, and the first ask is structural rather than a defect: a shipped
game reads baked shader packages and never compiles one, but referencing `ShaderPackage` meant
referencing `PrismSlang`, which carries a 36 MB per-RID payload. The types a runtime needs are all
plain managed data; only the thing that *makes* a package needs Slang.

#### Where the line falls

**`PrismSlang.Runtime`** — no native dependency, referenceable on its own:

| From | What |
|---|---|
| `PrismSlang.Baking` | `ShaderPackage` (container, `ReadFrom`/`WriteTo`, `IsUpToDate`, `IsCurrent`, `StaleDependencies`), `BakedEntryPoint`, `ShaderDependency` |
| `PrismSlang.Reflection` | `ProgramLayout` and its whole transitive surface, plus the `TypeLayout` family |
| `PrismSlang.Spirv` | `SpirvModule` and everything it returns — byte parsing that never needed Slang |
| `PrismSlang.Native` | the eleven generated enums that surface names, and nothing else |

**`PrismSlang.Vulkan`** now references `PrismSlang.Runtime` rather than `PrismSlang`. It never
touched the compiler; it was inheriting the payload through a reference it did not need.

**`PrismSlang`** keeps the compiler, the interop layer, the reflection *walk*, the hosting thunks —
and `ShaderPackageBaker.Bake(program, module)`, which is the one method the split forced out of
`ShaderPackage`. Reading a package needs managed code; making one needs a `LinkedProgram`, a
`SlangModule` and the library behind them, so the class had to divide along exactly that line.

#### The generated layer

`Native/` mixes two populations that happen to share a directory: COM-shaped interfaces that exist
only to call into `slang-compiler`, and plain enums that appear on a reflection snapshot's public
surface. The split runs through it, and only the enums move.

They move by *compilation*, not by file. The generator still writes all 127 files to
`dotnet/Native`; `NativeReaderEnums.props` names the eleven that belong to the reader, the runtime
project compiles them, and `PrismSlang` removes them from its own compile. One list, imported by
both projects, because the failure mode of two lists is a type-identity mismatch: two
`SlangStage`s that print the same and are not the same type.

`NativeTypeNameAttribute` is the exception and is compiled into both. It is `internal`, so two
copies are two types that never meet; moving it would mean making a ClangSharp implementation detail
public. Granting `InternalsVisibleTo` between the assemblies instead is what does not work — it
makes the runtime's copy visible where the generated layer has its own, which is CS0436 on every
generated file, and `-warnaserror` turns that into a failed build.

#### The acceptance criterion, as a project

The engine team wrote a testable criterion, so it is a test.
`tests/PrismSlang.Runtime.Consumer` is an executable referencing **only** `PrismSlang.Runtime` and
`PrismSlang.Vulkan`. It reads a package off disk, projects the layout to a `VulkanPipelineLayout`,
calls `VulkanSpecialization.Pack` and `VulkanEnvironment.Require`, and walks
`FieldLayout.UniformOffset` — the five things named. If any of them needed the compiler it would not
compile.

`AssemblySplitTests` checks the other half, which is a property of the deployment closure and not of
the reference graph: the consumer's output directory must contain no `slang-compiler`, no
`slang-glslang`, no `runtimes/` tree, and exactly three assemblies. Then it bakes a real package
here, where the compiler is, and runs the consumer as a separate process against it.

It is worth saying what that output actually is:

```
PrismSlang.Runtime.Consumer.dll   PrismSlang.Runtime.dll   PrismSlang.Vulkan.dll
```

Writing the consumer found one thing, in the consumer rather than the library: a conditional
expression unifies its branches to one numeric type before boxing, so
`c.IsFloatingPoint ? c.DefaultFloat : c.DefaultInt` hands `Pack` a `double` for an integer constant
whichever branch is taken. `Pack` refused it by name and type, which is the behaviour §9.8 asked for
working exactly as intended on the first real consumer to make the mistake.

#### The second ask: severity on a success result

Already there, and the answer is §9.8 C. `SlangDiagnostics.Classify` reads severity out of the blob,
`SlangDiagnosticPolicy.ThrowOnError` is the default, and an error-level diagnostic accompanying a
successful compile therefore throws rather than scrolling past — which is the missing-`spirv-opt`
signature exactly. `GlobalSessionOptions.RequireSpirvOptimizer` is the second mechanism, because the
diagnostic fires once per global session and a policy alone only catches the first shader of a
batch.

One real gap, now closed: `SlangDiagnosticHandler` received `(operation, text)`. A host under
`Report` — the policy that exists so a build can log warnings and fail on errors itself — had to
re-derive the severity, which means re-deriving `Classify`, which parses message prefixes because
Slang has no structured severity to read. The handler is now
`(operation, severity, text)`, classified once and used for both the throw decision and the report,
so the two can never disagree about the same text.

381 tests.

---

### 9.17 The fourth oracle, and the question it settled

Three oracles had accumulated: the emitted module (§9.11), invariants needing no oracle at all
(§9.12), and — once — the specification as read by a person (§9.14). That third one was the weak
link, and it was load-bearing: `VulkanProjection.RequireDistinctLocations` refuses a shader on the
strength of a rule read out of a document, in the one case where Slang and the emitted module agree
with each other and disagree with the reading. Nothing already in the repository could settle it,
because both sides of every comparison came from Slang.

`VulkanTestDevice` adds the party that decides. A headless Vulkan instance with
`VK_LAYER_KHRONOS_validation` and a debug messenger, no surface and no swapchain, dynamic rendering
instead of a render pass. `VulkanValidationTests` builds real `VkDescriptorSetLayout`s, a real
`VkPipelineLayout` and a real `VkPipeline` out of the projection and the SPIR-V it accompanies, and
asserts the layers said nothing. Skipped with an actionable reason wherever there is no loader, no
device or no layer — which is what happens under WSL, and what a CI runner would fix with lavapipe.

#### The answer

**The reading was right, and it is worse than it looked.** Given
`float3 position, double4 wide, float2 after`, Slang assigns locations 0, 1, 2 and decorates the
module to match. Handed to a device:

```
VUID-StandaloneSpirv-OpEntryPoint-08721: vkCreateShaderModule(): pCreateInfo->pCode
  (spirv-val produced an error):
Entry-point has conflicting input location assignment at location 2, component 0
  OpEntryPoint Vertex %vertexMain "main" %gl_Position %v_position %v_wide %v_after
```

Location 2 — the same location `RequireDistinctLocations` names. And the complaint comes from
**`vkCreateShaderModule`**, not from pipeline creation: the module is invalid SPIR-V on its own,
before any layout is involved. So this is not a disagreement about how to describe a valid shader.
Slang emits SPIR-V that does not satisfy the specification, and PrismSlang refusing it at cook time
is the earliest anything can.

The check is no longer the odd one out in that file. Its remarks now cite the VUID.

#### Everything else passed

A graphics pipeline with a constant buffer, a texture, a sampler, an unbounded bindless array, a
push-constant range and a `float4x4` instance attribute — descriptor set layouts, pipeline layout
and pipeline all built from `ToVulkan()` with nothing adjusted on the way through — draws no
complaint. Nor does a compute pipeline with a uniform buffer, two structured buffers and a formatted
storage image. Those cover the descriptor types, the stage flags, the push-constant range and the
vertex attribute formats that §9.9 through §9.14 corrected, and they are now checked by the party
that enforces them rather than by the party that reported them.

#### One more shader of mine, wrong again

The first draft of the graphics shader had `fragmentMain(VOut i, uint which : BINDLESS)` — a
fragment input the vertex stage does not write. The layers named it immediately:
`VUID-RuntimeSpirv-OpEntryPoint-08743`, "has a declared Input at Location 1 ... but the previous
stage has no Output declared there". A real defect in a test shader, found in the first minute the
harness existed, which is a fair advertisement for having it.

The probe that decides whether to skip was also wrong at first, and in the way that matters most for
a gate: it compared `VK_LAYER_KHRONOS_validation` against the names from
`vkEnumerateInstanceLayerProperties` and concluded the layer was absent on a machine that had it
registered. Reading a name out of a fixed-size buffer in a by-value struct is easy to get subtly
wrong, and a gate that is wrong skips every test in the file with a confident, false explanation —
which is strictly worse than failing. It now creates the instance and reads
`VK_ERROR_LAYER_NOT_PRESENT`, asking the loader the same question rather than re-deriving its
answer. That is the same lesson as §9.13's severity classification, arrived at from the other side.

#### On CI, through lavapipe

A GitHub runner has no GPU, so the linux-x64 job installs `mesa-vulkan-drivers` — which carries
lavapipe — and `vulkan-validationlayers`. Two of the three tests then run there for real. Windows
skips them: the layers mean the SDK installer, which is a large download for tests the Linux job
already covers, and the skip prints its reason.

Three things came out of getting that working, all of them the kind that only appear on a second
implementation:

- **No `VK_DRIVER_FILES`.** The first attempt pinned
  `/usr/share/vulkan/icd.d/lvp_icd.x86_64.json` and the loader reported *Found no drivers*: Mesa
  dropped the architecture suffix between the version in Ubuntu 24.04's archive and the one in its
  updates, so the file is now `lvp_icd.json`. Since lavapipe is the only ICD on the runner, the
  loader's own discovery finds it and there is nothing to pin — the pin was the only thing that
  could break.
- **The wide-attribute test cannot run on lavapipe at all**, and it took a while to see why. The
  driver reports `bufferFeatures 0` for `VK_FORMAT_R64G64B64A64_SFLOAT`, so the pipeline is rejected
  with `VUID-VkVertexInputAttributeDescription-format-00623` before anything gets as far as checking
  location assignment. Left to run, it would have asserted against whichever message arrived —
  passing or failing for a reason unrelated to the question. It now has a gate of its own that
  queries `vkGetPhysicalDeviceFormatProperties` and skips with that sentence.
- **The two implementations disagree about which VUID carries the message.** The SDK layers on a
  discrete GPU report the conflict under `VUID-StandaloneSpirv-OpEntryPoint-08721`; the layers on
  lavapipe report it under `VUID-VkShaderModuleCreateInfo-pCode-08737` and quote the spirv-val VUID
  inside the text. The assertion matches either, and the same wording for the part that matters.

And because a skipped test is a green test, the job asserts that at least two of them actually ran.
Installing lavapipe and never noticing it failed to load would switch the whole harness off in
silence, which is the failure mode this section exists to avoid — the same shape as the probe that
confidently reported the layer missing on a machine that had it.

384 tests: 381 everywhere, 383 on lavapipe, 384 on a device with 64-bit vertex formats.

### 9.18 What the layers said about the whole corpus

§9.17 built the fourth oracle and pointed it at three hand-written shaders. That left the only
oracle in the suite that is not Slang describing itself reading three shaders while
`ModuleAgreementTests` read twenty-eight — the most valuable instrument, aimed at the narrowest
target. So the same move that produced §9.11: stop choosing what to look at.

`VulkanCorpusValidationTests` compiles every corpus shader, projects it, and builds the descriptor
set layouts, pipeline layout and pipeline the projection describes. Nothing is adjusted on the way
through. Getting it to run at all took two harness fixes, and then it found three things.

**Two harness bugs first, because both would have made a green run meaningless.**

`pSetLayouts` is indexed by set number, so a shader using sets 0 and 2 needs three entries with an
empty one between them. The first version packed them, which would have moved set 2's bindings to
set 1 and created the pipeline successfully — the harness lying about the projection rather than
the projection being wrong. `explicit-spaces` and `parameter-blocks` both produce gaps.

The second is the one worth remembering. Sixteen of the twenty-eight shaders declare a fragment
stage and nothing else. That reflects perfectly well and it is not a Vulkan graphics pipeline, so
the first run reported *"raster stages but no vertex stage"* for most of the corpus and passed —
a gate that excluded almost everything, reporting the same green as a gate that excluded nothing.
The driver now supplies a vertex stage: a trivial one writing only `SV_Position`, or the case's own
`ValidationVertex` where the fragment stage reads a varying. Adding an entry point to a program is
not obviously inert, so `RequireTheAddedStageChangedNothing` re-projects the case without it and
requires the descriptor sets, push constants and fragment outputs to be identical.

**Then four rejections that were the test device, not the projection.** `shaderDrawParameters`,
`shaderOutputLayer`, `sampleRateShading` and `maintenance4` were all absent from `VulkanTestDevice`,
whose comment already said *"everything a corpus shader might declare, where the device has it"* —
true of the shaders it had seen. `maintenance4` is the interesting one: Slang emits `LocalSizeId`
rather than `LocalSize` whenever a workgroup dimension is a specialization constant, and
`LocalSizeId` is illegal without it.

#### A projection defect: input attachments are fragment-only

`VUID-VkDescriptorSetLayoutBinding-descriptorType-01510`. Per-binding stage visibility is
deliberately conservative here — Slang's snapshot cannot say which stage reads which binding, so
every binding is reported visible to every stage in the program, which over-permits rather than
omitting. For `VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT` that is not a conservative choice, it is an
invalid one: Vulkan requires input attachments to be fragment-only, and the object rejected is not
the pipeline but `vkCreateDescriptorSetLayout` — a host following the projection literally cannot
create it at all.

Fixed in `VulkanProjection.VisibleStages`. Nothing else in the suite could have found it: the
layout and the module agreed, because the module is not where the rule lives.

#### A constraint the projection should have named: unbounded is not always last

`VUID-VkDescriptorSetLayoutBindingFlagsCreateInfo-pBindingFlags-03004`.
`VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT` is legal only on the highest-numbered binding
in a set. Slang does not arrange for the unbounded array to be last: in `descriptor-arrays` the
unbounded texture array is binding 1 and an ordinary sampler is binding 2.

The projection is not wrong about the shader — the constraint is Vulkan's and the choice is the
host's — but `IsUnbounded` reads as an invitation to reach for variable count, and a host that does
is refused by `vkCreateDescriptorSetLayout`. Documented on `VulkanBinding` with the alternative that
is legal anywhere: `PARTIALLY_BOUND` with a fixed cap, which is what the harness now does.

#### A third Slang finding: `sample` without its capability

`interpolation-modifiers` is rejected by spirv-val, not by any device:

```
Operand 2 of Decorate requires one of these capabilities: SampleRateShading
  OpDecorate %v_imPerSample Sample
```

The module carries `Decoration::Sample` and does not declare `Capability::SampleRateShading`. That
is malformed SPIR-V rather than unsupported SPIR-V — no feature and no extension changes the answer
— and enabling `sampleRateShading` on the device, which fixed the other four, made no difference
here. Asserted directly against the module's own capability list by
`The_sample_interpolation_modifier_emits_a_module_spirv_val_rejects`, so the day it is fixed
upstream arrives as a failing test rather than as nothing.

Third of three, with the wide-64-bit vertex numbering (§9.14) and the specialization-sized resource
array (§9.12), and unreported for the same stated reason.

#### What is still out of reach, and why that is written down

Four shaders are excluded by name, each with its reason, checked against the corpus so a rename
cannot leave a dead entry excusing nothing:

| Shader | Why |
|---|---|
| `mesh`, `amplification` | need `VK_EXT_mesh_shader`, which lavapipe does not offer |
| `ray-tracing` | needs a ray tracing pipeline and a shader binding table; not a layout question |
| `tessellation`, `geometry` | hull/domain/geometry with no vertex stage, and a supplied one would have to reproduce the exact input patch or primitive — writing a second shader to test the first |
| `wide-vertex-inputs` | the projection refuses it on purpose (§9.14) |
| `interpolation-modifiers` | the module is invalid, above |

Two more things are reported rather than hidden. A device gap — a capability, extension or
zero-valued limit the module needs and the device lacks, like `descriptor-kinds`' ray query on
lavapipe — is printed with the layer's own text instead of being folded into a pass; the filter is
narrow on purpose, because a projection defect appears as a VUID about a binding, a stage, a
location or a range and none of those can match it. And `subpass-inputs` has its descriptor set
layouts and pipeline layout built and checked, with only the pipeline skipped: an input attachment
needs a render pass or Vulkan 1.4's `dynamicRenderingLocalRead`, and the part where the defect
above lived is not the part that was skipped.

427 tests.

### 9.19 Closing the reflection gaps, and what three of them turned out to be

A count of the surface put the position plainly: 278 flat functions declared, 95 reached. The
reflection number was the one that mattered — 91 of 172 — and most of the remainder fell into
"covered in effect" rather than "missing". Closing the ones that were genuinely missing took the
reachable set to **116 of 278, and 112 of 172 reflection functions**, and three of them did not
behave the way their names read.

#### Function signatures: the type names are not the source's

`spReflectionFunction_*` was bound in L0 and reached from nothing, so the declaration tree could say
a module offers `shade` and not what `shade` looks like. It now carries a `FunctionSignature`:
parameters with `in`/`out`/`inout`, the result type, declaration modifiers, and user attributes.

The surprise is in the spelling. `float3` in the source is `vector<float,3>` in reflection, and
`StructuredBuffer<float4>` is `StructuredBuffer<vector<float,4>, DefaultDataLayout>` — the implicit
layout parameter included. That is `GetFullName`, and it is the right call precisely because it
keeps the generic arguments: `GetName` says `StructuredBuffer` and drops the element type, which is
the part a hook check needs. A consumer matching against `"float3"` finds nothing.

Overloads are the second surprise, and they need both views to see. The declaration tree reports two
declarations of one name as two sibling nodes, each with a real signature, and `IsOverloaded` false
on both. Resolved by name through `LinkedProgram.FindFunction`, Slang returns *one* function that
says it is overloaded, with the real declarations underneath — and the outer signature is empty:
`void overloaded()`, which is not a signature any declaration in the source has. A caller reading
`Parameters` without checking `IsOverloaded` gets a function that appears to take nothing.

#### `isBinaryModuleUpToDate` answers from the session's file cache

Module serialization closes Tier 2's precompiled-module cache: `IModule.Serialize` writes Slang IR,
`SlangSession.LoadModuleFromIRBlob` reads it back without re-parsing, and `ReadModuleInfo` reports
the module's name and the compiler build that wrote it — the field that decides whether a cache
entry is usable at all, since a blob is only loadable by the build that produced it.

`ISession::isBinaryModuleUpToDate` is the staleness check, and the natural way to use it does not
work. It is answered against the session's own view of the file system, which the session populated
when it loaded the module. So:

| | same session that loaded and serialized | fresh session |
|---|---|---|
| source unchanged | `true` | `true` |
| source touched | **`true`** | `false` |
| path with no file behind it | **`true`** | `false` |
| malformed blob | `false` | `false` |

Load, serialize, ask — the obvious sequence — returns true unconditionally, including for a path
that has never existed. A cook doing that ships the version before last. A fresh session, which is
what a process has at startup and the only place the call is worth making, reads disk and answers
correctly. Nothing in the name says so.

#### Sub-object ranges say less than they look like they do

Slang's third view of the binding data was expected to connect the other two: the parameter list
says `soMaterial` exists, the descriptor-set view says which set holds it, and this was going to say
that the set *is* `soMaterial`. Two of the three things assumed about it were false.

- **`getSubObjectRangeOffset` carries no name.** It returns a variable layout with no variable
  behind it; every name came back null. They are reachable through
  `getBindingRangeLeafVariable` on the binding range the sub-object points at — the same object,
  reached the other way.
- **Neither space call gives the set.** `getSubObjectRangeSpaceOffset` returns 0 for a block
  occupying set 1 and 0 for one occupying set 2. `getBindingRangeDescriptorSetIndex` returns 0 for
  both, being an index into the enclosing scope's own list. So `SubObjectRange` deliberately carries
  no space at all, rather than a wrong number in an authoritative-looking field, and a caller joins
  on the name against `DescriptorSets`.
- **It is not a `ParameterBlock` detector.** A `ConstantBuffer<T>` holding nothing but values is
  reported alongside the blocks. What survives is the distinction it does draw: containers versus
  things bound directly.

#### And the rest, classified rather than counted

Sixty reflection functions remain unreached, and lumping them together as "coverage" would be the
same mistake as reporting 95/278 was:

| | | |
|---|---:|---|
| Lookup-by-name | 12 | `FindUserAttributeByName`, `findFieldIndexByName`, `findEntryPointByName`… The snapshot enumerates eagerly, so a consumer queries managed data. That was the §4.4 decision, and these are its cost. |
| Alternate access paths | 8 | `VariableLayout_GetImageFormat` where the binding-range route is used, `Parameter_GetBindingIndex` where `ShaderParameter.Bindings` already carries it. Same facts, second door. |
| Measured to return nothing | 2 | The two sub-object calls above. |
| Structurally unreachable | 1 | `spReflection_ToJson` takes an `ICompileRequest` — the legacy API, which §5 puts in Tier 4. |
| **Link-time specialization** | **~20** | `applySpecializations`, `specializeWithArgTypes`, `Generic_GetConcrete*`, `getPendingDataTypeLayout`, `getSpecializedTypeArg*`. Introspecting a *specialized* program rather than a generic one. The real remaining gap, and the one to close next. |
| Append/consume counters | 2 | `GetExplicitCounter` and its binding-range offset. Narrow, real, and nothing asks for it yet. |

Also closed alongside: `IMetadata::isParameterLocationUsed`, which is the only thing that knows a
parameter was declared, laid out, and never read by the emitted code — reflection and the module
agree on those by being equally silent. It is per-target and per-entry-point, so it cannot live on
`ProgramLayout`: a binding the fragment stage reads and the vertex stage does not is used by the
program and unused by one of its stages, and only `GetEntryPointMetadata` tells them apart.

460 tests.

### 9.20 The last two gaps, one of which crashes

§9.19 left two categories: the append/consume counter, and link-time specialization introspection.
Both are closed. Between them they produced a defect in this binding's own output and the fourth
Slang crash this project has measured — and the crash is the worst of the four, because it is an
ordinary caller mistake rather than an exotic shader.

#### An `AppendStructuredBuffer` costs two descriptors, and one of them is not a parameter

Slang emits a separate storage buffer to hold the count and reports it as an ordinary binding range
named `counter`. The corpus's `append-buffers` shader declares three buffers and produces **five
bindings**, while `ProgramLayout.Parameters` has three entries — at bindings 0, 2 and 4.

A renderer that builds its descriptor set layout from the parameter list, which is the obvious
thing to do, is short two storage buffers the shader will read.

Worse for a shader with both an append and a consume buffer: **both counters are named `counter`**,
so even reading the descriptor-set view there is nothing to say which belongs to which. That is
what `DescriptorRange.CounterBinding` is for — it names the counter's binding on the buffer that
owns it, and survives into `VulkanBinding` because `VkDescriptorSetLayoutBinding` has no concept of
one and the pairing would otherwise be lost at the last step.

`RequireEveryParameterIsPlaced` could not have caught this: it checks that every parameter has a
binding, and this is a binding with no parameter.

#### Specializing a generic: the working route is not the one the names suggest

`spReflection_specializeType` takes a type and argument types and looks like exactly the call for
"what would `Wrapper<Lambert>` lay out as". Against a generic reached by `FindTypeByName` it returns
**null, with no diagnostics at all**.

What works is to take the type's *generic container* and specialize that:

```
FindTypeByName("Wrapper")            -> a struct whose full name is Wrapper<T>
  GetGenericContainer                -> the generic
  specializeGeneric(TYPE, Lambert)   -> a specialized generic
    GetTypeParameter + GetConcreteType   -> T = Lambert
    GetInnerDecl -> getTypeFromDecl -> applySpecializations -> Wrapper<Lambert>
      GetTypeLayout                  -> field offsets, sizes, strides
```

That is `LinkedProgram.SpecializeGeneric`, and the payoff is the question a material system asks
after choosing an implementation: `SgWrapper<SgLambert>` is 32 bytes with `tint` at 16;
`SgWrapper<SgEmissive>` is 48 with `tint` at 32. Previously the only way to learn that was to author
a shader per candidate and reflect it.

Value generics work the same way and are the case Slang's *program-level* specialization API cannot
express at all — it reports type parameters exclusively — so this is the only route to
`Cascades<4>` being 64 bytes and `Cascades<8>` being 128.

`getSpecializedTypeArgCount` reports **0** on every path tried, including on a type written as
`Wrapper<Lambert>` in source and on the specialized type produced above. The arguments are
recoverable from `GetFullName`, which is where they are read from.

#### And the crash

Hand `spReflection_specializeGeneric` an `int` where a type parameter belongs and it
**access-violates**. Not a null, not a diagnostic — `0xC0000005`, which on Windows the CLR does not
convert into anything catchable. The whole test run aborted the first time, which is how it was
found.

That makes it the fourth measured Slang crash and the only one fatal on Windows as well as Linux.
It is also the easiest to hit: two arguments in the wrong order is enough.

Unlike §9.12's specialization-sized resource array, **this one is detectable**. The generic reports
how many parameters of each kind it takes, so `SpecializeGeneric` compares before calling and
refuses with a message naming what was expected against what arrived. `ProberTests` asserts the
unguarded behaviour from a child process, so the day Slang starts reporting it the guard can become
a pass-through and a red test will say so.

One residue, stated because it is not covered: a generic mixing type and value parameters reports
the two as separate lists, so their interleaving in the declaration is not recoverable and the
counts can match while the order is wrong. That combination reaches Slang unguarded.

469 tests.

### 9.21 The four crashes: what upstream says, and whether they are ours

Four Slang calls have been measured ending the process. Before treating any of them as an upstream
defect, two questions: has anyone reported it, and is PrismSlang calling correctly?

#### The upstream picture

Slang ran a **link-time-constant robustness programme** and closed all of it before the release
pinned here: [#8964](https://github.com/shader-slang/slang/issues/8964) (remove raw integer types
from type-layout passes), [#8965](https://github.com/shader-slang/slang/issues/8965) — *"Audit and
correct any layout query API functions … make sure that all the errors we encounter while
calculating layouts are fully propagated to the user rather than crashing or being asserted on"* —
[#9045](https://github.com/shader-slang/slang/issues/9045) (member offsets after a link-time
constant sized member crash the compiler),
[#9046](https://github.com/shader-slang/slang/issues/9046) (`-reflection-json` emits garbage), all
closed 2025-11-25. And [#8738](https://github.com/shader-slang/slang/issues/8738), the AV in
`spReflectionEntryPoint_getComputeThreadGroupSize` this document already cites, closed 2025-12-06 by
[#9245](https://github.com/shader-slang/slang/issues/9245).

That programme is visible in what we measure. §9.12 found `GetSize(UNIFORM)` and the following
field's offset returning `SLANG_UNKNOWN_SIZE` rather than crashing — #9045's fix, working. What it
did not reach is the element-count and binding-range paths, which still crash. So these are not
unknown territory: they are the unswept residue of a closed audit whose stated goal was exactly
this.

Nothing found reports the specific calls. Searches of the issue tracker for
`getBindingRangeCount`, `getSpecializedElementCount`, `getBindingRangeImageFormat` and
`spReflection_specializeGeneric` return either unrelated hits or nothing. The nearest neighbour is
[#12360](https://github.com/shader-slang/slang/issues/12360), an open access violation specializing
a generic method — a different path (slangc, dynamic dispatch, associated types) and not this.

#### Are they ours? `slangc` answers two of them outright

The strongest available check needs no argument at all: run Slang's own compiler on the same
shaders, with PrismSlang nowhere in the process.

| shader | `slangc -target spirv` | `slangc -reflection-json` |
|---|---|---|
| `float4 items[kSpecConstant]` in a `ConstantBuffer` | exit 0, 932 bytes of SPIR-V | **0xC0000409** |
| `Texture2D t[kSpecConstant]` | exit 0, 800 bytes of SPIR-V | **0xC0000409** |

Both compile. Only the reflection walk dies, in Slang's own tool, and `0xC0000409` is MSVC's
`__fastfail` — an assert or a stack-guard trip, not a wild pointer. There is no version of this that
is PrismSlang's fault.

#### The generic specialization crash is ours to *avoid*, not ours to *cause*

`spReflection_specializeGeneric` access-violates on an argument list that does not match the
generic's parameters. Two things were checked before calling that a Slang defect.

The marshalling. `SlangReflectionGenericArgType` is a plain C enum, four bytes, and the C# mirror
has no explicit base — four bytes. `SlangReflectionGenericArg` is a union of a pointer, an
`int64_t` and a `bool`, so eight bytes aligned to eight; the C# mirror is `LayoutKind.Explicit` with
every field at offset 0. If either were wrong the *correct* calls would misbehave too, and they do
not: `SpecializeGeneric` with a matching type argument returns `SgWrapper<SgLambert>` with the right
layout, and with a matching value argument returns `SgCascades<4>` at 64 bytes.

Which mismatches crash. Both, measured separately: an `int` where a type parameter belongs, and two
type arguments for one type parameter — the right kind, real types, nothing dangling. So the guard
in `SpecializeGeneric` covers exactly what the evidence covers rather than being widened on a hunch.

Passing a mismatched list is a caller's mistake. Ending the process without a diagnostic is not the
API's prerogative, and #8965's own wording is the standard being applied: errors should be
propagated to the user rather than crashed on.

#### And one of the four is not a crash any more

`spReflectionTypeLayout_getBindingRangeImageFormat` was recorded here as access-violating on a
`PARAMETER_BLOCK` binding range, *"measured against slang-compiler 2026.14.1"*. Re-measured against
2026.14.1, it returns `unknown` — for a parameter block, a nested one, a constant buffer, a sampler,
two raw buffers and an acceleration structure, every non-texture shape reachable from a shader.

Either it was measured on an earlier pin and the version travelled with the sentence, or the
original shape had an ingredient not reconstructed here. There is no way to tell from this side and
claiming either would be inventing history. What is certain is that a measured claim in this
document was carrying a version number it had not been re-checked against — the same defect this
project keeps finding in comments, in its own record, about someone else's compiler.

The guard stays: a free branch against a class of failure that ends the process is not a trade worth
reversing because one release behaves. What changes is that the claim is now a test.
`ProberTests.The_image_format_query_no_longer_crashes_on_any_binding_range_shape` asks the question
across every shape from a child process, so a regression is red rather than fatal, and the comment
in `ReflectionWalker` says what was measured when.

#### Where that leaves the four

| Call | Reported upstream? | Ours? |
|---|---|---|
| `spReflectionType_GetElementCount` / `GetSpecializedElementCount` on a spec-sized array | no; in scope of the closed audit #8965 / #9045 | **no** — `slangc -reflection-json` dies on the same shader |
| `getBindingRangeCount` on a spec-sized *resource* array | no; same audit | **no** — same |
| `spReflection_specializeGeneric` with a mismatched argument list | no; nearest is #12360, a different path | **no** — the marshalling is verified by the calls that work |
| `getBindingRangeImageFormat` on a non-texture range | n/a | **no longer reproducible on the pinned release** |

None reported by this project, for the reason stated when the first one was found.

471 tests.

### 9.22 The rest of the reflection surface, and what closing it cost

After §9.21 the reachable set stood at 127 of 278 flat functions, 123 of 172 reflection ones. This
round takes it to **134 / 278 and 130 / 172**, and the interesting part is again what the last few
calls turned out to need.

#### Attributes on a type, and what the arguments actually are

Variables and functions reported their user attributes; **types did not**, so a material system
tagging `[MyTag(7)] struct Material` rather than tagging fields had no path at all. Now on
`TypeLayout.Attributes`, asked only of a struct — the other kinds are built-in types nobody can
attach an attribute to, and asking each would be a call per node of the tree for an answer that is
always null.

Alongside it, `UserAttribute.ArgumentTypes`. The values were being recovered by *probing*: try the
int accessor, then the float one, then the string one, and take whichever succeeded. That works and
it guesses. `spReflectionUserAttribute_GetArgumentType` says what the argument is declared as, so
the guess is now accompanied by the declaration. The probe stays — Slang's accessors are keyed by
type and there is no variant to read — but a consumer that cares can tell a `float` argument with
an integral value from an `int` one.

#### A generic is not always a type

`SpecializeGeneric` handled `struct Wrapper<T>` and returned null for `float3 apply<T>(…)`, because
it looked the name up with `FindTypeByName` and a generic function is not in that table. Slang looks
the two up separately, so both are asked now, and `SpecializedType.Kind` says which came back —
otherwise a null `Layout` is indistinguishable from a failure.

Substitution then needed a second correction. `spReflectionFunction_applySpecializations` is the
call that looks like it produces a specialized signature, and the function it returns still reports
its parameters as `T`:

```
sgApply(T material, vector<float,3> albedo)          // applySpecializations on the function
sgApply(SgEmissive material, vector<float,3> albedo)  // …pushed through each parameter as well
```

The specialization has to be applied per node — through each parameter's *variable* and through the
result *type* — not once to the function that contains them. Two calls further down the uncalled
list, and the difference between a signature you can check a call site against and one you cannot.

#### The entry point read twice

`EntryPointLayout.Parameters` is the pipeline interface: varyings and system values with locations
and semantics. `spReflectionEntryPoint_getFunction` gives the same declaration as ordinary source,
and that is where the things that are not layout live — modifiers, and the user attributes an author
put on an entry point. Both are reported; neither is redundant.


#### And the rest of it

Everything else was closed in the same pass, because a surface finished in instalments is a surface
nobody can reason about. **All 172 reflection functions are now exercised**, and the way the last
forty were closed is worth more than the count.

Most of them are *alternate access paths*: Slang models the same fact twice, the projection reads
one route, and the other sits unused. Binding those as new API would be surface for its own sake —
a second way to ask what the snapshot already says. So they are called from `SecondOpinionTests`
instead, which requires the two routes to **agree**:

- a parameter's binding, from `spReflectionParameter_GetBindingIndex`/`GetBindingSpace` against the
  variable layout's per-category offsets
- a binding range's image format, from the variable layout against the binding range
- a struct's fields, from `spReflectionType_GetFieldCount` against the laid-out ones
- a field's index, from `findFieldIndexByName` against the order the walk used
- an append buffer's counter, from `getExplicitCounterBindingRangeOffset` against the projected
  `CounterBinding`
- every entry point, type parameter, member and attribute, from its by-name lookup against the
  enumeration

It is a weaker oracle than the emitted module — both answers come from the same compiler — and it
is not nothing: these are separate code paths in Slang, and §9.8's binding-range names were wrong
precisely because one path knew something the other did not.

The queries measured to report nothing are called too, and pinned reporting nothing:
`getSubObjectRangeOffset`, `getSubObjectRangeSpaceOffset`, `getSpecializedTypeArgCount`/`ArgType`,
`specializeType`, and the three pending-data layouts. "We never call it" and "we called it and it
says nothing" are different claims, and only the second is a measurement — so when one of them
starts answering, a test fails and the projection has something new to carry.

What genuinely became new API in this pass: `TypeLayout.Attributes` and
`UserAttribute.ArgumentTypes`; `DeclarationNode.QualifiedName`, `Modifiers`, `VariableType` and
`OuterGeneric`; `DescriptorRange.IsSpecializable`; `TypeLayout.GenericParameterIndex` and
`HasPendingData`; `ShaderParameter.Stage`; `SlangType.Fields` and `FindAttribute`;
`LinkedProgram.ResolveOverload` and `ToJson`; `SlangGlobalSession.FindCapability`, `SupportsTarget`
and `HashString`.

#### A fifth crash, found by the platform that was not being developed on

`spReflection_specializeType` handed a type that is not generic returns null on Windows. On Linux it
**segfaults** — exit 139.

It was found the worst possible way and the best possible way at once. The call sat in an ordinary
in-process test, pinning the measured "returns null with no diagnostics"; the suite was green on
Windows; the Linux leg aborted the whole run with a truncated message naming an unrelated module.
That is §9.1 again, exactly: a defect that exists only on the platform not being developed on, and
a reminder that the second leg of the matrix is the control rather than a formality.

Five crashes now, and four of them share a shape — a caller mistake that Slang does not diagnose:

| Call | Windows | Linux |
|---|---|---|
| `GetElementCount` / `GetSpecializedElementCount` on a spec-sized array | `SEHException` | abort |
| `getBindingRangeCount` on a spec-sized *resource* array | `SEHException` | SIGABRT |
| `specializeGeneric` with a mismatched argument list | `0xC0000005` | abort |
| `specializeType` on a non-generic type | **null** | **SIGSEGV** |
| `getBindingRangeImageFormat` on a non-texture range | no longer reproducible (§9.21) | |

Only the fourth is graceful anywhere, which is what made it dangerous: a Windows-only developer has
no way to discover it.

#### The one call the library will not make, and the ones outside the fence

`spReflectionType_GetSpecializedElementCount` is the single exclusion. It is documented as the
version that copes with "size depends on unresolved generic parameters or link-time constants", and
that is exactly the input it dies on — `SEHException` on Windows, an abort on Linux. It is called
once, from the prober, so the exclusion rests on a measurement rather than on the memory of one.

Outside the reflection API, twenty-four flat functions remain unreached and every one is a category
§5 put out of scope or a duplicate of something already called:

| | | |
|---|---:|---|
| Legacy `spCompileRequest*` | 81 | Tier 4. The dead pre-COM compilation API |
| Record/replay layer | 8 | Tier 4 |
| Bytecode VM and disassembly | 2 | Tier 4 |
| Coverage manifest | 1 | Tier 4 |
| Legacy session and repro | 5 | Tier 4 |
| Superseded global-session creators | 2 | `slang_createGlobalSession2` is the one bound; the v1 entry point and the no-core-module variant are not |
| Flat aliases of vtable methods | 5 | `slang_loadModuleFromSource`, `loadModuleFromIRBlob`, `loadModuleInfoFromIRBlob`, `spSessionCheckPassThroughSupport`, `spFindProfile` — each is a second door to a call already made through `ISession`/`IGlobalSession` |
| `slang_shutdown` | 1 | Deliberately never called: it tears down process-wide state, and a host that creates another session afterwards is in undefined territory |

That is the whole surface accounted for. Not "95 of 278 and the rest unexamined" — every function
either reached, exercised as a second opinion, pinned as silent, excluded with a measurement, or
named as out of scope by a decision made in §5 before any of it was written.

492 tests.
