# SharpSlang — Surface Study and Approach

Scope study for a fully-featured C# binding over the Slang shader compiler, including reflection,
for consumption by PrismEngine's Renderer Core.

Measured against `shader-slang/slang` @ `master`, cross-checked against release **v2026.14.1**
(30 July 2026). Counts below come from regex analysis of `include/slang.h` (6,133 lines) and
`include/slang-deprecated.h` (1,715 lines); treat them as accurate to within a few entries, not
to the unit. The pin has since moved to **v2026.21** (4 November 2026); the counts are the study's
and were not re-taken, while every number the test suite checks was.

> **How the binding is built and why.** It began as a study written before any of it existed, and
> the sections that argued a case are kept in the tense they argued it in — §4.3 predicted a
> generate-L0-hand-write-the-rest split and then validated it, and §7's risks can be read against
> what actually happened.
>
> Section numbers are cited from code comments by bare number, so `§4.4` in a source file means the
> heading below. They do not get renumbered.
>
> For *using* SharpSlang rather than understanding its shape, see [guide.md](guide.md) and the
> [README](../README.md).

---

## 1. Context

PrismEngine's foundation document (`docs/foundation.md`, "Shader Compilation") currently specifies
GLSL + shaderc for compilation and SPIRV-Cross for reflection, both bundled inside Vortice.Vulkan,
and records the intent:

> **Future migration path:** Slang via Slang.Sdk once C# bindings reach maturity and Linux/macOS
> support ships. Migration is contained to Renderer Core behind the SPI.

SharpSlang exists to be that binding rather than to wait for one. Two constraints from foundation.md
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

### 2.2 Where the surface lives

Two shapes, and the split decides the whole approach. The **object API** is 26 COM-style interfaces
carrying ~146 vtable methods, weighted heavily towards four of them — `IGlobalSession` (30),
`ISession` (21), `IComponentType` (14) and `IModule` (13) — with most of the rest at zero or one.
The **reflection API** is ~182 flat C functions, half of them on two receivers:
`spReflectionTypeLayout_*` (46) and `spReflection_*` (27).

Everything else is small: the byte-code runner, coverage tracing, cooperative-type metadata and the
experimental precompile service are skipped entirely (§5, Tier 4), and the reverse-direction
interfaces a host implements — `ISlangFileSystemExt`, `ISlangWriter` — are Tier 3.


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

**Mitigation, not faith:** pin the native payload per SharpSlang release (§3.5), and carry a
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
ship the exact libraries they built against. Combined with Slang's release cadence — eleven
releases between v2026.14.1 (30 July 2026) and v2026.21 (4 November 2026), the current pin — this
means:

- One SharpSlang version pins exactly one Slang version. No floating range.
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
That is not a maturity problem that time fixes — it is an architectural ceiling. If SharpSlang
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

**And it gets it right, which is a separate claim from getting it to compile.** Everything above says
the generator produces code that builds and dispatches. Nothing in it says the code is *correct* —
a field at the wrong offset, a struct sized wrongly, a bitfield packed the other way round or a
parameter dropped from a signature all compile, resolve and run, and are read as the wrong bytes at
the wrong address. The `regen` job cannot see any of it: it compares the committed output against
what the generator produces, which is the generator agreeing with itself.

The only party that can contradict it is a C++ compiler reading the same header, and
`NativeAbiTests` asks one. It generates a probe from the C# metadata, compiles it against the
pinned `slang.h` and compares:

| | |
|---|---:|
| Structs measured — `sizeof` and `alignof` | 51 |
| Field offsets — `offsetof`, every named field | 116 |
| Bitfield accessors — the exact bytes each property writes | 2 |
| Free-function signatures — `static_assert` per `DllImport` | 279 |
| Vtable method signatures — one per method an interface declares | 231 |

All of them agree, on all four platforms. Two of those numbers are the ones this section's argument
rested on and could not previously support: the bitfields are the only hand-rolled arithmetic in the
generated layer, and the signatures are what a wrong parameter list would corrupt a stack through.

Slot *order* is not in the probe. clang lays out a vtable only for a class it is going to emit one
for, and Slang's interfaces are pure-abstract with no key function, so nothing in a probe
translation unit causes one to exist — `-fforce-emit-vtables` included. `VtableOrderTests` derives
all 374 slots from the header's declaration order instead, which is the same ground by a different
route. Signatures are reachable regardless, because a pointer-to-member type needs no vtable.

Both read `dotnet/Native/*.cs` as text rather than reflecting over the assembly, and that is not a
stylistic choice: `VtblIndexAttribute` and `NativeTypeNameAttribute` are `[Conditional("DEBUG")]`,
so in the Release configuration CI builds neither attribute reaches the metadata. A reflection-based
version of either check would have found nothing to check and passed.

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

Surveyed before starting and re-surveyed once the reflection surface was finished, because two
load-bearing claims in the first survey turned out to be wrong.

**`Slang.Sdk`** (Ravbug) is the closest thing. It is a real, working binding, and the difference is
scope rather than quality: it wraps the compilation path and a useful slice of reflection, and it
carries its own native build. SharpSlang binds the reflection surface exhaustively — descriptor
sets, byte layouts down to matrix and array strides, the declaration tree, generics, specialization
constants — projects it to an immutable managed snapshot that outlives the compiler (§4.4), and
consumes Slang's own published release rather than building it.

