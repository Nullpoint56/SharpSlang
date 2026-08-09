# PrismSlang — Binding Surface and Approach

Scope study for a fully-featured C# binding over the Slang shader compiler, including reflection,
for consumption by PrismEngine's Renderer Core.

Measured against `shader-slang/slang` @ `master`, cross-checked against release **v2026.14.1**
(30 July 2026). Counts below come from regex analysis of `include/slang.h` (6,133 lines) and
`include/slang-deprecated.h` (1,715 lines); treat them as accurate to within a few entries, not
to the unit.

---

## 1. Context

PrismEngine's foundation document (`docs/foundation.md`, "Shader Compilation") currently specifies
GLSL + shaderc for compilation and SPIRV-Cross for reflection, both bundled inside Vortice.Vulkan,
and records the intent:

> **Future migration path:** Slang via Slang.Sdk once C# bindings reach maturity and Linux/macOS
> support ships. Migration is contained to Renderer Core behind the SPI.

PrismSlang exists to be that binding rather than to wait for one. Two constraints from foundation.md
bind this design directly:

- **V1.0 targets Windows x64 and Linux x64**, and no decision may structurally exclude macOS.
- **Vulkan only.** SPIR-V is the only code-generation target that matters. `slang-rhi` (Slang's own
  graphics abstraction layer) is out of scope entirely — the engine renders through Vortice.Vulkan.

The second constraint is worth stating loudly because `slang-rhi` is roughly as large as the
compiler API itself. Excluding it removes more surface than everything this document includes.

---

## 2. Surface inventory

### 2.1 Totals

| Layer | Entry points | Verdict |
|---|---:|---|
| COM interfaces (`slang.h`) — 26 interfaces | **~146** vtable methods | Bind (most) |
| Flat reflection C API (`slang-deprecated.h`) | **~182** functions | Bind (all) |
| Modern `slang_*` free functions (`slang.h`) | **19** | Bind (subset) |
| Legacy `spCompileRequest*` C API (`slang-deprecated.h`) | **83** | **Skip — genuinely dead** |
| **Total declared** | **~430** | |
| **Total live / bindable** | **~347** | |

Supporting type surface:

| Kind | Count | Notes |
|---|---:|---|
| Enums (declared) | 58 | |
| Enums (distinct concepts) | **47** | 23 are C/C++ mirror pairs (`SlangTypeKind` ≡ `slang::TypeReflection::Kind`) — bind once |
| Enum values (declared) | 648 | ~450 distinct after de-mirroring |
| Structs | 39 | 12 are opaque reflection handles with no data; ~15 are real POD descriptors |
| Opaque handle typedefs | 14 | `SlangReflectionType*`, `SlangReflectionTypeLayout*`, … |

The largest single enum is `CompilerOptionName` at **159 values** — it is the entire `slangc`
command line expressed as an API. It is also pure data: one C# enum, no logic.

### 2.2 COM interfaces, by weight

| Interface | Methods | Tier |
|---|---:|---|
| `IGlobalSession` | 30 | 0 |
| `ISession` | 21 | 0 |
| `IComponentType` | 14 | 0 |
| `IModule` | 13 | 0 |
| `IByteCodeRunner` | 11 | **skip** |
| `ICooperativeTypesMetadata` | 8 | **skip** |
| `ISlangFileSystemExt` | 7 | 3 (reverse) |
| `ISlangWriter` | 6 | 3 (reverse) |
| `ISlangMutableFileSystem` | 4 | 3 (reverse) |
| `ICoverageTracingMetadata` | 4 | **skip** |
| `ISlangProfiler` | 4 | **skip** |
| `IModulePrecompileService_Experimental` | 4 | **skip** |
| `IComponentType2` | 3 | 2 |
| `ICompileResult` | 3 | 2 |
| `ISyntheticResourceMetadata` | 3 | **skip** |
| `ISlangBlob` | 2 | 0 |
| `IEntryPoint`, `ITypeConformance`, `IMetadata`, `IBindlessResourceMetadata`, `ISlangCastable`, `ISlangClonable`, `ISlangFileSystem`, `ISlangSharedLibrary`(+`_Dep1`), `ISlangSharedLibraryLoader` | 0–1 each | mixed |

Every interface additionally inherits `ISlangUnknown`'s three methods
(`queryInterface`/`addRef`/`release`).

### 2.3 Flat reflection API, by receiver

This is the part that actually makes the binding "usable" for a renderer.

