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
//      while a child is alive. This also fixes the plain-GC case, not just finalization.
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
    }

    /// <summary>True once the native reference has been released.</summary>
    public bool IsDisposed => Volatile.Read(ref _unknown) == 0;

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
