using System.Runtime.InteropServices;

namespace SharpSlang.Native;

public static unsafe partial class SlangApi
{
    public const int SLANG_DIAGNOSTIC_FLAG_VERBOSE_PATHS = 0x01;
    public const int SLANG_DIAGNOSTIC_FLAG_TREAT_WARNINGS_AS_ERRORS = 0x02;

    /// <summary>Do as little mangling of names as possible, to try to preserve original names</summary>
    public const int SLANG_COMPILE_FLAG_NO_MANGLING = 1 << 3;
    /// <summary>Skip code generation step, just check the code and generate layout</summary>
    public const int SLANG_COMPILE_FLAG_NO_CODEGEN = 1 << 4;
    /// <summary>Obfuscate shader names on release products</summary>
    public const int SLANG_COMPILE_FLAG_OBFUSCATE = 1 << 5;
    /// <summary>Deprecated flags: kept around to allow existing applications to compile. Note that the relevant features will still be left in their default state.</summary>
    public const int SLANG_COMPILE_FLAG_NO_CHECKING = 0;
    public const int SLANG_COMPILE_FLAG_SPLIT_MIXED_TYPES = 0;

    /// <summary>When compiling for a D3D Shader Model 5.1 or higher target, allocate distinct register spaces for parameter blocks.</summary>
    /// <remarks>
    /// <para>deprecated: This behavior is now enabled unconditionally.</para>
    /// </remarks>
    public const int SLANG_TARGET_FLAG_PARAMETER_BLOCKS_USE_REGISTER_SPACES = 1 << 4;
    /// <summary>When set, will generate target code that contains all entrypoints defined in the input source or specified via the <c>spAddEntryPoint</c> function in a single output module (library/source file).</summary>
    public const int SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM = 1 << 8;
    /// <summary>When set, will dump out the IR between intermediate compilation steps.</summary>
    public const int SLANG_TARGET_FLAG_DUMP_IR = 1 << 9;
    /// <summary>When set, will generate SPIRV directly rather than via glslang.</summary>
    /// <remarks>
    /// <para>This flag will be deprecated, use CompilerOption instead.</para>
    /// </remarks>
    public const int SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY = 1 << 10;

    [NativeTypeName("const SlangTargetFlags")]
    public const uint kDefaultTargetFlags = (uint)(SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY);

    /// <summary>Get the build version 'tag' string. The string is the same as produced via <c>git describe --tags --match v*</c> for the project. If such a version could not be determined at build time then the contents will be 0.0.0-unknown. Any string can be set by passing -DSLANG_VERSION_FULL=whatever during the cmake invocation.</summary>
    /// <remarks>
    /// <para>This function will return exactly the same result as the method getBuildTagString on IGlobalSession.</para>
    /// <para>An advantage of using this function over the method is that doing so does not require the creation of a session, which can be a fairly costly operation.</para>
    /// </remarks>
    /// <returns>The build tag string</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spGetBuildTagString();

    /// <summary>Initialize an instance of the Slang library.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangSession *")]
    public static extern IGlobalSession* spCreateSession([NativeTypeName("const char *")] sbyte* deprecated = null);

    /// <summary>Clean up after an instance of the Slang library.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroySession([NativeTypeName("SlangSession *")] IGlobalSession* session);

    /// <summary>see: slang::IGlobalSession::setSharedLibraryLoader</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSessionSetSharedLibraryLoader([NativeTypeName("SlangSession *")] IGlobalSession* session, ISlangSharedLibraryLoader* loader);

    /// <summary>see: slang::IGlobalSession::getSharedLibraryLoader</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ISlangSharedLibraryLoader* spSessionGetSharedLibraryLoader([NativeTypeName("SlangSession *")] IGlobalSession* session);

    /// <summary>see: slang::IGlobalSession::checkCompileTargetSupport</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSessionCheckCompileTargetSupport([NativeTypeName("SlangSession *")] IGlobalSession* session, SlangCompileTarget target);

    /// <summary>see: slang::IGlobalSession::checkPassThroughSupport</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSessionCheckPassThroughSupport([NativeTypeName("SlangSession *")] IGlobalSession* session, SlangPassThrough passThrough);

    /// <summary>see: slang::IGlobalSession::addBuiltins</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddBuiltins([NativeTypeName("SlangSession *")] IGlobalSession* session, [NativeTypeName("const char *")] sbyte* sourcePath, [NativeTypeName("const char *")] sbyte* sourceString);

    /// <summary>see: slang::IGlobalSession::createCompileRequest</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangCompileRequest *")]
    public static extern ICompileRequest* spCreateCompileRequest([NativeTypeName("SlangSession *")] IGlobalSession* session);

    /// <summary>Destroy a compile request. Note a request is a COM object and can be destroyed via 'Release'.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroyCompileRequest([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::setFileSystem</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetFileSystem([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangFileSystem* fileSystem);

    /// <summary>see: slang::ICompileRequest::setCompileFlags</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetCompileFlags([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangCompileFlags")] uint flags);

