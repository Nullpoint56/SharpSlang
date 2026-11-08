using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IModulePrecompileService_Experimental : ISlangUnknown")]
public unsafe partial struct IModulePrecompileService_Experimental
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
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, SlangUUID*, void**, int>)(lpVtbl[0]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, uint>)(lpVtbl[1]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, uint>)(lpVtbl[2]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int precompileForTarget(SlangCompileTarget target, ISlangBlob** outDiagnostics)
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, SlangCompileTarget, ISlangBlob**, int>)(lpVtbl[3]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this), target, outDiagnostics);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int getPrecompiledTargetCode(SlangCompileTarget target, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, SlangCompileTarget, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[4]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this), target, outCode, outDiagnostics);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangInt")]
    public long getModuleDependencyCount()
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, long>)(lpVtbl[5]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getModuleDependency([NativeTypeName("SlangInt")] long dependencyIndex, IModule** outModule, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModulePrecompileService_Experimental*, long, IModule**, ISlangBlob**, int>)(lpVtbl[6]))((IModulePrecompileService_Experimental*)Unsafe.AsPointer(ref this), dependencyIndex, outModule, outDiagnostics);
    }
}