**There is no `SlangSharp`.** The name does not exist on GitHub or NuGet; an early draft of this
document cited it, which is the same defect it keeps finding elsewhere and is recorded here rather
than quietly deleted.

The remaining option is calling `slangc.exe` and parsing `-reflection-json`. That works, and it
costs a process per shader plus a JSON schema that is not versioned. For a cooker with a warm
session per worker thread it is the wrong shape.
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
  35.7 MB with glslang**, against a 54 MB archive. At v2026.21 the archive is 60.8 MB and the
  standard modules alone are 31.6 MB, 27 MB of it the four `numerics` modules that arrived after
  v2026.14.1, so the staged win-x64 payload with glslang is 63.1 MB.

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

This table was written before the binding existed and is now maintained against it. Each row names
the **artefact** that enforces the mitigation — a test class or a CI job — rather than a section of
this document, for a reason worth stating plainly: the vtable row below spent its whole life citing
§4.3 in support of "hand-maintained and reviewed, not generated", and §4.3 is the section that
overturns exactly that, in a sentence beginning *"This supersedes an earlier guess in this
document."* The citation resolved, the heading existed, and the claim was backwards.

Naming an artefact instead does not make a mitigation true, but it makes the claim falsifiable by
someone who is not reading carefully: `tools/ci/check-docs.sh` fails if a test named here does not
exist.

| Risk | Severity | Enforced by |
|---|---|---|
| Reflection C API relocated or renamed out of `slang-deprecated.h` (§3.3) | Medium | `NativeExportTests` resolves all 279 bound exports against the shipped binary, so a rename fails CI rather than surfacing as a `DllNotFoundException` in a consumer's build |
| Unstable ABI across Slang versions (§3.5) | Medium | `SlangGlobalSession.Create` refuses a payload whose build tag is not the pinned one; `Build_tag_matches_the_pinned_version` ties the pin itself to `Directory.Build.props` |
| `slang.dll` alias removal, end of 2026 (§3.4) | **Already handled** | `slang-compiler` bound from the first commit; the export test above would catch a regression |
| Vtable layout drift on version bump | Medium | `VtableOrderTests` derives all 374 slot indices from the declaration order in the pinned header and compares them against the `lpVtbl[n]` each generated method actually calls, so a reordering fails a test rather than depending on a human reading the `regen` diff. The `regen` job still runs and still fails on any diff |
| The generated layer mistranslating a layout or a signature | Medium | `NativeAbiTests` compiles a generated probe against the pinned `slang.h` with a real C++ compiler and checks 51 struct sizes, 116 field offsets, 2 bitfield accessors and 510 signatures — 279 free functions and 231 vtable methods (§4.3). Nothing else in the suite can see an offset error; the calls all still compile and dispatch |
| The projection reporting a layout that is self-consistently wrong | Medium | Every other reflection oracle is Slang describing itself. `SlangWalkerCorpusTests` compares `ReflectionWalker` against the expected results in Slang's own `tests/reflection`, which the Slang team wrote, and `SlangComputeCorpusTests` dispatches 56 shaders from `tests/compute` with their buffers, storage images and samplers bound where reflection says, and compares the readback with the words upstream recorded. 55 match; the one that does not is declared, with the decoding of why |
| Reverse-interop lifetime bugs (L4) | Medium | `CallbackBoundaryTests` drives every vtable slot with a null `this`, a null out-parameter and a provider that throws from inside a read; `LifetimeTests` covers ownership; the linux CI job runs the whole suite under `MALLOC_PERTURB_`, so a freed block is no longer quietly readable |
| `slang-glslang` needed at runtime after all | Low | Shipped. `DownstreamCompilerTests` asserts it from inside the process and a CI step asserts it in the staged payload, because its absence is reported once, on a *successful* result |
| Monthly upstream cadence outpacing maintenance | Low | L0 is generated, so a bump is a regenerate-and-diff. `DefaultsDriftTests` and `SlangResultDriftTests` re-read the pinned header and fail on a changed constant or default; the probe above fails on a changed layout or signature; `SurfaceCoverageTests` fails on an enum value the corpus has never produced and nobody has declared untried |

Two mitigations proposed here were **not** built as written. Recording that is cheaper than leaving
the rows to imply otherwise:

- *"Isolate [reverse interop] in one assembly."* It lives in `dotnet/Hosting/`, inside `SharpSlang`.
  The assembly split that did happen was a different one — the reader half into
  `SharpSlang.Pipeline` — driven by a consumer's requirement rather than by this risk.
- *"Fuzz the VFS callbacks."* They are boundary-tested exhaustively instead: every slot, every null,
  an exception thrown from inside a read. The fuzzer that exists, `MalformedInputTests`, points at
  the two file readers, which is where malformed bytes actually arrive from outside.

---

## 8. Tier 0 spike — done

`samples/SharpSlang.Smoke` drives the whole path against the real payload: global session → session
with a SPIR-V target → module from source → two entry points → compose → link → emit SPIR-V, then
validate the result structurally. It runs in CI on all four platforms.

It existed to answer four questions before the rest was built, and answered all four yes: vtable
dispatch through `delegate* unmanaged[Stdcall]` reaches the shipped library; the generated L0 layer
is callable as generated; the reflection C API returns what the emitted module contains; and the
same source builds and runs on a second platform. The fourth is the one that mattered — the first
Linux run found a borrowed pointer being released, invisible on the Windows heap.