    /// <summary>see: slang::ICompileRequest::getCompileFlags</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangCompileFlags")]
    public static extern uint spGetCompileFlags([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::setDumpIntermediates</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDumpIntermediates([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int enable);

    /// <summary>see: slang::ICompileRequest::setDumpIntermediatePrefix</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDumpIntermediatePrefix([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *")] sbyte* prefix);

    /// <summary>DEPRECATED: use <c>spSetTargetLineDirectiveMode</c> instead.</summary>
    /// <remarks>
    /// <para>see: slang::ICompileRequest::setLineDirectiveMode</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetLineDirectiveMode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangLineDirectiveMode mode);

    /// <summary>see: slang::ICompileRequest::setTargetLineDirectiveMode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetLineDirectiveMode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, SlangLineDirectiveMode mode);

    /// <summary>see: slang::ICompileRequest::setTargetLineDirectiveMode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetForceGLSLScalarBufferLayout([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, [NativeTypeName("bool")] byte forceScalarLayout);

    /// <summary>see: slang::ICompileRequest::setTargetUseMinimumSlangOptimization</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetUseMinimumSlangOptimization([NativeTypeName("slang::ICompileRequest *")] ICompileRequest* request, int targetIndex, [NativeTypeName("bool")] byte val);

    /// <summary>see: slang::ICompileRequest::setIgnoreCapabilityCheck</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetIgnoreCapabilityCheck([NativeTypeName("slang::ICompileRequest *")] ICompileRequest* request, [NativeTypeName("bool")] byte val);

    /// <summary>see: slang::ICompileRequest::setCodeGenTarget</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetCodeGenTarget([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangCompileTarget target);

    /// <summary>see: slang::ICompileRequest::addCodeGenTarget</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddCodeGenTarget([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangCompileTarget target);

    /// <summary>see: slang::ICompileRequest::setTargetProfile</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetProfile([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, SlangProfileID profile);

    /// <summary>see: slang::ICompileRequest::setTargetFlags</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetFlags([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, [NativeTypeName("SlangTargetFlags")] uint flags);

    /// <summary>see: slang::ICompileRequest::setTargetFloatingPointMode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetFloatingPointMode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, SlangFloatingPointMode mode);

    /// <summary>see: slang::ICompileRequest::addTargetCapability</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTargetCapability([NativeTypeName("slang::ICompileRequest *")] ICompileRequest* request, int targetIndex, SlangCapabilityID capability);

    /// <summary>DEPRECATED: use <c>spSetMatrixLayoutMode</c> instead.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetMatrixLayoutMode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, SlangMatrixLayoutMode mode);

    /// <summary>see: slang::ICompileRequest::setMatrixLayoutMode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetMatrixLayoutMode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangMatrixLayoutMode mode);

    /// <summary>see: slang::ICompileRequest::setDebugInfoLevel</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDebugInfoLevel([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangDebugInfoLevel level);

    /// <summary>see: slang::ICompileRequest::setDebugInfoFormat</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDebugInfoFormat([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangDebugInfoFormat format);

    /// <summary>see: slang::ICompileRequest::setOptimizationLevel</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetOptimizationLevel([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangOptimizationLevel level);

    /// <summary>see: slang::ICompileRequest::setOutputContainerFormat</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetOutputContainerFormat([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangContainerFormat format);

    /// <summary>see: slang::ICompileRequest::setPassThrough</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetPassThrough([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangPassThrough passThrough);

    /// <summary>see: slang::ICompileRequest::setDiagnosticCallback</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDiagnosticCallback([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangDiagnosticCallback")] delegate* unmanaged[Cdecl]<sbyte*, void*, void> callback, [NativeTypeName("const void *")] void* userData);

    /// <summary>see: slang::ICompileRequest::setWriter</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetWriter([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangWriterChannel channel, ISlangWriter* writer);

    /// <summary>see: slang::ICompileRequest::getWriter</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ISlangWriter* spGetWriter([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangWriterChannel channel);

    /// <summary>see: slang::ICompileRequest::addSearchPath</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddSearchPath([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *")] sbyte* searchDir);

    /// <summary>see: slang::ICompileRequest::addPreprocessorDefine</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddPreprocessorDefine([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value);

    /// <summary>see: slang::ICompileRequest::processCommandLineArguments</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spProcessCommandLineArguments([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *const *")] sbyte** args, int argCount);

    /// <summary>see: slang::ICompileRequest::addTranslationUnit</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddTranslationUnit([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, SlangSourceLanguage language, [NativeTypeName("const char *")] sbyte* name);

    /// <summary>see: slang::ICompileRequest::setDefaultModuleName</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDefaultModuleName([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *")] sbyte* defaultModuleName);

    /// <summary>see: slang::ICompileRequest::addPreprocessorDefine</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spTranslationUnit_addPreprocessorDefine([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* key, [NativeTypeName("const char *")] sbyte* value);

    /// <summary>see: slang::ICompileRequest::addTranslationUnitSourceFile</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceFile([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path);

    /// <summary>see: slang::ICompileRequest::addTranslationUnitSourceString</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceString([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* source);

    /// <summary>see: slang::ICompileRequest::addLibraryReference</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spAddLibraryReference([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("const char *")] sbyte* basePath, [NativeTypeName("const void *")] void* libData, [NativeTypeName("size_t")] nuint libDataSize);

    /// <summary>see: slang::ICompileRequest::addTranslationUnitSourceStringSpan</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceStringSpan([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* sourceBegin, [NativeTypeName("const char *")] sbyte* sourceEnd);

    /// <summary>see: slang::ICompileRequest::addTranslationUnitSourceBlob</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceBlob([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* path, ISlangBlob* sourceBlob);

    /// <summary>see: slang::IGlobalSession::findProfile</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangProfileID spFindProfile([NativeTypeName("SlangSession *")] IGlobalSession* session, [NativeTypeName("const char *")] sbyte* name);

    /// <summary>see: slang::IGlobalSession::findCapability</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangCapabilityID spFindCapability([NativeTypeName("SlangSession *")] IGlobalSession* session, [NativeTypeName("const char *")] sbyte* name);

    /// <summary>see: slang::ICompileRequest::addEntryPoint</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddEntryPoint([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage);

    /// <summary>see: slang::ICompileRequest::addEntryPointEx</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddEntryPointEx([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex, [NativeTypeName("const char *")] sbyte* name, SlangStage stage, int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs);

    /// <summary>see: slang::ICompileRequest::setGlobalGenericArgs</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSetGlobalGenericArgs([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int genericArgCount, [NativeTypeName("const char **")] sbyte** genericArgs);

    /// <summary>see: slang::ICompileRequest::setTypeNameForGlobalExistentialTypeParam</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSetTypeNameForGlobalExistentialTypeParam([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int slotIndex, [NativeTypeName("const char *")] sbyte* typeName);

    /// <summary>see: slang::ICompileRequest::setTypeNameForEntryPointExistentialTypeParam</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSetTypeNameForEntryPointExistentialTypeParam([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int entryPointIndex, int slotIndex, [NativeTypeName("const char *")] sbyte* typeName);

    /// <summary>see: slang::ICompileRequest::compile</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompile([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::getDiagnosticOutput</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spGetDiagnosticOutput([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::getDiagnosticOutputBlob</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetDiagnosticOutputBlob([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangBlob** outBlob);

    /// <summary>see: slang::ICompileRequest::getDependencyFileCount</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spGetDependencyFileCount([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::getDependencyFilePath</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spGetDependencyFilePath([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int index);

    /// <summary>see: slang::ICompileRequest::getTranslationUnitCount</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spGetTranslationUnitCount([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::getEntryPointSource</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spGetEntryPointSource([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int entryPointIndex);

    /// <summary>see: slang::ICompileRequest::getEntryPointCode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const void *")]
    public static extern void* spGetEntryPointCode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int entryPointIndex, [NativeTypeName("size_t *")] nuint* outSize);

    /// <summary>see: slang::ICompileRequest::getEntryPointCodeBlob</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetEntryPointCodeBlob([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int entryPointIndex, int targetIndex, ISlangBlob** outBlob);

    /// <summary>see: slang::ICompileRequest::getEntryPointHostCallable</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetEntryPointHostCallable([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int entryPointIndex, int targetIndex, ISlangSharedLibrary** outSharedLibrary);

    /// <summary>see: slang::ICompileRequest::getTargetCodeBlob</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetTargetCodeBlob([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, ISlangBlob** outBlob);

    /// <summary>see: slang::ICompileRequest::getTargetHostCallable</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetTargetHostCallable([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int targetIndex, ISlangSharedLibrary** outSharedLibrary);

    /// <summary>see: slang::ICompileRequest::getCompileRequestCode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const void *")]
    public static extern void* spGetCompileRequestCode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("size_t *")] nuint* outSize);

