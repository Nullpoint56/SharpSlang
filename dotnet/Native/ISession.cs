using System;
using System.Runtime.CompilerServices;
using static PrismSlang.Native.LayoutRules;

namespace PrismSlang.Native;

[NativeTypeName("struct ISession : ISlangUnknown")]
public unsafe partial struct ISession
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
        return ((delegate* unmanaged[Stdcall]<ISession*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISession*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, uint>)(lpVtbl[1]))((ISession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, uint>)(lpVtbl[2]))((ISession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("slang::IGlobalSession *")]
    public IGlobalSession* getGlobalSession()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, IGlobalSession*>)(lpVtbl[3]))((ISession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(4)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModule([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, ISlangBlob**, IModule*>)(lpVtbl[4]))((ISession*)Unsafe.AsPointer(ref this), moduleName, outDiagnostics);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModuleFromSource([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("slang::IBlob *")] ISlangBlob* source, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, sbyte*, ISlangBlob*, ISlangBlob**, IModule*>)(lpVtbl[5]))((ISession*)Unsafe.AsPointer(ref this), moduleName, path, source, outDiagnostics);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int createCompositeComponentType([NativeTypeName("IComponentType *const *")] IComponentType** componentTypes, [NativeTypeName("SlangInt")] long componentTypeCount, IComponentType** outCompositeComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, IComponentType**, long, IComponentType**, ISlangBlob**, int>)(lpVtbl[6]))((ISession*)Unsafe.AsPointer(ref this), componentTypes, componentTypeCount, outCompositeComponentType, outDiagnostics);
    }

    [VtblIndex(7)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* specializeType([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("const SpecializationArg *")] SpecializationArg* specializationArgs, [NativeTypeName("SlangInt")] long specializationArgCount, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, SpecializationArg*, long, ISlangBlob**, TypeReflection*>)(lpVtbl[7]))((ISession*)Unsafe.AsPointer(ref this), type, specializationArgs, specializationArgCount, outDiagnostics);
    }

    [VtblIndex(8)]
    [return: NativeTypeName("slang::TypeLayoutReflection *")]
    public TypeLayoutReflection* getTypeLayout([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("SlangInt")] long targetIndex = 0, [NativeTypeName("slang::LayoutRules")] LayoutRules rules = Default, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, long, LayoutRules, ISlangBlob**, TypeLayoutReflection*>)(lpVtbl[8]))((ISession*)Unsafe.AsPointer(ref this), type, targetIndex, rules, outDiagnostics);
    }

    [VtblIndex(9)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* getContainerType([NativeTypeName("slang::TypeReflection *")] TypeReflection* elementType, [NativeTypeName("slang::ContainerType")] ContainerType containerType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, ContainerType, ISlangBlob**, TypeReflection*>)(lpVtbl[9]))((ISession*)Unsafe.AsPointer(ref this), elementType, containerType, outDiagnostics);
    }

    [VtblIndex(10)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* getDynamicType()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*>)(lpVtbl[10]))((ISession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(11)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeRTTIMangledName([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, ISlangBlob** outNameBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, ISlangBlob**, int>)(lpVtbl[11]))((ISession*)Unsafe.AsPointer(ref this), type, outNameBlob);
    }

    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeConformanceWitnessMangledName([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, ISlangBlob** outNameBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, ISlangBlob**, int>)(lpVtbl[12]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outNameBlob);
    }

    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeConformanceWitnessSequentialID([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, [NativeTypeName("uint32_t *")] uint* outId)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, uint*, int>)(lpVtbl[13]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outId);
    }

    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int createCompileRequest([NativeTypeName("SlangCompileRequest **")] ICompileRequest** outCompileRequest)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, ICompileRequest**, int>)(lpVtbl[14]))((ISession*)Unsafe.AsPointer(ref this), outCompileRequest);
    }

    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int createTypeConformanceComponentType([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, ITypeConformance** outConformance, [NativeTypeName("SlangInt")] long conformanceIdOverride, ISlangBlob** outDiagnostics)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, ITypeConformance**, long, ISlangBlob**, int>)(lpVtbl[15]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outConformance, conformanceIdOverride, outDiagnostics);
    }

    [VtblIndex(16)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModuleFromIRBlob([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("slang::IBlob *")] ISlangBlob* source, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, sbyte*, ISlangBlob*, ISlangBlob**, IModule*>)(lpVtbl[16]))((ISession*)Unsafe.AsPointer(ref this), moduleName, path, source, outDiagnostics);
    }

    [VtblIndex(17)]
    [return: NativeTypeName("SlangInt")]
    public long getLoadedModuleCount()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, long>)(lpVtbl[17]))((ISession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(18)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* getLoadedModule([NativeTypeName("SlangInt")] long index)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, long, IModule*>)(lpVtbl[18]))((ISession*)Unsafe.AsPointer(ref this), index);
    }

    [VtblIndex(19)]
    public bool isBinaryModuleUpToDate([NativeTypeName("const char *")] sbyte* modulePath, [NativeTypeName("slang::IBlob *")] ISlangBlob* binaryModuleBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, ISlangBlob*, byte>)(lpVtbl[19]))((ISession*)Unsafe.AsPointer(ref this), modulePath, binaryModuleBlob) != 0;
    }

    [VtblIndex(20)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModuleFromSourceString([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* @string, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, sbyte*, sbyte*, ISlangBlob**, IModule*>)(lpVtbl[20]))((ISession*)Unsafe.AsPointer(ref this), moduleName, path, @string, outDiagnostics);
    }

    [VtblIndex(21)]
    [return: NativeTypeName("SlangResult")]
    public int getDynamicObjectRTTIBytes([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, [NativeTypeName("uint32_t *")] uint* outRTTIDataBuffer, [NativeTypeName("uint32_t")] uint bufferSizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, uint*, uint, int>)(lpVtbl[21]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outRTTIDataBuffer, bufferSizeInBytes);
    }

    [VtblIndex(22)]
    [return: NativeTypeName("SlangResult")]
    public int loadModuleInfoFromIRBlob([NativeTypeName("slang::IBlob *")] ISlangBlob* source, [NativeTypeName("SlangInt &")] long* outModuleVersion, [NativeTypeName("const char *&")] sbyte** outModuleCompilerVersion, [NativeTypeName("const char *&")] sbyte** outModuleName)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, ISlangBlob*, long*, sbyte**, sbyte**, int>)(lpVtbl[22]))((ISession*)Unsafe.AsPointer(ref this), source, outModuleVersion, outModuleCompilerVersion, outModuleName);
    }

    [VtblIndex(23)]
    [return: NativeTypeName("SlangResult")]
    public int getDeclSourceLocation([NativeTypeName("slang::DeclReflection *")] DeclReflection* decl, [NativeTypeName("slang::SourceLocation *")] SourceLocation* outLocation)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, DeclReflection*, SourceLocation*, int>)(lpVtbl[23]))((ISession*)Unsafe.AsPointer(ref this), decl, outLocation);
    }
}
