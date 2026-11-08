using System;
using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangSharedLibrary_Dep1 : ISlangUnknown")]
public unsafe partial struct ISlangSharedLibrary_Dep1
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
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary_Dep1*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangSharedLibrary_Dep1*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary_Dep1*, uint>)(lpVtbl[1]))((ISlangSharedLibrary_Dep1*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary_Dep1*, uint>)(lpVtbl[2]))((ISlangSharedLibrary_Dep1*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* findSymbolAddressByName([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary_Dep1*, sbyte*, void*>)(lpVtbl[3]))((ISlangSharedLibrary_Dep1*)Unsafe.AsPointer(ref this), name);
    }
}
