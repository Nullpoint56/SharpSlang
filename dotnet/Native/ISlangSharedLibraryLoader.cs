using System;
using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangSharedLibraryLoader : ISlangUnknown")]
public unsafe partial struct ISlangSharedLibraryLoader
{
    public void** lpVtbl;

    [return: NativeTypeName("SlangResult")]
    public int QueryInterface([NativeTypeName("const struct _GUID &")] Guid* uuid, void** outObject)
    {
        return queryInterface(unchecked((SlangUUID*)(uuid)), outObject);
    }

    [return: NativeTypeName("uint32_t")]
    public uint AddRef()
    {
        return addRef();
    }

    [return: NativeTypeName("uint32_t")]
    public uint Release()
    {
        return release();
    }

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

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int loadSharedLibrary([NativeTypeName("const char *")] sbyte* path, ISlangSharedLibrary** sharedLibraryOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibraryLoader*, sbyte*, ISlangSharedLibrary**, int>)(lpVtbl[3]))((ISlangSharedLibraryLoader*)Unsafe.AsPointer(ref this), path, sharedLibraryOut);
    }
}
