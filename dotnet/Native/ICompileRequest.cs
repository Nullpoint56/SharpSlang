using System;
using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ICompileRequest : ISlangUnknown")]
public unsafe partial struct ICompileRequest
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
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangUUID*, void**, int>)(lpVtbl[0]))((ICompileRequest*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint>)(lpVtbl[1]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint>)(lpVtbl[2]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    public void setFileSystem(ISlangFileSystem* fileSystem)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangFileSystem*, void>)(lpVtbl[3]))((ICompileRequest*)Unsafe.AsPointer(ref this), fileSystem);
    }

    [VtblIndex(4)]
    public void setCompileFlags([NativeTypeName("SlangCompileFlags")] uint flags)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint, void>)(lpVtbl[4]))((ICompileRequest*)Unsafe.AsPointer(ref this), flags);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangCompileFlags")]
    public uint getCompileFlags()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint>)(lpVtbl[5]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(6)]
    public void setDumpIntermediates(int enable)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, void>)(lpVtbl[6]))((ICompileRequest*)Unsafe.AsPointer(ref this), enable);
    }

    [VtblIndex(7)]
    public void setDumpIntermediatePrefix([NativeTypeName("const char *")] sbyte* prefix)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void>)(lpVtbl[7]))((ICompileRequest*)Unsafe.AsPointer(ref this), prefix);
    }

    [VtblIndex(8)]
    public void setLineDirectiveMode(SlangLineDirectiveMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangLineDirectiveMode, void>)(lpVtbl[8]))((ICompileRequest*)Unsafe.AsPointer(ref this), mode);
    }

    [VtblIndex(9)]
    public void setCodeGenTarget(SlangCompileTarget target)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangCompileTarget, void>)(lpVtbl[9]))((ICompileRequest*)Unsafe.AsPointer(ref this), target);
    }

    [VtblIndex(10)]
    public int addCodeGenTarget(SlangCompileTarget target)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangCompileTarget, int>)(lpVtbl[10]))((ICompileRequest*)Unsafe.AsPointer(ref this), target);
    }

    [VtblIndex(11)]
    public void setTargetProfile(int targetIndex, SlangProfileID profile)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, SlangProfileID, void>)(lpVtbl[11]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, profile);
    }

    [VtblIndex(12)]
    public void setTargetFlags(int targetIndex, [NativeTypeName("SlangTargetFlags")] uint flags)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, uint, void>)(lpVtbl[12]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, flags);
    }

    [VtblIndex(13)]
    public void setTargetFloatingPointMode(int targetIndex, SlangFloatingPointMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, SlangFloatingPointMode, void>)(lpVtbl[13]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, mode);
    }

    [VtblIndex(14)]
    public void setTargetMatrixLayoutMode(int targetIndex, SlangMatrixLayoutMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, SlangMatrixLayoutMode, void>)(lpVtbl[14]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, mode);
    }

    [VtblIndex(15)]
    public void setMatrixLayoutMode(SlangMatrixLayoutMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangMatrixLayoutMode, void>)(lpVtbl[15]))((ICompileRequest*)Unsafe.AsPointer(ref this), mode);
    }

    [VtblIndex(16)]
    public void setDebugInfoLevel(SlangDebugInfoLevel level)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangDebugInfoLevel, void>)(lpVtbl[16]))((ICompileRequest*)Unsafe.AsPointer(ref this), level);
    }

    [VtblIndex(17)]
    public void setOptimizationLevel(SlangOptimizationLevel level)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangOptimizationLevel, void>)(lpVtbl[17]))((ICompileRequest*)Unsafe.AsPointer(ref this), level);
    }

    [VtblIndex(18)]
    public void setOutputContainerFormat(SlangContainerFormat format)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangContainerFormat, void>)(lpVtbl[18]))((ICompileRequest*)Unsafe.AsPointer(ref this), format);
    }

    [VtblIndex(19)]
    public void setPassThrough(SlangPassThrough passThrough)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangPassThrough, void>)(lpVtbl[19]))((ICompileRequest*)Unsafe.AsPointer(ref this), passThrough);
    }

    [VtblIndex(20)]
    public void setDiagnosticCallback([NativeTypeName("SlangDiagnosticCallback")] delegate* unmanaged[Cdecl]<sbyte*, void*, void> callback, [NativeTypeName("const void *")] void* userData)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, delegate* unmanaged[Cdecl]<sbyte*, void*, void>, void*, void>)(lpVtbl[20]))((ICompileRequest*)Unsafe.AsPointer(ref this), callback, userData);
    }

    [VtblIndex(21)]
    public void setWriter(SlangWriterChannel channel, ISlangWriter* writer)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangWriterChannel, ISlangWriter*, void>)(lpVtbl[21]))((ICompileRequest*)Unsafe.AsPointer(ref this), channel, writer);
    }

    [VtblIndex(22)]
    public ISlangWriter* getWriter(SlangWriterChannel channel)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangWriterChannel, ISlangWriter*>)(lpVtbl[22]))((ICompileRequest*)Unsafe.AsPointer(ref this), channel);
    }

    [VtblIndex(23)]
    public void addSearchPath([NativeTypeName("const char *")] sbyte* searchDir)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void>)(lpVtbl[23]))((ICompileRequest*)Unsafe.AsPointer(ref this), searchDir);
    }

    [VtblIndex(24)]
    public void addPreprocessorDefine([NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, sbyte*, void>)(lpVtbl[24]))((ICompileRequest*)Unsafe.AsPointer(ref this), key, value);
    }

    [VtblIndex(25)]
    [return: NativeTypeName("SlangResult")]
    public int processCommandLineArguments([NativeTypeName("const char *const *")] sbyte** args, int argCount)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte**, int, int>)(lpVtbl[25]))((ICompileRequest*)Unsafe.AsPointer(ref this), args, argCount);
    }

    [VtblIndex(26)]
    public int addTranslationUnit(SlangSourceLanguage language, [NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangSourceLanguage, sbyte*, int>)(lpVtbl[26]))((ICompileRequest*)Unsafe.AsPointer(ref this), language, name);
    }

    [VtblIndex(27)]
    public void setDefaultModuleName([NativeTypeName("const char *")] sbyte* defaultModuleName)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void>)(lpVtbl[27]))((ICompileRequest*)Unsafe.AsPointer(ref this), defaultModuleName);
    }

    [VtblIndex(28)]
    public void addTranslationUnitPreprocessorDefine(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, void>)(lpVtbl[28]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, key, value);
    }

    [VtblIndex(29)]
    public void addTranslationUnitSourceFile(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, void>)(lpVtbl[29]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path);
    }

    [VtblIndex(30)]
    public void addTranslationUnitSourceString(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* source)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, void>)(lpVtbl[30]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, source);
    }

    [VtblIndex(31)]
    [return: NativeTypeName("SlangResult")]
    public int addLibraryReference([NativeTypeName("const char *")] sbyte* basePath, [NativeTypeName("const void *")] void* libData, [NativeTypeName("size_t")] nuint libDataSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void*, nuint, int>)(lpVtbl[31]))((ICompileRequest*)Unsafe.AsPointer(ref this), basePath, libData, libDataSize);
    }

    [VtblIndex(32)]
    public void addTranslationUnitSourceStringSpan(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* sourceBegin, [NativeTypeName("const char *")] sbyte* sourceEnd)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, sbyte*, void>)(lpVtbl[32]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, sourceBegin, sourceEnd);
    }

    [VtblIndex(33)]
    public void addTranslationUnitSourceBlob(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, ISlangBlob* sourceBlob)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, ISlangBlob*, void>)(lpVtbl[33]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, sourceBlob);
    }

    [VtblIndex(34)]
    public int addEntryPoint(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, SlangStage, int>)(lpVtbl[34]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, name, stage);
    }

    [VtblIndex(35)]
    public int addEntryPointEx(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage, int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, SlangStage, int, sbyte**, int>)(lpVtbl[35]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, name, stage, genericArgCount, genericArgs);
    }

    [VtblIndex(36)]
    [return: NativeTypeName("SlangResult")]
    public int setGlobalGenericArgs(int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte**, int>)(lpVtbl[36]))((ICompileRequest*)Unsafe.AsPointer(ref this), genericArgCount, genericArgs);
    }

    [VtblIndex(37)]
    [return: NativeTypeName("SlangResult")]
    public int setTypeNameForGlobalExistentialTypeParam(int slotIndex, [NativeTypeName("const char *")] sbyte* typeName)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, int>)(lpVtbl[37]))((ICompileRequest*)Unsafe.AsPointer(ref this), slotIndex, typeName);
    }

    [VtblIndex(38)]
    [return: NativeTypeName("SlangResult")]
    public int setTypeNameForEntryPointExistentialTypeParam(int entryPointIndex, int slotIndex, [NativeTypeName("const char *")] sbyte* typeName)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, sbyte*, int>)(lpVtbl[38]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, slotIndex, typeName);
    }

    [VtblIndex(39)]
    public void setAllowGLSLInput([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[39]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(40)]
    [return: NativeTypeName("SlangResult")]
    public int compile()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[40]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(41)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDiagnosticOutput()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*>)(lpVtbl[41]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(42)]
    [return: NativeTypeName("SlangResult")]
    public int getDiagnosticOutputBlob(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[42]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    [VtblIndex(43)]
    public int getDependencyFileCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[43]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(44)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDependencyFilePath(int index)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*>)(lpVtbl[44]))((ICompileRequest*)Unsafe.AsPointer(ref this), index);
    }

    [VtblIndex(45)]
    public int getTranslationUnitCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[45]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(46)]
    [return: NativeTypeName("const char *")]
    public sbyte* getEntryPointSource(int entryPointIndex)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*>)(lpVtbl[46]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex);
    }

    [VtblIndex(47)]
    [return: NativeTypeName("const void *")]
    public void* getEntryPointCode(int entryPointIndex, [NativeTypeName("size_t *")] nuint* outSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, nuint*, void*>)(lpVtbl[47]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, outSize);
    }

    [VtblIndex(48)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCodeBlob(int entryPointIndex, int targetIndex, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, ISlangBlob**, int>)(lpVtbl[48]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outBlob);
    }

    [VtblIndex(49)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointHostCallable(int entryPointIndex, int targetIndex, ISlangSharedLibrary** outSharedLibrary)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, ISlangSharedLibrary**, int>)(lpVtbl[49]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outSharedLibrary);
    }

    [VtblIndex(50)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCodeBlob(int targetIndex, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, ISlangBlob**, int>)(lpVtbl[50]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, outBlob);
    }

    [VtblIndex(51)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetHostCallable(int targetIndex, ISlangSharedLibrary** outSharedLibrary)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, ISlangSharedLibrary**, int>)(lpVtbl[51]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, outSharedLibrary);
    }

    [VtblIndex(52)]
    [return: NativeTypeName("const void *")]
    public void* getCompileRequestCode([NativeTypeName("size_t *")] nuint* outSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, nuint*, void*>)(lpVtbl[52]))((ICompileRequest*)Unsafe.AsPointer(ref this), outSize);
    }

    [VtblIndex(53)]
    public ISlangMutableFileSystem* getCompileRequestResultAsFileSystem()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangMutableFileSystem*>)(lpVtbl[53]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(54)]
    [return: NativeTypeName("SlangResult")]
    public int getContainerCode(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[54]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    [VtblIndex(55)]
    [return: NativeTypeName("SlangResult")]
    public int loadRepro(ISlangFileSystem* fileSystem, [NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangFileSystem*, void*, nuint, int>)(lpVtbl[55]))((ICompileRequest*)Unsafe.AsPointer(ref this), fileSystem, data, size);
    }

    [VtblIndex(56)]
    [return: NativeTypeName("SlangResult")]
    public int saveRepro(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[56]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    [VtblIndex(57)]
    [return: NativeTypeName("SlangResult")]
    public int enableReproCapture()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[57]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(58)]
    [return: NativeTypeName("SlangResult")]
    public int getProgram([NativeTypeName("slang::IComponentType **")] IComponentType** outProgram)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, IComponentType**, int>)(lpVtbl[58]))((ICompileRequest*)Unsafe.AsPointer(ref this), outProgram);
    }

    [VtblIndex(59)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPoint([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("slang::IComponentType **")] IComponentType** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, IComponentType**, int>)(lpVtbl[59]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, outEntryPoint);
    }

    [VtblIndex(60)]
    [return: NativeTypeName("SlangResult")]
    public int getModule([NativeTypeName("SlangInt")] long translationUnitIndex, [NativeTypeName("slang::IModule **")] IModule** outModule)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, IModule**, int>)(lpVtbl[60]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, outModule);
    }

    [VtblIndex(61)]
    [return: NativeTypeName("SlangResult")]
    public int getSession([NativeTypeName("slang::ISession **")] ISession** outSession)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISession**, int>)(lpVtbl[61]))((ICompileRequest*)Unsafe.AsPointer(ref this), outSession);
    }

    [VtblIndex(62)]
    [return: NativeTypeName("SlangReflection *")]
    public SlangProgramLayout* getReflection()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangProgramLayout*>)(lpVtbl[62]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(63)]
    public void setCommandLineCompilerMode()
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, void>)(lpVtbl[63]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(64)]
    [return: NativeTypeName("SlangResult")]
    public int addTargetCapability([NativeTypeName("SlangInt")] long targetIndex, SlangCapabilityID capability)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangCapabilityID, int>)(lpVtbl[64]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, capability);
    }

    [VtblIndex(65)]
    [return: NativeTypeName("SlangResult")]
    public int getProgramWithEntryPoints([NativeTypeName("slang::IComponentType **")] IComponentType** outProgram)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, IComponentType**, int>)(lpVtbl[65]))((ICompileRequest*)Unsafe.AsPointer(ref this), outProgram);
    }

    [VtblIndex(66)]
    [return: NativeTypeName("SlangResult")]
    public int isParameterLocationUsed([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, SlangParameterCategory category, [NativeTypeName("SlangUInt")] ulong spaceIndex, [NativeTypeName("SlangUInt")] ulong registerIndex, [NativeTypeName("bool &")] bool* outUsed)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, long, SlangParameterCategory, ulong, ulong, bool*, int>)(lpVtbl[66]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, category, spaceIndex, registerIndex, outUsed);
    }

    [VtblIndex(67)]
    public void setTargetLineDirectiveMode([NativeTypeName("SlangInt")] long targetIndex, SlangLineDirectiveMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangLineDirectiveMode, void>)(lpVtbl[67]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, mode);
    }

    [VtblIndex(68)]
    public void setTargetForceGLSLScalarBufferLayout(int targetIndex, [NativeTypeName("bool")] byte forceScalarLayout)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[68]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, forceScalarLayout);
    }

    [VtblIndex(69)]
    public void overrideDiagnosticSeverity([NativeTypeName("SlangInt")] long messageID, SlangSeverity overrideSeverity)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangSeverity, void>)(lpVtbl[69]))((ICompileRequest*)Unsafe.AsPointer(ref this), messageID, overrideSeverity);
    }

    [VtblIndex(70)]
    [return: NativeTypeName("SlangDiagnosticFlags")]
    public int getDiagnosticFlags()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[70]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(71)]
    public void setDiagnosticFlags([NativeTypeName("SlangDiagnosticFlags")] int flags)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, void>)(lpVtbl[71]))((ICompileRequest*)Unsafe.AsPointer(ref this), flags);
    }

    [VtblIndex(72)]
    public void setDebugInfoFormat(SlangDebugInfoFormat debugFormat)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangDebugInfoFormat, void>)(lpVtbl[72]))((ICompileRequest*)Unsafe.AsPointer(ref this), debugFormat);
    }

    [VtblIndex(73)]
    public void setEnableEffectAnnotations([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[73]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(74)]
    public void setReportDownstreamTime([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[74]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(75)]
    public void setReportPerfBenchmark([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[75]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(76)]
    public void setSkipSPIRVValidation([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[76]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(77)]
    public void setTargetUseMinimumSlangOptimization(int targetIndex, [NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[77]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, value);
    }

    [VtblIndex(78)]
    public void setIgnoreCapabilityCheck([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[78]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    [VtblIndex(79)]
    [return: NativeTypeName("SlangResult")]
    public int getCompileTimeProfile(ISlangProfiler** compileTimeProfile, [NativeTypeName("bool")] byte shouldClear)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangProfiler**, byte, int>)(lpVtbl[79]))((ICompileRequest*)Unsafe.AsPointer(ref this), compileTimeProfile, shouldClear);
    }

    [VtblIndex(80)]
    public void setTargetGenerateWholeProgram(int targetIndex, [NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[80]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, value);
    }

    [VtblIndex(81)]
    public void setTargetForceDXLayout(int targetIndex, [NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[81]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, value);
    }

    [VtblIndex(82)]
    public void setTargetEmbedDownstreamIR(int targetIndex, [NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[82]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, value);
    }

    [VtblIndex(83)]
    public void setTargetForceCLayout(int targetIndex, [NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[83]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, value);
    }
}
