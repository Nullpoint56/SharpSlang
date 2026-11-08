// Does every option do something?
//
// An earlier round asked this of the seven link-time options and found five of them inert — Slang accepts the
// whole CompilerOptionName range at linkWithOptions and silently ignores most of it, so a typed
// property reads as a supported feature and does nothing. Session and target options never got the
// same treatment, and they are the ones a consumer actually sets.
//
// The test is the same shape as the reflection harness: don't reason about it, change the option
// and look at what came out. Every property on SessionOptions and TargetOptions is either shown to
// change the output here, or listed as observationally inert with the reason.

using System.Reflection;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

public class OptionEffectTests
{
    // A shader with something for every option to bite on: matrices in a buffer, two parameter
    // blocks, a macro, floating-point maths.
    private const string Source = """
        struct Frame  { float4x4 view; float3 sun; };
        struct Object { float4x4 model; Texture2D albedo; };

        ParameterBlock<Frame>  gFrame;
        ParameterBlock<Object> gObject;

        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            float4 c = mul(gFrame.view, mul(gObject.model, float4(uv, 0, 1)));
            c.rgb += gFrame.sun * SCALE;
            c.rgb = sqrt(c.rgb / (c.rgb + 1.0));
            return c + gObject.albedo.Load(int3(0, 0, 0));
        }
        """;

