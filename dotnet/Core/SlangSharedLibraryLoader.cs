// L4 — reverse interop: ISlangSharedLibraryLoader and ISlangSharedLibrary, implemented in managed
// code so the application decides how Slang loads the libraries it loads by name (slang-glslang,
// dxcompiler, ...): from a pack, a different folder, or not at all.
//
// The loader is a COM object as SlangFileSystem's is: [0] vtable, [1] GCHandle to this wrapper,
// with a reference count Slang moves and Dispose checks. Each library it hands Slang is a separate
// COM object living entirely in native memory: [0] vtable, [1] the library handle, [2] its
// reference count. It frees itself, and the handle, when Slang releases it last, so it needs no
// managed object at all.
//
//     loader  ──> [0] vtable* ──> [ queryInterface, addRef, release, loadSharedLibrary ]
//                 [1] GCHandle
//     library ──> [0] vtable* ──> [ queryInterface, addRef, release, castAs, findSymbolAddressByName ]
//                 [1] handle      [2] refcount

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Hosting;

/// <summary>
/// An <c>ISlangSharedLibraryLoader</c> backed by a managed function:
/// <see cref="SlangGlobalSession.SetSharedLibraryLoader"/> makes Slang load every library it loads
/// by name through it.
/// </summary>
/// <remarks>
/// <para>
/// The function receives the name or path Slang asks for, undecorated (<c>dxcompiler</c>, a
/// versioned <c>slang-glslang</c>), and returns a native library handle, or 0 when it has none.
/// The handle then belongs to Slang's library object, which frees it with
/// <see cref="NativeLibrary.Free"/> when Slang releases it, so return a handle of its own from
/// each call (each <see cref="NativeLibrary.Load(string)"/> is one). A throw counts as not found.
/// </para>
/// <para>
/// Slang holds the loader for as long as a global session uses it; <see cref="Dispose"/> refuses
/// until then.
/// </para>
/// </remarks>
public sealed unsafe class SlangSharedLibraryLoader : IDisposable
{
    private readonly Func<string, nint> _load;
    private GCHandle _self;
    private void** _native;
    private int _refCount = 1;

    /// <param name="load">Returns a library handle for the name Slang asks for, or 0.</param>
    public SlangSharedLibraryLoader(Func<string, nint> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;

        _self = GCHandle.Alloc(this, GCHandleType.Normal);
        _native = (void**)NativeMemory.Alloc(2, (nuint)sizeof(void*));
        _native[0] = LoaderVtable;
        _native[1] = (void*)GCHandle.ToIntPtr(_self);
    }

