// Does a bake know when it has gone stale?
//
// The hole these close: ShaderPackage hashed the one source string it was handed, so editing a
// module a shader imports left every package that imports it claiming to be current. The symptom
// is a shader running the version before last, which reads as a renderer bug for as long as it
// takes someone to suspect the cooker.

using SharpSlang.Baking;

namespace SharpSlang.Tests;

public sealed class DependencyTrackingTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), $"sharpslang-deps-{Guid.NewGuid():N}");

    public DependencyTrackingTests()
    {
        Directory.CreateDirectory(_dir);

        Write("common.slang", """
            module common;
            public float3 tonemap(float3 c) { return c / (c + 1.0); }
            """);

        Write("tuning.hlsli", "#define GAIN 2.0\n");

        Write("main.slang", """
            import common;
            #include "tuning.hlsli"

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(tonemap(float3(GAIN, 0, 0)), 1); }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a locked file on Windows is not a test failure */ }
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    private ShaderPackage Bake()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions()],
            SearchPaths = [_dir],
        });
        using var module = session.LoadModule("main");
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        return ShaderPackageBaker.Bake(program, module, 0);
    }

    // ---------------------------------------------------------------- the module's own view

    [NativeFact]
    public void A_module_reports_its_imports_and_includes()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions()],
            SearchPaths = [_dir],
        });
        using var module = session.LoadModule("main");

        var names = module.DependencyFilePaths.Select(Path.GetFileName).Order().ToList();

        Assert.Equal(["common.slang", "main.slang", "tuning.hlsli"], names);
        Assert.Equal("main.slang", Path.GetFileName(module.FilePath));
    }

    [NativeFact]
    public void A_module_compiled_from_a_string_has_nothing_on_disk_to_track()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("s", "s.slang", """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        // Slang still reports the synthetic path, but nothing behind it is readable, so the bake
        // records no dependency rather than one it can never verify.
        Assert.Empty(ShaderPackageBaker.Bake(program, module, 0).Dependencies);
    }

    // ---------------------------------------------------------------- staleness

    [NativeFact]
    public void A_fresh_bake_is_current()
    {
        var package = Bake();

        Assert.Equal(3, package.Dependencies.Count);
        Assert.Empty(package.StaleDependencies());
        Assert.True(package.IsCurrent(SlangGlobalSession.BuildTag));
    }

    [NativeFact]
    public void Editing_an_imported_module_makes_the_bake_stale()
    {
        // The bug in one test. Nothing about main.slang changed.
        var package = Bake();
        Write("common.slang", """
            module common;
            public float3 tonemap(float3 c) { return c / (c + 2.0); }
            """);

        var stale = Assert.Single(package.StaleDependencies());
        Assert.Equal("common.slang", Path.GetFileName(stale));
        Assert.False(package.IsCurrent(SlangGlobalSession.BuildTag));
    }

    [NativeFact]
    public void Editing_an_included_header_makes_the_bake_stale()
    {
        var package = Bake();
        Write("tuning.hlsli", "#define GAIN 4.0\n");

        Assert.Equal("tuning.hlsli", Path.GetFileName(Assert.Single(package.StaleDependencies())));
    }

    [NativeFact]
    public void Editing_the_shader_itself_makes_the_bake_stale()
    {
        var package = Bake();
        Write("main.slang", """
            import common;
            #include "tuning.hlsli"

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(tonemap(float3(GAIN, 1, 0)), 1); }
            """);

        Assert.Equal("main.slang", Path.GetFileName(Assert.Single(package.StaleDependencies())));
    }

    [NativeFact]
    public void A_deleted_dependency_counts_as_changed()
    {
        // The package cannot be shown to be current, and assuming it is ships a stale shader.
        var package = Bake();
        File.Delete(Path.Combine(_dir, "common.slang"));

        Assert.Equal("common.slang", Path.GetFileName(Assert.Single(package.StaleDependencies())));
        Assert.False(package.IsCurrent(SlangGlobalSession.BuildTag));
    }

    [NativeFact]
    public void Touching_a_file_without_changing_it_is_not_a_change()
    {
        // Content hashes, not timestamps: a checkout or a formatter run should not rebuild
        // every shader in the project.
        var package = Bake();
        var path = Path.Combine(_dir, "common.slang");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1));

        Assert.Empty(package.StaleDependencies());
    }

    [NativeFact]
    public void A_moved_compiler_invalidates_the_bake_even_with_every_file_unchanged()
    {
        // Slang's layout rules are not stable across versions, so identical source is not enough.
        var package = Bake();

        Assert.Empty(package.StaleDependencies());
        Assert.False(package.IsCurrent("2025.13.2"));
    }

    [NativeFact]
    public void A_package_with_nothing_recorded_does_not_claim_to_be_current()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        const string Source = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """;
        using var module = session.LoadModuleFromSource("s", "s.slang", Source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        var package = ShaderPackageBaker.Bake(program, Source, 0);

        // Nothing to check, so IsCurrent says no rather than yes. The string overload's own
        // staleness check still works, and is the right one for an in-memory shader.
        Assert.False(package.IsCurrent(SlangGlobalSession.BuildTag));
        Assert.True(package.IsUpToDate(Source, SlangGlobalSession.BuildTag));
    }

    // ---------------------------------------------------------------- persistence

    [NativeFact]
    public void Dependencies_survive_the_round_trip()
    {
        var original = Bake();

        using var buffer = new MemoryStream();
        original.WriteTo(buffer);
        buffer.Position = 0;
        var restored = ShaderPackage.ReadFrom(buffer);

        Assert.Equal(
            original.Dependencies.Select(d => (d.Path, Hash: Convert.ToHexString(d.Hash))),
            restored.Dependencies.Select(d => (d.Path, Hash: Convert.ToHexString(d.Hash))));

        // And the check still works after reloading, which is the only time it matters.
        Assert.True(restored.IsCurrent(SlangGlobalSession.BuildTag));
        Write("common.slang", "module common;\npublic float3 tonemap(float3 c) { return c; }");
        Assert.False(restored.IsCurrent(SlangGlobalSession.BuildTag));
    }

    [NativeFact]
    public void The_combined_hash_does_not_depend_on_import_resolution_order()
    {
        // Two bakes of the same tree must agree, or every build looks like a change.
        Assert.Equal(
            Convert.ToHexString(Bake().SourceHash),
            Convert.ToHexString(Bake().SourceHash));
    }
}
