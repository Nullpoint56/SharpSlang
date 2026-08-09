using System;
using System.Runtime.CompilerServices;

namespace PrismSlang.Native;

[NativeTypeName("struct ISlangWriter : ISlangUnknown")]
public unsafe partial struct ISlangWriter
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
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangWriter*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, uint>)(lpVtbl[1]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, uint>)(lpVtbl[2]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("char *")]
    public sbyte* beginAppendBuffer([NativeTypeName("size_t")] nuint maxNumChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, nuint, sbyte*>)(lpVtbl[3]))((ISlangWriter*)Unsafe.AsPointer(ref this), maxNumChars);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int endAppendBuffer([NativeTypeName("char *")] sbyte* buffer, [NativeTypeName("size_t")] nuint numChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, sbyte*, nuint, int>)(lpVtbl[4]))((ISlangWriter*)Unsafe.AsPointer(ref this), buffer, numChars);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int write([NativeTypeName("const char *")] sbyte* chars, [NativeTypeName("size_t")] nuint numChars)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, sbyte*, nuint, int>)(lpVtbl[5]))((ISlangWriter*)Unsafe.AsPointer(ref this), chars, numChars);
    }

    [VtblIndex(6)]
    public void flush()
    {
        ((delegate* unmanaged[Stdcall]<ISlangWriter*, void>)(lpVtbl[6]))((ISlangWriter*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(7)]
    [return: NativeTypeName("SlangBool")]
    public bool isConsole()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, byte>)(lpVtbl[7]))((ISlangWriter*)Unsafe.AsPointer(ref this)) != 0;
    }

    [VtblIndex(8)]
    [return: NativeTypeName("SlangResult")]
    public int setMode(SlangWriterMode mode)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangWriter*, SlangWriterMode, int>)(lpVtbl[8]))((ISlangWriter*)Unsafe.AsPointer(ref this), mode);
    }
}
