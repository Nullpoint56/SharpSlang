using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangSharedLibrary : ISlangCastable")]
public unsafe partial struct ISlangSharedLibrary
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

    [return: NativeTypeName("SlangFuncPtr")]
    public delegate* unmanaged[Cdecl]<void> findFuncByName([NativeTypeName("const char *")] sbyte* name)
    {
        return (delegate* unmanaged[Cdecl]<void>)(findSymbolAddressByName(name));
    }

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, uint>)(lpVtbl[1]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, uint>)(lpVtbl[2]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    public void* findSymbolAddressByName([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, sbyte*, void*>)(lpVtbl[4]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), name);
    }
}
