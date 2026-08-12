// Per-link compiler options.
//
// These assert that options actually reach the compiler and change the output. That matters more
// than usual here: Slang accepts the whole CompilerOptionName range at link time and silently
// ignores most of it, so a binding that plumbed the options nowhere would pass any test that only
// checked "it still compiles".

using PrismSlang.Native;

namespace PrismSlang.Tests;

public class LinkOptionsTests
{
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D albedo;
        SamplerState samp;

        float3 heavy(float3 c)
        {
            // Enough redundant arithmetic that an optimiser has something to remove.
            for (int i = 0; i < 4; ++i) { c = c * 1.0 + 0.0; c = normalize(c + 0.0001); }
            return c;
        }

        [shader("fragment")]
        float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
        {
            float3 c = albedo.Sample(samp, uv).rgb * u.tint * u.exposure;
            return float4(heavy(c), 1.0);
        }
        """;

    private static byte[] Compile(LinkTimeOptions? options) => With(options, p => p.GetEntryPointCodeBytes(0));

    private static byte[] Hash(LinkTimeOptions? options, string source = Shader) =>
        With(options, p => p.GetEntryPointIdentityHash(0), source);

    private static byte[] With(
        LinkTimeOptions? options,
        Func<LinkedProgram, byte[]> read,
        string source = Shader)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, [ep], new LinkOptions { CompilerOptions = options });
        return read(program);
    }

    [NativeFact]
    public void The_identity_hash_is_deaf_to_the_optimisation_level()
    {
        // The reason getEntryPointHash went unbound for so long, now bound and pinned instead.
        //
        // The obvious use for a per-entry-point hash is a pipeline cache key, and for that it is
        // wrong: None and Maximal produce *different code* — the test below asserts exactly that —
        // and the *same hash*. A cooker keying a cache on it serves optimised code for an
        // unoptimised request, or the reverse, with nothing to notice.
        //
        // Pinned rather than documented, so upstream changing it is a failing test rather than a
        // sentence in a document quietly becoming false. If this ever fails, the remarks on
        // LinkedProgram.GetEntryPointIdentityHash are what need rewriting — and the news is good.
        var none = Hash(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE,
        });
        var maximal = Hash(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL,
        });

        Assert.NotEmpty(none);
        Assert.Equal(none, maximal);
    }

    [NativeFact]
    public void The_identity_hash_is_stable_and_follows_the_source()
    {
        // The other half: an identity key that never changes identifies nothing, and one that
        // changes run to run cannot be stored. Both directions, because either alone passes for a
        // constant.
        var first = Hash(null);
        var again = Hash(null);
        Assert.Equal(first, again);

        var edited = Hash(null, Shader.Replace("float4(heavy(c), 1.0)", "float4(heavy(c), 0.5)", StringComparison.Ordinal));
        Assert.NotEqual(first, edited);
    }

    [NativeFact]
    public void Optimisation_level_changes_the_generated_code()
    {
        var none = Compile(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE,
        });
        var maximal = Compile(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL,
        });

        Assert.NotEqual(none, maximal);
        Assert.True(maximal.Length < none.Length,
            $"expected maximal ({maximal.Length}) to be smaller than none ({none.Length})");
    }

    [NativeFact]
    public void Floating_point_mode_changes_the_generated_code()
    {
        var precise = Compile(new LinkTimeOptions
        {
            FloatingPointMode = SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_PRECISE,
        });

        // Compared against the default rather than against Fast: Fast *is* the default, so
        // asserting Fast != default would fail for the right reason and read as a bug.
        Assert.NotEqual(Compile(null), precise);
    }

    [NativeFact]
    public void Raw_entries_reach_the_compiler_identically_to_typed_ones()
    {
        // Proves the escape hatch is wired to the same place, so an option without a typed
        // property is still usable rather than quietly dropped.
        var typed = Compile(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE,
        });
        var raw = Compile(new LinkTimeOptions
        {
            RawEntries = [new RawCompilerOption(CompilerOptionName.Optimization, 0)],
        });

        Assert.Equal(typed, raw);
    }

    [NativeFact]
    public void No_options_takes_the_plain_link_path_and_matches_a_bare_link()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", Shader);
        using var ep = module.FindEntryPoint("fragmentMain");

        using var bare = session.Link(module, ep);
        using var empty = session.Link(module, [ep], new LinkOptions { CompilerOptions = new LinkTimeOptions() });

        Assert.Equal(bare.GetEntryPointCodeBytes(0), empty.GetEntryPointCodeBytes(0));
    }

    [NativeFact]
    public void Link_options_compose_with_specialisation()
    {
        // Both go through the same compose → specialise → link path, so they have to coexist.
        const string Generic = """
            interface IMaterial { float3 shade(float3 a); }
            struct Lambert : IMaterial { float3 shade(float3 a) { return a * 0.5; } }
            type_param Material : IMaterial;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { Material m; return float4(m.shade(float3(1,1,1)), 1); }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("g", "g.slang", Generic);
        using var ep = module.FindEntryPoint("fragmentMain");

        using var program = session.Link(module, [ep], new LinkOptions
        {
            SpecializationArguments = [module.RequireType("Lambert")],
            CompilerOptions = new LinkTimeOptions
            {
                Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_MAXIMAL,
            },
        });

        Assert.Equal(0, program.SpecializationParameterCount);
        Assert.NotEmpty(program.GetEntryPointCodeBytes(0));
    }

    [Fact]
    public void Empty_options_are_recognised_as_empty()
    {
        Assert.True(new LinkTimeOptions().IsEmpty);
        Assert.False(new LinkTimeOptions
        {
            Optimization = SlangOptimizationLevel.SLANG_OPTIMIZATION_LEVEL_NONE,
        }.IsEmpty);
        Assert.False(new LinkTimeOptions
        {
            RawEntries = [new RawCompilerOption(CompilerOptionName.Optimization, 0)],
        }.IsEmpty);
    }
}
