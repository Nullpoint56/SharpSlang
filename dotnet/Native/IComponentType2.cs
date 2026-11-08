using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IComponentType2 : ISlangUnknown")]
public unsafe partial struct IComponentType2
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
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, SlangUUID*, void**, int>)(lpVtbl[0]))((IComponentType2*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, uint>)(lpVtbl[1]))((IComponentType2*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, uint>)(lpVtbl[2]))((IComponentType2*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCompileResult([NativeTypeName("SlangInt")] long targetIndex, ICompileResult** outCompileResult, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, long, ICompileResult**, ISlangBlob**, int>)(lpVtbl[3]))((IComponentType2*)Unsafe.AsPointer(ref this), targetIndex, outCompileResult, outDiagnostics);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCompileResult([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, ICompileResult** outCompileResult, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, long, long, ICompileResult**, ISlangBlob**, int>)(lpVtbl[4]))((IComponentType2*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outCompileResult, outDiagnostics);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetHostCallable(int targetIndex, ISlangSharedLibrary** outSharedLibrary, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, int, ISlangSharedLibrary**, ISlangBlob**, int>)(lpVtbl[5]))((IComponentType2*)Unsafe.AsPointer(ref this), targetIndex, outSharedLibrary, outDiagnostics);
    }
}
