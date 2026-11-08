# Upstream reports

Every Slang defect this repository has found, as report text in Slang's terms, with what was filed
where. Nothing here has been posted: filing is the repository owner's decision, made on the exact
text, and the policy for it is kept outside the repository. Measurements, the guards in the binding
and the tests that hold each entry are in `AGENTS.md` under Oddities and caveats (the "U" numbers
match).

Measured against Slang **v2026.21** (tag commit e73d2689) unless an entry says v2026.14.1. Line
numbers are v2026.21's. "All four platforms" means win-x64, linux-x64, linux-arm64 and osx-arm64 on
GitHub-hosted runners (windows-latest, ubuntu-24.04, ubuntu-24.04-arm, macos-15).

**The index below is the patch registry**, generated from `docs/upstream.json`, which is the single
source of truth (`pwsh tools/ci/patch-registry.ps1 -Write`; lint fails when they differ). Per defect:
the area, the kind (Slang defect, Slang documentation defect, not a defect), the patch in `patches/`
that carries the fix, the test that holds it, the state, and the Slang version it was last verified
against. The states: `patched` (our build carries the patch), `fixed upstream in vYYYY.RR` (no patch,
or the patch dropped), `filed: <URL>` and `upstream PR from fork: <URL>` (the patch still carried),
`not patchable: <reason>`, and `unpatched` (recorded, no patch yet). Every defect gets a patch, test
first (owner, 2026-11-08); the order and the procedure are in `AGENTS.md` under Patches. A patch
reaches only a payload built from source, which is what a release packs; Slang's own release
archive, which CI tests until the switch (`AGENTS.md`, Decisions), has none of them.

