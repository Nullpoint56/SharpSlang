// L4 — reverse interop: letting Slang call *into* managed code.
//
// Everything below L4 is the consumer side of an interface — calling a vtable Slang implements.
// This is the implementor side: synthesising a native vtable that Slang can call, backed by a
// managed object. Binding generators emit the former and never the latter, so this is hand-written
// by necessity rather than by preference (AGENTS.md#descriptor-defaults-and-result-codes).
//
// Why a host needs it: an engine that loads assets from packs through its own virtual filesystem.
// Without this, `import` and `#include` in a .slang file can only reach the OS filesystem, which
// means shipping loose shader source next to the executable.
//
// The layout Slang expects is a COM object: a pointer to a pointer to a table of function
// pointers. We allocate that table once per instance, fill it with [UnmanagedCallersOnly] thunks,
// and stash a GCHandle immediately after the vtable pointer so each thunk can recover the managed
// implementation from the `this` it is handed.
//
//     nativeObject ──> [0] vtable*  ──> [ queryInterface, addRef, release, ... ]
//                      [1] GCHandle
//
// This is also where ClangSharp's Thiscall misinference lived (AGENTS.md#generator-and-abi): the callback function
// pointer in enumeratePathContents. A wrong calling convention here would corrupt the stack rather
// than fail to compile.

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Hosting;

/// <summary>
/// A filesystem Slang can read through — implement this over an engine VFS, an asset pack, or an
/// in-memory dictionary of shader sources.
/// </summary>
public interface ISlangFileProvider
{
    /// <summary>
    /// Returns the contents of <paramref name="path"/>, or null if it does not exist.
    /// Slang treats null as "not found" and continues searching its other paths.
    /// </summary>
    byte[]? TryRead(string path);
}

/// <summary>
/// A read-only <see cref="ISlangFileProvider"/> over an in-memory map. Useful for tests, for
/// generated shader variants, and as a worked example of the interface.
/// </summary>
public sealed class InMemoryFileProvider(IReadOnlyDictionary<string, string> files) : ISlangFileProvider
{
    public byte[]? TryRead(string path) =>
        files.TryGetValue(path, out var text) ? System.Text.Encoding.UTF8.GetBytes(text) : null;
}

/// <summary>
/// Exposes an <see cref="ISlangFileProvider"/> to Slang as a native <c>ISlangFileSystem</c>.
/// Dispose after the sessions that use it — Slang holds the pointer for each session's lifetime,
/// and <see cref="Dispose"/> refuses before then.
/// </summary>
public sealed unsafe class SlangFileSystem : IDisposable
{
    // ISlangFileSystem: ISlangCastable : ISlangUnknown
    //   0 queryInterface  1 addRef  2 release      (ISlangUnknown)
    //   3 castAs                                    (ISlangCastable)
    //   4 loadFile                                  (ISlangFileSystem)
    private const int VtableSlots = 5;

    private readonly ISlangFileProvider _provider;
    private GCHandle _self;
    private void** _native;      // [0] = vtable, [1] = GCHandle
    private void** _vtable;
    private int _refCount = 1;

    public SlangFileSystem(ISlangFileProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;

        _vtable = (void**)NativeMemory.Alloc((nuint)VtableSlots, (nuint)sizeof(void*));
        _vtable[0] = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void**, int>)&QueryInterface;
        _vtable[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&AddRef;
        _vtable[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&Release;
        _vtable[3] = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void*>)&CastAs;
        _vtable[4] = (delegate* unmanaged[Stdcall]<void*, sbyte*, ISlangBlob**, int>)&LoadFile;

        // Normal, not pinned: we only ever read it back through GCHandle.FromIntPtr, so the object
        // does not need a fixed address.
        _self = GCHandle.Alloc(this, GCHandleType.Normal);

        _native = (void**)NativeMemory.Alloc(2, (nuint)sizeof(void*));
        _native[0] = _vtable;
        _native[1] = (void*)GCHandle.ToIntPtr(_self);
    }

    // Sessions created with this file system and not yet disposed: Slang holds the pointer for each
    // session's lifetime, so freeing it under one would hand Slang freed memory on its next load.
    private int _sessions;

    /// <summary>The native pointer to hand to Slang, e.g. via <c>SessionDesc.fileSystem</c>.</summary>
    internal ISlangFileSystem* Handle => (ISlangFileSystem*)_native;

    /// <summary>
    /// Slang's own reference count on this object, as its <c>addRef</c> and <c>release</c> have
    /// left it: 1 is this wrapper's own. For measuring what Slang holds; the binding decides
    /// Dispose by <see cref="LiveSessions"/>, its own count, not by this.
    /// </summary>
    public int NativeReferenceCount => Volatile.Read(ref _refCount);

    /// <summary>Sessions created with this file system and not yet disposed. <see cref="Dispose"/> refuses while it is above zero.</summary>
    public int LiveSessions => Volatile.Read(ref _sessions);

    internal void SessionCreated() => Interlocked.Increment(ref _sessions);

    internal void SessionReleased() => Interlocked.Decrement(ref _sessions);

