using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A module is the granularity of shader code compilation and loading.</summary>
/// <remarks>
/// <para>In most cases a module corresponds to a single compile "translation unit." This will often be a single <c>.slang</c> or <c>.hlsl</c> file and everything it <c>#include</c>s.</para>
/// <para>Notably, a module <c>M</c> does *not* include the things it <c>import</c>s, as these as distinct modules that <c>M</c> depends on. There is a directed graph of module dependencies, and all modules in the graph must belong to the same session (<c>ISession</c>).</para>
/// <para>A module establishes a namespace for looking up types, functions, etc.</para>
/// </remarks>
[NativeTypeName("struct IModule : slang::IComponentType")]
public unsafe partial struct IModule
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, SlangUUID*, void**, int>)(lpVtbl[0]))((IModule*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, uint>)(lpVtbl[1]))((IModule*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, uint>)(lpVtbl[2]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the runtime session that this component type belongs to.</summary>
    [VtblIndex(3)]
    [return: NativeTypeName("slang::ISession *")]
    public ISession* getSession()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, ISession*>)(lpVtbl[3]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the layout for this program for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>The resulting layout will establish offsets/bindings for all of the global and entry-point shader parameters in the component type.</para>
    /// <para>If this component type has specialization parameters (that is, it is not fully specialized), then the resulting layout may be incomplete, and plugging in arguments for generic specialization parameters may result in a component type that doesn't have a compatible layout. If the component type only uses interface-type specialization parameters, then the layout for a specialization should be compatible with an unspecialized layout (all parameters in the unspecialized layout will have the same offset/binding in the specialized layout).</para>
    /// <para>If this component type is combined into a composite, then the absolute offsets/bindings of parameters may not stay the same. If the shader parameters in a component type don't make use of explicit binding annotations (e.g., <c>register(...)</c>), then the *relative* offset of shader parameters will stay the same when it is used in a composition.</para>
    /// </remarks>
    [VtblIndex(4)]
    [return: NativeTypeName("slang::ProgramLayout *")]
    public ShaderReflection* getLayout([NativeTypeName("SlangInt")] long targetIndex = 0, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, ISlangBlob**, ShaderReflection*>)(lpVtbl[4]))((IModule*)Unsafe.AsPointer(ref this), targetIndex, outDiagnostics);
    }

    /// <summary>Get the number of (unspecialized) specialization parameters for the component type.</summary>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangInt")]
    public long getSpecializationParamCount()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long>)(lpVtbl[5]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the compiled code for the entry point at <c>entryPointIndex</c> for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Entry point code requires a component type that is fully specialized and fully linked.</para>
    /// <para>If code has not already been generated for the given entry point and target, then a compilation error may be detected, in which case <c>outDiagnostics</c> (if non-null) will be filled in with a blob of messages diagnosing the error.</para>
    /// <para>Experimental threading note: after a component type has been fully specialized and linked, this method is supported for concurrent backend code generation, including from multiple threads compiling different linked <c>IComponentType</c> instances.</para>
    /// <para>The same experimental threading model also applies to <c>getResultAsFileSystem()</c>, <c>getTargetCode()</c>, <c>getTargetMetadata()</c>, and <c>getEntryPointMetadata()</c>.</para>
    /// <para>Front-end operations such as loading modules, specialization, and linking still require external synchronization unless documented otherwise.</para>
    /// </remarks>
    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCode([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[6]))((IModule*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outCode, outDiagnostics);
    }

    /// <summary>Get the compilation result as a file system.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// <para>The result is not written to the actual OS file system, but is made available as an in memory representation.</para>
    /// </remarks>
    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getResultAsFileSystem([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, ISlangMutableFileSystem** outFileSystem)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, long, ISlangMutableFileSystem**, int>)(lpVtbl[7]))((IModule*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outFileSystem);
    }

    /// <summary>Compute a hash for the entry point at <c>entryPointIndex</c> for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>This computes a hash based on all the dependencies for this component type as well as the target settings affecting the compiler backend. The computed hash is used as a key for caching the output of the compiler backend to implement shader caching.</para>
    /// </remarks>
    [VtblIndex(8)]
    public void getEntryPointHash([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outHash)
    {
        ((delegate* unmanaged[Stdcall]<IModule*, long, long, ISlangBlob**, void>)(lpVtbl[8]))((IModule*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outHash);
    }

    /// <summary>Specialize the component by binding its specialization parameters to concrete arguments.</summary>
    /// <remarks>
    /// <para>The <c>specializationArgs</c> array must have <c>specializationArgCount</c> entries, and this must match the number of specialization parameters on this component type.</para>
    /// <para>If any diagnostics (error or warnings) are produced, they will be written to <c>outDiagnostics</c>.</para>
    /// </remarks>
    [VtblIndex(9)]
    [return: NativeTypeName("SlangResult")]
    public int specialize([NativeTypeName("const SpecializationArg *")] SpecializationArg* specializationArgs, [NativeTypeName("SlangInt")] long specializationArgCount, IComponentType** outSpecializedComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, SpecializationArg*, long, IComponentType**, ISlangBlob**, int>)(lpVtbl[9]))((IModule*)Unsafe.AsPointer(ref this), specializationArgs, specializationArgCount, outSpecializedComponentType, outDiagnostics);
    }

    /// <summary>Link this component type against all of its unsatisfied dependencies.</summary>
    /// <remarks>
    /// <para>A component type may have unsatisfied dependencies. For example, a module depends on any other modules it <c>import</c>s, and an entry point depends on the module that defined it.</para>
    /// <para>A user can manually satisfy dependencies by creating a composite component type, and when doing so they retain full control over the relative ordering of shader parameters in the resulting layout.</para>
    /// <para>It is an error to try to generate/access compiled kernel code for a component type with unresolved dependencies, so if dependencies remain after whatever manual composition steps an application cares to perform, the <c>link()</c> function can be used to automatically compose in any remaining dependencies. The order of parameters (and hence the global layout) that results will be deterministic, but is not currently documented.</para>
    /// </remarks>
    [VtblIndex(10)]
    [return: NativeTypeName("SlangResult")]
    public int link(IComponentType** outLinkedComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, IComponentType**, ISlangBlob**, int>)(lpVtbl[10]))((IModule*)Unsafe.AsPointer(ref this), outLinkedComponentType, outDiagnostics);
    }

    /// <summary>Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.</summary>
    /// <remarks>
    /// <para>The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.</para>
    /// <para>NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.</para>
    /// <para><c>entryPointIndex</c>: The index of the entry point to get code for.</para>
    /// <para><c>targetIndex</c>: The index of the target to get code for (default: zero).</para>
    /// <para><c>outSharedLibrary</c>: A pointer to a ISharedLibrary interface which functions can be queried on.</para>
    /// </remarks>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(11)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointHostCallable(int entryPointIndex, int targetIndex, ISlangSharedLibrary** outSharedLibrary, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, int, int, ISlangSharedLibrary**, ISlangBlob**, int>)(lpVtbl[11]))((IModule*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outSharedLibrary, outDiagnostics);
    }

    /// <summary>Get a new ComponentType object that represents a renamed entry point.</summary>
    /// <remarks>
    /// <para>The current object must be a single EntryPoint, or a CompositeComponentType or SpecializedComponentType that contains one EntryPoint component.</para>
    /// </remarks>
    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int renameEntryPoint([NativeTypeName("const char *")] sbyte* newName, IComponentType** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*, IComponentType**, int>)(lpVtbl[12]))((IModule*)Unsafe.AsPointer(ref this), newName, outEntryPoint);
    }

    /// <summary>Link and specify additional compiler options when generating code from the linked program.</summary>
    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int linkWithOptions(IComponentType** outLinkedComponentType, [NativeTypeName("uint32_t")] uint compilerOptionEntryCount, [NativeTypeName("const CompilerOptionEntry *")] CompilerOptionEntry* compilerOptionEntries, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, IComponentType**, uint, CompilerOptionEntry*, ISlangBlob**, int>)(lpVtbl[13]))((IModule*)Unsafe.AsPointer(ref this), outLinkedComponentType, compilerOptionEntryCount, compilerOptionEntries, outDiagnostics);
    }

    /// <summary>Get the compiled code for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCode([NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[14]))((IModule*)Unsafe.AsPointer(ref this), targetIndex, outCode, outDiagnostics);
    }

    /// <summary>Get metadata for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetMetadata([NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[15]))((IModule*)Unsafe.AsPointer(ref this), targetIndex, outMetadata, outDiagnostics);
    }

    /// <summary>Get metadata for the entry point at <c>entryPointIndex</c> for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(16)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointMetadata([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, long, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[16]))((IModule*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outMetadata, outDiagnostics);
    }

    /// <summary>Find and an entry point by name.</summary>
    /// <remarks>
    /// <para>Note that this does not work in case the function is not explicitly designated as an entry</para>
    /// <para>point, e.g. using a <c>[shader("...")]</c> attribute. In such cases, consider using</para>
    /// <para><c>IModule::findAndCheckEntryPoint</c> instead.</para>
    /// </remarks>
    [VtblIndex(17)]
    [return: NativeTypeName("SlangResult")]
    public int findEntryPointByName([NativeTypeName("const char *")] sbyte* name, IEntryPoint** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*, IEntryPoint**, int>)(lpVtbl[17]))((IModule*)Unsafe.AsPointer(ref this), name, outEntryPoint);
    }

    /// <summary>Get number of entry points defined in the module. An entry point defined in a module</summary>
    /// <remarks>
    /// <para>is by default not included in the linkage, so calls to <c>IComponentType::getEntryPointCount</c></para>
    /// <para>on an <c>IModule</c> instance will always return 0. However <c>IModule::getDefinedEntryPointCount</c></para>
    /// <para>will return the number of defined entry points.</para>
    /// </remarks>
    [VtblIndex(18)]
    [return: NativeTypeName("SlangInt32")]
    public int getDefinedEntryPointCount()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, int>)(lpVtbl[18]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the name of an entry point defined in the module.</summary>
    [VtblIndex(19)]
    [return: NativeTypeName("SlangResult")]
    public int getDefinedEntryPoint([NativeTypeName("SlangInt32")] int index, IEntryPoint** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, int, IEntryPoint**, int>)(lpVtbl[19]))((IModule*)Unsafe.AsPointer(ref this), index, outEntryPoint);
    }

    /// <summary>Get a serialized representation of the checked module.</summary>
    [VtblIndex(20)]
    [return: NativeTypeName("SlangResult")]
    public int serialize(ISlangBlob** outSerializedBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, ISlangBlob**, int>)(lpVtbl[20]))((IModule*)Unsafe.AsPointer(ref this), outSerializedBlob);
    }

    /// <summary>Write the serialized representation of this module to a file.</summary>
    [VtblIndex(21)]
    [return: NativeTypeName("SlangResult")]
    public int writeToFile([NativeTypeName("const char *")] sbyte* fileName)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*, int>)(lpVtbl[21]))((IModule*)Unsafe.AsPointer(ref this), fileName);
    }

    /// <summary>Get the name of the module.</summary>
    [VtblIndex(22)]
    [return: NativeTypeName("const char *")]
    public sbyte* getName()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*>)(lpVtbl[22]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the path of the module.</summary>
    [VtblIndex(23)]
    [return: NativeTypeName("const char *")]
    public sbyte* getFilePath()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*>)(lpVtbl[23]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the unique identity of the module.</summary>
    [VtblIndex(24)]
    [return: NativeTypeName("const char *")]
    public sbyte* getUniqueIdentity()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*>)(lpVtbl[24]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Find and validate an entry point by name, even if the function is</summary>
    /// <remarks>
    /// <para>not marked with the <c>[shader("...")]</c> attribute.</para>
    /// </remarks>
    [VtblIndex(25)]
    [return: NativeTypeName("SlangResult")]
    public int findAndCheckEntryPoint([NativeTypeName("const char *")] sbyte* name, SlangStage stage, IEntryPoint** outEntryPoint, ISlangBlob** outDiagnostics)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, sbyte*, SlangStage, IEntryPoint**, ISlangBlob**, int>)(lpVtbl[25]))((IModule*)Unsafe.AsPointer(ref this), name, stage, outEntryPoint, outDiagnostics);
    }

    /// <summary>Get the number of dependency files that this module depends on.</summary>
    /// <remarks>
    /// <para>This includes both the explicit source files, as well as any</para>
    /// <para>additional files that were transitively referenced (e.g., via</para>
    /// <para>a <c>#include</c> directive).</para>
    /// </remarks>
    [VtblIndex(26)]
    [return: NativeTypeName("SlangInt32")]
    public int getDependencyFileCount()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, int>)(lpVtbl[26]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the path to a file this module depends on.</summary>
    [VtblIndex(27)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDependencyFilePath([NativeTypeName("SlangInt32")] int index)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, int, sbyte*>)(lpVtbl[27]))((IModule*)Unsafe.AsPointer(ref this), index);
    }

    [VtblIndex(28)]
    [return: NativeTypeName("slang::DeclReflection *")]
    public DeclReflection* getModuleReflection()
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, DeclReflection*>)(lpVtbl[28]))((IModule*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Disassemble a module.</summary>
    [VtblIndex(29)]
    [return: NativeTypeName("SlangResult")]
    public int disassemble([NativeTypeName("slang::IBlob **")] ISlangBlob** outDisassembledBlob)
    {
        return ((delegate* unmanaged[Stdcall]<IModule*, ISlangBlob**, int>)(lpVtbl[29]))((IModule*)Unsafe.AsPointer(ref this), outDisassembledBlob);
    }
}
