using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>A component type is a unit of shader code layout, reflection, and linking.</summary>
/// <remarks>
/// <para>A component type is a unit of shader code that can be included into a linked and compiled shader program. Each component type may have:</para>
/// <para>Zero or more uniform shader parameters, representing textures, buffers, etc. that the code in the component depends on.</para>
/// <para>Zero or more *specialization* parameters, which are type or value parameters that can be used to synthesize specialized versions of the component type.</para>
/// <para>Zero or more entry points, which are the individually invocable kernels that can have final code generated.</para>
/// <para>Zero or more *requirements*, which are other component types on which the component type depends.</para>
/// <para>One example of a component type is a module of Slang code:</para>
/// <para>The global-scope shader parameters declared in the module are the parameters when considered as a component type.</para>
/// <para>Any global-scope generic or interface type parameters introduce specialization parameters for the module.</para>
/// <para>A module does not by default include any entry points when considered as a component type (although the code of the module might *declare* some entry points).</para>
/// <para>Any other modules that are <c>import</c>ed in the source code become requirements of the module, when considered as a component type.</para>
/// <para>An entry point is another example of a component type:</para>
/// <para>The <c>uniform</c> parameters of the entry point function are its shader parameters when considered as a component type.</para>
/// <para>Any generic or interface-type parameters of the entry point introduce specialization parameters.</para>
/// <para>An entry point component type exposes a single entry point (itself).</para>
/// <para>An entry point has one requirement for the module in which it was defined.</para>
/// <para>Component types can be manipulated in a few ways:</para>
/// <para>Multiple component types can be combined into a composite, which combines all of their code, parameters, etc.</para>
/// <para>A component type can be specialized, by "plugging in" types and values for its specialization parameters.</para>
/// <para>A component type can be laid out for a particular target, giving offsets/bindings to the shader parameters it contains.</para>
/// <para>Generated kernel code can be requested for entry points.</para>
/// </remarks>
[NativeTypeName("struct IComponentType : ISlangUnknown")]
public unsafe partial struct IComponentType
{
    public void** lpVtbl;

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

    /// <summary>Get the runtime session that this component type belongs to.</summary>
    [VtblIndex(3)]
    [return: NativeTypeName("slang::ISession *")]
    public ISession* getSession()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, ISession*>)(lpVtbl[3]))((IComponentType*)Unsafe.AsPointer(ref this));
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, ISlangBlob**, ShaderReflection*>)(lpVtbl[4]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outDiagnostics);
    }

    /// <summary>Get the number of (unspecialized) specialization parameters for the component type.</summary>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangInt")]
    public long getSpecializationParamCount()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long>)(lpVtbl[5]))((IComponentType*)Unsafe.AsPointer(ref this));
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[6]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outCode, outDiagnostics);
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangMutableFileSystem**, int>)(lpVtbl[7]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outFileSystem);
    }

    /// <summary>Compute a hash for the entry point at <c>entryPointIndex</c> for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>This computes a hash based on all the dependencies for this component type as well as the target settings affecting the compiler backend. The computed hash is used as a key for caching the output of the compiler backend to implement shader caching.</para>
    /// </remarks>
    [VtblIndex(8)]
    public void getEntryPointHash([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outHash)
    {
        ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, ISlangBlob**, void>)(lpVtbl[8]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outHash);
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, SpecializationArg*, long, IComponentType**, ISlangBlob**, int>)(lpVtbl[9]))((IComponentType*)Unsafe.AsPointer(ref this), specializationArgs, specializationArgCount, outSpecializedComponentType, outDiagnostics);
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, IComponentType**, ISlangBlob**, int>)(lpVtbl[10]))((IComponentType*)Unsafe.AsPointer(ref this), outLinkedComponentType, outDiagnostics);
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
        return ((delegate* unmanaged[Stdcall]<IComponentType*, int, int, ISlangSharedLibrary**, ISlangBlob**, int>)(lpVtbl[11]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outSharedLibrary, outDiagnostics);
    }

    /// <summary>Get a new ComponentType object that represents a renamed entry point.</summary>
    /// <remarks>
    /// <para>The current object must be a single EntryPoint, or a CompositeComponentType or SpecializedComponentType that contains one EntryPoint component.</para>
    /// </remarks>
    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int renameEntryPoint([NativeTypeName("const char *")] sbyte* newName, IComponentType** outEntryPoint)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, sbyte*, IComponentType**, int>)(lpVtbl[12]))((IComponentType*)Unsafe.AsPointer(ref this), newName, outEntryPoint);
    }

    /// <summary>Link and specify additional compiler options when generating code from the linked program.</summary>
    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int linkWithOptions(IComponentType** outLinkedComponentType, [NativeTypeName("uint32_t")] uint compilerOptionEntryCount, [NativeTypeName("const CompilerOptionEntry *")] CompilerOptionEntry* compilerOptionEntries, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, IComponentType**, uint, CompilerOptionEntry*, ISlangBlob**, int>)(lpVtbl[13]))((IComponentType*)Unsafe.AsPointer(ref this), outLinkedComponentType, compilerOptionEntryCount, compilerOptionEntries, outDiagnostics);
    }

    /// <summary>Get the compiled code for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCode([NativeTypeName("SlangInt")] long targetIndex, [NativeTypeName("IBlob **")] ISlangBlob** outCode, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, ISlangBlob**, ISlangBlob**, int>)(lpVtbl[14]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outCode, outDiagnostics);
    }

    /// <summary>Get metadata for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetMetadata([NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[15]))((IComponentType*)Unsafe.AsPointer(ref this), targetIndex, outMetadata, outDiagnostics);
    }

    /// <summary>Get metadata for the entry point at <c>entryPointIndex</c> for the chosen <c>targetIndex</c>.</summary>
    /// <remarks>
    /// <para>Has the same requirements and experimental threading note as <c>getEntryPointCode()</c>.</para>
    /// </remarks>
    [VtblIndex(16)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointMetadata([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, IMetadata** outMetadata, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType*, long, long, IMetadata**, ISlangBlob**, int>)(lpVtbl[16]))((IComponentType*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outMetadata, outDiagnostics);
    }
}