    /// <summary>see: slang::ICompileRequest::getContainerCode</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetContainerCode([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangBlob** outBlob);

    /// <summary>see: slang::ICompileRequest::loadRepro</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spLoadRepro([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangFileSystem* fileSystem, [NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size);

    /// <summary>see: slang::ICompileRequest::saveRepro</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spSaveRepro([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangBlob** outBlob);

    /// <summary>see: slang::ICompileRequest::enableReproCapture</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spEnableReproCapture([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::getCompileTimeProfile</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spGetCompileTimeProfile([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangProfiler** compileTimeProfile, [NativeTypeName("bool")] byte shouldClear);

    /// <summary>Extract contents of a repro.</summary>
    /// <remarks>
    /// <para>Writes the contained files and manifest with their 'unique' names into fileSystem. For more details read the docs/repro.md documentation.</para>
    /// </remarks>
    /// <param name="session">The slang session</param>
    /// <param name="reproData">Holds the repro data</param>
    /// <param name="reproDataSize">The size of the repro data</param>
    /// <param name="fileSystem">File system that the contents of the repro will be written to</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spExtractRepro([NativeTypeName("SlangSession *")] IGlobalSession* session, [NativeTypeName("const void *")] void* reproData, [NativeTypeName("size_t")] nuint reproDataSize, ISlangMutableFileSystem* fileSystem);

    /// <summary>Turns a repro into a file system.</summary>
    /// <remarks>
    /// <para>Makes the contents of the repro available as a file system - that is able to access the files with the same paths as were used on the original repro file system.</para>
    /// </remarks>
    /// <param name="session">The slang session</param>
    /// <param name="reproData">The repro data</param>
    /// <param name="reproDataSize">The size of the repro data</param>
    /// <param name="replaceFileSystem">Will attempt to load by unique names from this file system before using contents of the repro. Optional.</param>
    /// <param name="outFileSystem">The file system that can be used to access contents</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spLoadReproAsFileSystem([NativeTypeName("SlangSession *")] IGlobalSession* session, [NativeTypeName("const void *")] void* reproData, [NativeTypeName("size_t")] nuint reproDataSize, ISlangFileSystem* replaceFileSystem, ISlangFileSystemExt** outFileSystem);

    /// <summary>see: slang::ICompileRequest::overrideDiagnosticSeverity</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spOverrideDiagnosticSeverity([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangInt")] long messageID, SlangSeverity overrideSeverity);