<!-- patch-registry:begin (generated from docs/upstream.json by tools/ci/patch-registry.ps1 -Write; do not edit) -->
| # | Defect | Area | Kind | Patch | Test | State | Upstream | Verified |
|---|---|---|---|---|---|---|---|---|
| U1 | `spReflectionType_GetElementCount` / `GetSpecializedElementCount` crash on an array sized by a link-time constant | reflection | Slang defect | `0002-element-count-of-a-specialization-sized-array.patch` | `ProberTests.The_element_count_of_a_specialization_sized_array_is_unknown` | patched |  | v2026.21 |
| U2 | `getBindingRangeCount` throws a C++ exception through `extern "C"` for a resource array sized by a link-time constant | reflection | Slang defect | `0003-binding-ranges-of-a-specialization-sized-array.patch` | `ProberTests.A_specialization_sized_resource_array_has_binding_ranges_of_unknown_count` | patched |  | v2026.21 |
| U3 | `spReflection_specializeGeneric` access-violates on a mismatched argument list | reflection | Slang defect | `0004-specialize-generic-mismatched-arguments.patch` | `ProberTests.A_mismatched_generic_argument_list_is_refused_with_a_diagnostic` | patched |  | v2026.21 |
| U4 | `spReflection_specializeType` on a non-generic type segfaults on Linux (an assert that removes its own check) | reflection | Slang defect | `0001-specialize-type-diagnose-argument-count.patch` | `ProberTests.Specializing_a_non_generic_type_is_refused_with_a_diagnostic` | patched |  | v2026.21 |
| U5 | A constant buffer whose block holds only link-time-sized arrays gets no binding | reflection | Slang defect | `0016-specialization-sized-constant-buffer-binding.patch` | `UnknownSizeTests.A_buffer_with_no_fixed_size_keeps_its_binding` | patched |  | v2026.21 |
| U6 | A 64-bit 3/4-component vertex input is given one location; Vulkan requires two | SPIR-V | Slang defect | `0014-wide-vertex-input-two-locations.patch` | `NriValidationTests.A_wide_64_bit_vertex_input_is_given_two_locations` | patched | [#13427](https://github.com/shader-slang/slang/issues/13427) | v2026.21 |
| U7 | `sample` interpolation without `SampleRateShading` (reported by others as #12441) | SPIR-V | Slang defect |  | `ModuleAgreementTests.The_sample_interpolation_modifier_declares_SampleRateShading` | fixed upstream in v2026.21 |  | v2026.21 |
| U8 | `float4x4` vertex input with a member `Offset` decoration | SPIR-V | Slang defect |  | `ModuleAgreementTests.A_matrix_vertex_input_carries_no_member_Offset` | fixed upstream in v2026.21 |  | v2026.21 |
| U9 | A missing `slang-glslang` is an error diagnostic on a successful result, reported once per global session | diagnostics | Slang defect | `0017-warn-on-each-unoptimized-spirv.patch` | `ProberTests.Without_the_optimiser_every_compile_warns_and_none_reports_an_error` | patched |  | v2026.21 |
| U10 | Windows: downstream libraries are not looked up in the compiler's own directory | loading | Slang defect | `0015-windows-load-beside-the-compiler.patch` | `ProberTests.Slang_finds_its_companions_beside_itself_without_the_bindings_preload` | patched |  | v2026.21 |
| U11 | Two public functions have C++ linkage | API header | Slang defect |  | `NativeExportTests.No_entry_point_is_bound_by_a_C_plus_plus_mangling` | not patchable: the fix gives both functions C linkage, which renames their exported symbols and breaks every C++ binary linked against them; keeping the old mangled export beside a C one needs a second declaration of the same name with another linkage, which C++ does not allow in one program without linker aliasing |  | v2026.21 |
| U12 | `ISession::loadModule*` returns a borrowed reference; the header does not say so | API header | Slang documentation defect | `0007-document-loaded-module-ownership.patch` | `HeaderDocumentationTests.The_header_says_a_loaded_module_is_owned_by_the_session` | patched |  | v2026.21 |
| U13 | Releasing a session's last reference from `~Linkage` destroys the global session inside it | lifetime | Slang defect |  | `LifetimeTests.The_global_session_is_never_destroyed_from_inside_the_linkage` | known issue: macOS only and last measured on v2026.14.1; re-measuring needs a Mac; left to Slang (owner, 2026-11-08) |  | v2026.14.1 |
| U14 | `isBinaryModuleUpToDate` answers from the session's file cache | modules | Slang defect | `0006-binary-module-up-to-date-reads-afresh.patch` | `ModuleSerializationTests.The_staleness_check_reads_the_files_afresh_in_the_session_that_loaded_the_module` | patched |  | v2026.21 |
| U15 | Reflection queries that answer nothing, and facts reflection does not carry | reflection | Slang defect |  | `SecondOpinionTests.The_calls_that_report_nothing_are_pinned_reporting_nothing` | unpatched |  | v2026.21 |
| U16 | `linkWithOptions` silently ignores most compiler options | linking | Slang defect | `0018-link-time-options.patch` | `LinkOptionsTests.A_link_time_option_takes_effect_or_says_it_cannot` | patched |  | v2026.21 |
| U17 | SPIR-V for `SV_VertexID` differs between x86-64 and aarch64 builds | SPIR-V | Slang defect |  | `SpirvDigestTests.The_corpus_compiles_deterministically_and_records_a_digest_for_every_shader` | unpatched |  | v2026.21 |
| U18 | GLSL: a hull shader's patch-constant function returns its struct with a bare `return;` | GLSL | Slang defect | `0009-glsl-patch-constant-function-void.patch` | `TargetValidationTests.Glsl_a_hull_shaders_patch_constant_function_compiles` | patched | [#14335](https://github.com/shader-slang/slang/issues/14335) | v2026.21 |
| U19 | GLSL: a domain shader's per-vertex input is sized by the patch, which GLSL forbids | GLSL | Slang defect | `0010-glsl-domain-input-unsized.patch` | `TargetValidationTests.Glsl_a_domain_shaders_patch_input_compiles` | patched | [#14335](https://github.com/shader-slang/slang/issues/14335) | v2026.21 |
| U20 | WGSL: a `const` is initialised from an `override` | WGSL | Slang defect | `0012-wgsl-override-not-const.patch` | `TargetValidationTests.Wgsl_a_value_computed_from_a_specialization_constant_is_not_const` | patched | [#14285](https://github.com/shader-slang/slang/issues/14285) | v2026.21 |
| U21 | WGSL: a matrix vertex input is written at one `@location` | WGSL | Slang defect |  | `TargetValidationTests.Wgsl_passes_naga` | known issue: the fix splits a matrix vertex input into one location per column and reassembles it, a varying-legalization feature (the TODO at slang-ir-legalize-varying-params.cpp:880); left to Slang (owner, 2026-11-08) | [#13736](https://github.com/shader-slang/slang/issues/13736) | v2026.21 |
| U22 | WGSL: arrays of textures and samplers are written as `binding_array` with no `enable` | WGSL | Slang defect |  | `TargetValidationTests.Wgsl_passes_naga` | not patchable: binding_array is not in the WGSL specification; naga takes it behind enable wgpu_binding_array and Tint behind requires sized_binding_array, so no one text is valid for both, and choosing a WGSL dialect is a Slang design decision (Slang #13914 proposes one) | [#13708](https://github.com/shader-slang/slang/issues/13708), [#13914](https://github.com/shader-slang/slang/issues/13914) | v2026.21 |
| U23 | An internal error where a diagnostic belongs: double on WGSL and Metal, varyings on CPU and CUDA | diagnostics | Slang defect | `0013-diagnose-unsupported-double-and-varyings.patch` | `TargetValidationTests.A_construct_the_target_cannot_express_is_diagnosed_not_an_internal_error` | patched | [#10232](https://github.com/shader-slang/slang/issues/10232) | v2026.21 |
| U24 | C++: the prelude has no vector arithmetic or comparison for `half` | C++ | Slang defect | `0011-cpp-prelude-half-vector-ops.patch` | `TargetValidationTests.Cpp_half_vector_arithmetic_and_comparison_compile` | patched | [#14037](https://github.com/shader-slang/slang/issues/14037), [#14194](https://github.com/shader-slang/slang/issues/14194) | v2026.21 |
| U25 | WGSL: 64-bit integers are written as `u64`, which WGSL does not have | WGSL | Slang defect |  | `TargetValidationTests.Slang_compute_corpus_wgsl_passes_naga` | not patchable: WGSL has no 64-bit integer; diagnosing u64 breaks the naga users Slang emits it for, and keeping it breaks Tint, so the fix is the WGSL dialect choice Slang #13914 proposes, which is the maintainers' design decision | [#13914](https://github.com/shader-slang/slang/issues/13914) | v2026.21 |
| U26 | Metal: a texture read is called with an overload Metal does not declare | Metal | Slang defect |  | `TargetValidationTests.Metal_compiles_to_air` | known issue: a Metal texture read overload; measured only on the macos-15 runner, the fix needs Metal-side investigation; left to Slang (owner, 2026-11-08) |  | v2026.21 |
| U27 | Metal: an unbounded resource array is written as a flexible array member | Metal | Slang defect |  | `TargetValidationTests.Metal_compiles_to_air` | known issue: unbounded resource arrays need Metal argument buffers, a feature (Slang #11970); left to Slang (owner, 2026-11-08) | [#11970](https://github.com/shader-slang/slang/issues/11970) | v2026.21 |
| U28 | Metal: a matrix vertex input at one `[[attribute]]` | Metal | Slang defect |  | `TargetValidationTests.Metal_compiles_to_air` | known issue: the same matrix-varying feature as U21, for Metal; left to Slang (owner, 2026-11-08) |  | v2026.21 |
| U29 | Metal: an array of device pointers as a kernel input | Metal | Slang defect |  | `TargetValidationTests.Slang_compute_corpus_metal_compiles_to_air` | known issue: device-pointer arrays as kernel inputs need an argument-buffer lowering, a feature; left to Slang (owner, 2026-11-08) |  | v2026.21 |
| U30 | A component type linked a second time ignores the second link's options | linking | Slang defect | `0008-link-with-options-owns-its-options.patch` | `CompositeTests.A_composite_linked_a_second_time_emits_what_its_second_links_options_say` | patched |  | v2026.21 |
| U31 | A second module loaded from source at an already-used path ends in an internal assert | diagnostics | Slang defect | `0005-module-path-already-loaded.patch` | `CompilationTests.A_second_module_from_source_at_the_same_path_is_diagnosed_naming_the_path` | patched |  | v2026.21 |
| U32 | C++: an append or consume structured buffer ends in an internal error | C++ output | Slang defect |  |  | unpatched |  | v2026.21 |
| U33 | A top-level parameter block's sub-object range reports space offset 0 | reflection | Slang defect | `0019-parameter-block-space-offset.patch` | `SecondOpinionTests.A_top_level_parameter_blocks_sub_object_range_reports_its_space` | patched |  | v2026.21 |
<!-- patch-registry:end -->

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

**Cause.** `spReflectionType_GetSpecializedElementCount` (`source/slang/slang-reflection-api.cpp`)
ends with

```cpp
return isUnsized                       ? 0
       : elementCount->isLinkTimeVal() ? SLANG_UNKNOWN_SIZE
                                       : (size_t)getIntVal(elementCount);   // 653-655
```

The length of an array sized by a specialization constant is a `DeclRefIntVal` that is not a
link-time value (`DeclRefIntVal::_isLinkTimeValOverride` checks for `extern`), and `tryFoldIntVal`
does not fold it, so `getIntVal` is reached; `getIntVal` of anything but a `ConstantIntVal` is
`SLANG_UNEXPECTED` (`source/slang/slang-syntax.cpp:898-905`), and the `Slang::InternalError` leaves
the `extern "C"` function. The size and offset paths were fixed by the #9045 work and this one was
not.

**Fix.** Return the value of a `ConstantIntVal` and `SLANG_UNKNOWN_SIZE` for any other length, the
answer a link-time constant already gets. Every count returned before is returned unchanged; only
the lengths that threw now report `SLANG_UNKNOWN_SIZE`. Regression test:
`tools/slang-unit-test/unit-test-specialization-sized-array-reflection.cpp`. Carried as
`patches/0002-element-count-of-a-specialization-sized-array.patch`.

**Measurement.** v2026.21, all four platforms: the call ends the process (non-zero exit; the type
reports `SLANG_TYPE_KIND_ARRAY` before the call); on win-x64 the exception surfaces as a structured
exception. With the fix, both calls return `0xFFFFFFFFFFFFFFFE` (win-x64 with MSVC, linux-x64 with
GCC 13.3, the tag built from source).

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

**Cause.** `ExtendedTypeLayoutContext::addRangesRec` (`source/slang/slang-reflection-api.cpp`),
which builds the binding ranges every binding-range query reports, multiplies an array element's
ranges by the array's length:

```cpp
if (arrayType->isUnsized())
    elementCount = LayoutSize::infinite();
else if (arrayType->getElementCount()->isLinkTimeVal())
    elementCount = LayoutSize::invalid();
else
    elementCount = LayoutSize::RawValue(getIntVal(arrayType->getElementCount()));   // 2146
```

A length that is a specialization constant is neither a link-time value nor a `ConstantIntVal`, so
`getIntVal` raises `SLANG_UNEXPECTED` (`source/slang/slang-syntax.cpp:898-905`), as in U1. The
array's own layout already treats every length that is not a `ConstantIntVal` as unknown
(`GetElementCount`, `source/slang/slang-type-layout.cpp:3085-3128`).

**Fix.** Take the length of a `ConstantIntVal` and `LayoutSize::invalid()` for any other, as the
link-time case already did, so the binding range is reported with a binding count of
`SLANG_UNKNOWN_SIZE`. A constant length, an unsized array and a link-time length give the ranges
they gave before. Regression test:
`tools/slang-unit-test/unit-test-specialization-sized-resource-array-reflection.cpp`. Carried as
`patches/0003-binding-ranges-of-a-specialization-sized-array.patch`.

**Measurement.** v2026.21, all four platforms, through a child process. With the fix: two binding
ranges, the texture array's binding count `SLANG_UNKNOWN_SIZE` and the sampler's 1 (win-x64 with
MSVC, linux-x64 with GCC 13.3, the tag built from source).

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

**Cause.** `Linkage::specializeGeneric` (`source/slang/slang-session.cpp`) takes the declaration
out of the checked application unconditionally:

```cpp
auto specializedDeclRef =
    as<DeclRefExpr>(visitor.checkGenericAppWithCheckedArgs(genericAppExpr))->declRef;   // 808-809
```

On a mismatched argument list, `checkGenericAppWithCheckedArgs` diagnoses it and returns an error
expression, the `GenericAppExpr` itself (`CreateErrorExpr` at the error label of
`CompleteOverloadCandidate`, `source/slang/slang-check-overload.cpp`). `as<DeclRefExpr>` of that is
null and the member access dereferences it: undefined behaviour, which is why macOS returns a
non-null generic where the other platforms access-violate.

**Fix.** Check the cast and return a null `DeclRef`, which `spReflection_specializeGeneric` returns
as null beside the diagnostics the check already produced. A matching argument list takes the same
path as before. Regression test: `tools/slang-unit-test/unit-test-specialize-generic-mismatch.cpp`.
Carried as `patches/0004-specialize-generic-mismatched-arguments.patch`.

With the fix: null with `expected a type` for the integer argument and `too many arguments` for the
two type arguments (win-x64 with MSVC, linux-x64 with GCC 13.3, the tag built from source).

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

**Fix.** Remove the assert and report the mismatch with the existing
`mismatch-specialization-arguments` diagnostic (38025) before returning null, so every build returns
null and says why. A call with a matching count is unchanged. Regression test: a second case in
`tools/slang-unit-test/unit-test-specialize-type.cpp`, through `ISession::specializeType` and
`ProgramLayout::specializeType`. Carried as `patches/0001-specialize-type-diagnose-argument-count.patch`.

**Measurement.** v2026.21, all four platforms, through a child process; the GCC 13.3 build of the
tag measured the same way on linux-x64. With the fix: null and `wrong number of specialization
arguments` (win-x64 with MSVC, linux-x64 with GCC 13.3, the tag built from source).

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

## U18 — GLSL: a hull shader's patch-constant function returns its struct with a bare `return;`

**Observed.** For a hull shader with `[patchconstantfunc("teConstants")]`, where `teConstants`
returns a struct of `SV_TessFactor` and `SV_InsideTessFactor`, the GLSL target emits the function
as `TePatch_0 teConstants_0(TeControl_0 patch_0[3])`, writes the tessellation levels to
`gl_TessLevelOuter` / `gl_TessLevelInner` inside it, and ends it with `return;`. glslang 16.6.0
(`glslangValidator -V --target-env vulkan1.2 -S tesc`): "ERROR: 0:10: 'return' : non-void function
must return a value". The SPIR-V target compiles the same shader to a module spirv-val accepts.

**Expected.** A `void` function, or a `return` of the struct.

**Measurement.** v2026.21, Windows x64 (the GLSL target's text does not depend on the host).

**Repro.**

```hlsl
struct TeControl { float3 tePosition : POSITION; };
struct TePatch   { float teEdges[3] : SV_TessFactor; float teInner : SV_InsideTessFactor; };

TePatch teConstants(InputPatch<TeControl, 3> patch)
{
    TePatch c;
    c.teEdges[0] = c.teEdges[1] = c.teEdges[2] = 1;
    c.teInner = 1;
    return c;
}

[shader("hull")] [domain("tri")] [partitioning("integer")] [outputtopology("triangle_cw")]
[outputcontrolpoints(3)] [patchconstantfunc("teConstants")]
TeControl teHull(InputPatch<TeControl, 3> patch, uint i : SV_OutputControlPointID) { return patch[i]; }
```

`slangc -target glsl -entry teHull -stage hull`, then `glslangValidator -V --target-env vulkan1.2 -S tesc`.

## U19 — GLSL: a domain shader's per-vertex input is sized by the patch, which GLSL forbids

**Observed.** For a domain shader taking `const OutputPatch<TeControl, 3> patch`, the GLSL target
declares `layout(location = 0) in vec3 patch_tePosition_0[3];`. glslang 16.6.0 (`-S tese`):
"ERROR: 0:2: '[]' : tessellation input array size must be gl_MaxPatchVertices or implicitly sized".
The SPIR-V target's module for the same shader passes spirv-val.

**Expected.** `in vec3 patch_tePosition_0[];` (implicitly sized), as GLSL 4.50 requires for a
tessellation evaluation input.

**Measurement.** v2026.21, Windows x64.

**Repro.** U18's `TeControl`, plus:

```hlsl
struct TeOut { float4 teClip : SV_Position; };

[shader("domain")] [domain("tri")]
TeOut teDomain(TePatch c, float3 bary : SV_DomainLocation, const OutputPatch<TeControl, 3> patch)
{ TeOut o; o.teClip = float4(patch[0].tePosition * bary.x, 1); return o; }
```

`slangc -target glsl -entry teDomain -stage domain`, then `glslangValidator -V --target-env vulkan1.2 -S tese`.

## U20 — WGSL: a `const` is initialised from an `override`

**Observed.** A specialization constant used in an expression is emitted as an `override` and the
expression as a `const`: `@id(0) override scCascades_0 : i32 = i32(3);` then, in the function,
`const _S1 : f32 = f32(scCascades_0);`. A `const` declaration takes a const-expression, and an
override is not one. naga 30.0.1: "Unexpected override-expression".

**Expected.** `let _S1 : f32 = f32(scCascades_0);` (or an `override` declaration).

**Measurement.** v2026.21, Windows x64.

**Repro.**

```hlsl
[vk::constant_id(0)] const int   scCascades = 3;
[vk::constant_id(9)] const float scBias     = 0.005;
[SpecializationConstant]  const bool scDebug = false;

[shader("fragment")]
float4 scFragment() : SV_Target { return float4(scCascades, scBias, scDebug ? 1 : 0, 1); }
```

`slangc -target wgsl -entry scFragment -stage fragment`, then `naga <file>.wgsl`.

## U21 — WGSL: a matrix vertex input is written at one `@location`

**Observed.** A `float4x4` vertex input becomes `@location(1) viInstance_0 : mat4x4<f32>` in the
vertex input struct, with the next input at `@location(2)`. WGSL's user-defined inputs and outputs
are numeric scalars and vectors only. naga 30.0.1: "The type [4] cannot be used for user-defined
entry point inputs or outputs. Only numeric scalars and vectors are allowed."

**Expected.** Four `vec4<f32>` inputs at four consecutive locations, reassembled in the body, or a
diagnostic.

**Measurement.** v2026.21, Windows x64.

**Repro.**

```hlsl
struct ViIn { float3 viPosition : POSITION; float4x4 viInstance : INSTANCE; float2 viTexCoord : TEXCOORD0; };

[shader("vertex")]
float4 viVertex(ViIn v) : SV_Position
{ return mul(v.viInstance, float4(v.viPosition, 1)) + float4(v.viTexCoord, 0, 0); }
```

`slangc -target wgsl -entry viVertex -stage vertex`, then `naga <file>.wgsl`. Reduced from the
measured shader (which also takes a `uint4` and `SV_VertexID`); confirm the reduction before filing.

## U22 — WGSL: arrays of textures and samplers are written as `binding_array` with no `enable`

**Observed.** `Texture2D daFixed[4]` becomes
`@binding(0) @group(0) var daFixed_0 : binding_array<texture_2d<f32>, i32(4)>;` (and an unbounded
array `binding_array<texture_2d<f32>>`; a `SamplerState` array the same), with no `enable` directive
in the module. naga 30.0.1: "the `wgpu_binding_array` enable extension is not enabled".

**Expected.** To be confirmed before filing: `binding_array` is naga's (wgpu's) extension, taken
behind `enable wgpu_binding_array;`; whether the WGSL specification or Tint defines it was not
checked here. If neither does, the expected behaviour is a diagnostic, as Slang gives for an array
of buffers (`E56105`, "array of resource types not supported in WGSL").

**Measurement.** v2026.21, Windows x64, naga 30.0.1 only.

**Repro.**

```hlsl
Texture2D    daFixed[4];
SamplerState daSampler;

[shader("fragment")]
float4 daFragment(float2 uv : TEXCOORD0) : SV_Target { return daFixed[1].Sample(daSampler, uv); }
```

`slangc -target wgsl -entry daFragment -stage fragment`, then `naga <file>.wgsl`. Reduced from the
measured shader (which also declares an unbounded array); confirm the reduction before filing.

## U23 — An internal error where a diagnostic belongs

**Observed.** Each of these is a construct the target cannot express, and each ends compilation
with an internal error rather than a diagnostic naming the construct and the target:

- WGSL and Metal, a `double`: measured with a `double4` vertex input, `double` members of a
  constant buffer, and Slang's own `tests/compute/bitcast-64bit.slang`:
  `error[E99997]: Slang compilation aborted due to an exception of class Slang::InternalError
  unexpected: 'double' type emitted`.
- CPU (C++ source) and CUDA, a fragment or vertex entry point:
  `internal error[E99999]: unimplemented feature in Slang compiler: this target doesn't support this
  system-defined varying parameter` (or "user-defined"). The same targets report a hull, geometry or
  mesh stage properly, as `E36107` "uses features that are not available in ... stage".
- C++ source, an `AppendStructuredBuffer` / `ConsumeStructuredBuffer`:
  `internal error[E99999]: unimplemented feature in Slang compiler: unexpected IR opcode during code
  emit`.

**Expected.** A diagnostic in the family the targets already use for what they lack (`E36107`,
`E55202`, `E56105`), naming the construct.

**Measurement.** v2026.21, Windows x64, from `getEntryPointCode`; the messages are Slang's.

**Repro.** For WGSL and Metal:

```hlsl
struct WsBlock { float wsLead; double wsWide; };
ConstantBuffer<WsBlock> wsBuffer;

[shader("fragment")]
float4 wsFragment() : SV_Target { return float4((float)wsBuffer.wsWide, wsBuffer.wsLead, 0, 1); }
```

`slangc -target wgsl -entry wsFragment -stage fragment` (or `-target metal`); this reduction is of
a measured shader and is not itself confirmed (confirm before filing). For C++: any fragment shader
of the corpus with `-target cpp`.

## U24 — C++: the prelude has no vector arithmetic or comparison for `half`

**Observed.** Slang's C++ output for half vectors (`Vector<half, 3> + Vector<half, 3>`,
`Vector<half, 2> < Vector<half, 2>`) does not compile against Slang's own prelude: clang 22.1.8:
"invalid operands to binary expression ('Vector<half, 3>' (aka 'Vector<_Float16, 3>') and
'Vector<half, 3>')". `slang-cpp-types-core.h` instantiates `SLANG_FLOAT_VECTOR_OPS` for `float` and
`double` (lines 371-372 at v2026.21) and not for `half`.

**Expected.** `SLANG_FLOAT_VECTOR_OPS(half)`, or a diagnostic for half vectors on the C++ target.

**Measurement.** v2026.21, Windows x64, `clang++ -std=c++17 -fsyntax-only` with the prelude
directory at the tag on the include path. Slang's own `tests/compute/half-vector-calc.slang` and
`half-vector-compare.slang` show it.

**Repro.** `slangc tests/compute/half-vector-calc.slang -target cpp -entry computeMain -stage compute`,
then `clang++ -std=c++17 -fsyntax-only -I prelude`.

## U25 — WGSL: 64-bit integers are written as `u64`, which WGSL does not have

**Observed.** Slang's own `tests/compute/byte-address-buffer-64bit.slang` compiles to WGSL using
`u64` (`var _S2 : u32 = u32((tmp64_0 >> (u64(32))));`). WGSL defines no 64-bit integer type. naga
30.0.1 rejects the module, on the conversion: "automatic conversions cannot convert elements of
`u64` to `u32`".

**Expected.** A diagnostic, as for `double` (once U23 is fixed), or a lowering to `vec2<u32>`.

**Measurement.** v2026.21, Windows x64, naga 30.0.1.

**Repro.** `slangc tests/compute/byte-address-buffer-64bit.slang -target wgsl -entry computeMain
-stage compute`, then `naga <file>.wgsl`.

## U26 — Metal: a texture read is called with an overload Metal does not declare

**Observed.** The corpus entry `descriptor-kinds/dkFragment` compiles to Metal, and Apple's
compiler refuses it: "descriptor-kinds.slang:42:434: error: no matching member function for call
to 'read'".

**Expected.** Metal that compiles, or a diagnostic naming the resource kind Metal cannot read that
way.

**Measurement.** v2026.21, `xcrun metal` on the GitHub-hosted macos-15 runner, run 42475073038
(2026-11-08). Reduced repro not taken yet: confirm the resource kind before filing.

## U27 — Metal: an unbounded resource array is written as a flexible array member

**Observed.** `descriptor-arrays/daFragment` and `buffer-arrays/baFragment` declare unbounded
resource arrays. Slang's Metal output declares them as flexible array members, and Apple's compiler
refuses both: "flexible array members are a C99 feature".

**Expected.** An argument-buffer or bounded form Metal accepts, or a diagnostic.

**Measurement.** As U26.

## U28 — Metal: a matrix vertex input at one `[[attribute]]`

**Observed.** `vertex-interface/viVertex` takes a `float4x4` vertex input. Slang writes it as one
`[[attribute(n)]]` of type `matrix<float, 4, 4>`, which Apple's compiler refuses: "type
'matrix<float, int(4), int(4)>' is not valid for attribute 'attribute'". The same input is U21 on
WGSL.

**Expected.** One attribute per column, as the SPIR-V target writes four locations.

**Measurement.** As U26.

## U29 — Metal: an array of device pointers as a kernel input

**Observed.** Slang's own `tests/compute/loop-unroll.slang` compiles to a kernel whose parameter is
`array<device int *, 3>`, which Apple's compiler refuses: "invalid type 'array<device int *,
int(3)>' for input declaration in a kernel function".

**Expected.** The buffers passed as separate kernel parameters or through an argument buffer.

**Measurement.** As U26.

**Not a defect, recorded here so it is not re-investigated:** `tests/compute/rw-texture-simple.slang`
declares more than eight read_write textures, and Apple's compiler refuses it with "number of
textures with read_write access exceeds maximum supported (8)". That is Metal's limit, not Slang's
output.

## U30 — A component type linked a second time ignores the second link's options

**Observed.** `IComponentType::linkWithOptions` called twice on one composite (from
`ISession::createCompositeComponentType`) returns, the second time, a program whose code is the
first link's: `SLANG_OPTIMIZATION_LEVEL_NONE` first and `MAXIMAL` second both emit the same
1,572-byte SPIR-V, where a fresh composite linked at `MAXIMAL` emits 1,188 bytes. No diagnostic.

**Expected.** Each link applies its own options, or the second call is refused.

**Measurement.** v2026.21, Windows (the workstation), `CompositeTests.Linking_with_options_one_call_at_a_time_emits_what_Link_emits`,
which asserts the wrong answer so a fix fails it. Filed: no.

## U31 — A second module loaded from source at an already-used path ends in an internal assert

**Observed.** In one `ISession`, `loadModuleFromSourceString("first", "shared.slang", src)` succeeds
and `loadModuleFromSourceString("second", "shared.slang", src)` returns null with the diagnostic text
`assert failure: slang-dictionary.h(325): The key already exists in Dictionary.` and no diagnostic
code. `Linkage::loadParsedModule` adds the module to `mapPathToLoadedModule` under the path's
unique identity with `Dictionary::add` (`source/slang/slang-session.cpp:1255`), which asserts on a
key that is already there (`source/core/slang-dictionary.h:322-325`). With another path the second
module loads.

**Expected.** A diagnostic naming the path the session already holds a module for, or both modules
loaded.

**Cause.** `Linkage::loadModuleFromBlob` (`source/slang/slang-session.cpp`) checks only the module's
name against `mapNameToLoadedModules`. A new name at a used path reaches `Linkage::loadParsedModule`,
whose `mapPathToLoadedModule.add` (1261) asserts on the existing key.

**Fix.** Check the path before loading, and diagnose a new error, E38208
`module-path-already-loaded`, naming the path and the module (`patches/0005-module-path-already-loaded.patch`,
with a `SLANG_UNIT_TEST`).

**Measurement.** v2026.21, win-x64 (the workstation): before, null with the assert text and no code
(`CompilationTests.A_second_module_from_source_at_the_same_path_ends_in_an_internal_assert`, on the release
payload); after, null with E38208 naming `shared.slang`, and a module at another path still loads
(`CompilationTests.A_second_module_from_source_at_the_same_path_is_diagnosed_naming_the_path`). Filed: no.
