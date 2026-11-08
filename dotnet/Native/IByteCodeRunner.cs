using System;
using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IByteCodeRunner : ISlangUnknown")]
public unsafe partial struct IByteCodeRunner
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
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, SlangUUID*, void**, int>)(lpVtbl[0]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, uint>)(lpVtbl[1]))((IByteCodeRunner*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, uint>)(lpVtbl[2]))((IByteCodeRunner*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int loadModule([NativeTypeName("slang::IBlob *")] ISlangBlob* moduleBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, ISlangBlob*, int>)(lpVtbl[3]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), moduleBlob);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int selectFunctionByIndex([NativeTypeName("uint32_t")] uint functionIndex)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, uint, int>)(lpVtbl[4]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), functionIndex);
    }

    [VtblIndex(5)]
    public int findFunctionByName([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, sbyte*, int>)(lpVtbl[5]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), name);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getFunctionInfo([NativeTypeName("uint32_t")] uint index, [NativeTypeName("slang::ByteCodeFuncInfo *")] ByteCodeFuncInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, uint, ByteCodeFuncInfo*, int>)(lpVtbl[6]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), index, outInfo);
    }

    [VtblIndex(7)]
    public void* getCurrentWorkingSet()
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, void*>)(lpVtbl[7]))((IByteCodeRunner*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(8)]
    [return: NativeTypeName("SlangResult")]
    public int execute(void* argumentData, [NativeTypeName("size_t")] nuint argumentSize)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, void*, nuint, int>)(lpVtbl[8]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), argumentData, argumentSize);
    }

    [VtblIndex(9)]
    public void getErrorString([NativeTypeName("IBlob **")] ISlangBlob** outBlob)
    {
        ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, ISlangBlob**, void>)(lpVtbl[9]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), outBlob);
    }

    [VtblIndex(10)]
    public void* getReturnValue([NativeTypeName("size_t *")] nuint* outValueSize)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, nuint*, void*>)(lpVtbl[10]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), outValueSize);
    }

    [VtblIndex(11)]
    public void setExtInstHandlerUserData(void* userData)
    {
        ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, void*, void>)(lpVtbl[11]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), userData);
    }

    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int registerExtCall([NativeTypeName("const char *")] sbyte* name, [NativeTypeName("slang::VMExtFunction")] delegate* unmanaged[Cdecl]<IByteCodeRunner*, VMExecInstHeader*, void*, void> functionPtr)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, sbyte*, delegate* unmanaged[Cdecl]<IByteCodeRunner*, VMExecInstHeader*, void*, void>, int>)(lpVtbl[12]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), name, functionPtr);
    }

    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int setPrintCallback([NativeTypeName("slang::VMPrintFunc")] delegate* unmanaged[Cdecl]<sbyte*, void*, void> callback, void* userData)
    {
        return ((delegate* unmanaged[Stdcall]<IByteCodeRunner*, delegate* unmanaged[Cdecl]<sbyte*, void*, void>, void*, int>)(lpVtbl[13]))((IByteCodeRunner*)Unsafe.AsPointer(ref this), callback, userData);
    }
}
