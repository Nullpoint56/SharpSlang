using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IGlobalSession : ISlangUnknown")]
public unsafe partial struct IGlobalSession
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
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangUUID*, void**, int>)(lpVtbl[0]))((IGlobalSession*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, uint>)(lpVtbl[1]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, uint>)(lpVtbl[2]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int createSession([NativeTypeName("const SessionDesc &")] SessionDesc* desc, ISession** outSession)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SessionDesc*, ISession**, int>)(lpVtbl[3]))((IGlobalSession*)Unsafe.AsPointer(ref this), desc, outSession);
    }

    [VtblIndex(4)]
    public SlangProfileID findProfile([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, SlangProfileID>)(lpVtbl[4]))((IGlobalSession*)Unsafe.AsPointer(ref this), name);
    }

    [VtblIndex(5)]
    public void setDownstreamCompilerPath(SlangPassThrough passThrough, [NativeTypeName("const char *")] sbyte* path)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, sbyte*, void>)(lpVtbl[5]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, path);
    }

    [VtblIndex(6)]
    public void setDownstreamCompilerPrelude(SlangPassThrough passThrough, [NativeTypeName("const char *")] sbyte* preludeText)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, sbyte*, void>)(lpVtbl[6]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, preludeText);
    }

    [VtblIndex(7)]
    public void getDownstreamCompilerPrelude(SlangPassThrough passThrough, ISlangBlob** outPrelude)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, ISlangBlob**, void>)(lpVtbl[7]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, outPrelude);
    }

    [VtblIndex(8)]
    [return: NativeTypeName("const char *")]
    public sbyte* getBuildTagString()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*>)(lpVtbl[8]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(9)]
    [return: NativeTypeName("SlangResult")]
    public int setDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage, SlangPassThrough defaultCompiler)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, SlangPassThrough, int>)(lpVtbl[9]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, defaultCompiler);
    }

    [VtblIndex(10)]
    public SlangPassThrough getDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, SlangPassThrough>)(lpVtbl[10]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage);
    }

    [VtblIndex(11)]
    public void setLanguagePrelude(SlangSourceLanguage sourceLanguage, [NativeTypeName("const char *")] sbyte* preludeText)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, sbyte*, void>)(lpVtbl[11]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, preludeText);
    }

    [VtblIndex(12)]
    public void getLanguagePrelude(SlangSourceLanguage sourceLanguage, ISlangBlob** outPrelude)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, ISlangBlob**, void>)(lpVtbl[12]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, outPrelude);
    }

    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    [Obsolete]
    public int createCompileRequest([NativeTypeName("slang::ICompileRequest **")] ICompileRequest** outCompileRequest)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, ICompileRequest**, int>)(lpVtbl[13]))((IGlobalSession*)Unsafe.AsPointer(ref this), outCompileRequest);
    }

    [VtblIndex(14)]
    [Obsolete]
    public void addBuiltins([NativeTypeName("const char *")] sbyte* sourcePath, [NativeTypeName("const char *")] sbyte* sourceString)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, sbyte*, void>)(lpVtbl[14]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourcePath, sourceString);
    }

    [VtblIndex(15)]
    public void setSharedLibraryLoader(ISlangSharedLibraryLoader* loader)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, ISlangSharedLibraryLoader*, void>)(lpVtbl[15]))((IGlobalSession*)Unsafe.AsPointer(ref this), loader);
    }

    [VtblIndex(16)]
    public ISlangSharedLibraryLoader* getSharedLibraryLoader()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, ISlangSharedLibraryLoader*>)(lpVtbl[16]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(17)]
    [return: NativeTypeName("SlangResult")]
    public int checkCompileTargetSupport(SlangCompileTarget target)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, int>)(lpVtbl[17]))((IGlobalSession*)Unsafe.AsPointer(ref this), target);
    }

    [VtblIndex(18)]
    [return: NativeTypeName("SlangResult")]
    public int checkPassThroughSupport(SlangPassThrough passThrough)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, int>)(lpVtbl[18]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough);
    }

    [VtblIndex(19)]
    [return: NativeTypeName("SlangResult")]
    public int compileCoreModule([NativeTypeName("slang::CompileCoreModuleFlags")] uint flags)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, uint, int>)(lpVtbl[19]))((IGlobalSession*)Unsafe.AsPointer(ref this), flags);
    }

    [VtblIndex(20)]
    [return: NativeTypeName("SlangResult")]
    public int loadCoreModule([NativeTypeName("const void *")] void* coreModule, [NativeTypeName("size_t")] nuint coreModuleSizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, void*, nuint, int>)(lpVtbl[20]))((IGlobalSession*)Unsafe.AsPointer(ref this), coreModule, coreModuleSizeInBytes);
    }

    [VtblIndex(21)]
    [return: NativeTypeName("SlangResult")]
    public int saveCoreModule(SlangArchiveType archiveType, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangArchiveType, ISlangBlob**, int>)(lpVtbl[21]))((IGlobalSession*)Unsafe.AsPointer(ref this), archiveType, outBlob);
    }

    [VtblIndex(22)]
    public SlangCapabilityID findCapability([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, SlangCapabilityID>)(lpVtbl[22]))((IGlobalSession*)Unsafe.AsPointer(ref this), name);
    }

    [VtblIndex(23)]
    public void setDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, SlangCompileTarget, SlangPassThrough, void>)(lpVtbl[23]))((IGlobalSession*)Unsafe.AsPointer(ref this), source, target, compiler);
    }

    [VtblIndex(24)]
    public SlangPassThrough getDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, SlangCompileTarget, SlangPassThrough>)(lpVtbl[24]))((IGlobalSession*)Unsafe.AsPointer(ref this), source, target);
    }

    [VtblIndex(25)]
    public void getCompilerElapsedTime(double* outTotalTime, double* outDownstreamTime)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, double*, double*, void>)(lpVtbl[25]))((IGlobalSession*)Unsafe.AsPointer(ref this), outTotalTime, outDownstreamTime);
    }

    [VtblIndex(26)]
    [return: NativeTypeName("SlangResult")]
    public int setSPIRVCoreGrammar([NativeTypeName("const char *")] sbyte* jsonPath)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, int>)(lpVtbl[26]))((IGlobalSession*)Unsafe.AsPointer(ref this), jsonPath);
    }

    [VtblIndex(27)]
    [return: NativeTypeName("SlangResult")]
    public int parseCommandLineArguments(int argc, [NativeTypeName("const char *const *")] sbyte** argv, [NativeTypeName("slang::SessionDesc *")] SessionDesc* outSessionDesc, ISlangUnknown** outAuxAllocation)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, int, sbyte**, SessionDesc*, ISlangUnknown**, int>)(lpVtbl[27]))((IGlobalSession*)Unsafe.AsPointer(ref this), argc, argv, outSessionDesc, outAuxAllocation);
    }

    [VtblIndex(28)]
    [return: NativeTypeName("SlangResult")]
    public int getSessionDescDigest([NativeTypeName("slang::SessionDesc *")] SessionDesc* sessionDesc, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SessionDesc*, ISlangBlob**, int>)(lpVtbl[28]))((IGlobalSession*)Unsafe.AsPointer(ref this), sessionDesc, outBlob);
    }

    [VtblIndex(29)]
    [return: NativeTypeName("SlangResult")]
    public int compileBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, [NativeTypeName("slang::CompileCoreModuleFlags")] uint flags)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, uint, int>)(lpVtbl[29]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, flags);
    }

    [VtblIndex(30)]
    [return: NativeTypeName("SlangResult")]
    public int loadBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, [NativeTypeName("const void *")] void* moduleData, [NativeTypeName("size_t")] nuint sizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, void*, nuint, int>)(lpVtbl[30]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, moduleData, sizeInBytes);
    }

    [VtblIndex(31)]
    [return: NativeTypeName("SlangResult")]
    public int saveBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, SlangArchiveType archiveType, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, SlangArchiveType, ISlangBlob**, int>)(lpVtbl[31]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, archiveType, outBlob);
    }

    [VtblIndex(32)]
    [return: NativeTypeName("SlangResult")]
    public int getDownstreamCompilerPath(SlangPassThrough passThrough, ISlangBlob** outPath)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, ISlangBlob**, int>)(lpVtbl[32]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, outPath);
    }
}
