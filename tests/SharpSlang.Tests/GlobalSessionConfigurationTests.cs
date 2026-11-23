// IGlobalSession's configuration and queries beyond creating sessions: a session from slangc's
// command line, the session-options digest, elapsed compile time, downstream compiler paths and
// choices, language preludes and the SPIR-V grammar. Each is judged by what it changes in Slang's
// output or behaviour, not by its getter alone.

using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

public class GlobalSessionConfigurationTests(ITestOutputHelper output)
{
    private const string Compute = """
        RWStructuredBuffer<float> result;

        [shader("compute")]
        [numthreads(4, 1, 1)]
        void computeMain(uint3 id : SV_DispatchThreadID)
        {
            result[id.x] = id.x * 2.0;
        }
        """;

    private static byte[] Compile(SlangSession session, string name)
    {
        using var module = session.LoadModuleFromSource(name, $"{name}.slang", Compute);
        using var entry = module.FindEntryPoint("computeMain");
        using var program = session.Link(module, entry);
        return program.GetEntryPointCodeBytes(0, 0);
    }

    [NativeFact]
    public void A_session_from_the_command_line_emits_what_the_same_options_emit()
    {
        using var global = SlangGlobalSession.Create();

        byte[] fromCommandLine, fromOptions;
        using (var session = global.CreateSessionFromCommandLine("-target", "spirv", "-profile", "spirv_1_3"))
        {
            fromCommandLine = Compile(session, "cl");
        }

        using (var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { SpirvVersion = SpirvVersion.V1_3 }],
        }))
        {
            fromOptions = Compile(session, "cl");
        }

        Assert.Equal(SpirvVersion.V1_3, SpirvModule.ReadVersion(fromCommandLine));
        Assert.Equal(fromOptions, fromCommandLine);
    }

    [NativeFact]
    public void An_option_Slang_does_not_know_is_refused()
    {
        using var global = SlangGlobalSession.Create();
        var e = Assert.Throws<SlangException>(() => global.CreateSessionFromCommandLine("-no-such-option"));
        output.WriteLine(e.Message);
    }

    [NativeFact]
    public void The_digest_follows_the_options()
    {
        using var global = SlangGlobalSession.Create();

        var plain = new SessionOptions { Targets = [new TargetOptions()] };
        var same = new SessionOptions { Targets = [new TargetOptions()] };
        var macro = new SessionOptions
        {
            Targets = [new TargetOptions()],
            PreprocessorMacros = new Dictionary<string, string> { ["QUALITY"] = "2" },
        };

        var a = global.GetSessionDescDigest(plain);
        var b = global.GetSessionDescDigest(same);
        var c = global.GetSessionDescDigest(macro);
        output.WriteLine($"{a.Length} bytes: {Convert.ToHexString(a)} / {Convert.ToHexString(c)}");

        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [NativeFact]
    public void Compiling_adds_to_the_elapsed_time()
    {
        using var global = SlangGlobalSession.Create();
        var before = global.CompilerElapsedTime;

        using (var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] }))
        {
            Compile(session, "et");
        }

        var after = global.CompilerElapsedTime;
        output.WriteLine($"before {before}, after {after}");

        Assert.True(after.Total > before.Total);
        Assert.InRange(after.Downstream, TimeSpan.Zero, after.Total);
    }

    [DxcFact]
    public void The_downstream_compiler_path_decides_which_library_Slang_loads()
    {
        var dxc = SlangPassThrough.SLANG_PASS_THROUGH_DXC;
        var empty = Directory.CreateTempSubdirectory("sharpslang-dxc-");
        try
        {
            using var global = SlangGlobalSession.Create();

            // Slang answers with the library it would load: by its usual search, the dxc beside us.
            var found = global.GetDownstreamCompilerPath(dxc);
            output.WriteLine($"found: {found}");
            Assert.NotNull(found);
            Assert.Equal("dxcompiler", Path.GetFileNameWithoutExtension(found), ignoreCase: true);

            // A directory with no dxc in it: none is found, though one is loaded in this process.
            global.SetDownstreamCompilerPath(dxc, empty.FullName);
            Assert.Null(global.GetDownstreamCompilerPath(dxc));
            Assert.False(global.IsDownstreamCompilerAvailable(dxc));

            // Its own directory: that file again.
            global.SetDownstreamCompilerPath(dxc, Path.GetDirectoryName(found));
            Assert.Equal(found, global.GetDownstreamCompilerPath(dxc), ignoreCase: true);
            Assert.True(global.IsDownstreamCompilerAvailable(dxc));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => global.SetDownstreamCompilerPath(SlangPassThrough.SLANG_PASS_THROUGH_NONE, empty.FullName));
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }
    [NativeFact]
    public void A_language_prelude_heads_the_generated_code()
    {
        const string Marker = "// sharpslang-prelude-marker";

        using var global = SlangGlobalSession.Create();
        output.WriteLine($"default C++ prelude: {global.GetLanguagePrelude(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_CPP)?.Length ?? 0} chars");

        global.SetLanguagePrelude(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_CPP, Marker + "\n");
        Assert.Equal(Marker + "\n", global.GetLanguagePrelude(SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_CPP));

        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Format = SlangCompileTarget.SLANG_CPP_SOURCE }],
        });

        var code = System.Text.Encoding.UTF8.GetString(Compile(session, "pre"));
        Assert.StartsWith(Marker, code.TrimStart('﻿'), StringComparison.Ordinal);
    }

    [NativeFact]
    public void The_default_downstream_compiler_reads_back_what_was_set()
    {
        using var global = SlangGlobalSession.Create();
        var cpp = SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_CPP;
        foreach (var language in new[] { SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_HLSL, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_C, cpp, SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_CUDA })
            output.WriteLine($"{language} defaults to {global.GetDefaultDownstreamCompiler(language)}");

        global.SetDefaultDownstreamCompiler(cpp, SlangPassThrough.SLANG_PASS_THROUGH_CLANG);
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_CLANG, global.GetDefaultDownstreamCompiler(cpp));

        global.SetDefaultDownstreamCompiler(cpp, SlangPassThrough.SLANG_PASS_THROUGH_GCC);
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_GCC, global.GetDefaultDownstreamCompiler(cpp));

        // Slang refuses to set none for a language it has no default for.
        var hlsl = SlangSourceLanguage.SLANG_SOURCE_LANGUAGE_HLSL;
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_NONE, global.GetDefaultDownstreamCompiler(hlsl));
        Assert.Throws<SlangException>(() => global.SetDefaultDownstreamCompiler(hlsl, SlangPassThrough.SLANG_PASS_THROUGH_NONE));
    }

    [NativeFact]
    public void The_compiler_for_a_transition_reads_back_what_was_set()
    {
        using var global = SlangGlobalSession.Create();
        var (from, to) = (SlangCompileTarget.SLANG_HLSL, SlangCompileTarget.SLANG_DXIL);

        var initial = global.GetDownstreamCompilerForTransition(from, to);
        output.WriteLine($"HLSL to DXIL goes through {initial}");
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_DXC, initial);

        global.SetDownstreamCompilerForTransition(from, to, SlangPassThrough.SLANG_PASS_THROUGH_FXC);
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_FXC, global.GetDownstreamCompilerForTransition(from, to));

        // None is stored as set, not read as "Slang's choice".
        global.SetDownstreamCompilerForTransition(from, to, SlangPassThrough.SLANG_PASS_THROUGH_NONE);
        Assert.Equal(SlangPassThrough.SLANG_PASS_THROUGH_NONE, global.GetDownstreamCompilerForTransition(from, to));

        global.SetDownstreamCompilerForTransition(from, to, initial);
        Assert.Equal(initial, global.GetDownstreamCompilerForTransition(from, to));
    }

    [NativeFact]
    public void A_host_library_loader_decides_what_Slang_loads()
    {
        var asked = new List<string>();
        using var refusing = new Hosting.SlangSharedLibraryLoader(name => { asked.Add(name); return 0; });
        var found = new List<string>();
        using var probing = new Hosting.SlangSharedLibraryLoader(name =>
        {
            found.Add(name);
            return System.Runtime.InteropServices.NativeLibrary.TryLoad(name, typeof(SlangGlobalSession).Assembly, null, out var h) ? h : 0;
        });

        var spirvOpt = SlangPassThrough.SLANG_PASS_THROUGH_SPIRV_OPT;
        var noCheck = new GlobalSessionOptions { RequireSpirvOptimizer = false };

        // A loader that has nothing: spirv-opt is not found, though it is beside the compiler.
        using (var global = SlangGlobalSession.Create(noCheck))
        {
            Assert.Null(global.SharedLibraryLoader);
            global.SetSharedLibraryLoader(refusing);
            Assert.Same(refusing, global.SharedLibraryLoader);
            Assert.False(global.IsDownstreamCompilerAvailable(spirvOpt));
            Assert.Throws<InvalidOperationException>(refusing.Dispose);
        }

        output.WriteLine($"asked for: {string.Join(", ", asked)}");
        Assert.Contains(asked, n => n.Contains("slang-glslang", StringComparison.Ordinal));
        Assert.Equal(1, refusing.NativeReferenceCount);

        // A loader that probes beside the binding: found, through it.
        using (var global = SlangGlobalSession.Create(noCheck))
        {
            global.SetSharedLibraryLoader(probing);
            Assert.True(global.IsDownstreamCompilerAvailable(spirvOpt));
            Assert.True(Hosting.SlangSharedLibraryLoader.LiveLibraries > 0);

            global.SetSharedLibraryLoader(null);
            Assert.Null(global.SharedLibraryLoader);
        }

        output.WriteLine($"loaded: {string.Join(", ", found)}; live libraries after {Hosting.SlangSharedLibraryLoader.LiveLibraries}");
        Assert.Contains(found, n => n.Contains("slang-glslang", StringComparison.Ordinal));
        Assert.Equal(1, probing.NativeReferenceCount);
        Assert.Equal(0, Hosting.SlangSharedLibraryLoader.LiveLibraries);
    }

    [NativeFact]
    public void A_missing_SPIRV_grammar_file_is_refused()
    {
        using var global = SlangGlobalSession.Create();
        var missing = Path.Combine(Path.GetTempPath(), $"sharpslang-{Guid.NewGuid():N}.json");

        var e = Assert.Throws<SlangException>(() => global.SetSpirvCoreGrammar(missing));
        output.WriteLine(e.Message);
    }
}
