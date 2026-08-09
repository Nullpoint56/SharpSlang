using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct ICoverageTracingMetadata : ISlangCastable")]
public unsafe partial struct ICoverageTracingMetadata
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
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[1]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[2]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("uint32_t")]
    public uint getCounterCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[4]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryInfo([NativeTypeName("uint32_t")] uint index, [NativeTypeName("slang::CoverageEntryInfo *")] CoverageEntryInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint, CoverageEntryInfo*, int>)(lpVtbl[5]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), index, outInfo);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getBufferInfo([NativeTypeName("slang::CoverageBufferInfo *")] CoverageBufferInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, CoverageBufferInfo*, int>)(lpVtbl[6]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), outInfo);
    }

    [VtblIndex(7)]
    [return: NativeTypeName("uint32_t")]
    public uint getEntryCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[7]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }
}
