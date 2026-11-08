using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISyntheticResourceMetadata : ISlangCastable")]
public unsafe partial struct ISyntheticResourceMetadata
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
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[1]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[2]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("uint32_t")]
    public uint getResourceCount()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[4]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getResourceInfo([NativeTypeName("uint32_t")] uint index, [NativeTypeName("slang::SyntheticResourceInfo *")] SyntheticResourceInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint, SyntheticResourceInfo*, int>)(lpVtbl[5]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), index, outInfo);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int findResourceIndexByID([NativeTypeName("uint32_t")] uint id, [NativeTypeName("uint32_t *")] uint* outIndex)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint, uint*, int>)(lpVtbl[6]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), id, outIndex);
    }
}
