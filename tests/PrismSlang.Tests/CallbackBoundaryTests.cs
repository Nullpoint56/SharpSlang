// The one place managed code runs underneath native code.
//
// Everywhere else in the binding, PrismSlang calls Slang. SlangFileSystem is the reverse: a native
// vtable of [UnmanagedCallersOnly] thunks that Slang calls into. A managed exception unwinding
// across that boundary is undefined behaviour — on Linux the runtime does not attempt it, it kills
// the process — and the provider behind those thunks is code this package did not write. An engine
// VFS reading from a pack file has every ordinary reason to throw.
//
// So the thunks are called here the way Slang calls them: through the function pointers, with the
// arguments a confused caller would pass, and with a provider that throws.

using System.Runtime.InteropServices;
using PrismSlang.Hosting;
using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang.Tests;

public unsafe class CallbackBoundaryTests
{
    /// <summary>A provider that fails the way a real one does: from inside the read.</summary>
    private sealed class ThrowingProvider(Exception failure) : ISlangFileProvider
    {
        public int Calls { get; private set; }

        public byte[]? TryRead(string path)
        {
            Calls++;
            throw failure;
        }
    }

    private const string Importer = """
        import shared;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target { return tint(); }
        """;

    private static SlangSession CreateSession(SlangGlobalSession global, SlangFileSystem fs) =>
        global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions()],
            FileSystem = fs,
            SearchPaths = ["/shaders"],
        });

    // ---------------------------------------------------------------- a provider that throws

    [NativeTheory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(OutOfMemoryException))]
    public void An_exception_from_the_provider_becomes_a_failed_compile_not_a_dead_process(Type type)
    {
        // The test is that the process is still here to assert anything at all. OutOfMemoryException
        // is in the list because a catch-all that filters by type is the obvious way to write this
        // guard and the wrong one: the boundary has to hold for exceptions nobody anticipated.
        var provider = new ThrowingProvider((Exception)Activator.CreateInstance(type)!);
        using var fs = new SlangFileSystem(provider);
        using var global = SlangGlobalSession.Create();
        using var session = CreateSession(global, fs);

        // Slang asks the filesystem for `shared`, the provider throws, the thunk turns it into a
        // failure code, and the compile reports a missing import.
        Assert.ThrowsAny<Exception>(() => session.LoadModuleFromSource("m", "m.slang", Importer));
        Assert.True(provider.Calls > 0, "the filesystem was never consulted, so nothing was tested");
    }

    [NativeFact]
    public void The_session_still_works_after_the_provider_has_thrown()
    {
        // A boundary that survives once but leaves the runtime damaged is not a boundary. Compile
        // something real through the same session afterwards.
        var provider = new ThrowingProvider(new InvalidOperationException("no"));
        using var fs = new SlangFileSystem(provider);
        using var global = SlangGlobalSession.Create();
        using var session = CreateSession(global, fs);

        Assert.ThrowsAny<Exception>(() => session.LoadModuleFromSource("m", "m.slang", Importer));

        using var module = session.LoadModuleFromSource("ok", "ok.slang", """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        Assert.True(Spirv.SpirvModule.IsSpirv(program.GetEntryPointCodeBytes(0)));
    }

    [NativeFact]
    public void A_provider_that_returns_null_is_a_miss_rather_than_a_failure()
    {
        // The contrast case: not finding a file is normal, and must not look like an error.
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>
        {
            ["/shaders/shared.slang"] = "public float4 tint() { return float4(1, 0, 0, 1); }",
        }));
        using var global = SlangGlobalSession.Create();
        using var session = CreateSession(global, fs);

        using var module = session.LoadModuleFromSource("m", "m.slang", Importer);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        Assert.True(Spirv.SpirvModule.IsSpirv(program.GetEntryPointCodeBytes(0)));
    }

    // ---------------------------------------------------------------- the vtable, called directly

    // A plain struct, not a record: a function pointer cannot be a record field, because the
    // compiler-generated Equals would have to compare one.
    private struct Vtable
    {
        public delegate* unmanaged[Stdcall]<void*, SlangUUID*, void**, int> QueryInterface;
        public delegate* unmanaged[Stdcall]<void*, uint> AddRef;
        public delegate* unmanaged[Stdcall]<void*, uint> Release;
        public delegate* unmanaged[Stdcall]<void*, SlangUUID*, void*> CastAs;
        public delegate* unmanaged[Stdcall]<void*, sbyte*, ISlangBlob**, int> LoadFile;
    }

    /// <summary>The five slots, read out of the object exactly as Slang would.</summary>
    private static Vtable Slots(void* native)
    {
        var table = (void**)((void**)native)[0];
        return new Vtable
        {
            QueryInterface = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void**, int>)table[0],
            AddRef = (delegate* unmanaged[Stdcall]<void*, uint>)table[1],
            Release = (delegate* unmanaged[Stdcall]<void*, uint>)table[2],
            CastAs = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void*>)table[3],
            LoadFile = (delegate* unmanaged[Stdcall]<void*, sbyte*, ISlangBlob**, int>)table[4],
        };
    }

    /// <summary>
    /// The native object Slang is handed, reached the way Slang reaches it.
    /// </summary>
    /// <remarks>
    /// Through reflection because <c>SlangFileSystem.Handle</c> is internal to the package and
    /// typed as <c>ISlangFileSystem*</c>. What matters is that the bytes are the same ones a
    /// session receives, which is why the vtable is read out of them rather than reconstructed.
    /// </remarks>
    private static void* Native(SlangFileSystem fs)
    {
        var field = typeof(SlangFileSystem).GetField(
            "_native",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(field);
        return System.Reflection.Pointer.Unbox(field.GetValue(fs)!);
    }

    [Fact]
    public void Every_slot_survives_a_null_self()
    {
        // Slang will not do this. Something else in the process might, and the answer has to be a
        // return code rather than an access violation inside a managed thunk.
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        var v = Slots(Native(fs));

        void* result = null;
        var uuid = SlangIID.ISlangFileSystem;

        Assert.Equal(SlangResult.InvalidArg, v.QueryInterface(null, (SlangUUID*)&uuid, &result));
        Assert.Equal(0u, v.AddRef(null));
        Assert.Equal(0u, v.Release(null));
        Assert.True(v.CastAs(null, (SlangUUID*)&uuid) is null);

        ISlangBlob* blob = null;
        Assert.NotEqual(SlangResult.Ok, v.LoadFile(null, null, &blob));
    }

    [Fact]
    public void Every_slot_survives_a_null_out_parameter()
    {
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        var native = Native(fs);
        var v = Slots(native);
        var uuid = SlangIID.ISlangFileSystem;

        Assert.Equal(SlangResult.InvalidArg, v.QueryInterface(native, (SlangUUID*)&uuid, null));
        Assert.Equal(SlangResult.InvalidArg, v.LoadFile(native, null, null));
    }

    [Fact]
    public void Querying_an_interface_it_does_not_implement_says_so_rather_than_guessing()
    {
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        var native = Native(fs);
        var v = Slots(native);

        var unrelated = Guid.NewGuid();
        void* result = (void*)0xDEAD;
        Assert.Equal(SlangResult.NoInterface, v.QueryInterface(native, (SlangUUID*)&unrelated, &result));

        // And it cleared the out-parameter before failing, so a caller that ignores the code does
        // not walk away with the sentinel it passed in.
        Assert.True(result is null);
        Assert.True(v.CastAs(native, (SlangUUID*)&unrelated) is null);
    }

    [Fact]
    public void The_interfaces_it_does_implement_all_resolve_to_the_same_object()
    {
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        var native = Native(fs);
        var v = Slots(native);

        foreach (var iid in new[] { SlangIID.ISlangUnknown, SlangIID.ISlangCastable, SlangIID.ISlangFileSystem })
        {
            var wanted = iid;
            void* result = null;
            Assert.Equal(SlangResult.Ok, v.QueryInterface(native, (SlangUUID*)&wanted, &result));
            Assert.True(result == native);
            Assert.True(v.CastAs(native, (SlangUUID*)&wanted) == native);
        }
    }

    [Fact]
    public void Reference_counting_does_not_go_negative()
    {
        // Release deliberately does not free — the managed object owns its memory — so an
        // over-release is a number going wrong rather than a heap going wrong. It still must not
        // report a negative count as a huge unsigned one.
        using var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        var native = Native(fs);
        var v = Slots(native);

        Assert.Equal(2u, v.AddRef(native));
        Assert.Equal(1u, v.Release(native));

        for (var i = 0; i < 5; i++) Assert.Equal(0u, v.Release(native));
    }

    [NativeFact]
    public void Disposing_the_filesystem_twice_is_a_no_op()
    {
        var fs = new SlangFileSystem(new InMemoryFileProvider(new Dictionary<string, string>()));
        fs.Dispose();
        fs.Dispose();
    }
}
