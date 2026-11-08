// Compile once, load many: Slang's binary IR as a module cache.
//
// A shader library where twenty entry points import the same six modules parses those six modules
// twenty times. Serializing a checked module to IR and reloading it skips the parse and the
// semantic check, which is the whole of Tier 2's "precompiled-module cache" and was bound in L0
// and reachable from nothing.
//
// The interesting part is not that it round-trips. It is what the blob refuses: it is tied to the
// compiler build that wrote it, so a cache entry from a previous Slang release is not stale, it is
// unusable — and a cook that treats those as the same thing ships the version before last.

using SharpSlang.Interop;

namespace SharpSlang.Tests;

public class ModuleSerializationTests(ITestOutputHelper output)
{
    private const string Library = """
        struct MsSurface { float3 albedo; float roughness; };

        float3 msShade(MsSurface s, float3 light) { return s.albedo * light; }
        """;

    private const string Consumer = """
        import mslib;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            MsSurface s; s.albedo = 1; s.roughness = 0;
            return float4(msShade(s, 1), 1);
        }
        """;

    private static (SlangGlobalSession, SlangSession) NewSession()
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        return (global, session);
    }

    [NativeFact]
    public void A_module_serializes_to_IR_and_loads_back()
    {
        byte[] ir;

        var (global, session) = NewSession();
        using (global) using (session)
        {
            using var module = session.LoadModuleFromSource("mslib", "mslib.slang", Library);
            using var blob = module.Serialize();
            ir = blob.ToArray();
        }

        output.WriteLine($"{ir.Length} bytes of IR for a two-declaration module");
        Assert.NotEmpty(ir);

        // A fresh session, with no memory of the source at all.
        var (global2, session2) = NewSession();
        using (global2) using (session2)
        {
            using var reloaded = session2.LoadModuleFromIRBlob("mslib", "mslib.slang", ir);
            Assert.Equal("mslib", reloaded.Name);

            // And it is a real module: something that imports it links and generates code.
            using var consumer = session2.LoadModuleFromSource("msuse", "msuse.slang", Consumer);
            using var entry = consumer.FindEntryPoint("fragmentMain");
            using var program = session2.Link(consumer, entry);
            Assert.NotEmpty(program.GetEntryPointCodeBytes(0));
        }
    }

    [NativeFact]
    public void The_blob_says_what_it_is_without_being_loaded()
    {
        var (global, session) = NewSession();
        using (global) using (session)
        {
            using var module = session.LoadModuleFromSource("mslib", "mslib.slang", Library);
            using var blob = module.Serialize();

            var info = session.ReadModuleInfo(blob.Span);
            output.WriteLine($"name={info.Name} version={info.Version} compiler={info.CompilerVersion}");

            Assert.Equal("mslib", info.Name);

            // The field that decides whether a cache entry is usable at all. It is the compiler's
            // build tag, which is what SharpSlang pins — so a cache written by another Slang
            // release announces itself here rather than failing later.
            Assert.Equal(SlangGlobalSession.BuildTag, info.CompilerVersion);
        }
    }

    [NativeFact]
    public void Not_a_module_is_refused_rather_than_misread()
    {
        var (global, session) = NewSession();
        using (global) using (session)
        {
            var garbage = new byte[256];
            Random.Shared.NextBytes(garbage);

            Assert.Throws<SlangException>(() => session.ReadModuleInfo(garbage));
            Assert.Throws<SlangException>(
                () => session.LoadModuleFromIRBlob("junk", "junk.slang", garbage));

            // And the session survives it — a rejected cache entry must be recoverable, since the
            // whole point of a cache is that you fall back to compiling.
            using var module = session.LoadModuleFromSource("mslib", "mslib.slang", Library);
            Assert.Equal("mslib", module.Name);
        }
    }

    [NativeFact]
    public void An_empty_blob_is_an_argument_error_not_a_native_call()
    {
        var (global, session) = NewSession();
        using (global) using (session)
        {
            Assert.Throws<ArgumentException>(
                () => session.LoadModuleFromIRBlob("empty", "empty.slang", []));
            Assert.Throws<ArgumentException>(() => session.ReadModuleInfo([]));
            Assert.False(session.IsBinaryModuleUpToDate("nowhere.slang", []));
        }
    }

    [NativeFact]
    public void Serializing_to_a_file_writes_the_same_bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sharpslang-{Guid.NewGuid():N}.slang-module");

        var (global, session) = NewSession();
        using (global) using (session)
        {
            using var module = session.LoadModuleFromSource("mslib", "mslib.slang", Library);
            using var blob = module.Serialize();

            module.WriteToFile(path);
            try
            {
                Assert.Equal(blob.ToArray(), File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [NativeFact]
    public void The_staleness_check_answers_from_the_sessions_file_cache_not_from_disk()
    {
        // The finding, and it decides whether the API is usable: `isBinaryModuleUpToDate` is
        // answered against the session's own view of the file system, which it populated when it
        // loaded the module. Ask the session that produced the IR and everything is current
        // forever — including a path with no file behind it. Ask a fresh session, the one a cook
        // actually has at startup, and it reads disk and answers correctly.
        //
        // Nothing in the name says so, and the wrong reading is the natural one: load, serialize,
        // ask. That sequence returns true unconditionally.
        var directory = Directory.CreateTempSubdirectory("sharpslang-modcache-");
        try
        {
            var source = Path.Combine(directory.FullName, "mslib.slang");
            var absent = Path.Combine(directory.FullName, "absent.slang");
            File.WriteAllText(source, Library);

            byte[] ir;

            var global = SlangGlobalSession.Create();
            var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions()],
                SearchPaths = [directory.FullName],
            });

            using (global) using (session)
            {
                using var fromFile = session.LoadModule("mslib");
                using var blob = fromFile.Serialize();
                ir = blob.ToArray();

                Assert.True(session.IsBinaryModuleUpToDate(source, ir));

                File.WriteAllText(source, Library + "\n// touched\n");
                File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(5));

                // Both of these are the cache talking. The second is the one that gives it away:
                // there is no file at this path and never was.
                Assert.True(session.IsBinaryModuleUpToDate(source, ir));
                Assert.True(session.IsBinaryModuleUpToDate(absent, ir));

                // A malformed blob is rejected regardless — that part is about the bytes, not the
                // file, so the cache has nothing to say about it.
                var garbage = new byte[512];
                Random.Shared.NextBytes(garbage);
                Assert.False(session.IsBinaryModuleUpToDate(source, garbage));
            }

            var (freshGlobal, freshSession) = NewSession();
            using (freshGlobal) using (freshSession)
            {
                Assert.False(freshSession.IsBinaryModuleUpToDate(source, ir));
                Assert.False(freshSession.IsBinaryModuleUpToDate(absent, ir));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [NativeFact]
    public void A_module_reports_the_name_and_identity_Slang_gave_it()
    {
        var directory = Directory.CreateTempSubdirectory("sharpslang-modid-");
        try
        {
            var source = Path.Combine(directory.FullName, "mslib.slang");
            File.WriteAllText(source, Library);

            var (global, session) = NewSession();
            using (global) using (session)
            {
                // Loaded under a name deliberately unlike the file's, because that is when the
                // difference between the two is visible at all.
                using var module = session.LoadModuleFromSource("loadedAs", source, Library);

                output.WriteLine($"Name={module.Name} DeclaredName={module.DeclaredName} "
                    + $"UniqueIdentity={module.UniqueIdentity}");

                Assert.Equal("loadedAs", module.Name);
                Assert.NotNull(module.UniqueIdentity);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
