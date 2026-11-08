# Upstream reports

Every Slang defect this repository has found, as report text in Slang's terms, with what was filed
where. Nothing here has been posted: filing is the repository owner's decision, made on the exact
text, and the policy for it is kept outside the repository. Measurements, the guards in the binding
and the tests that hold each entry are in `AGENTS.md` under Oddities and caveats (the "U" numbers
match).

Measured against Slang **v2026.21** (tag commit e73d2689) unless an entry says v2026.14.1. Line
numbers are v2026.21's. "All four platforms" means win-x64, linux-x64, linux-arm64 and osx-arm64 on
GitHub-hosted runners (windows-latest, ubuntu-24.04, ubuntu-24.04-arm, macos-15).

A **patch candidate** is an entry whose fix is small and local enough to carry as a patch once this
repository builds Slang from source; each would go test first (red against the unpatched build).
The rest are behaviour or documentation questions whose fix is the maintainers' call, so the
natural form is an issue.

| # | Defect | Kind | Patch candidate | Status |
|---|---|---|---|---|
| U1 | `spReflectionType_GetElementCount` / `GetSpecializedElementCount` crash on an array sized by a link-time constant | crash | yes | not filed |
| U2 | `getBindingRangeCount` throws a C++ exception through `extern "C"` for a resource array sized by a link-time constant | crash | yes | not filed |
| U3 | `spReflection_specializeGeneric` access-violates on a mismatched argument list | crash | yes | not filed |
| U4 | `spReflection_specializeType` on a non-generic type segfaults on Linux (an assert that removes its own check) | crash | yes | not filed |
| U5 | A constant buffer whose block holds only link-time-sized arrays gets no binding | wrong layout | after investigation | not filed |
| U6 | A 64-bit 3/4-component vertex input is given one location; Vulkan requires two | invalid SPIR-V | yes | not filed |
| U7 | `sample` interpolation without `SampleRateShading` | invalid SPIR-V | n/a | fixed upstream by v2026.21 (reported by others as #12441); held by a test |
| U8 | `float4x4` vertex input with a member `Offset` decoration | invalid SPIR-V | n/a | fixed upstream by v2026.21; held by a test |
| U9 | A missing `slang-glslang` is an error diagnostic on a successful result, reported once per global session | reporting | no | not filed |
| U10 | Windows: downstream libraries are not looked up in the compiler's own directory | loading | yes | not filed |
| U11 | Two public functions have C++ linkage | header | yes (ABI-breaking for C++ callers) | not filed |
| U12 | `ISession::loadModule*` returns a borrowed reference; the header does not say so | documentation | yes | not filed |
| U13 | Releasing a session's last reference from `~Linkage` destroys the global session inside it | crash (macOS) | after re-measurement | not filed |
| U14 | `isBinaryModuleUpToDate` answers from the session's file cache | wrong answer | no | not filed |
| U15 | Reflection queries that answer nothing, and facts reflection does not carry | reflection gaps | no | not filed |
| U16 | `linkWithOptions` silently ignores most compiler options | silent no-op | no | not filed (re-measure first) |
| U17 | SPIR-V for `SV_VertexID` differs between x86-64 and aarch64 builds | determinism | after investigation | not filed |

Related upstream items found while checking whether these were known: the layout-query robustness
work [#8964](https://github.com/shader-slang/slang/issues/8964),
[#8965](https://github.com/shader-slang/slang/issues/8965) ("make sure that all the errors we
encounter while calculating layouts are fully propagated to the user rather than crashing or being
asserted on"), [#9045](https://github.com/shader-slang/slang/issues/9045) and
[#9046](https://github.com/shader-slang/slang/issues/9046), all closed 2025-11-25; U1 and U2 are on
paths that work did not reach. [#12360](https://github.com/shader-slang/slang/issues/12360) is an
access violation specializing a generic method through a different path than U3. Searches of the
tracker for the calls in U1-U4 and for U6's construct and VUID found nothing (2026-08-10, v2026.14.1);
repeat them before filing.

Every repro below compiles the shader shown through the ordinary API: `slang_createGlobalSession2`,
`createSession` with one SPIR-V target (unless stated), `loadModuleFromSourceString`,
`findEntryPointByName`, `createCompositeComponentType`, `link`, then `getLayout(0)`.

---

## U1 — Element count of an array sized by a link-time constant crashes

**Observed.** For an array whose length is a specialization constant, both
`spReflectionType_GetElementCount` and `spReflectionType_GetSpecializedElementCount` end the
process: an access violation on Windows (surfacing as a structured exception), an abort on Linux.
`slangc -reflection-json` on the same shader dies the same way (exit `0xC0000409` on Windows,
v2026.14.1), and `slangc -target spirv` compiles it.

**Expected.** `slang.h:2633-2642` documents `TypeReflection::getElementCount`, which forwards to
`spReflectionType_GetSpecializedElementCount`: "Returns `SLANG_UNKNOWN_SIZE` when size depends on
unresolved generic parameters or link-time constants." `slang-deprecated.h:478-479` says
`spReflectionType_GetElementCount` returns zero when the size cannot be statically computed. The
size queries on the same array already return `SLANG_UNKNOWN_SIZE` (`GetSize(UNIFORM)` and the next
field's offset are `0xFFFFFFFFFFFFFFFE`), so only the element-count path is unhandled.

**Cause.** Not located yet; the size and offset paths were fixed by the #9045 work and the element
count was not.

**Measurement.** v2026.21, all four platforms: the call ends the process (non-zero exit; the type
reports `SLANG_TYPE_KIND_ARRAY` before the call).

**Repro.**

```hlsl
[vk::constant_id(0)] const int kSeCount = 3;
struct SeBlock { float4 seItems[kSeCount]; float seTail; };
ConstantBuffer<SeBlock> seBuffer;

[shader("fragment")]
float4 seFragment() : SV_Target { return seBuffer.seItems[0] + seBuffer.seTail; }
```

```cpp
auto* type  = spReflection_FindTypeByName(layout, "SeBlock");
auto* field = spReflectionType_GetFieldByIndex(type, 0);
auto* array = spReflectionVariable_GetType(field);         // kind == SLANG_TYPE_KIND_ARRAY
spReflectionType_GetSpecializedElementCount(array, layout); // crashes; so does GetElementCount(array)
```

## U2 — `getBindingRangeCount` throws through `extern "C"` for a link-time-sized resource array

**Observed.** A resource array whose length is a specialization constant makes
`spReflectionTypeLayout_getBindingRangeCount` on the global scope throw `Slang::InternalError`
out of the C API. On Windows SEH carries it to the caller as a structured exception; on Linux and
macOS nothing can catch it and the process terminates: SIGABRT, exit 134, "terminate called after
throwing an instance of 'Slang::InternalError'" (linux-x64), "libc++abi: terminating due to uncaught
exception of type Slang::InternalError" (osx-arm64). `slangc -reflection-json` dies on the same
shader (`0xC0000409`, Windows, v2026.14.1); `slangc -target spirv` compiles it.

**Expected.** An error reported through the API (a failure result or a diagnostic), as #8965 set out
for layout queries; or, since the binding-range count of such an array is not known before
specialization, `SLANG_UNKNOWN_SIZE` where a count is asked for.

**Why it matters.** Nothing distinguishes the shader before the call: every query that does not
crash answers exactly as for a fixed-size array (same categories, `DESCRIPTOR_TABLE_SLOT` 1,
stride 0, alignment, `genericParamIndex` -1), so a host cannot guard it. A batch reflection on Linux
loses the process where Windows loses one shader.

**Measurement.** v2026.21, all four platforms, through a child process.

**Repro.**

```hlsl
[vk::constant_id(0)] const int kCascades = 4;

Texture2D    scArray[kCascades];
SamplerState scSampler;

[shader("fragment")]
float4 scFragment() : SV_Target { return scArray[0].Sample(scSampler, 0); }
```

```cpp
auto* scope = spReflection_getGlobalParamsTypeLayout(layout);
spReflectionTypeLayout_getBindingRangeCount(scope);   // throws Slang::InternalError
```

## U3 — `spReflection_specializeGeneric` access-violates on a mismatched argument list

**Observed.** Given a generic with one type parameter, `spReflection_specializeGeneric` with an
integer argument, or with two type arguments, access-violates on Windows and Linux (x86-64 and
aarch64). On macOS (arm64) the same calls report `error[E30060]: expected a type` and
`too many arguments` respectively, and return a **non-null** generic alongside the error.

**Expected.** The macOS behaviour minus the non-null result: a diagnostic and null, on every
platform. The diagnostic path exists; Windows and Linux do not reach it.

**Measurement.** v2026.21, all four platforms, through a child process. The argument structs were
checked first: `SlangReflectionGenericArgType` is a 4-byte enum, `SlangReflectionGenericArg` an
8-byte union, and the same call with a matching argument returns the correct specialization (a
type argument gives `MgWrapper<MgLambert>` with the right layout; a value argument on a value
generic sizes the array).

**Repro.**

```hlsl
interface IMgMaterial { float3 shade(float3 albedo); }
struct MgLambert : IMgMaterial { float3 shade(float3 a) { return a; } }
struct MgWrapper<T : IMgMaterial> { T inner; float4 tint; };

[shader("fragment")]
float4 mgFragment() : SV_Target { return 1; }
```

```cpp
auto* declared = spReflection_FindTypeByName(layout, "MgWrapper");
auto* generic  = spReflectionType_GetGenericContainer(declared);

SlangReflectionGenericArgType kind = SLANG_GENERIC_ARG_INT;
SlangReflectionGenericArg arg; arg.intVal = 3;
ISlangBlob* diagnostics = nullptr;
spReflection_specializeGeneric(layout, generic, 1, &kind, &arg, &diagnostics);   // AV on Windows and Linux
```

## U4 — `spReflection_specializeType` on a non-generic type segfaults on Linux

**Observed.** `spReflection_specializeType` given a struct that is not generic, with one type
argument, returns null on Windows and macOS (no diagnostics) and segfaults on Linux (SIGSEGV, exit
139, x86-64 and aarch64).

**Expected.** Null and a diagnostic on every platform.

**Cause.** `Linkage::specializeType` (`source/slang/slang-check-shader.cpp`) asserts that the
parameter count matches and then checks the same condition:

```cpp
SLANG_ASSERT(specializationParams.getCount() == argCount);   // 4087
if (specializationParams.getCount() != argCount)             // 4088
    return nullptr;                                          // 4089
```

In a build without `_DEBUG`, `SLANG_ASSERT(x)` is `SLANG_ASSUME(x)` (`source/core/slang-common.h:371`),
so the compiler may treat the condition as true and remove the check after it. A non-generic type
collects no specialization parameters, the count is 0 against an `argCount` of 1, and the loop
below reads `specializationParams[0]` of an empty list. What happens next is the compiler's: the
GCC 11 build of the release segfaults, the MSVC build returns null, and the same sources built with
GCC 13.3 (Ubuntu 24.04) return a non-null specialized type with no diagnostic. Turning the assert
into a plain check, or keeping only the `if`, makes every build return null.

**Measurement.** v2026.21, all four platforms, through a child process; the GCC 13.3 build of the
tag measured the same way on linux-x64.

**Repro.**

```hlsl
struct SnBlock { float4 snTint; };
ConstantBuffer<SnBlock> snBuffer;

[shader("fragment")]
float4 snFragment() : SV_Target { return snBuffer.snTint; }
```

```cpp
auto* block = spReflection_FindTypeByName(layout, "SnBlock");   // SLANG_TYPE_KIND_STRUCT
SlangReflectionType* args[] = { block };
ISlangBlob* diagnostics = nullptr;
spReflection_specializeType(layout, block, 1, args, &diagnostics);   // null on Windows/macOS, SIGSEGV on Linux
```

## U5 — A constant buffer whose block holds only link-time-sized arrays gets no binding

**Observed.** `ConstantBuffer<Block>` with `struct Block { float4 values[kCount]; }`, `kCount` a
specialization constant, reports no binding of any kind: no descriptor range and no parameter
category. The `[[vk::push_constant]]` form reports no push-constant range either. At v2026.14.1 the emitted SPIR-V also declared no descriptor for the
buffer while the shader reads `gUnsized.values[0]`.

**Expected.** Either a binding (the descriptor exists whatever the array length), or a compile
error saying the buffer cannot be laid out before specialization. A block that also has a
fixed-size member does get its binding.

**Measurement.** Reflection half: v2026.21, all four platforms. Module half: v2026.14.1, to be
re-measured on v2026.21 with `spirv-dis` before filing.

**Repro.**

```hlsl
[vk::constant_id(0)] const int kCount = 4;
struct Block { float4 values[kCount]; };
ConstantBuffer<Block> gUnsized;

[shader("fragment")]
float4 fragmentMain() : SV_Target { return gUnsized.values[0]; }
```

Reflect `gUnsized`: `spReflectionVariableLayout_GetCategoryCount` reports no category.

## U6 — A 64-bit three- or four-component vertex input takes one location

**Observed.** For a vertex input `float3 position; double4 wide; float2 after;`, Slang decorates
locations 0, 1 and 2. A `double3` or `double4` consumes two locations in Vulkan ("Location
Assignment" in the Vulkan specification's interfaces chapter), so `after` collides with the second
half of `wide`. `vkCreateShaderModule` with validation reports
`VUID-StandaloneSpirv-OpEntryPoint-08721`: "Entry-point has conflicting input location assignment
at location 2, component 0". Reflection reports the same numbering, so a host building vertex input
state from reflection builds the overlapping one. `slangc` alone produces the module.

**Expected.** `after` at location 3.

**Measurement.** v2026.21, `slangc` and the API agree; spirv-val rejects the module. Rechecked with
`slangc` only at v2026.14.1 (2026-08-10), with no other tool in the process:

```
OpEntryPoint Vertex %vertexMain "main" %gl_Position %v_position %v_wide %v_after
OpDecorate %v_position Location 0
OpDecorate %v_wide     Location 1     ; double4: consumes 1 and 2
OpDecorate %v_after    Location 2     ; collides
```

**Repro.**

```hlsl
struct VIn
{
    float3  position : POSITION;
    double4 wide     : TEXCOORD0;
    float2  after    : TEXCOORD1;
};

[shader("vertex")]
float4 vertexMain(VIn v) : SV_Position
{ return float4(v.position, 1) + (float)v.wide.x + float4(v.after, 0, 0); }
```

`slangc -target spirv -entry vertexMain -stage vertex` then `spirv-val --target-env vulkan1.2`.

## U7 — `sample` interpolation without `SampleRateShading` (fixed upstream)

**Status.** Fixed upstream by v2026.21; reported by another user as
[#12441](https://github.com/shader-slang/slang/issues/12441) (opened 2026-08-09), which names the
cause: `_maybeEmitInterpolationModifierDecoration` in `slang-emit-spirv.cpp` wrote
`SpvDecorationSample` without `requireSPIRVCapability`. The fixing pull request is not identified
here. Kept while a test holds the fix: the module of a shader using `sample` must declare
`OpCapability SampleRateShading` (35) beside `OpDecorate ... Sample` (17).

**Observed at v2026.14.1.** `OpCapability Shader` only, `OpDecorate %v_uv Sample`; spirv-val:
"Operand 2 of Decorate requires one of these capabilities: SampleRateShading".

## U8 — `float4x4` vertex input with a member `Offset` (fixed upstream)

**Status.** Fixed upstream by v2026.21; not reported by us; the fixing pull request is not
identified here. Kept while a test holds the fix: no `Input` interface struct in the module carries
`Offset` on a member.

**Observed at v2026.14.1.** A `float4x4` vertex input was wrapped in a `_MatrixStorage` struct
whose member was decorated `Offset 0`, row- and column-major alike. Vulkan SDK 1.4.363's spirv-val
rejects it: `VUID-StandaloneSpirv-Offset-12509`, "the Input interface variable must not have Offset
on any member decoration".

## U9 — A missing `slang-glslang` is an error on a successful result, once per global session

**Observed.** With `slang-glslang` absent from the library search path, `getEntryPointCode` for a
SPIR-V target returns `SLANG_OK` and valid SPIR-V, and its diagnostics say:

```
error[E00100]: failed to load downstream compiler 'spirv-opt'
note[E99996]: failed to load dynamic library 'slang-glslang'
```

The output is unoptimised: 1,280 bytes against 992 for the same shader with `slang-glslang`
present, 22.5 % larger. The diagnostic is given for the first compile of a global session only;
later compiles in the same session are unoptimised and say nothing.

**Expected.** One of: a failure result when optimisation was requested and cannot run; or a warning
on every affected compile. An error-severity diagnostic attached to success, once, is the
combination a host cannot act on reliably.

**Measurement.** v2026.14.1 (the diagnostics, the sizes and the once-per-session behaviour). Not
re-measured on v2026.21: do that before filing.

**Repro.** Any SPIR-V compile with the default optimisation level and `slang-glslang` removed from
beside `slang-compiler`; compile two modules in one global session and compare their diagnostics.

## U10 — Windows: downstream libraries are not looked up in the compiler's own directory

**Observed.** On Windows, an application that loads `slang-compiler.dll` from a directory other than
the executable's (a plugin directory, a package's `runtimes\win-x64\native\`, or an interpreter
started from elsewhere) does not get `spirv-opt`: `slang-glslang.dll` beside `slang-compiler.dll`
is not found, and neither is `dxcompiler.dll`. On Linux and macOS the same layout works, because
`libslang-compiler.so` carries `DT_RUNPATH` `$ORIGIN/../lib:$ORIGIN` and the dylib `LC_RPATH`
`@loader_path/../lib` and `@loader_path`.

**Cause.** `SharedLibrary::loadWithPlatformPath` (`source/core/slang-platform.cpp:199-202`) calls
`LoadLibraryExW(name, nullptr, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS)` and then `LoadLibraryW(name)` with
the bare name `slang-glslang` (`source/compiler-core/slang-glslang-compiler.cpp:592`, via
`DownstreamCompilerUtil::loadSharedLibrary`, which uses no directory when no downstream path is
set). `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` includes `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR`, but that flag
only applies when the name is a fully qualified path, so the directory of `slang-compiler.dll` is
never searched. The comment at `slang-platform.cpp:189` lists that directory first.

**Fix sketch.** When no downstream path is set, try the directory of the module containing Slang
(`GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, ...)` plus `GetModuleFileNameW`) with
a full path before the search-order load, matching what the rpaths give Linux and macOS.
`IGlobalSession::setDownstreamCompilerPath` may serve as a per-compiler workaround today; not
tried.

**Measurement.** Windows 11, Slang 2026.14.1: a host started from another directory failed to load
`spirv-opt` with `SLANG_E_NOT_FOUND`; with the library preloaded by full path from the compiler's
directory, the same host optimised 2,048 bytes of SPIR-V to 1,664. Re-run on v2026.21 before filing
(the code above is v2026.21's).

## U11 — Two public functions have C++ linkage

**Observed.** `spReflection_GetSession` (`slang-deprecated.h:1084`) is declared after the
`extern "C"` block closes at line 1080, and `slang_getEmbeddedCoreModule` (`slang.h:6126`) is
declared `SLANG_API` without the `SLANG_EXTERN_C` its neighbours have. Both are exported with C++
mangling (`?spReflection_GetSession@@YAPEAUISession@slang@@PEAUSlangProgramLayout@@@Z` on Windows,
an Itanium name elsewhere), so a C caller or any foreign-function binding cannot reach them
portably: a binding by the MSVC name compiles everywhere and resolves only on Windows.

**Expected.** C linkage like every other `sp*` / `slang_*` function.

**Note.** Changing the linkage changes the exported name for existing C++ callers, which Slang's
contribution guide classes as a breaking change ("pr: breaking change"). Keeping a C++-linkage
forwarder beside a new C-linkage export avoids that.

## U12 — `ISession::loadModule*` returns a borrowed reference

**Observed.** `loadModule`, `loadModuleFromSource`, `loadModuleFromSourceString` and
`loadModuleFromIRBlob` return an `IModule*` that the caller does not own: the session keeps its
modules (`getLoadedModuleCount` / `getLoadedModule` enumerate them), and releasing the returned
pointer over-releases. Every other call that hands out an object does so through an out-parameter
that carries a reference. `slang.h:4640-4656` and `:4779-4785` say nothing about ownership.

**Effect.** A caller following the COM convention releases once too often. glibc aborts
(`free(): corrupted unsorted chunks`); the Windows heap tolerated the same corruption, so the defect
is invisible to a Windows-only host.

**Fix.** A sentence on each of the four declarations: the returned module is owned by the session;
call `addRef` to keep it beyond the session.

## U13 — The last session reference released from `~Linkage` destroys the global session inside it

**Observed.** `ISession` retains its `IGlobalSession`. An `IModule` does not retain its `ISession`,
and neither does a linked component type. Releasing the global session first, then the session,
module, entry point and finally the linked program (a refcount-correct order) destroys the Linkage
in the program's release; the Linkage holds the last reference to the global session and destroys
it from inside `~Linkage`, and a `WitnessTable` is freed twice between the two AST builders. macOS's
allocator reports it:

```
free_small                         "Incorrect checksum for freed object: probably modified after being freed"
Slang::WitnessTable::~WitnessTable()
Slang::Helper<Slang::InheritanceDecl>::destruct(void*)
Slang::ASTBuilder::~ASTBuilder()
Slang::RootASTBuilder::~RootASTBuilder()
Slang::ASTBuilder::~ASTBuilder()
Slang::Linkage::~Linkage()
non-virtual thunk to Slang::Linkage::release()
```

Windows, x86-64 Linux and aarch64 Linux ran the same order without a fault.

**Expected.** Either children retain their session (so the global session is never destroyed
inside a Linkage destructor), or the order requirement documented on `IGlobalSession`.

**Measurement.** v2026.14.1, osx-arm64 (macos-14 runner), refcounts read by `addRef`/`release`:
the session's count is 1 after `loadModule` and 1 after `link`. Not re-measured on v2026.21, where
the host this was found with holds the whole chain itself: reproduce without that before filing.

**Repro.** The shader is any; the order is the repro:

```cpp
// global, session, module, entry, program created as usual, then:
global->release(); session->release(); module->release(); entry->release();
program->release();   // macOS: double free inside ~Linkage
```

## U14 — `isBinaryModuleUpToDate` answers from the session's file cache

**Observed.** In the session that loaded a module from a file and serialized it,
`isBinaryModuleUpToDate(path, blob)` returns true after the source file has been rewritten with
different contents and a later timestamp, and returns true for a path where no file exists and never
did. A fresh session with the same search paths returns false for both. A malformed blob is false
in both.

**Expected.** The answer should not depend on whether this session loaded the module. The
declaration (`slang.h:4790-4801`) documents true when the primary source cannot be located, which
the fresh session does not do for the absent path, and documents nothing about the cache, which is
why the same-session answer for a changed file is true.

**Measurement.** v2026.21, all four platforms.

**Repro.** Load `mslib.slang` with `loadModule("mslib")`, `serialize()` it, rewrite the file, then
call `isBinaryModuleUpToDate` with the file's path and with a path to a file that does not exist,
first on the same session and then on a new one.

## U15 — Reflection queries that answer nothing, and facts reflection does not carry

Each measured on v2026.21; filed, if at all, as one issue listing them.

- `spReflectionTypeLayout_getSubObjectRangeOffset` returns a variable layout with no variable
  behind it (`spReflectionVariableLayout_GetVariable` is null), so a sub-object range has no name;
  the name is reachable only through `getBindingRangeLeafVariable` of the binding range it points at.
- `spReflectionTypeLayout_getSubObjectRangeSpaceOffset` returns 0 for a parameter block in set 1 and
  for one in set 2.
- `spReflectionType_getSpecializedTypeArgCount` returns 0 on every path tried, including a type
  written `Wrapper<Lambert>` in source and a type produced by specialization; the arguments are
  readable only from `GetFullName`.
- `spReflection_specializeType` on a generic struct found with `FindTypeByName` returns null with no
  diagnostics; specializing its generic container through `specializeGeneric` works.
- `spReflectionFunction_applySpecializations` returns a function whose parameters are still
  reported as `T`; applying the specialization to each parameter's variable and to the result type
  gives the substituted signature.
- An append and a consume structured buffer each get a counter buffer reported as a binding range
  named `counter`; with both in one shader the two are indistinguishable by name (the module names
  both payloads `elements`). The explicit-counter queries pair them.
- The global constant buffer binding is reported as 0 whether or not loose global uniforms exist.
- `[vk::push_constant]` leaves no trace in reflection: on SPIR-V the block is a push-constant range,
  on DXIL an ordinary constant buffer, and nothing reports that the attribute was there.

## U16 — `linkWithOptions` silently ignores most compiler options

**Observed.** `IComponentType::linkWithOptions` accepts the whole `CompilerOptionName` range.
Measured by diffing the emitted SPIR-V: `Optimization` (None 2,052 bytes, default 1,868, Maximal
1,668) and `FloatingPointMode` (Precise adds 36 bytes) take effect; `DebugInformation`,
`VulkanUseEntryPointName` (which works on the session or the target) and `Capability`
(`spirv_1_6` still emits SPIR-V 1.5) change nothing and report nothing.

**Expected.** A diagnostic for an option that has no effect at link time, or the list of link-time
options documented on `linkWithOptions`.

**Measurement.** v2026.14.1. Re-measure on v2026.21 before filing.

## U17 — SPIR-V for `SV_VertexID` differs between x86-64 and aarch64 builds

**Observed.** The same shader compiled with the same options by the x86-64 and the aarch64 release
builds gives different SPIR-V when it uses `SV_VertexID`, and identical SPIR-V otherwise. Windows
x86-64 and Linux x86-64 agree byte for byte; Linux aarch64 and macOS arm64 agree with each other. The
two modules have the same IDs and compute the same thing: the two builtin inputs `SV_VertexID` is
lowered to, `VertexIndex` and `BaseVertex`, are created in the opposite order, so which ID carries
which `BuiltIn` swaps, the pair swaps places in the `OpEntryPoint` interface list, and the operands of
the `OpISub` computing `VertexIndex - BaseVertex` swap with them.

**Expected.** Output that depends only on the input and options, so content hashes of compiled
shaders agree across a build farm of mixed architectures.

**Cause.** Not located. An iteration whose order depends on pointer values or on an
architecture-dependent hash would produce exactly this.

**Measurement.** v2026.21, three of 33 shaders in a corpus (every one that uses `SV_VertexID`, and
no other), on all four platforms, word by word.

**Repro.** A reduction, not yet confirmed to reproduce on its own (the three shaders that do are
larger; reduce and confirm before filing):

```hlsl
[shader("vertex")]
float4 vertexMain(uint id : SV_VertexID) : SV_Position { return float4(id, 0, 0, 1); }
```

Compile with `slangc -target spirv` on an x86-64 and an aarch64 machine and compare the bytes.
