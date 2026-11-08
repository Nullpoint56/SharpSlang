using System;
using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IComponentType : ISlangUnknown")]
public unsafe partial struct IComponentType
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, SlangUUID*, void**, int>)(lpVtbl[0]))((IComponentType*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, uint>)(lpVtbl[1]))((IComponentType*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, uint>)(lpVtbl[2]))((IComponentType*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("slang::ISession *")]
    public ISession* getSession()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, ISession*>)(lpVtbl[3]))((IComponentType*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(4)]
    [return: NativeTypeName("slang::ProgramLayout *")]
    public ShaderReflection* getLayout([NativeTypeName("SlangInt")] long targetIndex = 0, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, ISlangBlob**, ShaderReflection*>)(lpVtbl[4]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outDiagnostics);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangInt")]
    public long getSpecializationParamCount()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long>)(lpVtbl[5]))((IComponentType*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCode([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[6]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outCode, outDiagnostics);
    }

    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getResultAsFileSystem([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, ISlangMutableFileSystem** outFileSystem)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangMutableFileSystem**, int>)(lpVtbl[7]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outFileSystem);
    }

    [VtblIndex(8)]
    public void getEntryPointHash([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outHash)
    {
        ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangBlob**, void>)(lpVtbl[8]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outHash);
    }

    [VtblIndex(9)]
    [return: NativeTypeName("SlangResult")]
    public int specialize([NativeTypeName("const SpecializationArg *")] SpecializationArg* specializationArgs, [NativeTypeName("SlangInt")] long specializationArgCount, IComponentType** outSpecializedComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, SpecializationArg*, long, IComponentType**, ISlangBlob**, int>)(lpVtbl[9]))((IComponentType*)Unsafe.AsPointer(ref this), specializationArgs, specializationArgCount, outSpecializedComponentType, outDiagnostics);
    }

    [VtblIndex(10)]
    [return: NativeTypeName("SlangResult")]
    public int link(IComponentType** outLinkedComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, IComponentType**, ISlangBlob**, int>)(lpVtbl[10]))((IComponentType*)Unsafe.AsPointer(ref this), outLinkedComponentType, outDiagnostics);
    }

    [VtblIndex(11)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointHostCallable(int entryPointIndex, int targetIndex, ISlangSharedLibrary** outSharedLibrary, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, int, int, ISlangSharedLibrary**, ISlangBlob**, int>)(lpVtbl[11]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outSharedLibrary, outDiagnostics);
    }

    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int renameEntryPoint([NativeTypeName("const char *")] sbyte* newName, IComponentType** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, sbyte*, IComponentType**, int>)(lpVtbl[12]))((IComponentType*)Unsafe.AsPointer(ref this), newName, outEntryPoint);
    }

    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int linkWithOptions(IComponentType** outLinkedComponentType, [NativeTypeName("uint32_t")] uint compilerOptionEntryCount, [NativeTypeName("const CompilerOptionEntry *")] CompilerOptionEntry* compilerOptionEntries, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, IComponentType**, uint, CompilerOptionEntry*, ISlangBlob**, int>)(lpVtbl[13]))((IComponentType*)Unsafe.AsPointer(ref this), outLinkedComponentType, compilerOptionEntryCount, compilerOptionEntries, outDiagnostics);
    }

    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCode([NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[14]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outCode, outDiagnostics);
    }

    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetMetadata([NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[15]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outMetadata, outDiagnostics);
    }

    [VtblIndex(16)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointMetadata([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[16]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outMetadata, outDiagnostics);
    }
}