    internal ISlangSharedLibraryLoader* Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_native is null, this);
            return (ISlangSharedLibraryLoader*)_native;
        }
    }

    /// <summary>References on the native object: 1 for this wrapper, one more per holder in Slang.</summary>
    public int NativeReferenceCount => Volatile.Read(ref _refCount);

    /// <summary>Libraries this loader handed Slang that Slang has not released yet, across all loaders.</summary>
    public static int LiveLibraries => Volatile.Read(ref s_liveLibraries);

    /// <summary>Frees the native object.</summary>
    /// <exception cref="InvalidOperationException">A global session still holds it.</exception>
    public void Dispose()
    {
        if (_native is null) return;

        var held = Volatile.Read(ref _refCount) - 1;
        if (held > 0)
        {
            throw new InvalidOperationException(
                $"SlangSharedLibraryLoader disposed while Slang holds {held} reference(s) to it. Dispose the " +
                "global session using it, or set its loader back to null, first (AGENTS.md#ownership-and-lifetime).");
        }

        NativeMemory.Free(_native);
        _native = null;
        if (_self.IsAllocated) _self.Free();
    }

    // ---------------------------------------------------------------- vtables

    private static readonly void** LoaderVtable = MakeLoaderVtable();
    private static readonly void** LibraryVtable = MakeLibraryVtable();
    private static int s_liveLibraries;

    private static void** MakeLoaderVtable()
    {
        // ISlangSharedLibraryLoader : ISlangUnknown — 0 queryInterface, 1 addRef, 2 release, 3 loadSharedLibrary
        var v = (void**)NativeMemory.Alloc(4, (nuint)sizeof(void*));
        v[0] = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void**, int>)&LoaderQueryInterface;
        v[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&LoaderAddRef;
        v[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&LoaderRelease;
        v[3] = (delegate* unmanaged[Stdcall]<void*, sbyte*, ISlangSharedLibrary**, int>)&LoadSharedLibrary;
        return v;
    }

    private static void** MakeLibraryVtable()
    {
        // ISlangSharedLibrary : ISlangCastable — 0 queryInterface, 1 addRef, 2 release, 3 castAs,
        // 4 findSymbolAddressByName
        var v = (void**)NativeMemory.Alloc(5, (nuint)sizeof(void*));
        v[0] = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void**, int>)&LibraryQueryInterface;
        v[1] = (delegate* unmanaged[Stdcall]<void*, uint>)&LibraryAddRef;
        v[2] = (delegate* unmanaged[Stdcall]<void*, uint>)&LibraryRelease;
        v[3] = (delegate* unmanaged[Stdcall]<void*, SlangUUID*, void*>)&LibraryCastAs;
        v[4] = (delegate* unmanaged[Stdcall]<void*, sbyte*, void*>)&FindSymbolAddressByName;
        return v;
    }

    private static SlangSharedLibraryLoader? FromNative(void* self)
    {
        if (self is null) return null;
        var handle = GCHandle.FromIntPtr((nint)((void**)self)[1]);
        return handle.IsAllocated ? handle.Target as SlangSharedLibraryLoader : null;
    }

    // ---------------------------------------------------------------- loader thunks
    //
    // Stdcall to match SLANG_MCALL. Every thunk swallows managed exceptions: nothing may unwind
    // into Slang's frame (SlangFileSystem says why each one needs it).

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int LoaderQueryInterface(void* self, SlangUUID* uuid, void** outObject)
    {
        try
        {
            if (outObject is null) return SlangResult.InvalidArg;
            *outObject = null;
            if (self is null || uuid is null) return SlangResult.InvalidArg;

            var wanted = *(Guid*)uuid;
            if (wanted != SlangIID.ISlangUnknown && wanted != SlangIID.ISlangSharedLibraryLoader) return SlangResult.NoInterface;

            if (FromNative(self) is not { } loader) return SlangResult.Uninitialized;
            Interlocked.Increment(ref loader._refCount);
            *outObject = self;
            return SlangResult.Ok;
        }
        catch { return SlangResult.InternalFail; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint LoaderAddRef(void* self)
    {
        try { return FromNative(self) is { } loader ? (uint)Interlocked.Increment(ref loader._refCount) : 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint LoaderRelease(void* self)
    {
        // Never frees: the wrapper owns the native memory and frees it in Dispose.
        try { return FromNative(self) is { } loader ? (uint)Math.Max(Interlocked.Decrement(ref loader._refCount), 0) : 0; }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int LoadSharedLibrary(void* self, sbyte* path, ISlangSharedLibrary** outLibrary)
    {
        try
        {
            if (outLibrary is null) return SlangResult.InvalidArg;
            *outLibrary = null;

            if (FromNative(self) is not { } loader) return SlangResult.Uninitialized;
            var name = Utf8.Read(path);
            if (string.IsNullOrEmpty(name)) return SlangResult.InvalidArg;

            nint handle;
            try { handle = loader._load(name); }
            catch { return SlangResult.NotFound; }
            if (handle == 0) return SlangResult.NotFound;

            var library = (nint*)NativeMemory.Alloc(3, (nuint)sizeof(nint));
            library[0] = (nint)LibraryVtable;
            library[1] = handle;
            library[2] = 1;
            Interlocked.Increment(ref s_liveLibraries);

            *outLibrary = (ISlangSharedLibrary*)library;
            return SlangResult.Ok;
        }
        catch { return SlangResult.InternalFail; }
    }

    // ---------------------------------------------------------------- library thunks

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int LibraryQueryInterface(void* self, SlangUUID* uuid, void** outObject)
    {
        try
        {
            if (outObject is null) return SlangResult.InvalidArg;
            *outObject = LibraryInterface(self, uuid);
            return *outObject is null ? SlangResult.NoInterface : SlangResult.Ok;
        }
        catch { return SlangResult.InternalFail; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void* LibraryCastAs(void* self, SlangUUID* uuid)
    {
        // castAs hands back the interface without a reference (ISlangCastable).
        try
        {
            var found = LibraryInterface(self, uuid);
            if (found is not null) LibraryReleaseImpl(found);
            return found;
        }
        catch { return null; }
    }

    private static void* LibraryInterface(void* self, SlangUUID* uuid)
    {
        if (self is null || uuid is null) return null;
        var wanted = *(Guid*)uuid;
        if (wanted != SlangIID.ISlangUnknown && wanted != SlangIID.ISlangCastable && wanted != SlangIID.ISlangSharedLibrary) return null;

        Interlocked.Increment(ref *Count(self));
        return self;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint LibraryAddRef(void* self)
    {
        try { return self is null ? 0 : (uint)Interlocked.Increment(ref *Count(self)); }
        catch { return 0; }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint LibraryRelease(void* self)
    {
        try { return LibraryReleaseImpl(self); }
        catch { return 0; }
    }

    // The library's reference count, slot [2]; every supported RID is 64-bit, so a long.
    private static long* Count(void* self) => (long*)((nint*)self + 2);

    private static uint LibraryReleaseImpl(void* self)
    {
        if (self is null) return 0;
        var count = Interlocked.Decrement(ref *Count(self));
        if (count > 0) return (uint)count;

        NativeLibrary.Free(((nint*)self)[1]);
        NativeMemory.Free(self);
        Interlocked.Decrement(ref s_liveLibraries);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void* FindSymbolAddressByName(void* self, sbyte* name)
    {
        try
        {
            var symbol = Utf8.Read(name);
            if (self is null || string.IsNullOrEmpty(symbol)) return null;
            return NativeLibrary.TryGetExport(((nint*)self)[1], symbol, out var address) ? (void*)address : null;
        }
        catch { return null; }
    }
}
