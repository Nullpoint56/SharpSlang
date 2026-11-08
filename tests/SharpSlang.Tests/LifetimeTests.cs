// The rules SlangObject exists to enforce, asserted rather than assumed.
//
// Both ownership bugs in docs  were found by running the suite on a second platform, not by a
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
using SharpSlang.Interop;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

namespace SharpSlang.Tests;

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
        Assert.Throws<ObjectDisposedException>(blob.ToArray);
    }

    // ---------------------------------------------------------------- disposal order

    [NativeFact]
    public unsafe void Every_child_holds_a_native_reference_to_its_owner()
    {
        // The measurement the test below used to rest on a convention for.
        //
        // "Each object holds its own reference to what it needs" was read off COM convention and
        // never checked. On 2026.14.1 and 2026.21 it is true of some of Slang's objects and false
        // of others: ISession retains IGlobalSession, and IModule does *not* retain ISession. So
        // disposing a session while a module was alive dropped the last reference and destroyed
        // it. Windows and both Linux allocators tolerated the use-after-free; macOS did not, and
        // the test below is where its host died.
        //
        // SlangObject now takes a native reference on its owner, which is what makes the ordering
        // guarantee true. Asserted as a delta rather than an absolute: the absolute counts include
        // Slang's own internal references — a module sits at 4 before anything of ours — and those
        // are not ours to pin.
        using var global = SlangGlobalSession.Create();
        using var session = NewSession(global);

        var sessionBefore = RefCount(session);
        var globalBefore = RefCount(global);
        using var module = session.LoadModuleFromSource("rc", "rc.slang", Source);
        Assert.True(RefCount(session) > sessionBefore,
            "loading a module did not retain its session. Disposing the session first is then a "
            + "use-after-free, which is what SlangObject's owner reference exists to prevent.");

        // And its grandparent. The immediate owner is not enough on its own — see AGENTS.md#ownership-and-lifetime and the
        // test below — so the chain is held whole and this is where that is visible.
        Assert.True(RefCount(global) > globalBefore,
            "loading a module did not retain the global session. A module only protects it while "
            + "the session wrapper is undisposed, and disposal order is not ours to dictate.");

        var moduleBefore = RefCount(module);
        using var entry = module.FindEntryPoint("fragmentMain");
        Assert.True(RefCount(module) > moduleBefore, "an entry point did not retain its module");

        var sessionBeforeLink = RefCount(session);
        using var program = session.Link(module, entry);
        Assert.True(RefCount(session) > sessionBeforeLink,
            "a linked program did not retain its session");

        // addRef then release leaves the count where it was and reports it on the way back down.
        static uint RefCount(SlangObject o)
        {
            var p = (SharpSlang.Native.ISlangUnknown*)o.RawForTests;
            p->addRef();
            return p->release();
        }
    }

    [NativeFact]
    public unsafe void The_global_session_is_never_destroyed_from_inside_the_linkage()
    {
        // The invariant the test below rests on, checked one release before the one that would
        // break it — deliberately, so a regression fails as an assertion instead of as a dead test
        // host. Three macOS runs were spent learning that difference.
        //
        // Slang destroys the global session's RootASTBuilder from inside Slang::Linkage::~Linkage()
        // if the Linkage is holding the last reference to it, and the two AST arenas then
        // double-free a witness table:
        //
        //     Slang::WitnessTable::~WitnessTable()      <- free(): checksum botch
        //     Slang::RootASTBuilder::~RootASTBuilder()
        //     Slang::Linkage::~Linkage()
        //
        // Holding a reference on the immediate owner is not enough to prevent it, because that
        // chain is only transitive while the intermediate wrappers are alive — dispose the session
        // early and its grandchildren stop protecting the global session. Every wrapper therefore
        // holds a reference on the whole chain above it, and the measurement below is the
        // difference: 2 with that, 1 without, and 1 is the crash.
        var global = SlangGlobalSession.Create();
        var session = NewSession(global);
        var module = session.LoadModuleFromSource("m", "m.slang", Source);
        var ep = module.FindEntryPoint("fragmentMain");
        var program = session.Link(module, ep);

        // Read while it is unambiguously alive; Raw throws once the wrapper is disposed.
        var globalPtr = (SharpSlang.Native.ISlangUnknown*)global.RawForTests;

        global.Dispose();
        session.Dispose();
        module.Dispose();
        ep.Dispose();

        globalPtr->addRef();
        var remaining = globalPtr->release();

        Assert.True(remaining > 1,
            $"the global session is down to {remaining} reference(s) with a linked program still "
            + "alive, so the last release will destroy it from inside Slang::Linkage::~Linkage() "
            + "and double-free a witness table. SlangObject must hold a reference on every "
            + "ancestor, not only the immediate owner —");

        program.Dispose();
    }

    [NativeFact]
    public void An_owner_may_be_disposed_before_the_things_it_owns()
    {
        // Out of order on purpose, outermost first. This is only safe because SlangObject takes a
        // native reference on its owner — Slang itself does not guarantee it, and the test above
        // is the measurement. Before that reference existed this was a use-after-free that three
        // of four platforms happened to survive.
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
    public unsafe void Nothing_leaks_across_repeated_compile_cycles()
    {
        // The canary  made possible, and the reason it is a single number.
        //
        // Every wrapper now holds a native reference on its *whole* ancestor chain, so anything
        // left unreleased anywhere in the hierarchy — a session, a module, an entry point, a blob —
        // is still holding a reference to the global session. Its refcount is therefore a sentinel
        // for the entire tree: a cycle that gives back everything it took returns the count to
        // where it started, and one that does not has something below it still alive.
        //
        // Worth having because a refcount imbalance is invisible to every other test here. It does
        // not crash, it does not corrupt, and it does not produce a wrong answer — it just eats
        // memory until a batch cook dies, which is the failure mode a cooker actually meets. The
        // ownership rules changed twice in one day; nothing was watching this.
        using var global = SlangGlobalSession.Create();
        var globalPtr = (SharpSlang.Native.ISlangUnknown*)global.RawForTests;

        // One first, because the opening compile legitimately populates caches later ones reuse.
        // The count after it is the level a balanced cycle has to return to, not the count before.
        Cycle();
        var settled = Count(globalPtr);

        for (var i = 0; i < 24; i++) Cycle();

        Assert.Equal(settled, Count(globalPtr));

        void Cycle()
        {
            using var session = NewSession(global);
            using var module = session.LoadModuleFromSource("m", "m.slang", Source);
            using var ep = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, ep);
            using var code = program.GetTargetCode();
            Assert.NotEmpty(code.ToArray());
        }

        static uint Count(SharpSlang.Native.ISlangUnknown* p)
        {
            p->addRef();
            return p->release();
        }
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
        // use-after-free if any of them had escaped the walk. AGENTS.md#reflection-snapshot.
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
