// The rules SlangObject exists to enforce, asserted rather than assumed.
//
// Both ownership bugs in docs §9.1 were found by running the suite on a second platform, not by a
// test — glibc aborted on a double release that the Windows heap had tolerated silently since the
// first commit. The fixes are load-bearing and none of them was pinned:
//
//   - No finalizer. Slang's objects form a hierarchy the GC knows nothing about and finalizes in
//     arbitrary order, so a finalizer can release a module into a session that has already torn
//     down its allocator. Abandoning an object must leak, which is findable, rather than corrupt,
//     which is not.
//   - Every wrapper holds a managed reference to its owner, so the owner cannot be collected first.
//   - loadModule returns a *borrowed* pointer, unlike every other Slang out-parameter, so the
//     wrapper takes its own reference on adoption.
//   - Dispose is idempotent and use-after-dispose throws rather than dereferencing.
//
// A regression in any of these is heap corruption, which does not fail politely. That is the reason
// to have the tests and also the reason they look paranoid.

using System.Runtime.CompilerServices;
using PrismSlang.Interop;
using PrismSlang.Reflection;
using PrismSlang.Spirv;

namespace PrismSlang.Tests;

public class LifetimeTests
{
    private const string Source = """
        struct Uniforms { float4 tint; };
        ConstantBuffer<Uniforms> uniforms;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target { return uniforms.tint; }
        """;

