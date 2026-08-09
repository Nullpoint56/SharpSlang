// Reverse-interop tests: Slang calling back into managed code.
//
// This is the layer a binding generator cannot produce, and the one where a mistake is a stack or
// heap corruption rather than an exception — so it needs real tests, not smoke tests.

using PrismSlang.Hosting;
using PrismSlang.Interop;

namespace PrismSlang.Tests;

public class HostingTests
{
    private const string Library = """
        struct Light { float3 direction; float intensity; };

        float3 applyLight(float3 albedo, Light l)
        {
            return albedo * l.intensity;
        }
        """;

    private const string Main = """
        import lights;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            Light l; l.direction = float3(0, 1, 0); l.intensity = 0.75;
            return float4(applyLight(float3(1, 1, 1), l), 1.0);
        }
        """;

    [NativeFact]
    public void Slang_resolves_an_import_through_a_managed_filesystem()
    {
        // The point of L4: `import lights` is satisfied from memory, with nothing on disk.
        using var vfs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>
        {
            ["lights.slang"] = Library,
        }));

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
            FileSystem = vfs,
        });

        using var module = session.LoadModuleFromSource("main", "main.slang", Main);
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, fs);

        Assert.NotEmpty(program.GetEntryPointCodeBytes(0));
    }

    [NativeFact]
    public void Missing_file_surfaces_as_a_normal_compile_error()
    {
        // The provider returning null must read as "not found" and produce a diagnostic, not a
        // crash and not a silent success.
        using var vfs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
            FileSystem = vfs,
        });

        var ex = Assert.Throws<SlangException>(
            () => session.LoadModuleFromSource("main", "main.slang", Main));
        Assert.NotNull(ex.Diagnostics);
    }

    [NativeFact]
    public void Provider_exceptions_do_not_cross_the_native_boundary()
    {
        // A managed exception unwinding through a native frame is undefined behaviour, and on
        // Linux it terminates the process. The thunk must convert it to a failure code. If this
        // test ever crashes the host rather than failing, the catch in LoadFile has been removed.
        using var vfs = new SlangFileSystem(new ThrowingProvider());

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
            FileSystem = vfs,
        });

        Assert.Throws<SlangException>(() => session.LoadModuleFromSource("main", "main.slang", Main));
    }

    [NativeFact]
    public void Filesystem_survives_many_reads()
    {
        // Exercises the thunks repeatedly: a GCHandle mistake or a leaked native allocation per
        // call shows up here rather than in production.
        var files = new Dictionary<string, string> { ["lights.slang"] = Library };
        using var vfs = new SlangFileSystem(new InMemoryFileProvider(files));

        using var global = SlangGlobalSession.Create();

        for (var i = 0; i < 25; i++)
        {
            using var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions { Profile = "spirv_1_5" }],
                FileSystem = vfs,
            });
            using var module = session.LoadModuleFromSource($"main{i}", $"main{i}.slang", Main);
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, fs);
            Assert.NotEmpty(program.GetEntryPointCodeBytes(0));
        }
    }

    [Fact]
    public void Disposal_is_idempotent()
    {
        var vfs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        vfs.Dispose();
        vfs.Dispose();
    }

    private sealed class ThrowingProvider : ISlangFileProvider
    {
        public byte[]? TryRead(string path) => throw new InvalidOperationException("asset pack is on fire");
    }
}
