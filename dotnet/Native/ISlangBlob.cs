using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct ISlangBlob : ISlangUnknown")]
public unsafe partial struct ISlangBlob
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
        return ((delegate* unmanaged[Stdcall]<ISlangBlob*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangBlob*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangBlob*, uint>)(lpVtbl[1]))((ISlangBlob*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangBlob*, uint>)(lpVtbl[2]))((ISlangBlob*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("const void *")]
    public void* getBufferPointer()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangBlob*, void*>)(lpVtbl[3]))((ISlangBlob*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(4)]
    [return: NativeTypeName("size_t")]
    public nuint getBufferSize()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangBlob*, nuint>)(lpVtbl[4]))((ISlangBlob*)Unsafe.AsPointer(ref this));
    }
}
