using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A request for one or more compilation actions to be performed.</summary>
[NativeTypeName("struct ICompileRequest : ISlangUnknown")]
public unsafe partial struct ICompileRequest
{
    public void** lpVtbl;

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

    /// <summary>Set the filesystem hook to use for a compile request</summary>
    /// <remarks>
    /// <para>The provided <c>fileSystem</c> will be used to load any files that need to be loaded during processing of the compile <c>request</c>. This includes:</para>
    /// <para>- Source files loaded via <c>spAddTranslationUnitSourceFile</c> - Files referenced via <c>#include</c> - Files loaded to resolve <c>#import</c> operations</para>
    /// </remarks>
    [VtblIndex(3)]
    public void setFileSystem(ISlangFileSystem* fileSystem)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangFileSystem*, void>)(lpVtbl[3]))((ICompileRequest*)Unsafe.AsPointer(ref this), fileSystem);
    }

    /// <summary>Set flags to be used for compilation.</summary>
    [VtblIndex(4)]
    public void setCompileFlags([NativeTypeName("SlangCompileFlags")] uint flags)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint, void>)(lpVtbl[4]))((ICompileRequest*)Unsafe.AsPointer(ref this), flags);
    }

    /// <summary>Returns the compilation flags previously set with <c>setCompileFlags</c></summary>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangCompileFlags")]
    public uint getCompileFlags()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, uint>)(lpVtbl[5]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Set whether to dump intermediate results (for debugging) or not.</summary>
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

    /// <summary>Set whether (and how) <c>#line</c> directives should be output.</summary>
    [VtblIndex(8)]
    public void setLineDirectiveMode(SlangLineDirectiveMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangLineDirectiveMode, void>)(lpVtbl[8]))((ICompileRequest*)Unsafe.AsPointer(ref this), mode);
    }

    /// <summary>Sets the target for code generation.</summary>
    /// <param name="target">The code generation target. Possible values are: - SLANG_GLSL. Generates GLSL code. - SLANG_HLSL. Generates HLSL code. - SLANG_SPIRV. Generates SPIR-V code.</param>
    [VtblIndex(9)]
    public void setCodeGenTarget(SlangCompileTarget target)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangCompileTarget, void>)(lpVtbl[9]))((ICompileRequest*)Unsafe.AsPointer(ref this), target);
    }

    /// <summary>Add a code-generation target to be used.</summary>
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

    /// <summary>Set the floating point mode (e.g., precise or fast) to use a target.</summary>
    [VtblIndex(13)]
    public void setTargetFloatingPointMode(int targetIndex, SlangFloatingPointMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, SlangFloatingPointMode, void>)(lpVtbl[13]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, mode);
    }

    /// <summary>DEPRECATED: use <c>spSetMatrixLayoutMode</c> instead.</summary>
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

    /// <summary>Set the level of debug information to produce.</summary>
    [VtblIndex(16)]
    public void setDebugInfoLevel(SlangDebugInfoLevel level)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangDebugInfoLevel, void>)(lpVtbl[16]))((ICompileRequest*)Unsafe.AsPointer(ref this), level);
    }

    /// <summary>Set the level of optimization to perform.</summary>
    [VtblIndex(17)]
    public void setOptimizationLevel(SlangOptimizationLevel level)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangOptimizationLevel, void>)(lpVtbl[17]))((ICompileRequest*)Unsafe.AsPointer(ref this), level);
    }

    /// <summary>Set the container format to be used for binary output.</summary>
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

    /// <summary>Add a path to use when searching for referenced files. This will be used for both <c>#include</c> directives and also for explicit <c>__import</c> declarations.</summary>
    /// <remarks>
    /// <para><c>ctx</c>: The compilation context.</para>
    /// <para><c>searchDir</c>: The additional search directory.</para>
    /// </remarks>
    [VtblIndex(23)]
    public void addSearchPath([NativeTypeName("const char *")] sbyte* searchDir)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void>)(lpVtbl[23]))((ICompileRequest*)Unsafe.AsPointer(ref this), searchDir);
    }

    /// <summary>Add a macro definition to be used during preprocessing.</summary>
    /// <param name="key">The name of the macro to define.</param>
    /// <param name="value">The value of the macro to define.</param>
    [VtblIndex(24)]
    public void addPreprocessorDefine([NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, sbyte*, void>)(lpVtbl[24]))((ICompileRequest*)Unsafe.AsPointer(ref this), key, value);
    }

    /// <summary>Set options using arguments as if specified via command line.</summary>
    /// <returns>Returns SlangResult. On success SLANG_SUCCEEDED(result) is true.</returns>
    [VtblIndex(25)]
    [return: NativeTypeName("SlangResult")]
    public int processCommandLineArguments([NativeTypeName("const char *const *")] sbyte** args, int argCount)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte**, int, int>)(lpVtbl[25]))((ICompileRequest*)Unsafe.AsPointer(ref this), args, argCount);
    }

    /// <summary>Add a distinct translation unit to the compilation request</summary>
    /// <remarks>
    /// <para><c>name</c> is optional. Returns the zero-based index of the translation unit created.</para>
    /// </remarks>
    [VtblIndex(26)]
    public int addTranslationUnit(SlangSourceLanguage language, [NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangSourceLanguage, sbyte*, int>)(lpVtbl[26]))((ICompileRequest*)Unsafe.AsPointer(ref this), language, name);
    }

    /// <summary>Set a default module name. Translation units will default to this module name if one is not passed. If not set each translation unit will get a unique name.</summary>
    [VtblIndex(27)]
    public void setDefaultModuleName([NativeTypeName("const char *")] sbyte* defaultModuleName)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void>)(lpVtbl[27]))((ICompileRequest*)Unsafe.AsPointer(ref this), defaultModuleName);
    }

    /// <summary>Add a preprocessor definition that is scoped to a single translation unit.</summary>
    /// <param name="translationUnitIndex">The index of the translation unit to get the definition.</param>
    /// <param name="key">The name of the macro to define.</param>
    /// <param name="value">The value of the macro to define.</param>
    [VtblIndex(28)]
    public void addTranslationUnitPreprocessorDefine(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, void>)(lpVtbl[28]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, key, value);
    }

    /// <summary>Add a source file to the given translation unit.</summary>
    /// <remarks>
    /// <para>If a user-defined file system has been specified via <c>spSetFileSystem</c>, then it will be used to load the file at <c>path</c>. Otherwise, Slang will use the OS file system.</para>
    /// <para>This function does *not* search for a file using the registered search paths (<c>spAddSearchPath</c>), and instead using the given <c>path</c> as-is.</para>
    /// </remarks>
    [VtblIndex(29)]
    public void addTranslationUnitSourceFile(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, void>)(lpVtbl[29]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path);
    }

    /// <summary>Add a source string to the given translation unit.</summary>
    /// <remarks>
    /// <para>The implementation will make a copy of the source code data. An application may free the buffer immediately after this call returns.</para>
    /// <para>The <c>path</c> will be used in any diagnostic output, as well as to determine the base path when resolving relative <c>#include</c>s.</para>
    /// </remarks>
    /// <param name="translationUnitIndex">The index of the translation unit to add source to.</param>
    /// <param name="path">The file-system path that should be assumed for the source code.</param>
    /// <param name="source">A null-terminated UTF-8 encoded string of source code.</param>
    [VtblIndex(30)]
    public void addTranslationUnitSourceString(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* source)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, void>)(lpVtbl[30]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, source);
    }

    /// <summary>Add a slang library - such that its contents can be referenced during linking. This is equivalent to the -r command line option.</summary>
    /// <param name="basePath">The base path used to lookup referenced modules.</param>
    /// <param name="libData">The library data</param>
    /// <param name="libDataSize">The size of the library data</param>
    [VtblIndex(31)]
    [return: NativeTypeName("SlangResult")]
    public int addLibraryReference([NativeTypeName("const char *")] sbyte* basePath, [NativeTypeName("const void *")] void* libData, [NativeTypeName("size_t")] nuint libDataSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*, void*, nuint, int>)(lpVtbl[31]))((ICompileRequest*)Unsafe.AsPointer(ref this), basePath, libData, libDataSize);
    }

    /// <summary>Add a source string to the given translation unit.</summary>
    /// <remarks>
    /// <para>The implementation will make a copy of the source code data. An application may free the buffer immediately after this call returns.</para>
    /// <para>The <c>path</c> will be used in any diagnostic output, as well as to determine the base path when resolving relative <c>#include</c>s.</para>
    /// </remarks>
    /// <param name="translationUnitIndex">The index of the translation unit to add source to.</param>
    /// <param name="path">The file-system path that should be assumed for the source code.</param>
    /// <param name="sourceBegin">A pointer to a buffer of UTF-8 encoded source code.</param>
    /// <param name="sourceEnd">A pointer to to the end of the buffer specified in <c>sourceBegin</c></param>
    [VtblIndex(32)]
    public void addTranslationUnitSourceStringSpan(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* sourceBegin, [NativeTypeName("const char *")] sbyte* sourceEnd)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, sbyte*, sbyte*, void>)(lpVtbl[32]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, sourceBegin, sourceEnd);
    }

    /// <summary>Add a blob of source code to the given translation unit.</summary>
    /// <remarks>
    /// <para>The compile request will retain a reference to the blob.</para>
    /// <para>The <c>path</c> will be used in any diagnostic output, as well as to determine the base path when resolving relative <c>#include</c>s.</para>
    /// <para><c>translationUnitIndex</c>: The index of the translation unit to add source to.</para>
    /// <para><c>path</c>: The file-system path that should be assumed for the source code.</para>
    /// <para><c>sourceBlob</c>: A blob containing UTF-8 encoded source code.</para>
    /// <para><c>sourceEnd</c>: A pointer to to the end of the buffer specified in <c>sourceBegin</c></para>
    /// </remarks>
    [VtblIndex(33)]
    public void addTranslationUnitSourceBlob(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, ISlangBlob* sourceBlob)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, ISlangBlob*, void>)(lpVtbl[33]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, path, sourceBlob);
    }

    /// <summary>Add an entry point in a particular translation unit</summary>
    [VtblIndex(34)]
    public int addEntryPoint(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, SlangStage, int>)(lpVtbl[34]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, name, stage);
    }

    /// <summary>Add an entry point in a particular translation unit, with additional arguments that specify the concrete type names for entry-point generic type parameters.</summary>
    [VtblIndex(35)]
    public int addEntryPointEx(int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage, int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, SlangStage, int, sbyte**, int>)(lpVtbl[35]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, name, stage, genericArgCount, genericArgs);
    }

    /// <summary>Specify the arguments to use for global generic parameters.</summary>
    [VtblIndex(36)]
    [return: NativeTypeName("SlangResult")]
    public int setGlobalGenericArgs(int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte**, int>)(lpVtbl[36]))((ICompileRequest*)Unsafe.AsPointer(ref this), genericArgCount, genericArgs);
    }

    /// <summary>Specify the concrete type to be used for a global "existential slot."</summary>
    /// <remarks>
    /// <para>Every shader parameter (or leaf field of a <c>struct</c>-type shader parameter) that has an interface or array-of-interface type introduces an existential slot. The number of slots consumed by a shader parameter, and the starting slot of each parameter can be queried via the reflection API using <c>SLANG_PARAMETER_CATEGORY_EXISTENTIAL_TYPE_PARAM</c>.</para>
    /// <para>In order to generate specialized code, a concrete type needs to be specified for each existential slot. This function specifies the name of the type (or in general a type *expression*) to use for a specific slot at the global scope.</para>
    /// </remarks>
    [VtblIndex(37)]
    [return: NativeTypeName("SlangResult")]
    public int setTypeNameForGlobalExistentialTypeParam(int slotIndex, [NativeTypeName("const char *")] sbyte* typeName)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*, int>)(lpVtbl[37]))((ICompileRequest*)Unsafe.AsPointer(ref this), slotIndex, typeName);
    }

    /// <summary>Specify the concrete type to be used for an entry-point "existential slot."</summary>
    /// <remarks>
    /// <para>Every shader parameter (or leaf field of a <c>struct</c>-type shader parameter) that has an interface or array-of-interface type introduces an existential slot. The number of slots consumed by a shader parameter, and the starting slot of each parameter can be queried via the reflection API using <c>SLANG_PARAMETER_CATEGORY_EXISTENTIAL_TYPE_PARAM</c>.</para>
    /// <para>In order to generate specialized code, a concrete type needs to be specified for each existential slot. This function specifies the name of the type (or in general a type *expression*) to use for a specific slot at the entry-point scope.</para>
    /// </remarks>
    [VtblIndex(38)]
    [return: NativeTypeName("SlangResult")]
    public int setTypeNameForEntryPointExistentialTypeParam(int entryPointIndex, int slotIndex, [NativeTypeName("const char *")] sbyte* typeName)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, sbyte*, int>)(lpVtbl[38]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, slotIndex, typeName);
    }

    /// <summary>Deprecated. When enabled, treat every translation unit in this request as GLSL.</summary>
    /// <remarks>
    /// <para>Prefer selecting the source language when creating each translation unit:</para>
    /// <para>code: int translationUnit = request-&gt;addTranslationUnit( SLANG_SOURCE_LANGUAGE_GLSL, "moduleName"); request-&gt;addTranslationUnitSourceFile(translationUnit, path);</para>
    /// <para>endcode:</para>
    /// <para>Enabling this compatibility path emits deprecation diagnostic 117 when the request is compiled. If the request already contains a translation unit explicitly selected as a non-GLSL language, diagnostic 129 warns that this request-wide compatibility setting takes precedence and treats that translation unit as GLSL.</para>
    /// </remarks>
    [VtblIndex(39)]
    public void setAllowGLSLInput([NativeTypeName("bool")] byte value)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, byte, void>)(lpVtbl[39]))((ICompileRequest*)Unsafe.AsPointer(ref this), value);
    }

    /// <summary>Execute the compilation request.</summary>
    /// <returns>SlangResult, SLANG_OK on success. Use SLANG_SUCCEEDED() and SLANG_FAILED() to test SlangResult.</returns>
    [VtblIndex(40)]
    [return: NativeTypeName("SlangResult")]
    public int compile()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[40]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get any diagnostic messages reported by the compiler.</summary>
    /// <remarks>
    /// <para>The returned pointer is only guaranteed to be valid until <c>request</c> is destroyed. Applications that wish to hold on to the diagnostic output for longer should use <c>getDiagnosticOutputBlob</c>.</para>
    /// </remarks>
    /// <returns>A null-terminated UTF-8 encoded string of diagnostic messages.</returns>
    [VtblIndex(41)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDiagnosticOutput()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, sbyte*>)(lpVtbl[41]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get diagnostic messages reported by the compiler.</summary>
    /// <param name="outBlob">A pointer to receive a blob holding a nul-terminated UTF-8 encoded string of diagnostic messages.</param>
    /// <returns>A <c>SlangResult</c> indicating success or failure.</returns>
    [VtblIndex(42)]
    [return: NativeTypeName("SlangResult")]
    public int getDiagnosticOutputBlob(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[42]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    /// <summary>Get the number of files that this compilation depended on.</summary>
    /// <remarks>
    /// <para>This includes both the explicit source files, as well as any additional files that were transitively referenced (e.g., via a <c>#include</c> directive).</para>
    /// </remarks>
    [VtblIndex(43)]
    public int getDependencyFileCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[43]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the path to a file this compilation depended on.</summary>
    [VtblIndex(44)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDependencyFilePath(int index)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*>)(lpVtbl[44]))((ICompileRequest*)Unsafe.AsPointer(ref this), index);
    }

    /// <summary>Get the number of translation units associated with the compilation request</summary>
    [VtblIndex(45)]
    public int getTranslationUnitCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[45]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the output source code associated with a specific entry point.</summary>
    /// <remarks>
    /// <para>The lifetime of the output pointer is the same as <c>request</c>.</para>
    /// </remarks>
    [VtblIndex(46)]
    [return: NativeTypeName("const char *")]
    public sbyte* getEntryPointSource(int entryPointIndex)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, sbyte*>)(lpVtbl[46]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex);
    }

    /// <summary>Get the output bytecode associated with a specific entry point.</summary>
    /// <remarks>
    /// <para>The lifetime of the output pointer is the same as <c>request</c>.</para>
    /// </remarks>
    [VtblIndex(47)]
    [return: NativeTypeName("const void *")]
    public void* getEntryPointCode(int entryPointIndex, [NativeTypeName("size_t *")] nuint* outSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, nuint*, void*>)(lpVtbl[47]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, outSize);
    }

    /// <summary>Get the output code associated with a specific entry point.</summary>
    /// <param name="entryPointIndex">The index of the entry point to get code for.</param>
    /// <param name="targetIndex">The index of the target to get code for (default: zero).</param>
    /// <param name="outBlob">A pointer that will receive the blob of code</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(48)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCodeBlob(int entryPointIndex, int targetIndex, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, ISlangBlob**, int>)(lpVtbl[48]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outBlob);
    }

    /// <summary>Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.</summary>
    /// <remarks>
    /// <para>That the functions remain in scope as long as the ISlangSharedLibrary interface is in scope.</para>
    /// <para>NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.</para>
    /// </remarks>
    /// <param name="entryPointIndex">The index of the entry point to get code for.</param>
    /// <param name="targetIndex">The index of the target to get code for (default: zero).</param>
    /// <param name="outSharedLibrary">A pointer to a ISharedLibrary interface which functions can be queried on.</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(49)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointHostCallable(int entryPointIndex, int targetIndex, ISlangSharedLibrary** outSharedLibrary)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, int, ISlangSharedLibrary**, int>)(lpVtbl[49]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outSharedLibrary);
    }

    /// <summary>Get the output code associated with a specific target.</summary>
    /// <param name="targetIndex">The index of the target to get code for (default: zero).</param>
    /// <param name="outBlob">A pointer that will receive the blob of code</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(50)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCodeBlob(int targetIndex, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, ISlangBlob**, int>)(lpVtbl[50]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, outBlob);
    }

    /// <summary>Get 'callable' functions for a target accessible through the ISlangSharedLibrary interface.</summary>
    /// <remarks>
    /// <para>That the functions remain in scope as long as the ISlangSharedLibrary interface is in scope.</para>
    /// <para>NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.</para>
    /// </remarks>
    /// <param name="targetIndex">The index of the target to get code for (default: zero).</param>
    /// <param name="outSharedLibrary">A pointer to a ISharedLibrary interface which functions can be queried on.</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(51)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetHostCallable(int targetIndex, ISlangSharedLibrary** outSharedLibrary)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, ISlangSharedLibrary**, int>)(lpVtbl[51]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, outSharedLibrary);
    }

    /// <summary>Get the output bytecode associated with an entire compile request.</summary>
    /// <remarks>
    /// <para>The lifetime of the output pointer is the same as <c>request</c> and the last spCompile.</para>
    /// </remarks>
    /// <param name="outSize">The size of the containers contents in bytes. Will be zero if there is no code available.</param>
    /// <returns>Pointer to start of the contained data, or nullptr if there is no code available.</returns>
    [VtblIndex(52)]
    [return: NativeTypeName("const void *")]
    public void* getCompileRequestCode([NativeTypeName("size_t *")] nuint* outSize)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, nuint*, void*>)(lpVtbl[52]))((ICompileRequest*)Unsafe.AsPointer(ref this), outSize);
    }

    /// <summary>Get the compilation result as a file system. The result is not written to the actual OS file system, but is made available as an in memory representation.</summary>
    [VtblIndex(53)]
    public ISlangMutableFileSystem* getCompileRequestResultAsFileSystem()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangMutableFileSystem*>)(lpVtbl[53]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Return the container code as a blob. The container blob is created as part of a compilation (with spCompile), and a container is produced with a suitable ContainerFormat.</summary>
    /// <remarks>
    /// <para><c>outSize</c>: The blob containing the container data.</para>
    /// </remarks>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(54)]
    [return: NativeTypeName("SlangResult")]
    public int getContainerCode(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[54]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    /// <summary>Load repro from memory specified.</summary>
    /// <remarks>
    /// <para>Should only be performed on a newly created request.</para>
    /// <para>NOTE! When using the fileSystem, files will be loaded via their <c>unique names</c> as if they are part of the flat file system. This mechanism is described more fully in docs/repro.md.</para>
    /// </remarks>
    /// <param name="fileSystem">An (optional) filesystem. Pass nullptr to just use contents of repro held in data.</param>
    /// <param name="data">The data to load from.</param>
    /// <param name="size">The size of the data to load from.</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(55)]
    [return: NativeTypeName("SlangResult")]
    public int loadRepro(ISlangFileSystem* fileSystem, [NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangFileSystem*, void*, nuint, int>)(lpVtbl[55]))((ICompileRequest*)Unsafe.AsPointer(ref this), fileSystem, data, size);
    }

    /// <summary>Save repro state. Should *typically* be performed after spCompile, so that everything that is needed for a compilation is available.</summary>
    /// <param name="outBlob">Blob that will hold the serialized state</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(56)]
    [return: NativeTypeName("SlangResult")]
    public int saveRepro(ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISlangBlob**, int>)(lpVtbl[56]))((ICompileRequest*)Unsafe.AsPointer(ref this), outBlob);
    }

    /// <summary>Enable repro capture.</summary>
    /// <remarks>
    /// <para>Should be set after any ISlangFileSystem has been set, but before any compilation. It ensures that everything that the ISlangFileSystem accesses will be correctly recorded. Note that if a ISlangFileSystem/ISlangFileSystemExt isn't explicitly set (ie the default is used), then the request will automatically be set up to record everything appropriate.</para>
    /// </remarks>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(57)]
    [return: NativeTypeName("SlangResult")]
    public int enableReproCapture()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[57]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the (linked) program for a compile request.</summary>
    /// <remarks>
    /// <para>The linked program will include all of the global-scope modules for the translation units in the program, plus any modules that they <c>import</c> (transitively), specialized to any global specialization arguments that were provided via the API.</para>
    /// </remarks>
    [VtblIndex(58)]
    [return: NativeTypeName("SlangResult")]
    public int getProgram([NativeTypeName("slang::IComponentType **")] IComponentType** outProgram)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, IComponentType**, int>)(lpVtbl[58]))((ICompileRequest*)Unsafe.AsPointer(ref this), outProgram);
    }

    /// <summary>Get the (partially linked) component type for an entry point.</summary>
    /// <remarks>
    /// <para>The returned component type will include the entry point at the given index, and will be specialized using any specialization arguments that were provided for it via the API.</para>
    /// <para>The returned component will *not* include the modules representing the global scope and its dependencies/specialization, so a client program will typically want to compose this component type with the one returned by <c>spCompileRequest_getProgram</c> to get a complete and usable component type from which kernel code can be requested.</para>
    /// </remarks>
    [VtblIndex(59)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPoint([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("slang::IComponentType **")] IComponentType** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, IComponentType**, int>)(lpVtbl[59]))((ICompileRequest*)Unsafe.AsPointer(ref this), entryPointIndex, outEntryPoint);
    }

    /// <summary>Get the (un-linked) module for a translation unit.</summary>
    /// <remarks>
    /// <para>The returned module will not be linked against any dependencies, nor against any entry points (even entry points declared inside the module). Similarly, the module will not be specialized to the arguments that might have been provided via the API.</para>
    /// <para>This function provides an atomic unit of loaded code that is suitable for looking up types and entry points in the given module, and for linking together to produce a composite program that matches the needs of an application.</para>
    /// </remarks>
    [VtblIndex(60)]
    [return: NativeTypeName("SlangResult")]
    public int getModule([NativeTypeName("SlangInt")] long translationUnitIndex, [NativeTypeName("slang::IModule **")] IModule** outModule)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, IModule**, int>)(lpVtbl[60]))((ICompileRequest*)Unsafe.AsPointer(ref this), translationUnitIndex, outModule);
    }

    /// <summary>Get the <c>ISession</c> handle behind the <c>SlangCompileRequest</c>. TODO(JS): Arguably this should just return the session pointer.</summary>
    [VtblIndex(61)]
    [return: NativeTypeName("SlangResult")]
    public int getSession([NativeTypeName("slang::ISession **")] ISession** outSession)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, ISession**, int>)(lpVtbl[61]))((ICompileRequest*)Unsafe.AsPointer(ref this), outSession);
    }

    /// <summary>get reflection data from a compilation request</summary>
    [VtblIndex(62)]
    [return: NativeTypeName("SlangReflection *")]
    public SlangProgramLayout* getReflection()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, SlangProgramLayout*>)(lpVtbl[62]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Make output specially handled for command line output</summary>
    [VtblIndex(63)]
    public void setCommandLineCompilerMode()
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, void>)(lpVtbl[63]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Add a defined capability that should be assumed available on the target</summary>
    [VtblIndex(64)]
    [return: NativeTypeName("SlangResult")]
    public int addTargetCapability([NativeTypeName("SlangInt")] long targetIndex, SlangCapabilityID capability)
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangCapabilityID, int>)(lpVtbl[64]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, capability);
    }

    /// <summary>Get the (linked) program for a compile request, including all entry points.</summary>
    /// <remarks>
    /// <para>The resulting program will include all of the global-scope modules for the translation units in the program, plus any modules that they <c>import</c> (transitively), specialized to any global specialization arguments that were provided via the API, as well as all entry points specified for compilation, specialized to their entry-point specialization arguments.</para>
    /// </remarks>
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

    /// <summary>Set the line directive mode for a target.</summary>
    [VtblIndex(67)]
    public void setTargetLineDirectiveMode([NativeTypeName("SlangInt")] long targetIndex, SlangLineDirectiveMode mode)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangLineDirectiveMode, void>)(lpVtbl[67]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, mode);
    }

    /// <summary>Set whether to use scalar buffer layouts for GLSL/Vulkan targets. If true, the generated GLSL/Vulkan code will use <c>scalar</c> layout for storage buffers. If false, the resulting code will std430 for storage buffers.</summary>
    [VtblIndex(68)]
    public void setTargetForceGLSLScalarBufferLayout(int targetIndex, [NativeTypeName("bool")] byte forceScalarLayout)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, byte, void>)(lpVtbl[68]))((ICompileRequest*)Unsafe.AsPointer(ref this), targetIndex, forceScalarLayout);
    }

    /// <summary>Overrides the severity of a specific diagnostic message.</summary>
    /// <param name="messageID">Numeric identifier of the message to override, as defined in the 1st parameter of the DIAGNOSTIC macro.</param>
    /// <param name="overrideSeverity">New severity of the message. If the message is originally Error or Fatal, the new severity cannot be lower than that.</param>
    [VtblIndex(69)]
    public void overrideDiagnosticSeverity([NativeTypeName("SlangInt")] long messageID, SlangSeverity overrideSeverity)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, long, SlangSeverity, void>)(lpVtbl[69]))((ICompileRequest*)Unsafe.AsPointer(ref this), messageID, overrideSeverity);
    }

    /// <summary>Returns the currently active flags of the request's diagnostic sink.</summary>
    [VtblIndex(70)]
    [return: NativeTypeName("SlangDiagnosticFlags")]
    public int getDiagnosticFlags()
    {
        return ((delegate* unmanaged[Stdcall]<ICompileRequest*, int>)(lpVtbl[70]))((ICompileRequest*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Sets the flags of the request's diagnostic sink. The previously specified flags are discarded.</summary>
    [VtblIndex(71)]
    public void setDiagnosticFlags([NativeTypeName("SlangDiagnosticFlags")] int flags)
    {
        ((delegate* unmanaged[Stdcall]<ICompileRequest*, int, void>)(lpVtbl[71]))((ICompileRequest*)Unsafe.AsPointer(ref this), flags);
    }

    /// <summary>Set the debug format to be used for debugging information</summary>
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

    /// <summary>return a copy of internal profiling results, and if <c>shouldClear</c> is true, clear the internal</summary>
    /// <remarks>
    /// <para>profiling results before returning.</para>
    /// </remarks>
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