    /// <summary>Frees the native object.</summary>
    /// <exception cref="InvalidOperationException">
    /// A session created with this file system is still alive (<see cref="LiveSessions"/>); dispose
    /// it first. The file system stays usable.
    /// </exception>
    public void Dispose()
    {
        var live = Volatile.Read(ref _sessions);
        if (live > 0 && _native is not null)
        {
            throw new InvalidOperationException(
                $"SlangFileSystem disposed while {live} session(s) created with it are alive, and " +
                $"Slang holds it for each session's lifetime. Dispose the sessions first " +
                $"(AGENTS.md#ownership-and-lifetime).");
        }

        if (_native is not null) { NativeMemory.Free(_native); _native = null; }
        if (_vtable is not null) { NativeMemory.Free(_vtable); _vtable = null; }
        if (_self.IsAllocated) _self.Free();
    }

    private static SlangFileSystem? FromNative(void* self)
    {
        if (self is null) return null;
        var handle = GCHandle.FromIntPtr((nint)((void**)self)[1]);
        return handle.IsAllocated ? handle.Target as SlangFileSystem : null;
    }

    // ---------------------------------------------------------------- thunks
    //
    // Stdcall to match SLANG_MCALL: __stdcall on Windows, ignored elsewhere on 64-bit.
    //
    // Every thunk swallows managed exceptions. Letting one unwind across a native frame is
    // undefined behaviour — on Linux the runtime terminates the process outright rather than
    // attempt it — and this is the only boundary in the binding where managed code runs underneath
    // native code, so it is the only place the hazard exists.
    //
    // This comment claimed all five did that while only LoadFile had a catch. The other four look
    // like they cannot throw, and they nearly cannot: what they do is dereference `self`, compare
    // GUIDs and touch an interlocked counter. But every one of them starts by resolving a GCHandle
    // out of memory Slang owns the lifetime of, and GCHandle.FromIntPtr on a value that is not a
    // live handle throws. A guard that is only correct as long as nobody makes a mistake is not the
    // kind of guard this boundary should have.
    //
    // The thunks stay one line each: [UnmanagedCallersOnly] methods cannot be called from managed
    // code, so the implementations live beside them as ordinary statics that they and each other
    // can share.

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int QueryInterface(void* self, SlangUUID* uuid, void** outObject)
    {
        try { return QueryInterfaceImpl(self, uuid, outObject); }
        catch { return SlangResult.InternalFail; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint AddRef(void* self)
    {
        try { return AddRefImpl(self); }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint Release(void* self)
    {
        try { return ReleaseImpl(self); }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void* CastAs(void* self, SlangUUID* uuid)
    {
        try
        {
            void* result = null;
            return QueryInterfaceImpl(self, uuid, &result) == SlangResult.Ok ? result : null;
        }
        catch { return null; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int LoadFile(void* self, sbyte* path, ISlangBlob** outBlob)
    {
        try { return LoadFileImpl(self, path, outBlob); }
        catch { return SlangResult.InternalFail; }
    }

    // ---------------------------------------------------------------- implementations

    private static int QueryInterfaceImpl(void* self, SlangUUID* uuid, void** outObject)
    {
        if (outObject is null) return SlangResult.InvalidArg;
        *outObject = null;
        if (self is null || uuid is null) return SlangResult.InvalidArg;

        var wanted = *(Guid*)uuid;
        if (wanted == SlangIID.ISlangUnknown ||
            wanted == SlangIID.ISlangCastable ||
            wanted == SlangIID.ISlangFileSystem)
        {
            AddRefImpl(self);
            *outObject = self;
            return SlangResult.Ok;
        }
        return SlangResult.NoInterface;
    }

    private static uint AddRefImpl(void* self)
    {
        var fs = FromNative(self);
        return fs is null ? 0 : (uint)Interlocked.Increment(ref fs._refCount);
    }

    private static uint ReleaseImpl(void* self)
    {
        var fs = FromNative(self);
        if (fs is null) return 0;

        // Deliberately does NOT free on reaching zero. The managed object owns its native memory
        // and frees it in Dispose; freeing here would race a caller still holding the wrapper.
        var count = Interlocked.Decrement(ref fs._refCount);
        return (uint)Math.Max(count, 0);
    }

    private static int LoadFileImpl(void* self, sbyte* path, ISlangBlob** outBlob)
    {
        if (outBlob is null) return SlangResult.InvalidArg;
        *outBlob = null;

        try
        {
            var fs = FromNative(self);
            if (fs is null) return SlangResult.Uninitialized;

            var managedPath = Utf8.Read(path);
            if (managedPath is null) return SlangResult.InvalidArg;

            // The one call into code this package did not write. An engine VFS reading from a pack
            // file has every ordinary reason to throw — a bad path, a closed archive, a disk error
            // — and none of those may reach Slang's frame.
            var content = fs._provider.TryRead(managedPath);
            if (content is null) return SlangResult.NotFound;

            // slang_createBlob copies, so the managed array does not need pinning beyond the call.
            fixed (byte* p = content)
            {
                var blob = SlangApi.slang_createBlob(p, (nuint)content.Length);
                if (blob is null) return SlangResult.OutOfMemory;
                *outBlob = blob;
            }
            return SlangResult.Ok;
        }
        catch
        {
            // A managed exception must never cross back into Slang's frame.
            return SlangResult.InternalFail;
        }
    }
}
