// Functional tests against the real compiler, through the L2 API.
//
// These cover what the drift tests cannot: that the bindings actually drive Slang correctly.
// Where a test encodes a fact discovered empirically rather than read from a doc, it says so.

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class CompilationTests
{
    private const uint SpirvMagic = 0x07230203;

    private const string GoodShader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;

        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv)
        {
            VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        {
            return float4(albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure, 1.0);
        }
        """;

    private static SlangGlobalSession NewGlobal() => SlangGlobalSession.Create();

    private static SlangSession NewSession(SlangGlobalSession g) => g.CreateSession(new SessionOptions
    {
        Targets = [new TargetOptions { Profile = "spirv_1_5" }],
    });

    // ---------------------------------------------------------------- happy path

    [NativeFact]
    public void Compiles_source_to_valid_spirv()
    {
        using var global = NewGlobal();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        foreach (var (name, index) in program.EntryPointNames.Select((n, i) => (n, i)))
        {
            var spirv = program.GetEntryPointCodeBytes(index, 0);

            Assert.True(spirv.Length >= 20, $"{name}: implausibly short output");
            Assert.True(spirv.Length % 4 == 0, $"{name}: not a whole number of SPIR-V words");

            var words = MemoryMarshal.Cast<byte, uint>(spirv);
            Assert.Equal(SpirvMagic, words[0]);

            var major = (words[1] >> 16) & 0xFF;
            var minor = (words[1] >> 8) & 0xFF;
            Assert.Equal(1u, major);
            Assert.True(minor >= 5, $"{name}: asked for spirv_1_5, got 1.{minor}");
        }
    }

    [NativeFact]
    public void Module_reports_its_declared_entry_points()
    {
        using var global = NewGlobal();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);

        Assert.Equal(2, module.EntryPointCount);

        var eps = module.EntryPoints();
        try
        {
            Assert.Equal(
                ["fragmentMain", "vertexMain"],
                eps.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    [NativeFact]
    public void Code_can_be_fetched_by_entry_point_name()
    {
        using var global = NewGlobal();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);
        using var vs = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, vs);

        var byName = program.GetEntryPointCodeBytes("vertexMain", 0);
        var byIndex = program.GetEntryPointCodeBytes(0, 0);
        Assert.Equal(byIndex, byName);

        var ex = Assert.Throws<ArgumentException>(() => program.GetEntryPointCodeBytes("nope", 0));
        Assert.Contains("vertexMain", ex.Message);
    }

    // ---------------------------------------------------------------- diagnostics

    [NativeFact]
    public void Broken_shader_throws_with_the_compiler_message_attached()
    {
        const string Broken = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return undefinedThing * 2.0; }
            """;

        using var global = NewGlobal();
        using var session = NewSession(global);

        var ex = Assert.Throws<SlangException>(
            () => session.LoadModuleFromSource("bad", "bad.slang", Broken));

        // The point of the test: the compiler's own text survives, not just a hex result code.
        Assert.NotNull(ex.Diagnostics);
        Assert.Contains("undefinedThing", ex.Diagnostics);
        Assert.Contains("undefinedThing", ex.Message);
    }

    [NativeFact]
    public void Missing_entry_point_names_the_entry_point_it_looked_for()
    {
        using var global = NewGlobal();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);

        var ex = Assert.Throws<SlangException>(() => module.FindEntryPoint("noSuchEntryPoint"));
        Assert.Contains("noSuchEntryPoint", ex.Message);
    }

    [NativeFact]
    public void Unknown_profile_is_rejected_before_reaching_slang()
    {
        using var global = NewGlobal();

        var ex = Assert.Throws<ArgumentException>(() => global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "not_a_real_profile" }],
        }));
        Assert.Contains("not_a_real_profile", ex.Message);
    }

    // ---------------------------------------------------------------- configuration

    [NativeFact]
    public void Preprocessor_macros_reach_the_compiler()
    {
        const string Conditional = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target
            {
            #ifndef TINT_STRENGTH
                #error TINT_STRENGTH was not defined
            #endif
                return float4(TINT_STRENGTH, 0, 0, 1);
            }
            """;

        using var global = NewGlobal();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
            PreprocessorMacros = new Dictionary<string, string> { ["TINT_STRENGTH"] = "0.5" },
        });

        // Throws if the #error fires, which is the assertion.
        using var module = session.LoadModuleFromSource("m", "m.slang", Conditional);
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, fs);
        Assert.NotEmpty(program.GetEntryPointCodeBytes(0, 0));
    }

    [NativeTheory]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR)]
    [InlineData(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR)]
    public void Both_matrix_layouts_compile(SlangMatrixLayoutMode layout)
    {
        using var global = NewGlobal();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
            MatrixLayout = layout,
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);
        using var vs = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, vs);

        Assert.NotEmpty(program.GetEntryPointCodeBytes(0, 0));
    }

    [NativeFact]
    public void Row_and_column_major_produce_different_spirv()
    {
        // Guards the AGENTS.md#descriptor-defaults-and-result-codes hazard from the other side: if the matrix-layout option were being
        // dropped on the floor, both configurations would emit identical bytes and nothing else
        // in the suite would notice.
        static byte[] Compile(SlangMatrixLayoutMode layout)
        {
            using var global = SlangGlobalSession.Create();
            using var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions { Profile = "spirv_1_5" }],
                MatrixLayout = layout,
            });
            using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);
            using var vs = module.FindEntryPoint("vertexMain");
            using var program = session.Link(module, vs);
            return program.GetEntryPointCodeBytes(0, 0);
        }

        Assert.NotEqual(
            Compile(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR),
            Compile(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR));
    }

    [NativeFact]
    public void Session_requires_at_least_one_target()
    {
        using var global = NewGlobal();
        Assert.Throws<ArgumentException>(() => global.CreateSession(new SessionOptions { Targets = [] }));
    }

    // ---------------------------------------------------------------- lifetime

    [NativeFact]
    public void Double_dispose_is_safe()
    {
        var global = NewGlobal();
        global.Dispose();
        global.Dispose();
        Assert.True(global.IsDisposed);
    }

    [NativeFact]
    public void Use_after_dispose_throws_rather_than_crashing()
    {
        var global = NewGlobal();
        global.Dispose();

        // The value of the wrapper: an ObjectDisposedException instead of an access violation
        // that takes the process down with no stack.
        Assert.Throws<ObjectDisposedException>(() => global.FindProfile("spirv_1_5"));
    }

    [NativeFact]
    public void Blob_span_is_scoped_to_the_blob()
    {
        using var global = NewGlobal();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("t", "t.slang", GoodShader);
        using var vs = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, vs);

        var blob = program.GetEntryPointCode(0, 0);
        var copy = blob.ToArray();
        Assert.NotEmpty(copy);
        blob.Dispose();

        // The copy outlives the blob; touching the blob afterwards does not read freed memory.
        Assert.NotEmpty(copy);
        Assert.Throws<ObjectDisposedException>(() => _ = blob.Length);
    }

    [NativeFact]
    public void Module_disposal_does_not_over_release()
    {
        // Regression for a heap-corrupting ownership bug. ISession::loadModule* returns a pointer
        // the session still owns — unlike every other Slang out-parameter, it carries no reference
        // for the caller. Releasing it corrupted the heap.
        //
        // glibc catches this and aborts (`free(): corrupted unsorted chunks`); the Windows heap
        // did not, so the bug shipped green on Windows for this repository's whole history until
        // the linux-x64 run. Loading and disposing modules repeatedly is the shape that trips it.
        using var global = NewGlobal();
        using var session = NewSession(global);

        // Name *and* path must both be unique: a session keys loaded modules by both, and
        // reusing either raises "The key already exists in Dictionary" from Slang itself.
        for (var i = 0; i < 50; i++)
        {
            using var module = session.LoadModuleFromSource($"m{i}", $"m{i}.slang", GoodShader);
            using var ep = module.FindEntryPoint("vertexMain");
        }

        // Still usable afterwards: nothing the loop released belonged to the session.
        using var final = session.LoadModuleFromSource("final", "final.slang", GoodShader);
        Assert.Equal(2, final.EntryPointCount);
    }

    [NativeFact]
    public void Module_outlives_repeated_lookups_of_the_same_name()
    {
        // The session caches modules by name, so a second load returns the same native pointer.
        // Two wrappers over one module must each hold their own reference.
        using var global = NewGlobal();
        using var session = NewSession(global);

        var a = session.LoadModuleFromSource("shared", "shared.slang", GoodShader);
        var b = session.LoadModule("shared");

        a.Dispose();

        // b must still be usable after a is gone.
        Assert.Equal(2, b.EntryPointCount);
        b.Dispose();
    }

    [NativeFact]
    public void Repeated_sessions_do_not_accumulate_native_objects()
    {
        // Not a precise leak check — it is a smoke test that release() is actually wired up.
        // A missing release here would exhaust memory long before 200 iterations completed.
        using var global = NewGlobal();
        for (var i = 0; i < 200; i++)
        {
            using var session = NewSession(global);
            using var module = session.LoadModuleFromSource($"m{i}", "m.slang", GoodShader);
            using var vs = module.FindEntryPoint("vertexMain");
            using var program = session.Link(module, vs);
            Assert.NotEmpty(program.GetEntryPointCodeBytes(0, 0));
        }
    }

    [NativeFact]
    public void Build_tag_matches_the_pinned_version()
    {
        // Ties the loaded binary to the pin in Directory.Build.props. Catches a stale
        // runtimes/ folder, which would otherwise present as baffling behavioural differences.
        var pinned = SlangHeader.Pinned.Version.TrimStart('v');
        Assert.Equal(pinned, SlangGlobalSession.BuildTag);
    }

    // ---------------------------------------------------------------- the pin, checked at load

    [Fact]
    public void The_pin_reaches_the_assembly_that_has_to_enforce_it()
    {
        // Two independent readings of one MSBuild property: the test assembly's metadata, which
        // SlangHeader uses to fetch the right header, and SharpSlang's own, which Create compares
        // against the payload. They are wired separately in two csproj files and nothing but this
        // notices if one of them is dropped — at which point the load check either throws on every
        // startup or silently compares the wrong thing.
        Assert.Equal(SlangHeader.Pinned.Version.TrimStart('v'), SlangGlobalSession.PinnedBuildTag);
    }

    [NativeFact]
    public void Creating_a_global_session_accepts_the_pinned_payload()
    {
        // The staged payload is the pinned one, so Create's default must not reject it. Trivially
        // true today and the reason the check can be on by default; if a version bump ever landed
        // without a re-fetch this is what would say so, instead of the vtables disagreeing quietly.
        SlangGlobalSession.RequirePinnedVersion();
        using var global = SlangGlobalSession.Create();
        Assert.Equal(SlangGlobalSession.PinnedBuildTag, SlangGlobalSession.BuildTag);
    }

    [Fact]
    public void A_payload_from_another_release_is_refused_by_name()
    {
        // The comparison and the message, which is all that can be wrong here. Staging a second
        // Slang release to provoke it for real would cost 36 MB and prove the same thing.
        var e = Assert.Throws<SlangVersionMismatchException>(
            () => SlangGlobalSession.Verify("2026.14.1", "2026.9.2"));

        Assert.Equal("2026.14.1", e.Expected);
        Assert.Equal("2026.9.2", e.Loaded);

        // Both versions in the text, not just the properties: an unhandled exception shows a
        // message, and "the wrong Slang is loaded" without saying which is a bug report with the
        // interesting half missing.
        Assert.Contains("2026.14.1", e.Message);
        Assert.Contains("2026.9.2", e.Message);
        Assert.Contains("fetch-native.ps1", e.Message);
        Assert.Contains(nameof(GlobalSessionOptions.RequirePinnedSlangVersion), e.Message);
    }

    [Fact]
    public void The_matching_case_is_not_an_exception()
    {
        SlangGlobalSession.Verify("2026.14.1", "2026.14.1");
    }

    [NativeFact]
    public void The_check_can_be_turned_off()
    {
        // The opt-out exists for a consumer who is deliberately running a different release, and
        // an option nobody exercises is an option that stops compiling. It cannot be shown to skip
        // the check here — the payload matches — only that it is honoured on the path that works.
        using var global = SlangGlobalSession.Create(
            new GlobalSessionOptions { RequirePinnedSlangVersion = false });
        Assert.NotNull(global);
    }

    [NativeFact]
    public void A_second_module_from_source_at_the_same_path_ends_in_an_internal_assert()
    {
        // U31, asserted as wrong: two modules of different names loaded from source under one path
        // in one session. The second load fails with Slang's own assertion text and no diagnostic
        // code (v2026.21, win-x64). When Slang reports it as a diagnostic, or loads both, this
        // fails: drop the caller's rule in AGENTS.md#compilation-linking-and-diagnostics.
        const string Source = """[shader("compute")] [numthreads(1, 1, 1)] void main() {}""";
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions());
        using var first = session.LoadModuleFromSource("first", "shared.slang", Source);

        var e = Assert.Throws<SlangException>(() => session.LoadModuleFromSource("second", "shared.slang", Source));
        Assert.Contains("assert failure: slang-dictionary.h(325): The key already exists in Dictionary.", e.Message, StringComparison.Ordinal);

        // Another path is fine
        using var other = session.LoadModuleFromSource("second", "other.slang", Source);
    }
}
