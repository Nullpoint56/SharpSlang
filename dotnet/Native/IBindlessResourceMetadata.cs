using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct IBindlessResourceMetadata : ISlangCastable")]
public unsafe partial struct IBindlessResourceMetadata
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
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, uint>)(lpVtbl[1]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, uint>)(lpVtbl[2]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    public bool usesBindlessResourceHeap()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, byte>)(lpVtbl[4]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this)) != 0;
    }
}