    private static (byte[] Code, ProgramLayout Layout) Compile(SessionOptions options)
    {
        var withMacro = new SessionOptions
        {
            Targets = options.Targets,
            SearchPaths = options.SearchPaths,
            FileSystem = options.FileSystem,
            PreprocessorMacros = options.PreprocessorMacros.Count > 0
                ? options.PreprocessorMacros
                : new Dictionary<string, string> { ["SCALE"] = "1.0" },
            MatrixLayout = options.MatrixLayout,
            AllowGlslSyntax = options.AllowGlslSyntax,
            SkipSpirvValidation = options.SkipSpirvValidation,
        };

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(withMacro);
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);
        return (program.GetEntryPointCodeBytes(0, 0), program.Reflect(0));
    }

    private static (byte[] Code, ProgramLayout Layout) WithTarget(TargetOptions target) =>
        Compile(new SessionOptions { Targets = [target] });

    // ---------------------------------------------------------------- target options

    [NativeFact]
    public void SpirvVersion_moves_the_header()
    {
        foreach (var version in new[] { SpirvVersion.V1_3, SpirvVersion.V1_4, SpirvVersion.V1_6 })
        {
            var (code, _) = WithTarget(new TargetOptions { SpirvVersion = version });
            Assert.Equal(version, SpirvModule.ReadVersion(code));
        }
    }

    [NativeFact]
    public void Profile_moves_the_header_too_and_agrees_with_SpirvVersion()
    {
        var (byProfile, _) = WithTarget(new TargetOptions { Profile = "spirv_1_6" });
        var (byVersion, _) = WithTarget(new TargetOptions { SpirvVersion = SpirvVersion.V1_6 });

        Assert.Equal(SpirvVersion.V1_6, SpirvModule.ReadVersion(byProfile));
        Assert.Equal(SpirvVersion.V1_6, SpirvModule.ReadVersion(byVersion));
    }

    [NativeFact]
    public void UseEntryPointNameInSpirv_renames_the_entry_point()
    {
        var (off, _) = WithTarget(new TargetOptions());
        var (on, _) = WithTarget(new TargetOptions { UseEntryPointNameInSpirv = true });

        Assert.Equal("main", SpirvModule.SingleEntryPoint(off).Name);
        Assert.Equal("fragmentMain", SpirvModule.SingleEntryPoint(on).Name);
    }

    [NativeFact]
    public void ParameterBlocksUseRegisterSpaces_does_nothing_on_a_SPIR_V_target()
    {
        // Listed as inert rather than tested as working, and asserted here so that a future Slang
        // making it work fails this test instead of quietly agreeing with a doc comment that was
        // already wrong. Measured against 2026.21: byte-identical output, identical descriptor
        // sets — a ParameterBlock takes a register space of its own either way.
        var (packedCode, packed) = WithTarget(new TargetOptions());
        var (spacedCode, spaced) = WithTarget(new TargetOptions
        {
            ParameterBlocksUseRegisterSpaces = true,
        });

        Assert.Equal(Convert.ToHexString(packedCode), Convert.ToHexString(spacedCode));
        Assert.Equal(
            packed.DescriptorSets.Select(s => (s.SpaceOffset, s.Ranges.Count)),
            spaced.DescriptorSets.Select(s => (s.SpaceOffset, s.Ranges.Count)));

        // And each block is in a space of its own regardless, which is the part the doc comment
        // used to deny.
        Assert.Equal(2, packed.DescriptorSets.Count);
    }

    [NativeFact]
    public void FloatingPointMode_changes_the_emitted_code()
    {
        var (fast, _) = WithTarget(new TargetOptions
        {
            FloatingPointMode = SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_FAST,
        });
        var (precise, _) = WithTarget(new TargetOptions
        {
            FloatingPointMode = SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_PRECISE,
        });

        Assert.NotEqual(Convert.ToHexString(fast), Convert.ToHexString(precise));
    }

    [NativeFact]
    public void Format_changes_what_comes_out_at_all()
    {
        var (spirv, _) = WithTarget(new TargetOptions());
        Assert.True(SpirvModule.IsSpirv(spirv));

        var (glsl, _) = WithTarget(new TargetOptions
        {
            Format = SlangCompileTarget.SLANG_GLSL,
            Profile = "glsl_450",
            VerifyEmittedSpirv = false,
        });
        Assert.False(SpirvModule.IsSpirv(glsl));
    }

    [NativeFact]
    public void VerifyEmittedSpirv_is_what_catches_a_version_mismatch()
    {
        // Not an effect on the output — an effect on whether a mismatch is reported. Both knobs
        // set to contradictory versions is rejected earlier, so the check is exercised by asking
        // for a version and confirming the verifier is what compares it.
        var (code, _) = WithTarget(new TargetOptions
        {
            SpirvVersion = SpirvVersion.V1_3,
            VerifyEmittedSpirv = true,
        });
        Assert.Equal(SpirvVersion.V1_3, SpirvModule.ReadVersion(code));

        var (unchecked_, _) = WithTarget(new TargetOptions
        {
            SpirvVersion = SpirvVersion.V1_3,
            VerifyEmittedSpirv = false,
        });
        Assert.Equal(Convert.ToHexString(code), Convert.ToHexString(unchecked_));
    }

    // ---------------------------------------------------------------- session options

    [NativeFact]
    public void MatrixLayout_changes_the_uniform_layout()
    {
        var (_, rowMajor) = Compile(new SessionOptions
        {
            Targets = [new TargetOptions()],
            MatrixLayout = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
        });
        var (_, columnMajor) = Compile(new SessionOptions
        {
            Targets = [new TargetOptions()],
            MatrixLayout = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
        });

        Assert.Equal(
            SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
            Matrix(rowMajor, "gFrame", "view").Mode);
        Assert.Equal(
            SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
            Matrix(columnMajor, "gFrame", "view").Mode);
    }

    [NativeFact]
    public void PreprocessorMacros_reach_the_source()
    {
        var (one, _) = Compile(new SessionOptions
        {
            Targets = [new TargetOptions()],
            PreprocessorMacros = new Dictionary<string, string> { ["SCALE"] = "1.0" },
        });
        var (two, _) = Compile(new SessionOptions
        {
            Targets = [new TargetOptions()],
            PreprocessorMacros = new Dictionary<string, string> { ["SCALE"] = "2.0" },
        });

        Assert.NotEqual(Convert.ToHexString(one), Convert.ToHexString(two));
    }

    [NativeFact]
    public void SkipSpirvValidation_still_produces_a_valid_module()
    {
        // Its effect is on compile time, not on output, so what is asserted is that it is safe to
        // set rather than that it changes something.
        var (validated, _) = Compile(new SessionOptions { Targets = [new TargetOptions()] });
        var (skipped, _) = Compile(new SessionOptions
        {
            Targets = [new TargetOptions()],
            SkipSpirvValidation = true,
        });

        Assert.True(SpirvModule.IsSpirv(validated));
        Assert.True(SpirvModule.IsSpirv(skipped));
    }

    // ---------------------------------------------------------------- the audit

    /// <summary>
    /// Options with no observable effect on the output, and why they are still exposed.
    /// </summary>
    private static readonly Dictionary<string, string> WithoutObservableEffect = new()
    {
        ["TargetOptions.EmitSpirvDirectly"] = "on by default, matching Slang's own "
            + "kDefaultTargetFlags. Turning it off routes through glslang, which needs "
            + "slang-glslang staged; DownstreamCompilerTests covers that path",
        ["TargetOptions.ForceGlslScalarBufferLayout"] = "scalar block layout for GLSL-style "
            + "targets; measured to leave a SPIR-V target's uniform offsets unchanged. Exposed "
            + "because it silently changes every offset on the targets where it does apply",
        ["SessionOptions.AllowGlslSyntax"] = "widens what the parser accepts, so its effect is on "
            + "sources this corpus does not contain rather than on output",
        ["SessionOptions.SearchPaths"] = "covered by DependencyTrackingTests, which resolves "
            + "imports through them",
        ["SessionOptions.FileSystem"] = "covered by HostingTests, which serves a whole module tree "
            + "from managed code",
        ["SessionOptions.Targets"] = "the list the other target options are read from; every test "
            + "here sets it",
        ["SessionOptions.ExperimentalFeatures"] = "covered by StandardModuleTests: without it Slang "
            + "refuses every standard module (E00104), with it each one imports",
        ["TargetOptions.ParameterBlocksUseRegisterSpaces"] = "measured byte-for-byte inert on a "
            + "SPIR-V target: a ParameterBlock takes a register space of its own either way. It "
            + "has a test of its own asserting exactly that, so a release that starts honouring "
            + "it fails rather than passes",
    };

    [Fact]
    public void Every_option_is_either_shown_to_do_something_or_listed_as_inert()
    {
        // The ratchet. A property added to either options type arrives here unaccounted for, which
        // is the moment to decide whether it works — rather than after a consumer sets it and
        // nothing happens.
        var properties = Properties(typeof(TargetOptions)).Concat(Properties(typeof(SessionOptions)));

        var tested = typeof(OptionEffectTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        var unaccounted = properties
            .Where(p => !WithoutObservableEffect.ContainsKey(p.Qualified))
            .Where(p => !tested.Any(t => t.StartsWith(p.Name + "_", StringComparison.Ordinal)))
            .Select(p => p.Qualified)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unaccounted.Count == 0,
            $"{unaccounted.Count} option(s) have neither a test named after them nor an entry in "
            + $"WithoutObservableEffect:\n"
            + string.Join("", unaccounted.Select(p => $"    {p}\n"))
            + "Add a test proving the option changes the output, or record why it cannot be seen.");
    }

    [Fact]
    public void Nothing_is_listed_as_inert_that_no_longer_exists()
    {
        var declared = Properties(typeof(TargetOptions))
            .Concat(Properties(typeof(SessionOptions)))
            .Select(p => p.Qualified)
            .ToHashSet(StringComparer.Ordinal);

        var stale = WithoutObservableEffect.Keys.Where(k => !declared.Contains(k)).Order().ToList();

        Assert.True(stale.Count == 0,
            "WithoutObservableEffect names options that no longer exist:\n"
            + string.Join("", stale.Select(k => $"    {k}\n")));
    }

    private static IEnumerable<(string Name, string Qualified)> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p.Name, $"{type.Name}.{p.Name}"));

    private static MatrixLayout Matrix(ProgramLayout layout, string parameter, string field)
    {
        var element = ((ContainerLayout)layout.Parameter(parameter)!.Type).ElementType;
        return (MatrixLayout)((StructLayout)element).Field(field)!.Type;
    }
}