    /// <summary>see: slang::ICompileRequest::getDiagnosticFlags</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangDiagnosticFlags")]
    public static extern int spGetDiagnosticFlags([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>see: slang::ICompileRequest::setDiagnosticFlags</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDiagnosticFlags([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangDiagnosticFlags")] int flags);

    /// <summary>get reflection data from a compilation request</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangReflection *")]
    public static extern SlangProgramLayout* spGetReflection([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request);

    /// <summary>User Attribute</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionUserAttribute_GetName(SlangReflectionUserAttribute* attrib);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionUserAttribute_GetArgumentCount(SlangReflectionUserAttribute* attrib);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionUserAttribute_GetArgumentType(SlangReflectionUserAttribute* attrib, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflectionUserAttribute_GetArgumentValueInt(SlangReflectionUserAttribute* attrib, [NativeTypeName("unsigned int")] uint index, int* rs);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflectionUserAttribute_GetArgumentValueFloat(SlangReflectionUserAttribute* attrib, [NativeTypeName("unsigned int")] uint index, float* rs);

    /// <summary>Returns the string-typed value of a user attribute argument The string returned is not null-terminated. The length of the string is returned via <c>outSize</c>. If index of out of range, or if the specified argument is not a string, the function will return nullptr.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionUserAttribute_GetArgumentValueString(SlangReflectionUserAttribute* attrib, [NativeTypeName("unsigned int")] uint index, [NativeTypeName("size_t *")] nuint* outSize);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangTypeKind spReflectionType_GetKind(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionType_GetUserAttributeCount(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionType_GetUserAttribute(SlangReflectionType* type, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionType_FindUserAttributeByName(SlangReflectionType* type, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionType_applySpecializations(SlangReflectionType* type, SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionType_GetFieldCount(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionType_GetFieldByIndex(SlangReflectionType* type, [NativeTypeName("unsigned int")] uint index);

    /// <summary>Returns the number of elements in the given type.</summary>
    /// <remarks>
    /// <para>This operation is valid for vector and array types. For other types it returns zero.</para>
    /// <para>When invoked on an unbounded-size array it will return <c>SLANG_UNBOUNDED_SIZE</c>, which is defined to be <c>~size_t(0)</c>.</para>
    /// <para>If the size of a type cannot be statically computed, perhaps because it depends on a generic parameter that has not been bound to a specific value, this function returns zero.</para>
    /// <para>Use spReflectionType_GetSpecializedElementCount if the size is dependent on a link time constant</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionType_GetElementCount(SlangReflectionType* type);

    /// <summary>The same as spReflectionType_GetElementCount except it takes into account specialization information from the given reflection info</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionType_GetSpecializedElementCount(SlangReflectionType* type, [NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionType_GetElementType(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionType_GetRowCount(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionType_GetColumnCount(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangScalarType spReflectionType_GetScalarType(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResourceShape spReflectionType_GetResourceShape(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResourceAccess spReflectionType_GetResourceAccess(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionType_GetResourceResultType(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionType_GetName(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflectionType_GetFullName(SlangReflectionType* type, ISlangBlob** outNameBlob);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionType_GetGenericContainer(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionTypeLayout_GetType(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangTypeKind spReflectionTypeLayout_getKind(SlangReflectionTypeLayout* type);

    /// <summary>Get the size of a type layout in the specified parameter category.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources (e.g., unsized arrays). Returns <c>SLANG_UNKNOWN_SIZE</c> when the size depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionTypeLayout_GetSize(SlangReflectionTypeLayout* type, SlangParameterCategory category);

    /// <summary>Get the stride of a type layout in the specified parameter category.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources. Returns <c>SLANG_UNKNOWN_SIZE</c> when stride depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionTypeLayout_GetStride(SlangReflectionTypeLayout* type, SlangParameterCategory category);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("int32_t")]
    public static extern int spReflectionTypeLayout_getAlignment(SlangReflectionTypeLayout* type, SlangParameterCategory category);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("uint32_t")]
    public static extern uint spReflectionTypeLayout_GetFieldCount(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_GetFieldByIndex(SlangReflectionTypeLayout* type, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_findFieldIndexByName(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("const char *")] sbyte* nameBegin, [NativeTypeName("const char *")] sbyte* nameEnd);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_GetExplicitCounter(SlangReflectionTypeLayout* typeLayout);

    /// <summary>Get the stride between elements of an array type layout.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources. Returns <c>SLANG_UNKNOWN_SIZE</c> when element stride depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionTypeLayout_GetElementStride(SlangReflectionTypeLayout* type, SlangParameterCategory category);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflectionTypeLayout_GetElementTypeLayout(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_GetElementVarLayout(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_getContainerVarLayout(SlangReflectionTypeLayout* type);

    /// <summary>Get the variable layout for the "content" of a container-like type layout.</summary>
    /// <remarks>
    /// <para>The "content" is what a container holds, as opposed to the container ("wrapper") itself: the single element for a constant buffer / parameter block / texture buffer, and the sequence of elements for a structured buffer. For a constant buffer / parameter block / texture buffer this returns the element variable layout, whose offsets are relative to the container (they account for the container's own resource usage). For a structured buffer this returns a variable layout over an (unbounded) array of the element type at offset zero, so element stride and element type can be queried through the normal array-layout accessors. Returns null for type layouts that are not container-like.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_GetContentVarLayout(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangParameterCategory spReflectionTypeLayout_GetParameterCategory(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionTypeLayout_GetCategoryCount(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangParameterCategory spReflectionTypeLayout_GetCategoryByIndex(SlangReflectionTypeLayout* type, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangMatrixLayoutMode spReflectionTypeLayout_GetMatrixLayoutMode(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spReflectionTypeLayout_getGenericParamIndex(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflectionTypeLayout_getPendingDataTypeLayout(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(SlangReflectionTypeLayout* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionType_getSpecializedTypeArgCount(SlangReflectionType* type);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionType_getSpecializedTypeArgType(SlangReflectionType* type, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getBindingRangeCount(SlangReflectionTypeLayout* typeLayout);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangBindingType spReflectionTypeLayout_getBindingRangeType(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_isBindingRangeSpecializable(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    /// <summary>Get the binding count for a binding range at the specified index.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources. Returns <c>SLANG_UNKNOWN_SIZE</c> when the count depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getBindingRangeBindingCount(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflectionTypeLayout_getBindingRangeLeafTypeLayout(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionTypeLayout_getBindingRangeLeafVariable(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangImageFormat spReflectionTypeLayout_getBindingRangeImageFormat(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getFieldBindingRangeOffset(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long fieldIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(SlangReflectionTypeLayout* inTypeLayout);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getDescriptorSetCount(SlangReflectionTypeLayout* typeLayout);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getDescriptorSetSpaceOffset(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex);

    /// <summary>Get the index offset for a descriptor range within a descriptor set.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the offset depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex, [NativeTypeName("SlangInt")] long rangeIndex);

    /// <summary>Get the descriptor count for a descriptor range within a descriptor set.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources. Returns <c>SLANG_UNKNOWN_SIZE</c> when the count depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex, [NativeTypeName("SlangInt")] long rangeIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangBindingType spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex, [NativeTypeName("SlangInt")] long rangeIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangParameterCategory spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long setIndex, [NativeTypeName("SlangInt")] long rangeIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getSubObjectRangeCount(SlangReflectionTypeLayout* typeLayout);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long subObjectRangeIndex);

    /// <summary>Get the space offset for a sub-object range.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the offset depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflectionTypeLayout_getSubObjectRangeSpaceOffset(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long subObjectRangeIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionTypeLayout_getSubObjectRangeOffset(SlangReflectionTypeLayout* typeLayout, [NativeTypeName("SlangInt")] long subObjectRangeIndex);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionVariable_GetName(SlangReflectionVariable* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionVariable_GetType(SlangReflectionVariable* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionModifier* spReflectionVariable_FindModifier(SlangReflectionVariable* var, SlangModifierID modifierID);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionVariable_GetUserAttributeCount(SlangReflectionVariable* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionVariable_GetUserAttribute(SlangReflectionVariable* var, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionVariable_FindUserAttributeByName(SlangReflectionVariable* var, [NativeTypeName("SlangSession *")] IGlobalSession* globalSession, [NativeTypeName("const char *")] sbyte* name);

    /// <summary>DEPRECATED: use <c>slang::VariableReflection::getDefaultValueBlob</c> and check for a null blob instead.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("bool")]
    public static extern byte spReflectionVariable_HasDefaultValue(SlangReflectionVariable* inVar);

    /// <summary>DEPRECATED: use <c>slang::VariableReflection::getDefaultValueBlob</c> instead.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflectionVariable_GetDefaultValueInt(SlangReflectionVariable* inVar, [NativeTypeName("int64_t *")] long* rs);

    /// <summary>DEPRECATED: use <c>slang::VariableReflection::getDefaultValueBlob</c> instead.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflectionVariable_GetDefaultValueFloat(SlangReflectionVariable* inVar, float* rs);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionVariable_GetGenericContainer(SlangReflectionVariable* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionVariable_applySpecializations(SlangReflectionVariable* var, SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionVariableLayout_GetVariable(SlangReflectionVariableLayout* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflectionVariableLayout_GetTypeLayout(SlangReflectionVariableLayout* var);

    /// <summary>Get the offset of a variable in the specified parameter category.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the offset depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionVariableLayout_GetOffset(SlangReflectionVariableLayout* var, SlangParameterCategory category);

    /// <summary>Get the register space/set of a variable in the specified parameter category.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the space depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionVariableLayout_GetSpace(SlangReflectionVariableLayout* var, SlangParameterCategory category);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangImageFormat spReflectionVariableLayout_GetImageFormat(SlangReflectionVariableLayout* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionVariableLayout_GetSemanticName(SlangReflectionVariableLayout* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflectionVariableLayout_GetSemanticIndex(SlangReflectionVariableLayout* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionDecl* spReflectionFunction_asDecl(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionFunction_GetName(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionModifier* spReflectionFunction_FindModifier(SlangReflectionFunction* var, SlangModifierID modifierID);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionFunction_GetUserAttributeCount(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionFunction_GetUserAttribute(SlangReflectionFunction* func, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionUserAttribute* spReflectionFunction_FindUserAttributeByName(SlangReflectionFunction* func, [NativeTypeName("SlangSession *")] IGlobalSession* globalSession, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionFunction_GetParameterCount(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionFunction_GetParameter(SlangReflectionFunction* func, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionFunction_GetResultType(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionFunction_GetGenericContainer(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflectionFunction_applySpecializations(SlangReflectionFunction* func, SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflectionFunction_specializeWithArgTypes(SlangReflectionFunction* func, [NativeTypeName("SlangInt")] long argTypeCount, [NativeTypeName("SlangReflectionType *const *")] SlangReflectionType** argTypes);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("bool")]
    public static extern byte spReflectionFunction_isOverloaded(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionFunction_getOverloadCount(SlangReflectionFunction* func);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflectionFunction_getOverload(SlangReflectionFunction* func, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionDecl_getChildrenCount(SlangReflectionDecl* parentDecl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionDecl* spReflectionDecl_getChild(SlangReflectionDecl* parentDecl, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionDecl_getName(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangDeclKind spReflectionDecl_getKind(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflectionDecl_castToFunction(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionDecl_castToVariable(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionDecl_castToGeneric(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflection_getTypeFromDecl(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionDecl* spReflectionDecl_getParent(SlangReflectionDecl* decl);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionModifier* spReflectionDecl_findModifier(SlangReflectionDecl* decl, SlangModifierID modifierID);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionDecl* spReflectionGeneric_asDecl(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionGeneric_GetName(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionGeneric_GetTypeParameterCount(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionGeneric_GetTypeParameter(SlangReflectionGeneric* generic, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionGeneric_GetValueParameterCount(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflectionGeneric_GetValueParameter(SlangReflectionGeneric* generic, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionGeneric_GetTypeParameterConstraintCount(SlangReflectionGeneric* generic, SlangReflectionVariable* typeParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionGeneric_GetTypeParameterConstraintType(SlangReflectionGeneric* generic, SlangReflectionVariable* typeParam, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangDeclKind spReflectionGeneric_GetInnerKind(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionDecl* spReflectionGeneric_GetInnerDecl(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionGeneric_GetOuterGenericContainer(SlangReflectionGeneric* generic);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionGeneric_GetConcreteType(SlangReflectionGeneric* generic, SlangReflectionVariable* typeParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("int64_t")]
    public static extern long spReflectionGeneric_GetConcreteIntVal(SlangReflectionGeneric* generic, SlangReflectionVariable* valueParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflectionGeneric_applySpecializations(SlangReflectionGeneric* currGeneric, SlangReflectionGeneric* generic);

    /// <summary>Get the stage that a variable belongs to (if any).</summary>
    /// <remarks>
    /// <para>A variable "belongs" to a specific stage when it is a varying input/output parameter either defined as part of the parameter list for an entry point *or* at the global scope of a stage-specific GLSL code file (e.g., an <c>in</c> parameter in a GLSL <c>.vs</c> file belongs to the vertex stage).</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangStage spReflectionVariableLayout_getStage(SlangReflectionVariableLayout* var);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionVariableLayout_getPendingDataLayout(SlangReflectionVariableLayout* var);

    /// <summary>Get the binding index for a shader parameter.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the index depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionParameter_GetBindingIndex([NativeTypeName("SlangReflectionParameter *")] SlangReflectionVariableLayout* parameter);

    /// <summary>Get the binding space for a shader parameter.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the space depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionParameter_GetBindingSpace([NativeTypeName("SlangReflectionParameter *")] SlangReflectionVariableLayout* parameter);

    /// <summary>is this a <c>t</c> register? <c>s</c> register?</summary>
    /// <remarks>
    /// <para><c>space</c> for D3D12, <c>set</c> for Vulkan</para>
    /// <para><c>register</c> for D3D12, <c>binding</c> for Vulkan</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spIsParameterLocationUsed([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, SlangParameterCategory category, [NativeTypeName("SlangUInt")] ulong spaceIndex, [NativeTypeName("SlangUInt")] ulong registerIndex, [NativeTypeName("bool &")] bool* outUsed);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionEntryPoint_getName([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionEntryPoint_getNameOverride([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflectionEntryPoint_getFunction([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionEntryPoint_getParameterCount([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionEntryPoint_getParameterByIndex([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangStage spReflectionEntryPoint_getStage([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spReflectionEntryPoint_getComputeThreadGroupSize([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint, [NativeTypeName("SlangUInt")] ulong axisCount, [NativeTypeName("SlangUInt *")] ulong* outSizeAlongAxis);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spReflectionEntryPoint_getComputeWaveSize([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint, [NativeTypeName("SlangUInt *")] ulong* outWaveSize);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spReflectionEntryPoint_usesAnySampleRateInput([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionEntryPoint_getVarLayout([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflectionEntryPoint_getResultVarLayout([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spReflectionEntryPoint_hasDefaultConstantBuffer([NativeTypeName("SlangReflectionEntryPoint *")] SlangEntryPointLayout* entryPoint);

    /// <summary>SlangReflectionTypeParameter</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflectionTypeParameter_GetName(SlangReflectionTypeParameter* typeParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionTypeParameter_GetIndex(SlangReflectionTypeParameter* typeParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflectionTypeParameter_GetConstraintCount(SlangReflectionTypeParameter* typeParam);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflectionTypeParameter_GetConstraintByIndex(SlangReflectionTypeParameter* typeParam, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spReflection_ToJson([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, ISlangBlob** outBlob);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflection_GetParameterCount([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangReflectionParameter *")]
    public static extern SlangReflectionVariableLayout* spReflection_GetParameterByIndex([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("unsigned int")]
    public static extern uint spReflection_GetTypeParameterCount([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeParameter* spReflection_GetTypeParameterByIndex([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("unsigned int")] uint index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeParameter* spReflection_FindTypeParameter([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflection_FindTypeByName([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflection_GetTypeLayout([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, SlangReflectionType* reflectionType, SlangLayoutRules rules);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflection_FindFunctionByName([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflection_FindFunctionByNameInType([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, SlangReflectionType* reflType, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariable* spReflection_FindVarByNameInType([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, SlangReflectionType* reflType, [NativeTypeName("const char *")] sbyte* name);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionFunction* spReflection_TryResolveOverloadedFunction([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("uint32_t")] uint candidateCount, SlangReflectionFunction** candidates);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangUInt")]
    public static extern ulong spReflection_getEntryPointCount([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangReflectionEntryPoint *")]
    public static extern SlangEntryPointLayout* spReflection_getEntryPointByIndex([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("SlangUInt")] ulong index);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangReflectionEntryPoint *")]
    public static extern SlangEntryPointLayout* spReflection_findEntryPointByName([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("const char *")] sbyte* name);

    /// <summary>Get the binding index for the global constant buffer.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNKNOWN_SIZE</c> when the binding depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangUInt")]
    public static extern ulong spReflection_getGlobalConstantBufferBinding([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    /// <summary>Get the size of the global constant buffer.</summary>
    /// <remarks>
    /// <para>Returns <c>SLANG_UNBOUNDED_SIZE</c> for unbounded resources. Returns <c>SLANG_UNKNOWN_SIZE</c> when the size depends on unresolved generic parameters or link-time constants.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint spReflection_getGlobalConstantBufferSize([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionType* spReflection_specializeType([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, SlangReflectionType* type, [NativeTypeName("SlangInt")] long specializationArgCount, [NativeTypeName("SlangReflectionType *const *")] SlangReflectionType** specializationArgs, ISlangBlob** outDiagnostics);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionGeneric* spReflection_specializeGeneric([NativeTypeName("SlangReflection *")] SlangProgramLayout* inProgramLayout, SlangReflectionGeneric* generic, [NativeTypeName("SlangInt")] long argCount, [NativeTypeName("const SlangReflectionGenericArgType *")] SlangReflectionGenericArgType* argTypes, [NativeTypeName("const SlangReflectionGenericArg *")] SlangReflectionGenericArg* args, ISlangBlob** outDiagnostics);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("bool")]
    public static extern byte spReflection_isSubType([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, SlangReflectionType* subType, SlangReflectionType* superType);

    /// <summary>Get the number of hashed strings</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangUInt")]
    public static extern ulong spReflection_getHashedStringCount([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    /// <summary>Get a hashed string. The number of chars is written in outCount.</summary>
    /// <remarks>
    /// <para>The count does *NOT* including terminating 0. The returned string will be 0 terminated.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spReflection_getHashedString([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection, [NativeTypeName("SlangUInt")] ulong index, [NativeTypeName("size_t *")] nuint* outCount);

    /// <summary>Compute a string hash.</summary>
    /// <remarks>
    /// <para>Count should *NOT* include terminating zero.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangUInt32")]
    public static extern uint spComputeStringHash([NativeTypeName("const char *")] sbyte* chars, [NativeTypeName("size_t")] nuint count);

    /// <summary>Get a type layout representing reflection information for the global-scope parameters.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionTypeLayout* spReflection_getGlobalParamsTypeLayout([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    /// <summary>Get a variable layout representing reflection information for the global-scope parameters.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionVariableLayout* spReflection_getGlobalParamsVarLayout([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* spGetTranslationUnitSource([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, int translationUnitIndex);

    /// <summary>Get the descriptor set/space index reserved for the bindless resource heap.</summary>
    /// <remarks>
    /// <para>This is a layout/reflection reservation made before final target lowering and optimization. It can remain non-negative even when the emitted target code no longer uses a bindless heap/resource-handle path. Query <c>IBindlessResourceMetadata</c> from target metadata to determine whether such a path survived in the compiled target IR.</para>
    /// <para>Returns -1 only when no bindless heap space was reserved for the program layout.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangInt")]
    public static extern long spReflection_getBindlessSpaceIndex([NativeTypeName("SlangReflection *")] SlangProgramLayout* reflection);

    /// <summary>see: slang::ICompileRequest::getProgram</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompileRequest_getProgram([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("slang::IComponentType **")] IComponentType** outProgram);

    /// <summary>see: slang::ICompileRequest::getProgramWithEntryPoints</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompileRequest_getProgramWithEntryPoints([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("slang::IComponentType **")] IComponentType** outProgram);

    /// <summary>see: slang::ICompileRequest::getEntryPoint</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompileRequest_getEntryPoint([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("slang::IComponentType **")] IComponentType** outEntryPoint);

    /// <summary>see: slang::ICompileRequest::getModule</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompileRequest_getModule([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("SlangInt")] long translationUnitIndex, [NativeTypeName("slang::IModule **")] IModule** outModule);

    /// <summary>see: slang::ICompileRequest::getSession</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int spCompileRequest_getSession([NativeTypeName("SlangCompileRequest *")] ICompileRequest* request, [NativeTypeName("slang::ISession **")] ISession** outSession);

    public const int kSessionFlags_None = 0;

    [NativeTypeName("const uint32_t")]
    public const uint kInvalidCoverageCounterIndex = 0xffffffffU;

    [NativeTypeName("const uint32_t")]
    public const uint kUnboundedSyntheticResourceArraySize = ~(uint)(0);

    /// <summary>Create a blob from binary data.</summary>
    /// <param name="data">Pointer to the binary data to store in the blob. Must not be null.</param>
    /// <param name="size">Size of the data in bytes. Must be greater than 0.</param>
    /// <returns>The created blob on success, or nullptr on failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ISlangBlob* slang_createBlob([NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size);

    /// <summary>Serialize coverage metadata into the canonical <c>.coverage-manifest.json</c> shape. Same bytes that <c>slangc</c> writes alongside compiled output when <c>-trace-coverage</c> is on, available in-process for hosts compiling via the C++ API.</summary>
    /// <remarks>
    /// <para>The returned JSON is consumable by <c>tools/shader-coverage/slang-coverage-to-lcov.py</c> and tools expecting the source-entry manifest format. If <c>metadata</c> is the artifact metadata object returned by Slang, it also supports <c>ISyntheticResourceMetadata</c>, and the serializer includes the coverage buffer's descriptor or uniform-marshaling fields when available.</para>
    /// </remarks>
    /// <param name="metadata">The coverage metadata, obtained via <c>castAs&lt;ICoverageTracingMetadata&gt;</c> on the artifact's <c>IMetadata</c>. Must not be null.</param>
    /// <param name="outBlob">(out) The JSON bytes. Caller releases when done.</param>
    /// <returns><c>SLANG_OK</c> on success, <c>SLANG_E_INVALID_ARG</c> for null arguments.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_writeCoverageManifestJson([NativeTypeName("slang::ICoverageTracingMetadata *")] ICoverageTracingMetadata* metadata, ISlangBlob** outBlob);

    /// <summary>Load a module from source code with size specification.</summary>
    /// <param name="session">The session to load the module into.</param>
    /// <param name="moduleName">The name of the module.</param>
    /// <param name="path">The path for the module.</param>
    /// <param name="source">Pointer to the source code data.</param>
    /// <param name="sourceSize">Size of the source code data in bytes.</param>
    /// <param name="outDiagnostics">(out, optional) Diagnostics output.</param>
    /// <returns>The loaded module on success, or nullptr on failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("slang::IModule *")]
    public static extern IModule* slang_loadModuleFromSource([NativeTypeName("slang::ISession *")] ISession* session, [NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* source, [NativeTypeName("size_t")] nuint sourceSize, ISlangBlob** outDiagnostics = null);

    /// <summary>Load a module from IR data.</summary>
    /// <param name="session">The session to load the module into.</param>
    /// <param name="moduleName">Name of the module to load.</param>
    /// <param name="path">Path for the module (used for diagnostics).</param>
    /// <param name="source">IR data containing the module.</param>
    /// <param name="sourceSize">Size of the IR data in bytes.</param>
    /// <param name="outDiagnostics">(out, optional) Diagnostics output.</param>
    /// <returns>The loaded module on success, or nullptr on failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("slang::IModule *")]
    public static extern IModule* slang_loadModuleFromIRBlob([NativeTypeName("slang::ISession *")] ISession* session, [NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const void *")] void* source, [NativeTypeName("size_t")] nuint sourceSize, ISlangBlob** outDiagnostics = null);

    /// <summary>Read module info (name and version) from IR data.</summary>
    /// <param name="session">The session to use for loading module info.</param>
    /// <param name="source">IR data containing the module.</param>
    /// <param name="sourceSize">Size of the IR data in bytes.</param>
    /// <param name="outModuleVersion">(out) Module version number.</param>
    /// <param name="outModuleCompilerVersion">(out) Compiler version that created the module.</param>
    /// <param name="outModuleName">(out) Name of the module.</param>
    /// <returns>SLANG_OK on success, or an error code on failure.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_loadModuleInfoFromIRBlob([NativeTypeName("slang::ISession *")] ISession* session, [NativeTypeName("const void *")] void* source, [NativeTypeName("size_t")] nuint sourceSize, [NativeTypeName("SlangInt &")] long* outModuleVersion, [NativeTypeName("const char *&")] sbyte** outModuleCompilerVersion, [NativeTypeName("const char *&")] sbyte** outModuleName);

    /// <summary>Create a global session, with the built-in core module.</summary>
    /// <param name="apiVersion">Pass in SLANG_API_VERSION</param>
    /// <param name="outGlobalSession">(out)The created global session.</param>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_createGlobalSession([NativeTypeName("SlangInt")] long apiVersion, [NativeTypeName("slang::IGlobalSession **")] IGlobalSession** outGlobalSession);

    /// <summary>Create a global session, with the built-in core module.</summary>
    /// <param name="desc">Description of the global session.</param>
    /// <param name="outGlobalSession">(out)The created global session.</param>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_createGlobalSession2([NativeTypeName("const SlangGlobalSessionDesc *")] SlangGlobalSessionDesc* desc, [NativeTypeName("slang::IGlobalSession **")] IGlobalSession** outGlobalSession);

    /// <summary>Create a global session, but do not set up the core module. The core module can then be loaded via loadCoreModule or compileCoreModule</summary>
    /// <remarks>
    /// <para>NOTE! API is experimental and not ready for production code</para>
    /// </remarks>
    /// <param name="apiVersion">Pass in SLANG_API_VERSION</param>
    /// <param name="outGlobalSession">(out)The created global session that doesn't have a core module setup.</param>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_createGlobalSessionWithoutCoreModule([NativeTypeName("SlangInt")] long apiVersion, [NativeTypeName("slang::IGlobalSession **")] IGlobalSession** outGlobalSession);

    /// <summary>Cleanup all global allocations used by Slang, to prevent memory leak detectors from reporting them as leaks. This function should only be called after all Slang objects have been released. No other Slang functions such as <c>createGlobalSession</c> should be called after this function.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void slang_shutdown();

    /// <summary>Enable or disable the record layer for API call recording. When enabled, API calls are captured for later replay. The record layer can also be enabled by setting the SLANG_RECORD_LAYER=1 environment variable.</summary>
    /// <remarks>
    /// <para>Environment variables: - SLANG_RECORD_LAYER=1: Enable recording on startup - SLANG_RECORD_PATH=&lt;path&gt;: Use the exact path specified for recording output instead of generating a timestamped folder under .slang-replays/</para>
    /// <para>Thread safety: enable or disable the layer only when no other thread is inside a Slang API call. Enable it once during startup (or via SLANG_RECORD_LAYER) before issuing concurrent calls; changing the mode while other threads are mid-call is not supported.</para>
    /// </remarks>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void slang_enableRecordLayer([NativeTypeName("bool")] byte enable);

    /// <summary>Check if the record layer is currently enabled.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("bool")]
    public static extern byte slang_isRecordLayerEnabled();

    /// <summary>Set the base directory for replay files (default: ".slang-replays"). Must be called before enabling recording.</summary>
    /// <param name="path">Path to the replay directory.</param>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void slang_setReplayDirectory([NativeTypeName("const char *")] sbyte* path);

    /// <summary>Get the current replay base directory.</summary>
    /// <returns>Path to the replay directory.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* slang_getReplayDirectory();

    /// <summary>Get the path to the current recording session folder.</summary>
    /// <returns>Path to the current replay folder, or nullptr if not recording.</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* slang_getCurrentReplayPath();

    /// <summary>Load a replay from a folder path (reads stream.bin). Switches to playback mode on success.</summary>
    /// <param name="folderPath">Path to the replay folder.</param>
    /// <returns>SLANG_OK on success, SLANG_E_NOT_FOUND if stream.bin doesn't exist. Returns SLANG_E_NOT_AVAILABLE when Slang is built with the record-replay layer excluded (SLANG_ENABLE_RECORD_REPLAY=OFF).</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_loadReplay([NativeTypeName("const char *")] sbyte* folderPath);

    /// <summary>Load the most recent replay from the replay directory. Switches to playback mode on success.</summary>
    /// <returns>SLANG_OK on success, SLANG_E_NOT_FOUND if no replays exist. Returns SLANG_E_NOT_AVAILABLE when Slang is built with the record-replay layer excluded (SLANG_ENABLE_RECORD_REPLAY=OFF).</returns>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_loadLatestReplay();

    /// <summary>Insert a labeled marker into the replay stream. Useful for debugging replay streams. Marks a point with a human-readable label. No-op if the record layer is not active.</summary>
    /// <param name="label">The marker label string.</param>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void slang_replayMarker([NativeTypeName("const char *")] sbyte* label);

    /// <summary>Return the last signaled internal error message.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("const char *")]
    public static extern sbyte* slang_getLastInternalErrorMessage();

    /// <summary>Create a byte code runner that can execute Slang byte code.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_createByteCodeRunner([NativeTypeName("const slang::ByteCodeRunnerDesc *")] ByteCodeRunnerDesc* desc, [NativeTypeName("slang::IByteCodeRunner **")] IByteCodeRunner** outByteCodeRunner);

    /// <summary>Disassemble a Slang byte code blob into human-readable text.</summary>
    [DllImport("slang-compiler", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("SlangResult")]
    public static extern int slang_disassembleByteCode([NativeTypeName("slang::IBlob *")] ISlangBlob* moduleBlob, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDisassemblyBlob);

    public static void shutdown()
    {
        slang_shutdown();
    }

    [return: NativeTypeName("const char *")]
    public static sbyte* getLastInternalErrorMessage()
    {
        return slang_getLastInternalErrorMessage();
    }
}
