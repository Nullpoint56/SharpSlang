using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct ICompileResult : ISlangCastable")]
public unsafe partial struct ICompileResult
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
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, SlangUUID*, void**, int>)(lpVtbl[0]))((ICompileResult*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, uint>)(lpVtbl[1]))((ICompileResult*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, uint>)(lpVtbl[2]))((ICompileResult*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, SlangUUID*, void*>)(lpVtbl[3]))((ICompileResult*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("uint32_t")]
    public uint getItemCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, uint>)(lpVtbl[4]))((ICompileResult*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getItemData([NativeTypeName("uint32_t")] uint index, [NativeTypeName("IBlob **")] ISlangBlob** outblob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, uint, ISlangBlob**, int>)(lpVtbl[5]))((ICompileResult*)Unsafe.AsPointer(ref this), index, outblob);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getMetadata(IMetadata** outMetadata)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileResult*, IMetadata**, int>)(lpVtbl[6]))((ICompileResult*)Unsafe.AsPointer(ref this), outMetadata);
    }
}
