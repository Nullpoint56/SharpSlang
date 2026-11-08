// Every corpus shader, compiled to every target, judged by that target's own tool.
//
// The device tests hold SPIR-V and DXIL to a driver (PixelTests, NriDeviceTests). The other
// targets have no device here, so each is held to the strongest outside oracle it has
// (AGENTS.md#tests-and-skips): a validator where the target has one, a compiler where the output is
// source text. SPIR-V and DXIL are validated here too, because a validator reads every instruction
// and a device only the paths a test happens to take.
//
// Two corpora: ShaderCorpus, a theory row per case, and Slang's own tests/compute, one fact per
// target. Each entry point is compiled alone for the target and what happened is compared with what
// is declared for it below. Undeclared means "Slang emits it and the tool accepts it". Declared is
// exact and checked both ways, as the rest of the suite's lists are: an outcome that stops happening
// fails, so a Slang or tool fix shows up as a failing test naming the line to delete.
//
//   SlangRefuses       Slang does not compile it for this target, with these words.
//   ValidatorRejects   Slang emitted it and the target's tool refused it, with these words.
//
// Each declaration says why. Where the reason is a Slang defect it names the report in
// docs/upstream.md (U18 and on); where it is the construct having no meaning on the target, or the
// tool lacking something the target defines, it says that instead.
//
// Slang's refusals are a property of Slang alone, so they are also checked with no tool at all
// (Every_targets_refusals_are_the_declared_ones), on every leg: Metal's are measured off macOS that
// way, and only xcrun's verdicts wait for a Mac.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public sealed class TargetValidationTests(ITestOutputHelper output)
{
    public enum Outcome { Valid, SlangRefuses, ValidatorRejects }

    /// <summary>What a declared entry expects: the outcome, words its message must contain, and why.</summary>
    /// <summary>A declared outcome. <paramref name="FixedBy"/> names the defect whose patch makes the row
    /// <see cref="Outcome.Valid"/> on a payload built with that patch, or, with <paramref name="FixedWords"/>, the same outcome in those words (AGENTS.md#patches).</summary>
    public sealed record Expected(Outcome Outcome, string Words, string Reason, string? FixedBy = null, string? FixedWords = null)
    {
        /// <summary>What the staged payload should produce: <see cref="Outcome.Valid"/> where it carries the fix.</summary>
        public Expected ForStagedPayload() =>
            FixedBy is not null && PatchRegistry.PatchFor(FixedBy) is { } patch && StagedPayload.Carries(patch)
                ? (FixedWords is null ? new Expected(Outcome.Valid, "", $"patched ({FixedBy})") : new Expected(Outcome, FixedWords, $"patched ({FixedBy})"))
                : this;
    }

    /// <summary>One entry point's code for one target, or Slang's reason for not producing it.</summary>
    private sealed record Emitted(string Key, SlangStage Stage, byte[]? Code, string? Refusal);

    /// <summary>
    /// spirv-val options a module needs because of a device feature it requires, which a Vulkan
    /// environment alone does not grant.
    /// </summary>
    private static readonly Dictionary<string, string[]> SpirvValFlags = new(StringComparer.Ordinal)
    {
        // A specialization-constant workgroup size is emitted as LocalSizeId, which needs
        // maintenance4 on the device (AGENTS.md#layout-and-the-emitted-module); spirv-val's own
        // message names this flag for that feature.
        ["compute-specialized/csCompute"] = ["--allow-localsizeid"],
    };

    /// <summary>A target: how Slang is asked for it, and how its tool judges one emitted entry.</summary>
    private sealed record Target(string Name, Validator Validator, Func<TargetOptions> Options, Func<Emitted, Verdict> Judge);

    private static readonly Target[] Targets =
    [
        new("spirv", Validator.SpirvVal, () => new TargetOptions(),
            e => TargetValidators.SpirvVal(e.Key, e.Code!, SpirvValFlags.GetValueOrDefault(e.Key) ?? [])),
        new("dxil", Validator.DxilValidator, () => NriDeviceTests.Dxil,
            e => TargetValidators.Dxc.Validate(e.Code!)),
        new("glsl", Validator.GlslangValidator, () => new TargetOptions { Format = SlangCompileTarget.SLANG_GLSL },
            e => TargetValidators.Glslang(e.Key, e.Stage, e.Code!)),
        new("hlsl", Validator.DxcHlsl, () => new TargetOptions { Format = SlangCompileTarget.SLANG_HLSL, Profile = "sm_6_5" },
            e => TargetValidators.Dxc.Compile(e.Code!, DxcArguments(e))),
        new("metal", Validator.XcrunMetal, () => new TargetOptions { Format = SlangCompileTarget.SLANG_METAL },
            e => TargetValidators.Metal(e.Key, e.Code!)),
        new("wgsl", Validator.Naga, () => new TargetOptions { Format = SlangCompileTarget.SLANG_WGSL },
            e => TargetValidators.Naga(e.Key, e.Code!)),
        new("cuda", Validator.Nvrtc, () => new TargetOptions { Format = SlangCompileTarget.SLANG_CUDA_SOURCE },
            e => TargetValidators.Nvrtc.Compile(e.Key, e.Code!, SlangPrelude.Directory)),
        new("cpp", Validator.HostCxx, () => new TargetOptions { Format = SlangCompileTarget.SLANG_CPP_SOURCE },
            e => TargetValidators.Cxx(e.Key, e.Code!, SlangPrelude.Directory)),
    ];

    private static Target TargetNamed(string name) => Targets.Single(t => t.Name == name);

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    // ---------------------------------------------------------------- ShaderCorpus, per target

    [ValidatorTheory(Validator.SpirvVal)]
    [MemberData(nameof(Cases))]
    public void Spirv_passes_spirv_val(string name) => CheckCase("spirv", name);

    [ValidatorTheory(Validator.DxilValidator)]
    [MemberData(nameof(Cases))]
    public void Dxil_passes_the_dxil_dll_validator(string name) => CheckCase("dxil", name);

    [ValidatorTheory(Validator.GlslangValidator)]
    [MemberData(nameof(Cases))]
    public void Glsl_compiles_with_glslang(string name) => CheckCase("glsl", name);

    [ValidatorTheory(Validator.DxcHlsl)]
    [MemberData(nameof(Cases))]
    public void Hlsl_compiles_with_dxc(string name) => CheckCase("hlsl", name);

    [ValidatorTheory(Validator.XcrunMetal)]
    [MemberData(nameof(Cases))]
    public void Metal_compiles_to_air(string name) => CheckCase("metal", name);

    [ValidatorTheory(Validator.Naga)]
    [MemberData(nameof(Cases))]
    public void Wgsl_passes_naga(string name) => CheckCase("wgsl", name);

    [ValidatorTheory(Validator.Nvrtc)]
    [MemberData(nameof(Cases))]
    public void Cuda_compiles_with_nvrtc(string name) => CheckCase("cuda", name);

    [ValidatorTheory(Validator.HostCxx)]
    [MemberData(nameof(Cases))]
    public void Cpp_compiles_with_the_host_compiler(string name) => CheckCase("cpp", name);

    // ---------------------------------------------------------------- Slang's tests/compute, per target

    [ValidatorFact(Validator.SpirvVal)]
    public void Slang_compute_corpus_spirv_passes_spirv_val() => CheckCompute("spirv");

    [ValidatorFact(Validator.DxilValidator)]
    public void Slang_compute_corpus_dxil_passes_the_dxil_dll_validator() => CheckCompute("dxil");

    [ValidatorFact(Validator.GlslangValidator)]
    public void Slang_compute_corpus_glsl_compiles_with_glslang() => CheckCompute("glsl");

    [ValidatorFact(Validator.DxcHlsl)]
    public void Slang_compute_corpus_hlsl_compiles_with_dxc() => CheckCompute("hlsl");

    [ValidatorFact(Validator.XcrunMetal)]
    public void Slang_compute_corpus_metal_compiles_to_air() => CheckCompute("metal");

    [ValidatorFact(Validator.Naga)]
    public void Slang_compute_corpus_wgsl_passes_naga() => CheckCompute("wgsl");

    [ValidatorFact(Validator.Nvrtc)]
    public void Slang_compute_corpus_cuda_compiles_with_nvrtc() => CheckCompute("cuda");

    [ValidatorFact(Validator.HostCxx)]
    public void Slang_compute_corpus_cpp_compiles_with_the_host_compiler() => CheckCompute("cpp");

    // ---------------------------------------------------------------- each judge can say no

    // An oracle that accepts everything passes every row above. Each target's tool is handed
    // compute-fixed's output, which it accepts, broken: a SPIR-V module whose ID bound is 1, a DXIL
    // container with its program's bytes zeroed, and source text with a line that is no language
    // appended. Each must refuse it.

    [ValidatorFact(Validator.SpirvVal)]
    public void Spirv_val_rejects_a_broken_module() => RequireRejection("spirv", code =>
    {
        BitConverter.GetBytes(1u).CopyTo(code, 12); // word 3, the ID bound
        return code;
    });

    [ValidatorFact(Validator.DxilValidator)]
    public void Dxil_validator_rejects_a_broken_container() => RequireRejection("dxil", code =>
    {
        // The last quarter of the container is inside its DXIL program part.
        Array.Clear(code, code.Length * 3 / 4, code.Length / 8);
        return code;
    });

    [ValidatorFact(Validator.GlslangValidator)]
    public void Glsl_glslang_rejects_broken_source() => RequireRejection("glsl", AppendNonsense);

    [ValidatorFact(Validator.DxcHlsl)]
    public void Hlsl_dxc_rejects_broken_source() => RequireRejection("hlsl", AppendNonsense);

    [ValidatorFact(Validator.XcrunMetal)]
    public void Metal_xcrun_rejects_broken_source() => RequireRejection("metal", AppendNonsense);

    [ValidatorFact(Validator.Naga)]
    public void Wgsl_naga_rejects_broken_source() => RequireRejection("wgsl", AppendNonsense);

    [ValidatorFact(Validator.Nvrtc)]
    public void Cuda_nvrtc_rejects_broken_source() => RequireRejection("cuda", AppendNonsense);

    [ValidatorFact(Validator.HostCxx)]
    public void Cpp_the_host_compiler_rejects_broken_source() => RequireRejection("cpp", AppendNonsense);

    private static byte[] AppendNonsense(byte[] source) =>
        [.. source.TakeWhile(b => b != 0), .. "\nthis line is not a declaration in any shading language;\n"u8];

    private void RequireRejection(string target, Func<byte[], byte[]> breakIt)
    {
        var t = TargetNamed(target);
        using var global = SlangGlobalSession.Create();
        var e = Assert.Single(EmitCase(global, ShaderCorpus.Case("compute-fixed"), t.Options()));
        Assert.Null(e.Refusal);

        var intact = t.Judge(e);
        Assert.True(intact.Accepted, $"{target}: the intact output was refused, so the broken one proves nothing: {intact.Message}");

        var broken = t.Judge(e with { Key = e.Key + ".broken", Code = breakIt([.. e.Code!]) });
        output.WriteLine($"{target}, broken: {Oneline(broken.Message)}");
        Assert.False(broken.Accepted, $"{TargetValidators.Describe(t.Validator)} accepted broken {target} output");
        Assert.False(string.IsNullOrWhiteSpace(broken.Message), $"{target}: the tool refused without saying why");
    }

    // ---------------------------------------------------------------- the declarations themselves

    [NativeFact]
    public void Every_targets_refusals_are_the_declared_ones()
    {
        // Slang alone: no validator, so this runs on every leg and measures Metal's refusals off
        // macOS. Only the targets whose tool is missing here: where the tool is present, its
        // theory and fact check the same refusals as part of their rows, and compiling the corpora
        // twice would only cost time. DXIL needs dxc to be emitted at all, so where dxc is absent
        // nothing can be said about it here either.
        var problems = new List<string>();
        var checkedTargets = new List<string>();

        foreach (var target in Targets)
        {
            if (TargetValidators.Unavailable(target.Validator) is null) continue;
            if (target.Name == "dxil" && DxcNative.Unavailable is not null) continue;
            checkedTargets.Add(target.Name);

            using var global = SlangGlobalSession.Create();
            var emitted = ShaderCorpus.All.SelectMany(c => EmitCase(global, c, target.Options()))
                .Concat(SlangTestCorpus.Compute.Select(c => EmitCompute(global, c, target.Options())))
                .ToList();
            foreach (var e in emitted)
            {
                var expected = Declared.For(target.Name).GetValueOrDefault(e.Key)?.ForStagedPayload();
                var refusedNow = e.Refusal is not null;
                var declaredRefused = expected?.Outcome == Outcome.SlangRefuses;
                if (refusedNow != declaredRefused
                    || (refusedNow && !e.Refusal!.Contains(expected!.Words, StringComparison.Ordinal)))
                {
                    problems.Add($"{target.Name} {e.Key}: declared {expected?.Outcome ?? Outcome.Valid}, "
                               + $"Slang {(refusedNow ? "refused: " + Oneline(e.Refusal!) : "emitted it")}");
                }
            }

            output.WriteLine($"{target.Name}: {emitted.Count} entry points, {emitted.Count(e => e.Refusal is not null)} refused by Slang");
        }

        output.WriteLine($"checked without a tool: {(checkedTargets.Count == 0 ? "none (every tool is present)" : string.Join(", ", checkedTargets))}");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [NativeFact]
    public void Every_declared_outcome_names_an_entry_point_that_exists()
    {
        var entries = ShaderCorpus.All.SelectMany(c => c.EntryPoints.Select(e => $"{c.Name}/{e}")).ToHashSet(StringComparer.Ordinal);
        var stale = Targets
            .SelectMany(t => Declared.For(t.Name).Keys.Where(k => !k.StartsWith(ComputePrefix, StringComparison.Ordinal) && !entries.Contains(k))
                .Select(k => $"{t.Name}: {k}"))
            .Concat(SpirvValFlags.Keys.Where(k => !k.StartsWith(ComputePrefix, StringComparison.Ordinal) && !entries.Contains(k)).Select(k => $"spirv-val flags: {k}"))
            .ToList();
        Assert.True(stale.Count == 0, "declared outcomes for entry points the corpus does not have:\n  " + string.Join("\n  ", stale));
    }

    // ---------------------------------------------------------------- judging

    /// <summary>
    /// The registry test of a patch that fixes a target's output: the case must be <see cref="Outcome.Valid"/>
    /// on our build, so the build without the patch is red (AGENTS.md#patches).
    /// </summary>
    private static void RequirePatchedCaseValid(string target, string name, Validator validator)
    {
        var missing = TargetValidators.Unavailable(validator);
        Assert.SkipWhen(missing is not null, missing ?? "");
        var t = TargetNamed(target);
        using var global = SlangGlobalSession.Create();
        var caseName = name.Split('/')[0];
        var emitted = EmitCase(global, ShaderCorpus.Case(caseName), t.Options()).Where(e => e.Key == name).ToList();
        Assert.NotEmpty(emitted);
        foreach (var e in emitted)
        {
            Assert.True(e.Refusal is null, $"{target} {e.Key}: Slang refused: {e.Refusal}");
            var verdict = t.Judge(e);
            Assert.True(verdict.Accepted, $"{target} {e.Key}: {verdict.Message}");
        }
    }

    [PatchedFact("U18")]
    public void Glsl_a_hull_shaders_patch_constant_function_compiles() =>
        RequirePatchedCaseValid("glsl", "tessellation/teHull", Validator.GlslangValidator);

    /// <summary>The registry test of a patch that fixes a compute case's output on <paramref name="target"/>.</summary>
    private static void RequirePatchedComputeValid(string target, Validator validator, params string[] keys)
    {
        var missing = TargetValidators.Unavailable(validator);
        Assert.SkipWhen(missing is not null, missing ?? "");
        var t = TargetNamed(target);
        using var global = SlangGlobalSession.Create();
        foreach (var key in keys)
        {
            var test = SlangTestCorpus.Compute.Single(c => ComputePrefix + c.Source == key);
            var e = EmitCompute(global, test, t.Options());
            Assert.True(e.Refusal is null, $"{target} {key}: Slang refused: {e.Refusal}");
            var verdict = t.Judge(e);
            Assert.True(verdict.Accepted, $"{target} {key}: {verdict.Message}");
        }
    }

    [PatchedFact("U20")]
    public void Wgsl_a_value_computed_from_a_specialization_constant_is_not_const() =>
        RequirePatchedCaseValid("wgsl", "specialization-constants/scFragment", Validator.Naga);

    [PatchedFact("U23")]
    public void A_construct_the_target_cannot_express_is_diagnosed_not_an_internal_error()
    {
        // U23, patched (0013): double on WGSL and Metal is E56110, and a rasterization stage's
        // varyings on the C++ and CUDA targets are E56111, each a diagnostic rather than an internal
        // error.
        using var global = SlangGlobalSession.Create();
        foreach (var (target, key, words) in new[]
                 {
                     ("wgsl", "wide-scalars/wsFragment", "56110"),
                     ("metal", "wide-scalars/wsFragment", "56110"),
                     ("cpp", "system-values/svFragment", "56111"),
                     ("cuda", "vertex-interface/viVertex", "56111"),
                 })
        {
            var e = EmitCase(global, ShaderCorpus.Case(key.Split('/')[0]), TargetNamed(target).Options()).Single(x => x.Key == key);
            Assert.True(e.Refusal is not null, $"{target} {key}: compiled");
            Assert.Contains(words, e.Refusal, StringComparison.Ordinal);
            Assert.DoesNotContain("E9999", e.Refusal, StringComparison.Ordinal);
        }
    }
    [PatchedFact("U24")]
    public void Cpp_half_vector_arithmetic_and_comparison_compile() =>
        RequirePatchedComputeValid("cpp", Validator.HostCxx, "compute:half-vector-calc.slang", "compute:half-vector-compare.slang");
    [PatchedFact("U19")]
    public void Glsl_a_domain_shaders_patch_input_compiles() =>
        RequirePatchedCaseValid("glsl", "tessellation/teDomain", Validator.GlslangValidator);
    private void CheckCase(string target, string name)
    {
        var t = TargetNamed(target);
        using var global = SlangGlobalSession.Create();
        Judge(t, EmitCase(global, ShaderCorpus.Case(name), t.Options()), $"{target}, {name}");
    }

    private void CheckCompute(string target)
    {
        var t = TargetNamed(target);
        var cases = SlangTestCorpus.Compute;
        Assert.True(cases.Count >= 50, $"the compute corpus has {cases.Count} cases; the listing or the fetch went wrong");

        using var global = SlangGlobalSession.Create();
        var emitted = cases.Select(c => EmitCompute(global, c, t.Options())).ToList();
        Judge(t, emitted, $"{target}, Slang's tests/compute");

        // Stale declarations: a key for a case the corpus no longer has is a failure, not a no-op.
        var keys = emitted.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var stale = Declared.For(target).Keys.Where(k => k.StartsWith(ComputePrefix, StringComparison.Ordinal) && !keys.Contains(k)).ToList();
        Assert.True(stale.Count == 0, $"{target}: declared outcomes for compute cases that do not exist: " + string.Join(", ", stale));
    }

    private void Judge(Target target, IReadOnlyList<Emitted> emitted, string what)
    {
        output.WriteLine($"{what}: {TargetValidators.Describe(target.Validator)}");
        var declared = Declared.For(target.Name);
        var problems = new List<string>();
        var tally = new Dictionary<Outcome, int>();

        foreach (var e in emitted)
        {
            Outcome outcome;
            string message;
            if (e.Refusal is not null)
            {
                (outcome, message) = (Outcome.SlangRefuses, e.Refusal);
            }
            else
            {
                var verdict = target.Judge(e);
                (outcome, message) = (verdict.Accepted ? Outcome.Valid : Outcome.ValidatorRejects, verdict.Message);
            }

            tally[outcome] = tally.GetValueOrDefault(outcome) + 1;
            output.WriteLine($"{target.Name} {e.Key} ({e.Stage}): {outcome}{(message.Length > 0 ? ": " + Oneline(message) : "")}");

            var expected = (declared.GetValueOrDefault(e.Key) ?? new Expected(Outcome.Valid, "", "")).ForStagedPayload();
            if (outcome != expected.Outcome || !message.Contains(expected.Words, StringComparison.Ordinal))
            {
                problems.Add($"{e.Key}: expected {expected.Outcome}{(expected.Words.Length > 0 ? $" ('{expected.Words}')" : "")}, "
                           + $"got {outcome}: {message}");
            }
        }

        output.WriteLine($"{what}: " + string.Join(", ", tally.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} {kv.Key}")));
        Assert.True(problems.Count == 0, $"{what}:\n" + string.Join("\n", problems));
    }

    // ---------------------------------------------------------------- compiling

    private const string ComputePrefix = "compute:";

    /// <summary>Each entry point compiled alone, so one refusal cannot hide another entry's code.</summary>
    /// <remarks>
    /// A session per case, from one global session per test: the session is what a case's
    /// compile could leave state in, and a global session costs more to create than most compiles.
    /// </remarks>
    private static List<Emitted> EmitCase(SlangGlobalSession global, ShaderCase c, TargetOptions target)
    {
        using var session = global.CreateSession(new SessionOptions { Targets = [target] });

        SlangModule module;
        try
        {
            module = session.LoadModuleFromSource(c.Name, $"{c.Name}.slang", c.Source);
        }
        catch (SlangException e)
        {
            // Some targets refuse a construct while checking the module, before any entry point
            // is linked; that refusal is every entry point's.
            return [.. c.EntryPoints.Select(name => new Emitted($"{c.Name}/{name}", SlangStage.SLANG_STAGE_NONE, null, e.Message))];
        }

        using var owned = module;
        return [.. c.EntryPoints.Select(name => Link(session, module, $"{c.Name}/{name}", () => module.FindEntryPoint(name)))];
    }

    /// <summary>One of Slang's compute tests, compiled the way SlangComputeCorpusTests compiles it.</summary>
    private static Emitted EmitCompute(SlangGlobalSession global, SlangTestCorpus.ComputeCase test, TargetOptions target)
    {
        var key = ComputePrefix + test.Source;
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [target],
            SearchPaths = [SlangTestCorpus.ComputeDirectory],
            MatrixLayout = test.Arguments.Contains("-matrix-layout-column-major", StringComparison.Ordinal)
                ? SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR
                : SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
        });

        SlangModule module;
        try
        {
            module = session.LoadModuleFromSource(Path.GetFileNameWithoutExtension(test.Source), test.Source, test.Text);
        }
        catch (SlangException e)
        {
            return new Emitted(key, SlangStage.SLANG_STAGE_NONE, null, e.Message);
        }

        using var owned = module;
        return Link(session, module, key, () => module.FindEntryPoint("computeMain", SlangStage.SLANG_STAGE_COMPUTE));
    }

    private static Emitted Link(SlangSession session, SlangModule module, string key, Func<SlangEntryPoint> find)
    {
        try
        {
            using var entryPoint = find();
            using var program = session.Link(module, entryPoint);
            var stage = program.Reflect(0).EntryPoints[0].Stage;
            try
            {
                return new Emitted(key, stage, program.GetEntryPointCodeBytes(0, 0), null);
            }
            catch (SlangException e)
            {
                return new Emitted(key, stage, null, e.Message);
            }
        }
        catch (SlangException e)
        {
            return new Emitted(key, SlangStage.SLANG_STAGE_NONE, null, e.Message);
        }
    }

    /// <summary>dxc's arguments for one entry point of Slang's HLSL: the stage's profile at 6.5.</summary>
    /// <remarks>
    /// Shader model 6.5, the profile Slang was asked for and the binding's DXIL default. The entry
    /// point keeps its Slang name in Slang's HLSL (measured: every row compiles with -E set to it).
    /// Ray-tracing stages are a library (lib_6_5), which takes no entry point.
    /// </remarks>
    private static string[] DxcArguments(Emitted e)
    {
        var prefix = e.Stage switch
        {
            SlangStage.SLANG_STAGE_VERTEX => "vs",
            SlangStage.SLANG_STAGE_HULL => "hs",
            SlangStage.SLANG_STAGE_DOMAIN => "ds",
            SlangStage.SLANG_STAGE_GEOMETRY => "gs",
            SlangStage.SLANG_STAGE_FRAGMENT => "ps",
            SlangStage.SLANG_STAGE_COMPUTE => "cs",
            SlangStage.SLANG_STAGE_MESH => "ms",
            SlangStage.SLANG_STAGE_AMPLIFICATION => "as",
            _ => "lib",
        };

        var entry = e.Key.StartsWith(ComputePrefix, StringComparison.Ordinal) ? "computeMain" : e.Key[(e.Key.IndexOf('/', StringComparison.Ordinal) + 1)..];
        return prefix == "lib" ? ["-T", "lib_6_5"] : ["-T", $"{prefix}_6_5", "-E", entry];
    }

    private static string Oneline(string s) => s.ReplaceLineEndings(" | ");

    // ---------------------------------------------------------------- what is declared

    internal static class Declared
    {
        private const string NotInStage = "uses features that are not available in";
        private const string SpecializedNumthreads = "specialization constants not supported for numthreads";
        private const string NoStageForTarget = "the stage or a construct in it does not exist on this target; Slang diagnoses it (E36107)";
        private const string NoSpecializedNumthreads = "the target has no specialization-constant workgroup size; Slang diagnoses it (E55205)";
        private const string SystemVarying = "this target doesn't support this system-defined varying parameter";
        private const string UserVarying = "this target doesn't support this user-defined varying parameter";
        private const string NoRasterStage = "a rasterization stage, which the target does not have; Slang says so as an internal error, E99999, not a diagnostic (U23)";

        private static Dictionary<string, Expected> Table(params (string[] Keys, Expected Expected)[] groups)
        {
            var table = new Dictionary<string, Expected>(StringComparer.Ordinal);
            foreach (var (keys, expected) in groups)
                foreach (var key in keys)
                    table.Add(key, expected);
            return table;
        }

        private static readonly string[] RayTracing =
            ["ray-tracing/rtRayGen", "ray-tracing/rtClosestHit", "ray-tracing/rtAnyHit", "ray-tracing/rtIntersection", "ray-tracing/rtMiss", "ray-tracing/rtCallable"];

        private static readonly string[] Fragments =
        [
            "descriptor-arrays/daFragment", "parameter-blocks/pbFragment", "explicit-spaces/esFragment", "explicit-buffer-spaces/ebFragment",
            "uniform-layout/ulFragment", "push-constants/pcFragment", "specialization-constants/scFragment", "wide-vertex-inputs/wvVertex",
            "entry-point-uniforms/euFragment", "global-uniforms/guFragment", "matrix-orientation-overrides/moFragment", "buffer-arrays/baFragment",
            "nested-uniform-structs/nuFragment", "wide-scalars/wsFragment", "system-values/svFragment", "interpolation-modifiers/imFragment",
            "scalar-widths/swFragment", "stage-narrowing/snFragment", "existential-types/etFragment",
        ];

        private static readonly string[] UserVaryings =
            ["vertex-interface/viVertex", "fragment-outputs/foFragment", "system-values/svVertex", "interpolation-modifiers/imVertex", "stage-narrowing/snVertex"];

        public static readonly Dictionary<string, Expected> Spirv = Table();

        public static readonly Dictionary<string, Expected> Dxil = Table(
            (["subpass-inputs/siFragment"], new(Outcome.SlangRefuses, "SubpassInput is a Vulkan specific feature", "Vulkan only; dxc refuses it (DxilTests.CannotCompile)")),
            (["wide-vertex-inputs/wvVertex"], new(Outcome.SlangRefuses, "cannot be used as shader inputs or outputs", "D3D has no 64-bit stage input; dxc refuses it (DxilTests.CannotCompile)")),
            (["compute-specialized/csCompute"], new(Outcome.SlangRefuses, SpecializedNumthreads, NoSpecializedNumthreads)));

        public static readonly Dictionary<string, Expected> Hlsl = Table(
            (["subpass-inputs/siFragment"], new(Outcome.ValidatorRejects, "SubpassInput is a Vulkan specific feature", "Vulkan only: Slang writes it into the HLSL and dxc refuses it for DXIL, as on the DXIL target")),
            (["wide-vertex-inputs/wvVertex"], new(Outcome.ValidatorRejects, "cannot be used as shader inputs or outputs", "D3D has no 64-bit stage input: Slang writes it into the HLSL and dxc refuses it, as on the DXIL target")),
            (["compute-specialized/csCompute"], new(Outcome.SlangRefuses, SpecializedNumthreads, NoSpecializedNumthreads)));

        public static readonly Dictionary<string, Expected> Glsl = Table(
            (["tessellation/teHull"], new(Outcome.ValidatorRejects, "non-void function must return a value", "U18: the patch-constant function is declared to return its struct and ends in a bare return", FixedBy: "U18")),
            (["tessellation/teDomain"], new(Outcome.ValidatorRejects, "tessellation input array size must be gl_MaxPatchVertices or implicitly sized", "U19: the domain stage's per-vertex input is declared with the patch size")));

        public static readonly Dictionary<string, Expected> Metal = Table(
            (["tessellation/teHull", "tessellation/teDomain", "geometry/gsGeometry", .. RayTracing, "append-buffers/abCompute"],
                new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (["mesh/msMesh"], new(Outcome.SlangRefuses, "mesh output must be assigned as whole struct", "Metal writes a mesh vertex whole; Slang diagnoses a per-member write (E56104)")),
            (["wide-vertex-inputs/wvVertex", "wide-scalars/wsFragment", "scalar-widths/swFragment", "compute:bitcast-64bit.slang"],
                new(Outcome.SlangRefuses, "'double' type emitted", "Metal has no double; Slang ends with an InternalError, E99997, instead of a diagnostic (U23)", FixedBy: "U23", FixedWords: "56110")),
            // Measured by xcrun metal on the GitHub-hosted macos-15 runner, run 42475073038
            // (2026-11-08, Slang v2026.21): the first time these rows met Apple's compiler.
            (["descriptor-kinds/dkFragment"], new(Outcome.ValidatorRejects, "no matching member function for call to 'read'", "U26: Slang calls a texture read overload Metal does not declare")),
            (["descriptor-arrays/daFragment", "buffer-arrays/baFragment"],
                new(Outcome.ValidatorRejects, "flexible array members are a C99 feature", "U27: an unbounded resource array is written as a flexible array member, which Metal's C++ does not have")),
            (["vertex-interface/viVertex"], new(Outcome.ValidatorRejects, "is not valid for attribute 'attribute'", "U28: a matrix vertex input at one [[attribute]], as U21 on WGSL")),
            (["compute:loop-unroll.slang"], new(Outcome.ValidatorRejects, "for input declaration in a kernel function", "U29: an array of device pointers passed as a kernel input")),
            (["compute:rw-texture-simple.slang"], new(Outcome.ValidatorRejects, "number of textures with read_write access exceeds maximum supported (8)", "Metal's limit of 8 read_write textures; the test shader declares more. Not judged as a Slang defect")));

        public static readonly Dictionary<string, Expected> Wgsl = Table(
            (["descriptor-kinds/dkFragment", "subpass-inputs/siFragment", "tessellation/teHull", "tessellation/teDomain", "geometry/gsGeometry",
              "mesh/msMesh", "amplification/asAmplification", .. RayTracing, "append-buffers/abCompute", "atomics/atCompute"],
                new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (["compute-specialized/csCompute"], new(Outcome.SlangRefuses, SpecializedNumthreads, NoSpecializedNumthreads)),
            (["wide-vertex-inputs/wvVertex", "wide-scalars/wsFragment", "scalar-widths/swFragment"],
                new(Outcome.SlangRefuses, "'double' type emitted", "WGSL has no f64; Slang ends with an InternalError, E99997, instead of a diagnostic (U23)", FixedBy: "U23", FixedWords: "56110")),
            (["system-values/svVertex", "system-values/svFragment"], new(Outcome.SlangRefuses, "system value semantic", "WGSL has no such builtin; Slang diagnoses it (E55202)")),
            (["descriptor-arrays/daFragment", "buffer-arrays/baFragment"],
                new(Outcome.ValidatorRejects, "the `wgpu_binding_array` enable extension is not enabled", "U22: binding_array is not WGSL; naga takes it only behind its own enable, which Slang does not write")),
            (["specialization-constants/scFragment"], new(Outcome.ValidatorRejects, "Unexpected override-expression", "U20: a const declaration initialised from an override", FixedBy: "U20")),
            (["vertex-interface/viVertex"], new(Outcome.ValidatorRejects, "Only numeric scalars and vectors are allowed", "U21: a matrix vertex input at one @location")),
            (["push-constants/pcFragment"], new(Outcome.ValidatorRejects, "unknown language extension `immediate_address_space`", "naga 30.0.1 does not know the language extension Slang requires for push constants; not judged as a Slang defect")),
            (["wave-ops/woCompute"], new(Outcome.ValidatorRejects, "the `subgroups` enable-extension is not yet supported", "naga 30.0.1 does not implement WGSL's subgroups extension; not judged as a Slang defect")),
            (["compute:atomics-groupshared.slang", "compute:atomics.slang"], new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (["compute:bitcast-64bit.slang"], new(Outcome.SlangRefuses, "'double' type emitted", "WGSL has no f64; Slang ends with an InternalError, E99997, instead of a diagnostic (U23)", FixedBy: "U23", FixedWords: "56110")),
            (["compute:byte-address-buffer-64bit.slang"], new(Outcome.ValidatorRejects, "automatic conversions cannot convert elements of `u64` to `u32`", "U25: WGSL has no 64-bit integer, and Slang writes u64 into it")),
            (["compute:half-rw-texture-convert.slang", "compute:half-rw-texture-convert2.slang"],
                new(Outcome.SlangRefuses, "does not support 'read_write' access for storage textures in WGSL", "WGSL has no read-write rgba16float storage texture; Slang diagnoses it (E56106)")),
            (["compute:loop-unroll.slang"], new(Outcome.SlangRefuses, "array of resource types not supported in WGSL", "WGSL has no arrays of buffers; Slang diagnoses it (E56105)")));

        public static readonly Dictionary<string, Expected> Cuda = Table(
            (["descriptor-kinds/dkFragment", "subpass-inputs/siFragment", "tessellation/teHull", "tessellation/teDomain", "geometry/gsGeometry",
              "mesh/msMesh", "amplification/asAmplification"], new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (Fragments, new(Outcome.SlangRefuses, SystemVarying, NoRasterStage, FixedBy: "U23", FixedWords: "56111")),
            (UserVaryings, new(Outcome.SlangRefuses, UserVarying, NoRasterStage, FixedBy: "U23", FixedWords: "56111")),
            (["ray-tracing/rtRayGen", "ray-tracing/rtClosestHit", "ray-tracing/rtAnyHit", "ray-tracing/rtIntersection", "ray-tracing/rtMiss"],
                new(Outcome.ValidatorRejects, "identifier \"optix", "CUDA ray tracing is OptiX: the source calls OptiX device functions, whose header is the OptiX SDK's, not the CUDA toolkit's")));

        public static readonly Dictionary<string, Expected> Cpp = Table(
            (["descriptor-kinds/dkFragment", "subpass-inputs/siFragment", "tessellation/teHull", "tessellation/teDomain", "geometry/gsGeometry",
              "mesh/msMesh", "amplification/asAmplification", .. RayTracing, "wave-ops/woCompute", "atomics/atCompute"],
                new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (["compute-specialized/csCompute"], new(Outcome.SlangRefuses, SpecializedNumthreads, NoSpecializedNumthreads)),
            (Fragments, new(Outcome.SlangRefuses, SystemVarying, NoRasterStage, FixedBy: "U23", FixedWords: "56111")),
            (UserVaryings, new(Outcome.SlangRefuses, UserVarying, NoRasterStage, FixedBy: "U23", FixedWords: "56111")),
            (["append-buffers/abCompute"], new(Outcome.SlangRefuses, "unexpected IR opcode during code emit", "U23: append and consume buffers on the C++ target end in an internal error, E99999")),
            (["compute:atomics-groupshared.slang", "compute:atomics.slang", "compute:groupshared.slang", "compute:half-rw-texture-convert2.slang"],
                new(Outcome.SlangRefuses, NotInStage, NoStageForTarget)),
            (["compute:half-vector-calc.slang", "compute:half-vector-compare.slang"],
                new(Outcome.ValidatorRejects, "invalid operands to binary expression", "U24: Slang's C++ prelude defines vector arithmetic and comparison for float and double, not half", FixedBy: "U24")));

        public static Dictionary<string, Expected> For(string target) => target switch
        {
            "spirv" => Spirv,
            "dxil" => Dxil,
            "glsl" => Glsl,
            "hlsl" => Hlsl,
            "metal" => Metal,
            "wgsl" => Wgsl,
            "cuda" => Cuda,
            "cpp" => Cpp,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };
    }
}

/// <summary>Slang's C++ and CUDA preludes at the pinned tag, which their emitted source includes.</summary>
/// <remarks>
/// The emitted source starts with <c>#include "slang-cpp-prelude.h"</c> or
/// <c>#include "slang-cuda-prelude.h"</c>; Slang's release ships them in <c>include/</c>, and a
/// consumer compiling the output needs them. Fetched from Slang's <c>prelude/</c> at the pinned tag
/// into the header cache, as SlangHeader fetches <c>slang.h</c>, with every <c>slang-*.h</c> they
/// include.
/// </remarks>
internal static class SlangPrelude
{
    private static readonly Lazy<string> LazyDirectory = new(Fetch);

    /// <summary>A directory holding <c>slang-cpp-prelude.h</c>, <c>slang-cuda-prelude.h</c> and what they include.</summary>
    public static string Directory => LazyDirectory.Value;

    private static string Fetch()
    {
        // A payload built from source is judged against the submodule's prelude, its patches applied
        // (AGENTS.md#patches): a prelude patch changes what the emitted source compiles against.
        var submodule = Path.Combine(SlangHeader.RepoRoot(), "slang", "prelude");
        if (StagedPayload.IsBuild && File.Exists(Path.Combine(submodule, "slang-cpp-prelude.h")))
            return submodule;

        var version = SlangHeader.Pinned.Version;
        var directory = Path.Combine(SlangHeader.RepoRoot(), "obj", "slang-headers", version, "prelude");
        System.IO.Directory.CreateDirectory(directory);

        var pending = new Queue<string>(["slang-cpp-prelude.h", "slang-cuda-prelude.h"]);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (!seen.Add(name)) continue;

            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                var url = $"https://raw.githubusercontent.com/shader-slang/slang/{version}/prelude/{name}";
                File.WriteAllText(path, http.GetStringAsync(new Uri(url)).GetAwaiter().GetResult());
            }

            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(path), "#\\s*include\\s+\"(?<file>slang-[\\w.-]+\\.h)\""))
            {
                pending.Enqueue(m.Groups["file"].Value);
            }
        }

        return directory;
    }
}
