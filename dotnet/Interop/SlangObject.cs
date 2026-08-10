// L1 — lifetime for Slang's COM-style objects.
//
// The generated layer exposes addRef() and release() but says nothing about when to call them.
// That gap is not something a binding generator can close: ownership lives in prose in slang.h,
// not in the signatures. This file turns it into types.
//
// Two populations, and conflating them is a use-after-free:
//
//   OWNED, ref-counted     Anything Slang hands back through an out-parameter or as a
//                          COM-interface return: IGlobalSession, ISession, IModule, IEntryPoint,
//                          IComponentType, ISlangBlob. COM convention says these arrive with a
//                          reference already taken, and the caller must release exactly once.
//                          These get a SlangObject wrapper.
//
//   BORROWED, not counted  Reflection pointers (ProgramLayout*, SlangReflectionTypeLayout*, ...).
//                          They are interior pointers owned by the IComponentType that produced
//                          them and have no refcount at all. They must NEVER be wrapped here —
//                          releasing one would corrupt the heap, and holding one past its owner
//                          is the use-after-free docs §4.4 is about. They stay inside L3 and are
//                          projected to a managed snapshot instead.
//
// Why IDisposable and not SafeHandle: these are not OS handles. SafeHandle exists to defeat
// handle-recycling attacks and to survive P/Invoke marshalling, and pays interlocked ref-counting
// plus a CriticalFinalizerObject for it. Neither applies. A plain disposable with an explicit
// release is both faster and easier to reason about.
//
// Why NO finalizer — learned the hard way, see below.
//
// This type originally had `~SlangObject() => Release();` as a safety net for a forgotten Dispose.
// That is actively dangerous here, and linux-x64 proved it: the test host died with
// `free(): corrupted unsorted chunks`, while the same code appeared healthy on Windows because its
// heap does not check as aggressively.
//
// The reason is that Slang's objects form an ownership hierarchy — IGlobalSession owns ISession
// owns IModule owns IEntryPoint — but **the GC finalizes in arbitrary order**. Nothing stops it
// finalizing the global session first and then calling release() on a module whose owner has
// already torn down its allocator. That is a use-after-free, and a finalizer turns a leak (benign,
// findable) into heap corruption (silent, catastrophic, and not reproducible on demand).
//
// So: no finalizer. Two things replace it.
//   1. Every wrapper holds a managed reference to its owner, so the owner cannot be collected
//      while a child is alive, and a *native* reference on every ancestor above it, so none of
//      them can be destroyed early or in the wrong order. See _ancestors — the native half took
//      two more platforms and two more findings to get right (§9.25, §9.26).
//   2. Failing to Dispose leaks until process exit. That is the correct trade: a leak is a bug you
//      can find with a memory profiler, whereas corruption is a bug you find in a crash dump from
//      a player six months later.

using PrismSlang.Native;

namespace PrismSlang.Interop;

/// <summary>
/// Base for an owned reference to a Slang COM object. Disposing releases the native reference.
/// </summary>
public abstract unsafe class SlangObject : IDisposable
{
    // Every Slang interface begins with `void** lpVtbl` and inherits ISlangUnknown's three slots,
    // so a release can always go through ISlangUnknown* regardless of the concrete interface.
    // Held as nint so Dispose and the finalizer can race safely through Interlocked.
    private nint _unknown;
    private readonly string _what;

    // Keeps the owning object alive for at least as long as this one. Slang's objects form a
    // hierarchy the GC knows nothing about, so without this a session could be collected while a
    // module created from it is still in use.
    private readonly SlangObject? _owner;

    // A *native* reference on every ancestor, nearest first. Worth reading before touching any of
    // this, because two separate bugs live here and the second one hides behind the fix for the
    // first.
    //
    // The managed reference above stops the GC collecting the owner's wrapper. It does nothing
    // about the owner's native refcount, and the two were assumed to be the same problem because
    // COM convention says a child retains its parent. Measured on 2026.14.1, that is true of some
    // of Slang's objects and false of others:
    //
    //     global, alone                  1
    //     global, after CreateSession    2      <- ISession does retain IGlobalSession
    //     session, alone                 1
    //     session, after LoadModule      1      <- IModule does NOT retain ISession
    //     session, after Link            1      <- nor does the linked program
    //
    // So `session.Dispose()` while a module was still alive dropped the last reference and
    // destroyed it, leaving every module and program pointing into freed memory (§9.25).
    //
    // Holding a reference on the *immediate* owner fixed that and did not fix the crash, because
    // the chain it builds is only transitive while the intermediate wrappers are alive. Dispose the
    // session early and its grandchildren stop protecting the global session — so the last release
    // of all destroys the Linkage, and Slang destroys the global session from inside
    // `Slang::Linkage::~Linkage()`. Its `RootASTBuilder` and the Linkage's own `ASTBuilder` then
    // double-free a witness table between them:
    //
    //     Slang::WitnessTable::~WitnessTable()          <- free(): checksum botch
    //     Slang::ASTBuilder::~ASTBuilder()
    //     Slang::RootASTBuilder::~RootASTBuilder()      <- the global session's arena
    //     Slang::Linkage::~Linkage()
    //     non-virtual thunk to Slang::Linkage::release()
    //
    // Slang requires an IGlobalSession to outlive the Linkages made from it, and its own refcount
    // does not enforce that. So each wrapper holds a reference on the whole chain above it, and
    // Dispose releases them nearest-first: whatever is released last, an ancestor is always
    // destroyed *after* its descendants and at the top level, never nested inside one of their
    // destructors. That is what makes "disposal order does not matter" true (§9.26).
    //
    // Pointers are captured here rather than read from _owner later, because by then the owner's
    // wrapper may be disposed and Raw would throw. Depth is at most four.
    private readonly nint[] _ancestors;