| Receiver | Functions |
|---|---:|
| `spReflectionTypeLayout_*` | 46 |
| `spReflection_*` | 27 |
| `spReflectionType_*` | 18 |
| `spReflectionGeneric_*` | 14 |
| `spReflectionFunction_*` | 14 |
| `spReflectionEntryPoint_*` | 12 |
| `spReflectionVariable_*` | 11 |
| `spReflectionVariableLayout_*` | 10 |
| `spReflectionDecl_*` | 9 |
| `spReflectionUserAttribute_*` | 6 |
| `spReflectionTypeParameter_*` | 4 |
| `spReflectionParameter_*` | 2 |
| `spGetReflection`, `spIsParameterLocationUsed`, misc | ~9 |

---

## 3. Five findings that determine the approach

### 3.1 Slang's public API is already ABI-flat. There is nothing to flatten.

This is the decisive difference from PrismUSD and PrismOIIO. OpenUSD and OpenImageIO expose C++
class APIs with STL types in their signatures — un-marshalable, hence the hand-written `extern "C"`
shim in `native/`. Slang does not need one:

- The object API is **COM-style vtables**, deliberately binary-compatible with COM. `ISlangUnknown`'s
  GUID is `{00000000-0000-0000-C000-000000000046}` — that is literally `IID_IUnknown`. The
  compatibility is intentional, not incidental.
- `SLANG_MCALL` is `__stdcall` on Windows and empty elsewhere. Under the x64 ABI `__stdcall` is a
  no-op, so a single declaration is correct on every 64-bit target we care about.
- **Signature audit: clean.** Of 143 fully-parsed vtable signatures, zero take or return a struct
  by value. Everything is pointers, primitives, and enums. The three entries my scan flagged
  (`getOSPathKind` → enum, `getEntryTimeMS` → `long`, `getSessionDescDigest` → `SessionDesc*`) are
  all benign on inspection.

A C++ shim over this would be transcription, not translation.

### 3.2 The reflection API is flat C — and that is the *only* way to call it.

`slang::TypeLayoutReflection`, `ShaderReflection`, `VariableReflection` and their nine siblings
look like C++ classes. They are not, in any ABI sense: **they declare zero virtual methods**. All
172 of their method bodies are `inline` forwarders, and 163 of those forward directly to an
`spReflection*` C function. The 9 that don't are trivial composites over other flat calls
(`isArray()`, `getTotalArrayElementCount()`, `unwrapArray()`, …), each a few lines to reimplement
in C#.

There is no vtable to bind even if we wanted one. The flat C functions *are* the reflection ABI,
for C++ callers as much as for us.

### 3.3 …but they are declared in `slang-deprecated.h`. Assessed risk: real, low, mitigable.

That header opens with:

> Everything in this file represents deprecated APIs/definition that are only being kept around for
> source/binary compatibility with old client code. New code should not use any of these
> declarations, and the Slang API will drop these declarations over time.

Taken at face value this would sink the whole plan. It should not be taken at face value, because
the file mixes two populations:

- **83 `spCompileRequest*` functions** — the genuinely dead pre-COM compilation API, superseded by
  `IGlobalSession`/`ISession`. This is what the warning is about. We skip it entirely.
- **~182 `spReflection*` functions** — implemented in `source/slang/slang-reflection-api.cpp`, a
  live 5,700-line file carrying 186 `SLANG_API` exports, with no deprecation markers in the
  implementation. The blessed, non-deprecated C++ reflection API in `slang.h` is a header-only
  veneer that compiles down to calls into exactly these symbols.

Slang cannot drop the reflection exports without rewriting its own public C++ reflection API. The
warning is a filing accident: the declarations live in the legacy header for historical reasons.

**Mitigation, not faith:** pin the native payload per PrismSlang release (§5.4), and carry a
smoke test that resolves every bound symbol from the shipped binary at CI time. A rename then
fails the build instead of the game.

### 3.4 `slang.dll` is now `slang-compiler.dll`, and the alias expires this year.

As of **v2025.21** the primary library was renamed from `slang.dll`/`libslang.so` to
`slang-compiler.dll`/`libslang-compiler.so`, because "the S-Lang library of the same name is
commonly preinstalled on Linux distributions." Windows ships a proxy `slang.dll` that forwards to
the new name; Linux and macOS ship symlinks. All of it **"will be removed at the end of 2026."**

We bind `slang-compiler` from day one. Given today's date this is not a future concern — a binding
written against `slang.dll` would break within months.

### 3.5 Slang's ABI is explicitly unstable, so the native payload must be pinned.

`docs/building.md` states the public ABI "is not currently stable" and directs redistributors to
ship the exact libraries they built against. Combined with Slang's release cadence — roughly
monthly, v2026.14.1 being the current tag — this means:

- One PrismSlang version pins exactly one Slang version. No floating range.
- The Slang version belongs in the package identity and in an assert-on-load version check
  (`spGetBuildTagString`).

This is already how PrismUSD ships, so it is a familiar constraint rather than a new one.

