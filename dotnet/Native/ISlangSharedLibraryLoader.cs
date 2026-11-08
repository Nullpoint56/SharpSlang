using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangSharedLibraryLoader : ISlangUnknown")]
public unsafe partial struct ISlangSharedLibraryLoader
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibraryLoader*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangSharedLibraryLoader*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibraryLoader*, uint>)(lpVtbl[1]))((ISlangSharedLibraryLoader*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibraryLoader*, uint>)(lpVtbl[2]))((ISlangSharedLibraryLoader*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Load a shared library. In typical usage the library name should *not* contain any platform specific elements. For example on windows a dll name should *not* be passed with a '.dll' extension, and similarly on linux a shared library should *not* be passed with the 'lib' prefix and '.so' extension</summary>
    /// <remarks>
    /// <para>path: path The unadorned filename and/or path for the shared library @ param sharedLibraryOut Holds the shared library if successfully loaded</para>
    /// </remarks>
    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int loadSharedLibrary([NativeTypeName("const char *")] sbyte* path, ISlangSharedLibrary** sharedLibraryOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibraryLoader*, sbyte*, ISlangSharedLibrary**, int>)(lpVtbl[3]))((ISlangSharedLibraryLoader*)Unsafe.AsPointer(ref this), path, sharedLibraryOut);
    }
}