    // Releasing native objects while the runtime is tearing down a process can run after the
    // native library has already been unloaded. Leaking at exit is harmless; crashing is not.
    private static volatile bool s_processExiting;

    static SlangObject()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => s_processExiting = true;
        AppDomain.CurrentDomain.DomainUnload += static (_, _) => s_processExiting = true;
    }

    private protected SlangObject(void* owned, string what, SlangObject? owner = null)
    {
        ArgumentNullException.ThrowIfNull(owned);
        _unknown = (nint)owned;
        _what = what;
        _owner = owner;

        // Taken before anything can fail, and released exactly once each in Dispose. An owner
        // already disposed by the time a child is built is a caller error the child cannot recover
        // from, so Raw's ObjectDisposedException is the right outcome and is left to propagate —
        // and it propagates before any reference is taken, so a throw here leaks nothing.
        if (owner is null)
        {
            _ancestors = [];
            return;
        }

        // The owner's chain is known to be live: the owner is not disposed, so it still holds a
        // reference on every one of them.
        var chain = new nint[owner._ancestors.Length + 1];
        chain[0] = (nint)owner.Raw;
        Array.Copy(owner._ancestors, 0, chain, 1, owner._ancestors.Length);

        foreach (var ancestor in chain) ((ISlangUnknown*)ancestor)->addRef();
        _ancestors = chain;
    }

    /// <summary>True once the native reference has been released.</summary>
    public bool IsDisposed => Volatile.Read(ref _unknown) == 0;

    /// <summary>
    /// The live native pointer, for measuring reference counts from the prober.
    /// </summary>
    /// <remarks>
    /// Internal and named so nobody reaches for it by accident. It exists because the claim that
    /// "each object holds its own reference to what it needs" was read off COM convention and never
    /// measured, and the only way to measure it is to ask Slang for the count — see §9.25.
    /// </remarks>
    internal void* RawForTests => Raw;

    /// <summary>
    /// The live native pointer. Throws rather than returning a dangling pointer, so a
    /// use-after-dispose surfaces as an exception instead of an access violation.
    /// </summary>
    private protected void* Raw
    {
        get
        {
            var p = Volatile.Read(ref _unknown);
            ObjectDisposedException.ThrowIf(p == 0, _what);
            return (void*)p;
        }
    }

    /// <summary>
    /// Takes an additional reference. Only needed when handing the same native object to something
    /// that will release it independently — Slang's own out-parameters already carry a reference.
    /// </summary>
    private protected void AddReference()
    {
        var p = (ISlangUnknown*)Raw;
        p->addRef();
    }

    public void Dispose()
    {
        // Interlocked so concurrent Dispose calls cannot double-release.
        var p = Interlocked.Exchange(ref _unknown, 0);
        if (p == 0 || s_processExiting) return;

        ((ISlangUnknown*)p)->release();

        // Ours, taken in the constructor. Nearest-first and after this object's own release, so an
        // ancestor is never destroyed while a descendant's release is still running through it —
        // and never from inside a descendant's destructor, which is the part Slang cannot survive.
        foreach (var ancestor in _ancestors) ((ISlangUnknown*)ancestor)->release();

        // The owner must outlive this release, not merely reach it.
        GC.KeepAlive(_owner);
    }

    public override string ToString() => IsDisposed ? $"{_what} (disposed)" : _what;
}

/// <summary>An owned reference to a specific Slang interface.</summary>
/// <typeparam name="T">The generated interface struct, e.g. <c>ISession</c>.</typeparam>
public abstract unsafe class SlangObject<T> : SlangObject
    where T : unmanaged
{
    private protected SlangObject(T* owned, string what, SlangObject? owner = null)
        : base(owned, what, owner) { }

    /// <summary>The live typed pointer. Throws if disposed.</summary>
    private protected T* Ptr => (T*)Raw;
}