---

## 4. Recommended approach

> **Pure-managed P/Invoke. No native code in this repository at all.**
> Consume the official Slang release binaries per RID; write zero C++, no CMake, no `build-native.ps1`.

### 4.1 Why this deviates from the PrismUSD / PrismOIIO house pattern

The existing pattern — `native/` shim + `dotnet/NativeMethods.cs` + `build-native.ps1` + CMake — is
the right answer when the upstream library only offers a C++ ABI. It is the wrong answer here, and
the cost of applying it anyway is not neutral:

| | C++ shim (house pattern) | Pure managed (recommended) |
|---|---|---|
| Work to reach a callable API | Transcribe ~347 entries into `extern "C"`, then bind them again in C# | Bind ~347 entries once |
| Toolchain | CMake + MSVC + GCC/Clang + Xcode, per RID | .NET SDK only |
| Cross-platform cost | A native build, CI runner, and payload per RID | Download three official zips |
| Slang version bump | Rebuild and re-test three native artifacts | Swap the payload, rerun the symbol test |
| macOS (must not be excluded) | Needs a Mac in CI | Free |

The cross-platform column is the whole argument. **This is precisely what disqualifies Slang.Sdk**,
the binding foundation.md nominated: it is implemented in C++/CLI, which does not exist outside
Windows on modern .NET. Its own README lists Linux and macOS as future work gated on sponsorship,
and it remains alpha ("for experimental use only, should not be included in production builds").
That is not a maturity problem that time fixes — it is an architectural ceiling. If PrismSlang
adopts a native-shim design, it inherits a weaker version of the same ceiling.

A C++ shim also buys a benefit worth naming honestly: it could batch a whole reflection walk into
one ABI crossing. §4.4 shows why that benefit is worth approximately nothing here.

### 4.2 Layering

Five layers, mirroring the engine's own SPI discipline — each layer's types stop at its boundary.

```
L4  Slang.Hosting        Managed ISlangFileSystem / ISlangWriter exposed to Slang
                         (reverse interop — engine VFS, asset paks, diagnostics routing)
L3  Slang.Reflection     Immutable managed snapshot of a program's layout       ← the deliverable
L2  Slang.Compilation    Session / module / entry point / link / codegen, idiomatic C#
L1  Slang.Interop        Ref-counting, blobs, SlangResult → exceptions, diagnostics, UTF-8
L0  Slang.Native         Raw. Generated. Never referenced outside L1.
```

**L0 — raw.** Two mechanisms, no third:

- Flat C functions → `[LibraryImport("slang-compiler")]`, source-generated marshalling,
  NativeAOT-clean.
- COM interfaces → `[StructLayout(Sequential)]` vtable structs of
  `delegate* unmanaged[Stdcall]<...>`, invoked as `(*(ISessionVtbl**)p)->LoadModule(p, …)`.
  `Stdcall` is correct on Windows and an accepted no-op on 64-bit Unix, so one declaration serves
  every RID.

`ComWrappers` and built-in COM interop are rejected: they would work on Windows precisely because
of §3.1's IID_IUnknown compatibility, and would not port.

**L1 — interop hygiene.** Ref-counted handle wrappers with deterministic `Dispose`, `ISlangBlob`
→ `ReadOnlySpan<byte>` without copying, `SlangResult` → typed exceptions, diagnostic blobs
attached to those exceptions rather than dropped.

**L2 — compilation.** The ergonomic surface: build a session from a `TargetDesc` and options,
load modules, find entry points, compose, link, get SPIR-V.

**L3 — reflection.** See §4.4. This is the layer that determines whether the binding is "usable."

**L4 — hosting.** Reverse interop, so Slang can `import` from the engine's virtual filesystem
instead of the OS one: an unmanaged vtable of `[UnmanagedCallersOnly]` thunks, with a `GCHandle`
to the managed implementation stored alongside. Standard pattern, but the fiddliest code in the
project and the one most worth isolating.

### 4.3 Generate L0 with ClangSharp. Hand-write L1–L4. — *validated empirically*

L0 is the only layer that churns on a Slang version bump, and it is pure mechanism. At a monthly
upstream cadence, hand-maintaining it is the wrong posture.

**This was tested, not assumed.** `ClangSharpPInvokeGenerator` 21.1.8.4 (libclang 21.1.8) was run
against `slang.h` + `slang-deprecated.h` and the output compiled against `net10.0`. Result:

| | |
|---|---:|
| Generated files / lines | 126 / 4,998 |
| Flat C functions bound (`DllImport("slang-compiler")`) | 280 |
| — of which `spReflection*` | **173** |
| — of which modern `slang_*` | 21 |
| Vtable call sites (`delegate* unmanaged`) | 383 |
| Enums | 54 |
| **Hand-written support to make it compile** | **24 lines + 6 one-line patches** |
| **Final build** | **0 errors, 0 warnings** |

