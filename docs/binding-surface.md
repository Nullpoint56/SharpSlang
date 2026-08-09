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

All five layers are built and green on **win-x64 and linux-x64**: 99 tests, the Tier 0 spike, and
a full reflection walk on both.

| Layer | State |
|---|---|
| L0 raw | generated; 280 `DllImport`, 383 vtable sites, 27 IIDs, ~5,100 lines |
| L1 interop | UTF-8, blobs, diagnostics, result codes, descriptor defaults, **ownership** |
| L2 compilation | `SlangGlobalSession` → `SlangSession` → `SlangModule` → `LinkedProgram` |
| L2 Tier 2 | type lookup, specialisation, type conformances, subtype checks, link-time options |
| L3 reflection | type tree + bindings + descriptor sets + generics + declaration tree |
| L4 hosting | managed `ISlangFileSystem` via `[UnmanagedCallersOnly]` thunks |
| Baking | `ShaderPackage`: SPIR-V + layout + version + source hash in one container |
| `PrismSlang.Vulkan` | separate package: descriptor sets, push constants, vertex inputs |

| Supporting | State |
|---|---|
| `generate-native.ps1` | self-checking; asserts every interface has an IID |
| `fetch-native.ps1` | win-x64 + linux-x64 verified; osx-arm64 mapped, unrun |
| `tools/ci/verify-linux.sh` | build + tests + spike under WSL |
| `.github/workflows` | CI matrix, regeneration check, package-contents check, publish |
| Tests | 125 across drift, compilation, reflection, Vulkan, hosting, generics, baking, link options |

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
| `VulkanUseEntryPointName` | none, and redundant: Slang already emits the entry point's own name |

The last two are reachable via `RawEntries` if a future release honours them. A test asserts that a
raw entry and its typed equivalent produce byte-identical output, so the escape hatch cannot rot.

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
- **CI has never run.** The workflows are written and every step rehearsed locally, but no push has
  exercised them on GitHub's runners, and the YAML has not been machine-validated.
