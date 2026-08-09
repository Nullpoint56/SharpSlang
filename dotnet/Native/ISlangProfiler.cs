using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct ISlangProfiler : ISlangUnknown")]
public unsafe partial struct ISlangProfiler
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
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangProfiler*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, uint>)(lpVtbl[1]))((ISlangProfiler*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, uint>)(lpVtbl[2]))((ISlangProfiler*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("size_t")]
    public nuint getEntryCount()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, nuint>)(lpVtbl[3]))((ISlangProfiler*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(4)]
    [return: NativeTypeName("const char *")]
    public sbyte* getEntryName([NativeTypeName("uint32_t")] uint index)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, uint, sbyte*>)(lpVtbl[4]))((ISlangProfiler*)Unsafe.AsPointer(ref this), index);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("long")]
    public int getEntryTimeMS([NativeTypeName("uint32_t")] uint index)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, uint, int>)(lpVtbl[5]))((ISlangProfiler*)Unsafe.AsPointer(ref this), index);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("uint32_t")]
    public uint getEntryInvocationTimes([NativeTypeName("uint32_t")] uint index)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangProfiler*, uint, uint>)(lpVtbl[6]))((ISlangProfiler*)Unsafe.AsPointer(ref this), index);
    }
}