The generator is clearly the right tool. Two results are worth recording specifically.

**It gets the hard part right.** COM vtable dispatch comes out exactly as §4.2 specifies — down to
the calling convention and `[VtblIndex]` documentation attributes:

```csharp
[VtblIndex(0)]
public int queryInterface(SlangUUID* uuid, void** outObject)
    => ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangUUID*, void**, int>)(lpVtbl[0]))(
           (IGlobalSession*)Unsafe.AsPointer(ref this), uuid, outObject);
```

This supersedes an earlier guess in this document that the 26 vtable structs would need hand-writing.
They do not. `IGlobalSession` generated all 33 entries (30 + `ISlangUnknown`'s 3) correctly.

**It cannot translate the C++ reflection veneer — which is fine, because §4.4 discards it anyway.**
ClangSharp does attempt the `inline` forwarders in `TypeLayoutReflection` et al., and the attempt
fails structurally: in C++ `this` is a pointer, in a C# struct it is a value, so it emits
`(SlangReflectionTypeLayout*)(this)`, which does not compile. Left in, those 12 types produced
**~700 of 776 errors**, concentrated exactly there:

```
178  TypeLayoutReflection.cs      52  VariableLayoutReflection.cs
106  ShaderReflection.cs          48  EntryPointReflection.cs
 78  TypeReflection.cs            44  VariableReflection.cs
 56  FunctionReflection.cs        42  DeclReflection.cs
 56  GenericReflection.cs         24  SlangAttribute.cs   16  TypeParameterReflection.cs
```

Excluding all 12 and replacing them with 12 empty opaque `struct` declarations — 24 lines, since
they carry no data and no vtable — clears every one of those errors and loses nothing: the 173
flat functions they were forwarding to are all still bound. The generator's failure and this
document's independent recommendation point the same way.

**The full residue after configuration** — every manual intervention needed, end to end:

| Issue | Fix | Cost |
|---|---|---|
| `struct Attribute` shadows `System.Attribute`, breaking every generated helper attribute (~3,400 errors) | `--remap-type Attribute=SlangAttribute` | config |
| C++ inline reflection veneer untranslatable (~700 errors) | `--exclude` × 12 + opaque struct decls | 24 lines |
| `SLANG_COM_INTERFACE` GUID macro → `new byte[8]` into a fixed buffer (2 errors/interface) | `--exclude getTypeGuid`; emit the 26 GUIDs separately | script |
| Mirrored nested enums: `int`-backed `Kind` initialized from `uint` C constants (58 errors) | `: uint` backing, 3 sites | sed |
| C++ default member initializers on `SlangGlobalSessionDesc` (10 errors) | `--exclude createGlobalSession`; write the 6-line helper | 6 lines |
| Callback function-pointer typedefs mis-inferred as `Thiscall` (7 sites) | rewrite to `Cdecl` | sed |
| Three C++ pointer-arithmetic helpers (`ISlangSharedLibrary`, `SlangTerminatedChars`, `VMExecInstHeader`) | `Unsafe.AsPointer(ref this)` | 4 lines |

Note the fourth and sixth rows. The mirrored-enum failure is the same 23 C/C++ duplicate pairs
identified in §2.1 — the redundancy is not merely cosmetic, it actively breaks codegen. And the
`Thiscall` misinference lands in the `ISlangFileSystemExt` callback signature, i.e. squarely in
the Tier 3 reverse-interop layer. **That one would have been a silent ABI corruption had it
compiled.** It is the strongest argument for the CI symbol/signature test in §3.3, and for
treating L4 as hand-written rather than trusted output.

**Therefore:** commit the generator invocation as `tools/generate-native.ps1`, commit its output,
and review the diff on every Slang bump. Do not generate anything above L0 — L1–L4 are
engine-facing design decisions, and L4 in particular is where the generator is actively unsafe.

Rejected alternatives: **CppSharp** (heavier, generates a C++ glue layer we explicitly do not
want); **SWIG** (poor C#, no function-pointer vtables); **hand-writing L0** (~5,000 lines of
mechanism, re-audited monthly, for no gain over a reviewed diff).

One gap worth noting: ClangSharp emits `DllImport`, not `[LibraryImport]`. For this API that costs
nothing measurable — every signature is already blittable pointers and primitives — but it means
no source-generated marshalling. If that matters later, `disable-runtime-marshalling` is a config
flag away, though it will require replacing the handful of `bool` fields in descriptor structs.

### 4.4 Reflection must be projected to a managed snapshot, not proxied

**Decision: walk the native reflection tree exactly once, at load, and emit an immutable managed
object graph. Native reflection pointers never escape L3.**

Three independent reasons, any one sufficient:

1. **Lifetime.** `SlangReflection*` and everything reachable from it is owned by the
   `IComponentType` that produced it. Handing those pointers to engine code creates a
   use-after-free that will present as corrupt descriptor bindings, not as a clean crash.
2. **Architecture.** foundation.md's rule is that nothing above the SPI knows the implementation.
   A `TypeLayoutReflection` proxy holding a native pointer is the implementation, leaking upward.
   The renderer should receive plain managed data describing descriptor sets, bindings, push
   constants, and vertex inputs.
3. **Offline baking.** A managed snapshot is serializable. The asset pipeline can bake SPIR-V plus
   layout at build time and ship both, so the shipped game need not load the compiler at all —
   which also lets the ~50 MB native payload become a tooling-only dependency for release builds.

The cost is negligible and worth quantifying, because "too many P/Invokes" is the usual argument
for a batching shim. Reflection getters are O(1) reads over layout data Slang has already computed.
A complex shader might have a few thousand nodes; at ~10–20 ns per blittable `[LibraryImport]` call
that is **tens of microseconds, once, at load**. Not per frame. The shim's batching advantage
(§4.1) is real and irrelevant.

### 4.5 What the generator cannot produce, and how each gap is closed

Two distinct populations. The first is bounded and now handled; the second is the actual project.

#### A. Gaps inside L0 — things in the headers a generator cannot express

Ordered by how dangerous they are, which is *inversely* related to how loud they are.

| Gap | Why it resists generation | Failure mode | Resolution |
|---|---|---|---|
| **C++ default member initializers** on 14 descriptor structs | ClangSharp drops them without warning | **Silent.** Compiles; wrong behaviour | `dotnet/Defaults.cs` — hand-written factories, drift-tested against the header |
| **Result-code macros** (`SLANG_OK`, `SLANG_FAILED`, all `SLANG_E_*`) | Function-like macros: `SLANG_MAKE_ERROR(fac, code)` is a computed expression libclang won't evaluate | **Silent-ish.** Zero codes emitted, so success cannot be tested at all | `dotnet/SlangResult.cs` — hand-transcribed, drift-tested |
| **Interface IIDs** (`SLANG_COM_INTERFACE`) | constexpr `SlangUUID` → `new byte[8]` into a fixed buffer | Loud (build error) | Parsed from the header by the generator script; 27 emitted, completeness-asserted |
| **C++ reflection veneer** (12 types) | C++ `this` is a pointer, C# struct `this` is not | Loud | Excluded; 24-line opaque structs; object model hand-written in L3 |
| `SpecializationArg.Kind.Type` vs `System.Type` | `using static` + `ImplicitUsings` collision | Loud | Script qualifies the two uses |
| Callback fn-ptr typedefs inferred `Thiscall` | libclang member-function context | **Would have been silent ABI corruption** had it compiled | Script rewrites 7 sites to `Cdecl` |
| Mirrored nested enums `int`-backed from `uint` constants | The 23 C/C++ duplicate pairs of §2.1 | Loud | Script adds `: uint` |
| 3 pointer-arithmetic inline helpers | Same `this` problem | Loud | Script patches to `Unsafe.AsPointer` |

**The top two rows are the ones that matter.** Everything else announces itself at build time. These
two compile clean and misbehave at runtime, so they are the only entries that need a *test* rather
than a fix. Two concrete instances, both of which would mis-compile every shader in the engine:

```
SessionDesc.defaultMatrixLayoutMode   C++ default ROW_MAJOR (1)   →  zero-init UNKNOWN (0)
TargetDesc.flags                      C++ default GENERATE_SPIRV_DIRECTLY  →  zero-init 0,
                                      i.e. SPIR-V emitted *via glslang* rather than directly
```

The second one silently inverts the payload decision in §6 and pre-empts spike question 2 in §8.
`structureSize` is subtler still: Slang uses it to version the struct, so leaving it 0 means the
compiler cannot tell which layout you built against.

**Rule:** never construct a Slang descriptor with `new`. Always start from `Defaults.Create*()`.

#### B. What no generator could produce — this is the binding

A generator emits *call syntax*. Everything below is *semantics*, and none of it is in the headers
in machine-readable form:

- **Lifetime.** The generated layer exposes `addRef()`/`release()`; it says nothing about when to
  call them. Ref-counted handle wrappers with deterministic disposal are L1's whole job.
- **Ownership rules that exist only in prose.** Blob memory is valid while the blob lives;
  `SlangReflection*` is owned by the `IComponentType` that produced it; `getEntryPointCode`'s
  output has its own lifetime. These are English sentences in doc comments. They become types and
  invariants only by hand — which is exactly why §4.4 projects reflection to a snapshot.
- **Error model.** `int` → typed exceptions with the diagnostic blob attached.
- **UTF-8 marshalling.** The generated surface is `sbyte*` end to end.
- **The reflection object model (L3).** The actual deliverable: descriptor sets, bindings, push
  constants, vertex inputs, as plain managed data.
- **Reverse interop (L4).** *Categorical.* Binding generators emit the **consumer** side of an
  interface — how to call a vtable someone else implements. They never emit the **implementor**
  side. A managed `ISlangFileSystem` needs a synthesised native vtable of `[UnmanagedCallersOnly]`
  thunks plus a `GCHandle` back to the managed object. No amount of generator configuration
  produces that, and it is where the `Thiscall` misinference above was lurking.

So: the generator covers ~5,100 lines of mechanism and none of the design. That ratio is the
argument both for using it and for not expecting more from it.

---

## 4.6 Prior art — what already exists in C#

Surveyed August 2026. There is no `SlangSharp`; the name does not exist on GitHub or NuGet.

| | Slang.Sdk | Slangc.NET | PrismSlang |
|---|---|---|---|
| Approach | C++/CLI interop layer | in-process `slangc` CLI | pure managed P/Invoke |
| Public API | typed compilation + reflection | `byte[] Compile(string[] args)` | L0 raw + emerging L1 |
| Reflection | nearly complete (no `DeclReflection`) | **none** | not yet started |
| Platforms | Windows x64/ARM64 only | any .NET RID | any RID Slang publishes |
| Slang pinned to | 2025.13.2 | current | 2026.14.1 |
| Latest release | **0.5.1, Aug 2025** | 2026.5.1, current | — |
| Self-described status | "alpha… do not include in production builds" | maintained | pre-alpha |

**Slangc.NET solves a different problem.** Its entire surface is `SlangCompiler.Compile(args)` —
the `slangc` command line, in-process, returning bytes. That is a fine build-tool wrapper and it is
genuinely current, but it exposes no reflection at all. Reflection is the reason PrismEngine wants
Slang, so this cannot be the engine's binding regardless of quality.

**Slang.Sdk is the real comparable, and it is further along than PrismSlang on ergonomics.** Its
reflection abstraction is close to complete; ours has not started. Being behind on that is worth
stating plainly.

It is nevertheless not adoptable here, for reasons that are structural rather than fixable:

- **C++/CLI does not exist off Windows on modern .NET.** Its own matrix lists Linux and macOS as
  "planned"; that is not a maturity gap that time closes, it is the implementation choice. Making
  it cross-platform means abandoning C++/CLI — i.e. the rewrite this repository already is.
  foundation.md requires Linux x64 at V1.0 and forbids structurally excluding macOS.
- **Stale.** v0.5.1 is the latest, from August 2025; the roadmap targeted v1.0.0 for early October
  2025. Nothing has shipped in 2026. It is pinned to Slang 2025.13.2 — many releases behind, and
  crucially predating the `slang.dll` → `slang-compiler.dll` rename (§3.4).
- **Alpha by its own declaration**, for a component that sits under every shader the engine loads.

So the duplicated effort is real but narrow: the compilation and reflection *wrappers*. The
foundation differs in kind — no native build, current Slang, generated-and-diffable L0, drift
tests. Contributing upstream to Slang.Sdk was considered and rejected: the blocker is its
interop mechanism, so "fixing" it is a rewrite, not a patch.

---

## 5. Scope tiers

### Tier 0 — Compile (~50 entry points)
Global session, `ISession` + `TargetDesc`(SPIRV) + `CompilerOptionEntry`, module loading (source /
file / IR blob), `IEntryPoint`, `IComponentType` composition and linking, `getEntryPointCode` /
`getTargetCode`, `ISlangBlob`, diagnostics, `SlangResult`.
*Milestone: PrismEngine can replace shaderc.*

### Tier 1 — Reflection (~150 entry points)
The full `spReflection*` family except the generic/decl subtrees: shader, type, type layout,
variable, variable layout, entry point, function, user attributes, type parameters, parameters.
Projected per §4.4 into descriptor-set / binding / push-constant / vertex-input data.
*Milestone: PrismEngine can replace SPIRV-Cross. This is where the binding becomes worth having.*

### Tier 2 — Slang's actual advantages (~60 entry points)
`GenericReflection` (14) and `DeclReflection` (9) for AST-level introspection; `specialize()` +
`SpecializationArg` for generic shader variants; `ITypeConformance` for dynamic dispatch;
`IComponentType2` / `ICompileResult`; `IMetadata::isParameterLocationUsed` for stripping unused
bindings; `IBindlessResourceMetadata`; module serialization for a precompiled-module cache.

This tier is the reason to migrate off GLSL at all. Generics, interfaces, and link-time
specialization are what Slang has that shaderc does not; user attributes plus `DeclReflection` are
what a data-driven material system reflects against. Tiers 0–1 alone buy a lateral move.

### Tier 3 — Host integration (~25 entry points, disproportionate complexity)
`ISlangFileSystem` / `ISlangFileSystemExt` / `ISlangMutableFileSystem`, managed `ISlangBlob`,
`ISlangWriter`. Reverse interop; budget for it separately from its entry-point count.

### Tier 4 — Explicitly out of scope (~120 entry points)
- **83** legacy `spCompileRequest*` functions — dead API (§3.3).
- `IByteCodeRunner` (11) + `slangi` VM — CPU-side Slang execution; irrelevant to a Vulkan renderer.
- Coverage tracing (4), `ISlangProfiler` (4), `IModulePrecompileService_Experimental` (4).
- Cooperative matrix / vector metadata (11) — ML and tensor shaders.
- `slang-rhi` in its entirety — the engine owns Vulkan through Vortice.

**Fully featured ≈ Tiers 0–3 ≈ 285 entry points**, out of ~430 declared.

---

## 6. Packaging

Follow the PrismUSD model — native payload inside the package, RID-native for NuGet consumers and
flat-copy for `ProjectReference` — with three changes:

- **Three RIDs**, not one: `win-x64`, `linux-x64`, `osx-arm64`. Nothing about the design makes the
  third harder than the first, and foundation.md requires macOS not be structurally excluded.
- **Trim the payload.** The official zips are 54 MB (win-x64), 76 MB (linux-x64), 55 MB
  (osx-arm64). Measured contents of the win-x64 archive:

  | | | |
  |---|---:|---|
  | `bin/slang-llvm.dll` | 82 MB | CPU/host codegen. **Not needed.** |
  | `bin/slang-compiler.dll` | 24 MB | the compiler. Required. |
  | `bin/slang-glslang.dll` | 6 MB | glslang **and `spirv-opt`**. See below. |
  | `bin/slang-standard-module-*/` | 5.7 MB | loose `.slang-module` files |
  | `bin/slang-glsl-module.dll` | 1.5 MB | GLSL *input* only |
  | `bin/gfx.dll` | 1 MB | Slang's own graphics layer. We use Vortice. |
  | `bin/slang*.exe`, `bin/slang.dll` | — | tooling; legacy proxy |

  `tools/fetch-native.ps1` stages three profiles from this. **Measured win-x64: 29.8 MB minimal,
  35.7 MB with glslang**, against a 54 MB archive.

- **`slang-glslang` is required in practice — but not for the reason the docs suggest.** The spike
  settled this (§8). Direct SPIR-V emission genuinely does not need glslang: with the minimal
  payload the compile *succeeds* and produces valid SPIR-V 1.5. But it emits

  ```
  error[E00100]: failed to load downstream compiler 'spirv-opt'
  note[E99996]: failed to load dynamic library 'slang-glslang'
  ```

  on a **successful** `SlangResult`, and silently degrades to unoptimised output — 1,280 bytes
  versus 992 with glslang present, **22.5 % larger**, for the same shader. So ship it: 6 MB to
  avoid shipping unoptimised shaders and a spurious error on every compile. Note the shape of that
  failure — success code, error-level diagnostic — which is exactly why L1 must surface diagnostic
  blobs on the success path and not only when the result is a failure.
- **No `build-native.ps1`.** Replace it with `fetch-native.ps1`: download the pinned release per
  RID, verify checksums, extract the trimmed subset. Deterministic, and it runs anywhere.

---

## 7. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| Reflection C API relocated or renamed out of `slang-deprecated.h` | Medium | Pinned version + CI symbol-resolution test over every bound export (§3.3) |
| Unstable ABI across Slang versions | Medium | One PrismSlang release pins one Slang release; `spGetBuildTagString` assert on load |
| `slang.dll` alias removal, end of 2026 | **Already handled** | Bind `slang-compiler` from the first commit |
| Vtable layout drift on version bump | Medium | Hand-maintained and reviewed, not generated (§4.3); ordering-sensitive by nature |
| Reverse-interop lifetime bugs (L4) | Medium | Isolate in one assembly; `GCHandle` discipline; fuzz the VFS callbacks |
| `slang-glslang` needed at runtime after all | Low | Ship it; ~few MB |
| Monthly upstream cadence outpacing maintenance | Low | L0 generated; bumps are a regenerate-and-diff, not a rewrite |

---

## 8. Tier 0 spike — done

`samples/PrismSlang.Smoke` drives the full path against the real `slang-compiler.dll`: global
session → session with a SPIR-V target → load module from source → resolve two entry points →
compose → link → emit SPIR-V, then structurally validate the result (magic, version, word count).

```
slang-compiler 2026.14.1
  [ok] vtable dispatch — IGlobalSession::createSession
  [ok] module loaded — 2 entry points defined
  [ok] resolved 2 entry points
  [ok] composed and linked
  [ok] vertexMain       992 bytes  SPIR-V 1.5  248 words  bound=45
  [ok] fragmentMain    1320 bytes  SPIR-V 1.5  330 words  bound=44
```

Of the four questions:

1. **Vtable dispatch works.** `delegate* unmanaged[Stdcall]` through `lpVtbl` calls the shipped DLL
   correctly across `IGlobalSession`, `ISession`, `IModule`, `IEntryPoint`, and `IComponentType`.
   The generated layer needed no hand-editing to make this work. **Answered.**
2. **`slang-glslang` is needed for `spirv-opt`, not for SPIR-V.** Measured both ways; see §6.
   **Answered.**
3. **Payload: 29.8 MB minimal / 35.7 MB shipping, win-x64**, from a 54 MB archive. **Answered.**
4. **linux-x64 unverified.** Not testable on this machine — Windows only. `fetch-native.ps1` maps
   the RID and the P/Invoke declarations are RID-agnostic by construction, but that is reasoning,
   not evidence. **Still open**, and the one remaining risk to the cross-platform claim in §4.1.

Two findings worth carrying forward:

- Slang can return an **error-level diagnostic on a successful result** (the `spirv-opt` case). L1
  must therefore read the diagnostic blob on both paths. `Interop/Diagnostics.cs` does.
- `Defaults.SpirvTargetDesc()` is load-bearing. A `default(TargetDesc)` here would have routed
  through glslang and quietly produced different output — the §4.5 hazard, live.

**Next:** Tier 1 reflection (~150 entry points) — walk `ShaderReflection` over this same linked
program and project it to the managed snapshot described in §4.4. The spike's shader already has a
constant buffer, a texture, a sampler, and varyings specifically so there is something to reflect.

---

## 9. Status

All five layers are built and green on **win-x64 and linux-x64**: 375 tests, the Tier 0 spike, and
a full reflection walk on both.

| Layer | State |
|---|---|
| L0 raw | generated; 278 `DllImport`, 383 vtable sites, 27 IIDs, ~5,100 lines |
| L1 interop | UTF-8, blobs, diagnostics + severity policy, result codes, defaults, **ownership** |
| L2 compilation | `SlangGlobalSession` → `SlangSession` → `SlangModule` → `LinkedProgram` |
| L2 Tier 2 | type lookup, specialisation, type conformances, subtype checks, link-time options |
| L2 target env | pinned SPIR-V version, emitted-module verification, entry-point naming |
| L3 reflection | type tree + bindings + descriptor sets + generics + declarations + spec constants + buffer byte layout (matrix and element strides) |
| L3 SPIR-V | header, `OpEntryPoint` and workgroup-size reader, for facts reflection cannot report |
| L4 hosting | managed `ISlangFileSystem` via `[UnmanagedCallersOnly]` thunks |
| Baking | `ShaderPackage`: SPIR-V + layout + module entry-point name + version + workgroup size + dependency hashes |
| `PrismSlang.Vulkan` | separate package: descriptor sets, push constants, vertex inputs, fragment outputs, input attachments, stages and dispatch geometry, `VkSpecializationInfo`, target-env mapping |

| Supporting | State |
|---|---|
| `generate-native.ps1` | self-checking; asserts every interface has an IID |
| `fetch-native.ps1` | win-x64 + linux-x64 verified; osx-arm64 mapped, unrun |
| `tools/ci/verify-linux.sh` | build + tests + spike under WSL |
| `.github/workflows` | CI matrix, regeneration check, package-contents check, publish |
| Tests | 375. `ModuleAgreementTests` diffs a 27-shader corpus against the emitted module as sets, both directions; `SurfaceCoverageTests` fails on any Slang enum value the corpus neither exercises nor declares untried. See §9.11 and §9.13 |

Package sizes tell the story of the split: `PrismSlang` is 34 MB (it carries the compiler for every
RID), `PrismSlang.Vulkan` is **10 KB**. A D3D12 consumer pays nothing for Vulkan.

### 9.1 Two bugs the cross-platform run found

Both were invisible on Windows. This is the entry that justifies §8's fourth question.

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

- **osx-arm64** — RID mapped in `fetch-native.ps1`, never executed. Same class of risk linux-x64
  turned out to carry, so treat it as unverified rather than probably-fine.
- **CI has never fully run.** The workflows are written and every step rehearsed locally, but the
  YAML has not been machine-validated.
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