    private static SlangSession NewSession(SlangGlobalSession global) =>
        global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });

    // ---------------------------------------------------------------- dispose is idempotent

    [NativeFact]
    public void Disposing_twice_releases_once()
    {
        // Interlocked.Exchange is what makes this safe; without it the second Dispose releases a
        // reference nobody holds, which is the exact shape of the linux-x64 abort.
        using var global = SlangGlobalSession.Create();
        var session = NewSession(global);
        var module = session.LoadModuleFromSource("m", "m.slang", Source);
        var ep = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, ep);

        foreach (var disposable in new IDisposable[] { program, ep, module, session })
        {
            disposable.Dispose();
            disposable.Dispose();
            disposable.Dispose();
        }
    }

    [NativeFact]
    public void Disposing_concurrently_releases_once()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        var module = session.LoadModuleFromSource("m", "m.slang", Source);

        Parallel.For(0, 32, _ => module.Dispose());

        Assert.True(module.IsDisposed);
    }

    [NativeFact]
    public void IsDisposed_says_so()
    {
        using var global = SlangGlobalSession.Create();
        var session = NewSession(global);

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        Assert.Contains("disposed", session.ToString());
    }

    // ---------------------------------------------------------------- use after dispose

    [NativeFact]
    public void Using_a_disposed_object_throws_instead_of_dereferencing()
    {
        // Every one of these would be an access violation if Raw returned the stale pointer.
        using var global = SlangGlobalSession.Create();
        var session = NewSession(global);
        var module = session.LoadModuleFromSource("m", "m.slang", Source);
        var ep = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, ep);

        program.Dispose();
        Assert.Throws<ObjectDisposedException>(() => program.GetEntryPointCodeBytes(0));
        Assert.Throws<ObjectDisposedException>(() => program.Reflect());

        module.Dispose();
        Assert.Throws<ObjectDisposedException>(() => module.FindEntryPoint("fragmentMain"));

        session.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => session.LoadModuleFromSource("x", "x.slang", Source));

        ep.Dispose();
    }

    [NativeFact]
    public void A_disposed_global_session_cannot_make_a_session()
    {
        var global = SlangGlobalSession.Create();
        global.Dispose();

        Assert.Throws<ObjectDisposedException>(() => NewSession(global));
    }

    [NativeFact]
    public void A_disposed_blob_cannot_be_read()
    {
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        var blob = program.GetTargetCode();
        Assert.True(blob.ToArray().Length > 0);

        blob.Dispose();
        Assert.Throws<ObjectDisposedException>(() => blob.ToArray());
    }

    // ---------------------------------------------------------------- disposal order

    [NativeFact]
    public void An_owner_may_be_disposed_before_the_things_it_owns()
    {
        // Out of order on purpose, outermost first. Under COM rules each object holds its own
        // reference to whatever it needs, so releasing our reference to the session does not
        // destroy it while a module is still alive. If that were wrong, this is a heap abort.
        var global = SlangGlobalSession.Create();
        var session = NewSession(global);
        var module = session.LoadModuleFromSource("m", "m.slang", Source);
        var ep = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, ep);

        global.Dispose();
        session.Dispose();
        module.Dispose();
        ep.Dispose();
        program.Dispose();
    }

    [NativeFact]
    public void A_program_outlives_the_module_it_was_linked_from()
    {
        // The realistic version of the same thing: a cooker links, drops the module, and only then
        // asks for code — which has to still work, because the program holds its own references.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);

        LinkedProgram program;
        using (var module = session.LoadModuleFromSource("m", "m.slang", Source))
        using (var ep = module.FindEntryPoint("fragmentMain"))
        {
            program = session.Link(module, ep);
        }

        using (program)
        {
            Assert.True(SpirvModule.IsSpirv(program.GetEntryPointCodeBytes(0)));
            Assert.NotEmpty(program.Reflect().Parameters);
        }
    }

    [NativeFact]
    public void A_reflection_snapshot_outlives_everything_it_came_from()
    {
        // The whole reason L3 projects to managed data instead of holding reflection pointers:
        // those are interior pointers owned by the program, and this is what would be a
        // use-after-free if any of them had escaped the walk. Docs §4.4.
        ProgramLayout layout;
        using (var global = SlangGlobalSession.Create())
        using (var session = NewSession(global))
        using (var module = session.LoadModuleFromSource("m", "m.slang", Source))
        using (var ep = module.FindEntryPoint("fragmentMain"))
        using (var program = session.Link(module, ep))
        {
            layout = program.Reflect();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var parameter = Assert.Single(layout.Parameters);
        Assert.Equal("uniforms", parameter.Name);
        Assert.NotEmpty(layout.ToJson());
    }

    // ---------------------------------------------------------------- the garbage collector

    [NativeFact]
    public void Abandoning_objects_leaks_rather_than_corrupting()
    {
        // The regression test for removing the finalizer. Build a full hierarchy, drop every
        // reference without disposing anything, and make the GC work — repeatedly, so a finalizer
        // reintroduced anywhere in the chain gets its chance to release into a torn-down owner.
        // A pass here means a leak until process exit, which is the intended trade.
        for (var round = 0; round < 8; round++)
        {
            Abandon();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }

        // Still alive, and Slang still works.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, ep);

        Assert.True(SpirvModule.IsSpirv(program.GetEntryPointCodeBytes(0)));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Abandon()
        {
            var global = SlangGlobalSession.Create();
            var session = NewSession(global);
            var module = session.LoadModuleFromSource("m", "m.slang", Source);
            var ep = module.FindEntryPoint("fragmentMain");
            var program = session.Link(module, ep);

            // Touch everything so nothing is optimised away, then let all of it go.
            Assert.True(program.GetEntryPointCodeBytes(0).Length > 0);
        }
    }

    [NativeFact]
    public void A_child_keeps_its_owner_reachable()
    {
        // The other half of removing the finalizer: a wrapper holds a managed reference to its
        // owner, so the owner cannot be collected while the child is alive. Without it the GC is
        // free to collect a session whose module is still in use.
        var (child, owner) = MakeChild();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        Assert.True(owner.IsAlive, "the session was collected while a module from it was alive");

        // And the module is still usable, which is the point of keeping the owner around.
        using (child) Assert.NotNull(child.FindEntryPoint("fragmentMain"));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static (SlangModule Child, WeakReference Owner) MakeChild()
        {
            var global = SlangGlobalSession.Create();
            var session = NewSession(global);
            return (session.LoadModuleFromSource("m", "m.slang", Source), new WeakReference(session));
        }
    }

    // ---------------------------------------------------------------- the borrowed pointer

    [NativeFact]
    public void A_module_can_be_disposed_and_loaded_again()
    {
        // loadModule returns a borrowed pointer — the session retains its modules — so the wrapper
        // takes its own reference on adoption. Disposing must therefore not invalidate the
        // session's copy: loading the same module again has to work.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);

        for (var i = 0; i < 4; i++)
        {
            using var module = session.LoadModuleFromSource("m", "m.slang", Source);
            using var ep = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, ep);
            Assert.True(program.GetEntryPointCodeBytes(0).Length > 0);
        }
    }

    [NativeFact]
    public void Two_wrappers_over_the_same_module_can_both_be_disposed()
    {
        // Same module name, twice, from one session: Slang hands back the same retained object.
        // Both wrappers took a reference, so both must be able to release one.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);

        var first = session.LoadModuleFromSource("m", "m.slang", Source);
        var second = session.LoadModuleFromSource("m", "m.slang", Source);

        first.Dispose();

        // The second is still live, which it would not be if the first had over-released.
        using (var ep = second.FindEntryPoint("fragmentMain"))
        using (var program = session.Link(second, ep))
        {
            Assert.True(program.GetEntryPointCodeBytes(0).Length > 0);
        }

        second.Dispose();
    }
}
