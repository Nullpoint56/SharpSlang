using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A global session for interaction with the Slang library.</summary>
/// <remarks>
/// <para>An application may create and re-use a single global session across multiple sessions, in order to amortize startups costs (in current Slang this is mostly the cost of loading the Slang standard library).</para>
/// <para>A single global session object is currently *not* thread-safe. Unless documented otherwise, a global session and the objects created from it should be externally synchronized when shared across threads. Distinct global sessions may be used from different threads in parallel.</para>
/// </remarks>
[NativeTypeName("struct IGlobalSession : ISlangUnknown")]
public unsafe partial struct IGlobalSession
{
    public void** lpVtbl;

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

    /// <summary>Create a new session for loading and compiling code.</summary>
    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int createSession([NativeTypeName("const SessionDesc &")] SessionDesc* desc, ISession** outSession)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SessionDesc*, ISession**, int>)(lpVtbl[3]))((IGlobalSession*)Unsafe.AsPointer(ref this), desc, outSession);
    }

    /// <summary>Look up the internal ID of a profile by its <c>name</c>.</summary>
    /// <remarks>
    /// <para>Profile IDs are *not* guaranteed to be stable across versions of the Slang library, so clients are expected to look up profiles by name at runtime.</para>
    /// </remarks>
    [VtblIndex(4)]
    public SlangProfileID findProfile([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, SlangProfileID>)(lpVtbl[4]))((IGlobalSession*)Unsafe.AsPointer(ref this), name);
    }

    /// <summary>Set the path that downstream compilers (aka back end compilers) will be looked from.</summary>
    /// <remarks>
    /// <para>For back ends that are dlls/shared libraries, it will mean the path will be prefixed with the path when calls are made out to ISlangSharedLibraryLoader. For executables - it will look for executables along the path</para>
    /// </remarks>
    /// <param name="passThrough">Identifies the downstream compiler</param>
    /// <param name="path">The path to find the downstream compiler (shared library/dll/executable)</param>
    [VtblIndex(5)]
    public void setDownstreamCompilerPath(SlangPassThrough passThrough, [NativeTypeName("const char *")] sbyte* path)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, sbyte*, void>)(lpVtbl[5]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, path);
    }

    /// <summary>DEPRECATED: Use setLanguagePrelude</summary>
    /// <remarks>
    /// <para>Set the 'prelude' for generated code for a 'downstream compiler'.</para>
    /// <para>That for pass-through usage, prelude is not pre-pended, preludes are for code generation only.</para>
    /// </remarks>
    /// <param name="passThrough">The downstream compiler for generated code that will have the prelude applied to it.</param>
    /// <param name="preludeText">The text added pre-pended verbatim before the generated source</param>
    [VtblIndex(6)]
    public void setDownstreamCompilerPrelude(SlangPassThrough passThrough, [NativeTypeName("const char *")] sbyte* preludeText)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, sbyte*, void>)(lpVtbl[6]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, preludeText);
    }

    /// <summary>DEPRECATED: Use getLanguagePrelude</summary>
    /// <remarks>
    /// <para>Get the 'prelude' for generated code for a 'downstream compiler'.</para>
    /// </remarks>
    /// <param name="passThrough">The downstream compiler for generated code that will have the prelude applied to it.</param>
    /// <param name="outPrelude">On exit holds a blob that holds the string of the prelude.</param>
    [VtblIndex(7)]
    public void getDownstreamCompilerPrelude(SlangPassThrough passThrough, ISlangBlob** outPrelude)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, ISlangBlob**, void>)(lpVtbl[7]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, outPrelude);
    }

    /// <summary>Get the build version 'tag' string. The string is the same as produced via <c>git describe --tags</c> for the project. If Slang is built separately from the automated build scripts the contents will by default be 'unknown'. Any string can be set by changing the contents of 'slang-tag-version.h' file and recompiling the project.</summary>
    /// <remarks>
    /// <para>This method will return exactly the same result as the free function spGetBuildTagString.</para>
    /// </remarks>
    /// <returns>The build tag string</returns>
    [VtblIndex(8)]
    [return: NativeTypeName("const char *")]
    public sbyte* getBuildTagString()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*>)(lpVtbl[8]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    /// <summary>For a given source language set the default compiler. If a default cannot be chosen (for example the target cannot be achieved by the default), the default will not be used.</summary>
    /// <param name="sourceLanguage">the source language</param>
    /// <param name="defaultCompiler">the default compiler for that language</param>
    [VtblIndex(9)]
    [return: NativeTypeName("SlangResult")]
    public int setDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage, SlangPassThrough defaultCompiler)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, SlangPassThrough, int>)(lpVtbl[9]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, defaultCompiler);
    }

    /// <summary>For a source type get the default compiler</summary>
    /// <param name="sourceLanguage">the source language</param>
    /// <returns>The downstream compiler for that source language</returns>
    [VtblIndex(10)]
    public SlangPassThrough getDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, SlangPassThrough>)(lpVtbl[10]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage);
    }

    /// <summary>Set the 'prelude' placed before generated code for a specific language type.</summary>
    /// <remarks>
    /// <para>Note! That for pass-through usage, prelude is not pre-pended, preludes are for code generation only.</para>
    /// </remarks>
    /// <param name="sourceLanguage">The language the prelude should be inserted on.</param>
    /// <param name="preludeText">The text added pre-pended verbatim before the generated source</param>
    [VtblIndex(11)]
    public void setLanguagePrelude(SlangSourceLanguage sourceLanguage, [NativeTypeName("const char *")] sbyte* preludeText)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, sbyte*, void>)(lpVtbl[11]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, preludeText);
    }

    /// <summary>Get the 'prelude' associated with a specific source language.</summary>
    /// <param name="sourceLanguage">The language the prelude should be inserted on.</param>
    /// <param name="outPrelude">On exit holds a blob that holds the string of the prelude.</param>
    [VtblIndex(12)]
    public void getLanguagePrelude(SlangSourceLanguage sourceLanguage, ISlangBlob** outPrelude)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangSourceLanguage, ISlangBlob**, void>)(lpVtbl[12]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourceLanguage, outPrelude);
    }

    /// <summary>Create a compile request.</summary>
    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    [Obsolete("slang.h declares this SLANG_DEPRECATED.")]
    public int createCompileRequest([NativeTypeName("slang::ICompileRequest **")] ICompileRequest** outCompileRequest)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, ICompileRequest**, int>)(lpVtbl[13]))((IGlobalSession*)Unsafe.AsPointer(ref this), outCompileRequest);
    }

    /// <summary>Add new builtin declarations to be used in subsequent compiles.</summary>
    [VtblIndex(14)]
    [Obsolete("slang.h declares this SLANG_DEPRECATED.")]
    public void addBuiltins([NativeTypeName("const char *")] sbyte* sourcePath, [NativeTypeName("const char *")] sbyte* sourceString)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, sbyte*, void>)(lpVtbl[14]))((IGlobalSession*)Unsafe.AsPointer(ref this), sourcePath, sourceString);
    }

    /// <summary>Set the session shared library loader. If this changes the loader, it may cause shared libraries to be unloaded</summary>
    /// <param name="loader">The loader to set. Setting nullptr sets the default loader.</param>
    [VtblIndex(15)]
    public void setSharedLibraryLoader(ISlangSharedLibraryLoader* loader)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, ISlangSharedLibraryLoader*, void>)(lpVtbl[15]))((IGlobalSession*)Unsafe.AsPointer(ref this), loader);
    }

    /// <summary>Gets the currently set shared library loader</summary>
    /// <returns>Gets the currently set loader. If returns nullptr, it's the default loader</returns>
    [VtblIndex(16)]
    public ISlangSharedLibraryLoader* getSharedLibraryLoader()
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, ISlangSharedLibraryLoader*>)(lpVtbl[16]))((IGlobalSession*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Returns SLANG_OK if the compilation target is supported for this session</summary>
    /// <param name="target">The compilation target to test</param>
    /// <returns>SLANG_OK if the target is available SLANG_E_NOT_IMPLEMENTED if not implemented in this build SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work could not be found SLANG_FAIL other kinds of failures</returns>
    [VtblIndex(17)]
    [return: NativeTypeName("SlangResult")]
    public int checkCompileTargetSupport(SlangCompileTarget target)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, int>)(lpVtbl[17]))((IGlobalSession*)Unsafe.AsPointer(ref this), target);
    }

    /// <summary>Returns SLANG_OK if the pass through support is supported for this session</summary>
    /// <remarks>
    /// <para><c>session</c>: Session</para>
    /// <para><c>target</c>: The compilation target to test</para>
    /// </remarks>
    /// <returns>SLANG_OK if the target is available SLANG_E_NOT_IMPLEMENTED if not implemented in this build SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work could not be found SLANG_FAIL other kinds of failures</returns>
    [VtblIndex(18)]
    [return: NativeTypeName("SlangResult")]
    public int checkPassThroughSupport(SlangPassThrough passThrough)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, int>)(lpVtbl[18]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough);
    }

    /// <summary>Compile from (embedded source) the core module on the session. Will return a failure if there is already a core module available NOTE! API is experimental and not ready for production code</summary>
    /// <param name="flags">to control compilation</param>
    [VtblIndex(19)]
    [return: NativeTypeName("SlangResult")]
    public int compileCoreModule([NativeTypeName("slang::CompileCoreModuleFlags")] uint flags)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, uint, int>)(lpVtbl[19]))((IGlobalSession*)Unsafe.AsPointer(ref this), flags);
    }

    /// <summary>Load the core module. Currently loads modules from the file system.</summary>
    /// <remarks>
    /// <para>NOTE! API is experimental and not ready for production code</para>
    /// </remarks>
    /// <param name="coreModule">Start address of the serialized core module</param>
    /// <param name="coreModuleSizeInBytes">The size in bytes of the serialized core module</param>
    [VtblIndex(20)]
    [return: NativeTypeName("SlangResult")]
    public int loadCoreModule([NativeTypeName("const void *")] void* coreModule, [NativeTypeName("size_t")] nuint coreModuleSizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, void*, nuint, int>)(lpVtbl[20]))((IGlobalSession*)Unsafe.AsPointer(ref this), coreModule, coreModuleSizeInBytes);
    }

    /// <summary>Save the core module to the file system</summary>
    /// <remarks>
    /// <para>NOTE! API is experimental and not ready for production code</para>
    /// </remarks>
    /// <param name="archiveType">The type of archive used to hold the core module</param>
    /// <param name="outBlob">The serialized blob containing the core module</param>
    [VtblIndex(21)]
    [return: NativeTypeName("SlangResult")]
    public int saveCoreModule(SlangArchiveType archiveType, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangArchiveType, ISlangBlob**, int>)(lpVtbl[21]))((IGlobalSession*)Unsafe.AsPointer(ref this), archiveType, outBlob);
    }

    /// <summary>Look up the internal ID of a capability by its <c>name</c>.</summary>
    /// <remarks>
    /// <para>Capability IDs are *not* guaranteed to be stable across versions of the Slang library, so clients are expected to look up capabilities by name at runtime.</para>
    /// </remarks>
    [VtblIndex(22)]
    public SlangCapabilityID findCapability([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, SlangCapabilityID>)(lpVtbl[22]))((IGlobalSession*)Unsafe.AsPointer(ref this), name);
    }

    /// <summary>Set the downstream/pass through compiler to be used for a transition from the source type to the target type</summary>
    /// <param name="source">The source 'code gen target'</param>
    /// <param name="target">The target 'code gen target'</param>
    /// <param name="compiler">The compiler/pass through to use for the transition from source to target</param>
    [VtblIndex(23)]
    public void setDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, SlangCompileTarget, SlangPassThrough, void>)(lpVtbl[23]))((IGlobalSession*)Unsafe.AsPointer(ref this), source, target, compiler);
    }

    /// <summary>Get the downstream/pass through compiler for a transition specified by source and target</summary>
    /// <param name="source">The source 'code gen target'</param>
    /// <param name="target">The target 'code gen target'</param>
    /// <returns>The compiler that is used for the transition. Returns SLANG_PASS_THROUGH_NONE it is not defined</returns>
    [VtblIndex(24)]
    public SlangPassThrough getDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangCompileTarget, SlangCompileTarget, SlangPassThrough>)(lpVtbl[24]))((IGlobalSession*)Unsafe.AsPointer(ref this), source, target);
    }

    /// <summary>Get the time in seconds spent in the slang and downstream compiler.</summary>
    [VtblIndex(25)]
    public void getCompilerElapsedTime(double* outTotalTime, double* outDownstreamTime)
    {
        ((delegate* unmanaged[Stdcall]<IGlobalSession*, double*, double*, void>)(lpVtbl[25]))((IGlobalSession*)Unsafe.AsPointer(ref this), outTotalTime, outDownstreamTime);
    }

    /// <summary>Specify a spirv.core.grammar.json file to load and use when parsing and checking any SPIR-V code</summary>
    [VtblIndex(26)]
    [return: NativeTypeName("SlangResult")]
    public int setSPIRVCoreGrammar([NativeTypeName("const char *")] sbyte* jsonPath)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, sbyte*, int>)(lpVtbl[26]))((IGlobalSession*)Unsafe.AsPointer(ref this), jsonPath);
    }

    /// <summary>Parse slangc command line options into a SessionDesc that can be used to create a session with the targets and compiler options specified in the command line.</summary>
    /// <remarks>
    /// <para>The session description receives: - One target per <c>-target</c>. With no <c>-target</c>, targets are inferred from the extensions of <c>-o</c> paths, or else from a <c>-profile</c> that implies a target. - The target options given after a <c>-target</c>, in that target's <c>compilerOptionEntries</c>. Target options are <c>-profile</c>, <c>-capability</c>, <c>-fp-mode</c>, <c>-whole-program</c>, <c>-embed-downstream-ir</c>, the layout options <c>-fvk-use-scalar-layout</c>, <c>-fvk-use-dx-layout</c> and <c>-fvk-use-c-layout</c>, and the <c>-emit-spirv-*</c> and <c>-emit-cpu-*</c> options. The profile's version is also stored in <c>TargetDesc::profile</c>. A stage that the profile names (for example the compute stage of <c>cs_6_0</c>) is not kept, because entry points supply their own stages. Each target's <c>compilerOptionEntries</c> also repeat the session options other than <c>-X...</c>. - Target options given before the first <c>-target</c>. With a single target, declared or inferred, they apply to it. With no target, they become session options for the targets added to the session, except that a <c>-profile</c> fails the call. With several targets they are rejected. - All other options, wherever they appear (for example <c>-I</c>, <c>-D</c>, <c>-O</c>, <c>-g</c>, <c>-matrix-layout-row-major</c>, <c>-r</c>, <c>-X...</c>), as session options.</para>
    /// <para>Options that configure a compilation rather than a session are accepted and ignored, for example positional input paths (source files, <c>.slang-module</c> and <c>.slang-lib</c> files, and the stdin path <c>-</c> after <c>--</c>, none of which are read), <c>-entry</c>, <c>-stage</c>, <c>-specialize</c>, <c>-module-name</c> and <c>-depfile</c>. A module given with <c>-r</c> is instead loaded during the call and kept as a session option. Output paths are used only to infer a target from their extension when no <c>-target</c> is given, and are not retained, so <c>-target hlsl -o foo.spv</c> yields only the HLSL target. Only an explicit <c>-whole-program</c> selects whole-program code generation for a target. Neither an output path nor the implicit output that slangc gives a single target with no <c>-o</c> (for example <c>-target spirv</c>, <c>-target metal</c> or <c>-target cuda</c>) selects it. Because the session receives no entry points or output paths, the slangc checks on them, such as the error for a <c>-stage</c> with no <c>-entry</c>, are not made.</para>
    /// <para>Some options act on this global session during the call, for example <c>-load-core-module</c>, <c>-compile-core-module</c>, <c>-save-core-module</c>, <c>-spirv-core-grammar</c>, <c>-default-downstream-compiler</c> and the <c>-&lt;compiler&gt;-path</c> options. Their effects are not undone when the call fails.</para>
    /// <para>On any failure <c>outSessionDesc</c> is left unchanged and <c>outAuxAllocation</c> is set to null. No diagnostic text is returned.</para>
    /// </remarks>
    /// <param name="argc">The number of command line arguments.</param>
    /// <param name="argv">An input array of command line arguments to parse.</param>
    /// <param name="outSessionDesc">A pointer to a SessionDesc struct to receive parsed session desc.</param>
    /// <param name="outAuxAllocation">Auxiliary memory allocated to hold data used in the session desc.</param>
    /// <returns>SLANG_OK on success. A failure result if <c>outSessionDesc-&gt;structureSize</c> is too small, or if the parser reports an error, for example: - a malformed argument: an unknown option, a missing option value, an unknown target, profile, capability, stage or option value, or a <c>-r</c> module that cannot be loaded; - conflicting settings: a target repeated after a different <c>-target</c>, conflicting profiles for a target, a <c>-profile</c> with no <c>-target</c> that implies no target, a profile or target option given before several targets, a capability that the profile cannot provide, or options that require or exclude each other.</returns>
    [VtblIndex(27)]
    [return: NativeTypeName("SlangResult")]
    public int parseCommandLineArguments(int argc, [NativeTypeName("const char *const *")] sbyte** argv, [NativeTypeName("slang::SessionDesc *")] SessionDesc* outSessionDesc, ISlangUnknown** outAuxAllocation)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, int, sbyte**, SessionDesc*, ISlangUnknown**, int>)(lpVtbl[27]))((IGlobalSession*)Unsafe.AsPointer(ref this), argc, argv, outSessionDesc, outAuxAllocation);
    }

    /// <summary>Computes a digest that uniquely identifies the session description.</summary>
    [VtblIndex(28)]
    [return: NativeTypeName("SlangResult")]
    public int getSessionDescDigest([NativeTypeName("slang::SessionDesc *")] SessionDesc* sessionDesc, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SessionDesc*, ISlangBlob**, int>)(lpVtbl[28]))((IGlobalSession*)Unsafe.AsPointer(ref this), sessionDesc, outBlob);
    }

    /// <summary>Compile from (embedded source) the builtin module on the session. Will return a failure if there is already a builtin module available. NOTE! API is experimental and not ready for production code.</summary>
    /// <param name="module">The builtin module name.</param>
    /// <param name="flags">to control compilation</param>
    [VtblIndex(29)]
    [return: NativeTypeName("SlangResult")]
    public int compileBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, [NativeTypeName("slang::CompileCoreModuleFlags")] uint flags)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, uint, int>)(lpVtbl[29]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, flags);
    }

    /// <summary>Load a builtin module. Currently loads modules from the file system.</summary>
    /// <remarks>
    /// <para>NOTE! API is experimental and not ready for production code</para>
    /// </remarks>
    /// <param name="module">The builtin module name</param>
    /// <param name="moduleData">Start address of the serialized core module</param>
    /// <param name="sizeInBytes">The size in bytes of the serialized builtin module</param>
    [VtblIndex(30)]
    [return: NativeTypeName("SlangResult")]
    public int loadBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, [NativeTypeName("const void *")] void* moduleData, [NativeTypeName("size_t")] nuint sizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, void*, nuint, int>)(lpVtbl[30]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, moduleData, sizeInBytes);
    }

    /// <summary>Save the builtin module to the file system</summary>
    /// <remarks>
    /// <para>NOTE! API is experimental and not ready for production code</para>
    /// </remarks>
    /// <param name="module">The builtin module name</param>
    /// <param name="archiveType">The type of archive used to hold the builtin module</param>
    /// <param name="outBlob">The serialized blob containing the builtin module</param>
    [VtblIndex(31)]
    [return: NativeTypeName("SlangResult")]
    public int saveBuiltinModule([NativeTypeName("slang::BuiltinModuleName")] BuiltinModuleName module, SlangArchiveType archiveType, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, BuiltinModuleName, SlangArchiveType, ISlangBlob**, int>)(lpVtbl[31]))((IGlobalSession*)Unsafe.AsPointer(ref this), module, archiveType, outBlob);
    }

    /// <summary>Get the on-disk path of the downstream/pass-through compiler that Slang will actually load and use for <c>passThrough</c>, applying the same lazy discovery and library search order used during compilation. This lets a client locate the exact library Slang selected - for example, the specific NVRTC that will compile CUDA - and load it itself to query capabilities (such as the supported architectures) directly.</summary>
    /// <remarks>
    /// <para>This is not a cheap accessor: the first call for a given <c>passThrough</c> performs discovery and loads the downstream library into the process (then memoizes it for subsequent calls).</para>
    /// <para>The path is recovered from the loaded shared library, so it is available for the shared-library pass-throughs (e.g. NVRTC, DXC, FXC, glslang). A pass-through backed by an executable located on <c>PATH</c> (e.g. Clang/GCC/VS) or a target without shared-library introspection (e.g. WASM) has no such path and returns SLANG_E_NOT_AVAILABLE, which the client must keep distinct from SLANG_E_NOT_FOUND (the compiler was not located at all).</para>
    /// </remarks>
    /// <param name="passThrough">The downstream compiler to query (e.g. SLANG_PASS_THROUGH_NVRTC).</param>
    /// <param name="outPath">Must be non-null. On SLANG_OK receives the resolved library path as a blob; left untouched on any failure return.</param>
    /// <returns>SLANG_OK if the compiler was located, loaded, and its path recovered. SLANG_E_NOT_FOUND if the compiler could not be located or loaded (and likewise for SLANG_PASS_THROUGH_NONE or an out-of-range value). SLANG_E_NOT_AVAILABLE if the compiler was loaded but has no recoverable on-disk path -- it is not backed by a shared library (an executable-based command-line compiler such as Clang/GCC/VS, or Metal) or the platform has no shared-library introspection (e.g. WASM).</returns>
    [VtblIndex(32)]
    [return: NativeTypeName("SlangResult")]
    public int getDownstreamCompilerPath(SlangPassThrough passThrough, ISlangBlob** outPath)
    {
        return ((delegate* unmanaged[Stdcall]<IGlobalSession*, SlangPassThrough, ISlangBlob**, int>)(lpVtbl[32]))((IGlobalSession*)Unsafe.AsPointer(ref this), passThrough, outPath);
    }
}
