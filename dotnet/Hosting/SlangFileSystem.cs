// L4 — reverse interop: letting Slang call *into* managed code.
//
// Everything below L4 is the consumer side of an interface — calling a vtable Slang implements.
// This is the implementor side: synthesising a native vtable that Slang can call, backed by a
// managed object. Binding generators emit the former and never the latter, so this is hand-written
// by necessity rather than by preference (docs §4.5).
//
// Why the engine needs it: PrismEngine loads assets from packs through its own virtual filesystem.
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
// This is also where ClangSharp's Thiscall misinference lived (docs §4.3): the callback function
// pointer in enumeratePathContents. A wrong calling convention here would corrupt the stack rather
// than fail to compile.

using System.Runtime.InteropServices;
using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang.Hosting;

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
/// Dispose after the session that uses it — Slang holds the pointer for the session's lifetime.
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

    /// <summary>The native pointer to hand to Slang, e.g. via <c>SessionDesc.fileSystem</c>.</summary>
    internal ISlangFileSystem* Handle => (ISlangFileSystem*)_native;

    public void Dispose()
    {
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
    // Stdcall to match SLANG_MCALL: __stdcall on Windows, ignored elsewhere on 64-bit. Every thunk
    // must swallow managed exceptions — letting one unwind across the native frame is undefined
    // behaviour, and on Linux it terminates the process outright.

    // The thunks stay one line each: [UnmanagedCallersOnly] methods cannot be called from managed
    // code, so the implementations live beside them as ordinary statics that they and each other
    // can share.

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int QueryInterface(void* self, SlangUUID* uuid, void** outObject) =>
        QueryInterfaceImpl(self, uuid, outObject);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint AddRef(void* self) => AddRefImpl(self);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static uint Release(void* self) => ReleaseImpl(self);

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void* CastAs(void* self, SlangUUID* uuid)
    {
        void* result = null;
        return QueryInterfaceImpl(self, uuid, &result) == SlangResult.Ok ? result : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int LoadFile(void* self, sbyte* path, ISlangBlob** outBlob) =>
        LoadFileImpl(self, path, outBlob);

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
