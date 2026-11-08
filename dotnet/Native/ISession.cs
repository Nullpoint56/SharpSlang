using System.Runtime.CompilerServices;
using static SharpSlang.Native.LayoutRules;

namespace SharpSlang.Native;

/// <summary>A session provides a scope for code that is loaded.</summary>
/// <remarks>
/// <para>A session can be used to load modules of Slang source code, and to request target-specific compiled binaries and layout information.</para>
/// <para>In order to be able to load code, the session owns a set of active "search paths" for resolving <c>#include</c> directives and <c>import</c> declarations, as well as a set of global preprocessor definitions that will be used for all code that gets <c>import</c>ed in the session.</para>
/// <para>If multiple user shaders are loaded in the same session, and import the same module (e.g., two source files do <c>import X</c>) then there will only be one copy of <c>X</c> loaded within the session.</para>
/// <para>In order to be able to generate target code, the session owns a list of available compilation targets, which specify code generation options.</para>
/// <para>Code loaded and compiled within a session is owned by the session and will remain resident in memory until the session is released. Applications wishing to control the memory usage for compiled and loaded code should use multiple sessions.</para>
/// </remarks>
[NativeTypeName("struct ISession : ISlangUnknown")]
public unsafe partial struct ISession
{
    public void** lpVtbl;

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

    /// <summary>Get the global session thas was used to create this session.</summary>
    [VtblIndex(3)]
    [return: NativeTypeName("slang::IGlobalSession *")]
    public IGlobalSession* getGlobalSession()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, IGlobalSession*>)(lpVtbl[3]))((ISession*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Load a module as it would be by code using <c>import</c>.</summary>
    [VtblIndex(4)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModule([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, ISlangBlob**, IModule*>)(lpVtbl[4]))((ISession*)Unsafe.AsPointer(ref this), moduleName, outDiagnostics);
    }

    /// <summary>Load a module from Slang source code.</summary>
    /// <remarks>
    /// <para>If <c>source</c> is null and <c>path</c> names a readable file, the module is loaded from that file's contents. If <c>source</c> is null and <c>path</c> cannot be read, the call returns null and (when <c>outDiagnostics</c> is provided) writes a <c>CannotOpenFile</c> diagnostic.</para>
    /// </remarks>
    [VtblIndex(5)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModuleFromSource([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("slang::IBlob *")] ISlangBlob* source, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, sbyte*, ISlangBlob*, ISlangBlob**, IModule*>)(lpVtbl[5]))((ISession*)Unsafe.AsPointer(ref this), moduleName, path, source, outDiagnostics);
    }

    /// <summary>Combine multiple component types to create a composite component type.</summary>
    /// <remarks>
    /// <para>The <c>componentTypes</c> array must contain <c>componentTypeCount</c> pointers to component types that were loaded or created using the same session.</para>
    /// <para>The shader parameters and specialization parameters of the composite will be the union of those in <c>componentTypes</c>. The relative order of child component types is significant, and will affect the order in which parameters are reflected and laid out.</para>
    /// <para>The entry-point functions of the composite will be the union of those in <c>componentTypes</c>, and will follow the ordering of <c>componentTypes</c>.</para>
    /// <para>The requirements of the composite component type will be a subset of those in <c>componentTypes</c>. If an entry in <c>componentTypes</c> has a requirement that can be satisfied by another entry, then the composition will satisfy the requirement and it will not appear as a requirement of the composite. If multiple entries in <c>componentTypes</c> have a requirement for the same type, then only the first such requirement will be retained on the composite. The relative ordering of requirements on the composite will otherwise match that of <c>componentTypes</c>.</para>
    /// <para>If any diagnostics are generated during creation of the composite, they will be written to <c>outDiagnostics</c>. If an error is encountered, the function will return null.</para>
    /// <para>It is an error to create a composite component type that recursively aggregates a single module more than once.</para>
    /// </remarks>
    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int createCompositeComponentType([NativeTypeName("IComponentType *const *")] IComponentType** componentTypes, [NativeTypeName("SlangInt")] long componentTypeCount, IComponentType** outCompositeComponentType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, IComponentType**, long, IComponentType**, ISlangBlob**, int>)(lpVtbl[6]))((ISession*)Unsafe.AsPointer(ref this), componentTypes, componentTypeCount, outCompositeComponentType, outDiagnostics);
    }

    /// <summary>Specialize a type based on type arguments.</summary>
    [VtblIndex(7)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* specializeType([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("const SpecializationArg *")] SpecializationArg* specializationArgs, [NativeTypeName("SlangInt")] long specializationArgCount, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, SpecializationArg*, long, ISlangBlob**, TypeReflection*>)(lpVtbl[7]))((ISession*)Unsafe.AsPointer(ref this), type, specializationArgs, specializationArgCount, outDiagnostics);
    }

    /// <summary>Get the layout <c>type</c> on the chosen <c>target</c>.</summary>
    [VtblIndex(8)]
    [return: NativeTypeName("slang::TypeLayoutReflection *")]
    public TypeLayoutReflection* getTypeLayout([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("SlangInt")] long targetIndex = 0, [NativeTypeName("slang::LayoutRules")] LayoutRules rules = Default, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, long, LayoutRules, ISlangBlob**, TypeLayoutReflection*>)(lpVtbl[8]))((ISession*)Unsafe.AsPointer(ref this), type, targetIndex, rules, outDiagnostics);
    }

    /// <summary>Get a container type from <c>elementType</c>. For example, given type <c>T</c>, returns a type that represents <c>StructuredBuffer&lt;T&gt;</c>.</summary>
    /// <remarks>
    /// <para>param: <c>elementType</c>: the element type to wrap around.</para>
    /// <para>param: <c>containerType</c>: the type of the container to wrap <c>elementType</c> in.</para>
    /// <para>param: <c>outDiagnostics</c>: a blob to receive diagnostic messages.</para>
    /// </remarks>
    [VtblIndex(9)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* getContainerType([NativeTypeName("slang::TypeReflection *")] TypeReflection* elementType, [NativeTypeName("slang::ContainerType")] ContainerType containerType, ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, ContainerType, ISlangBlob**, TypeReflection*>)(lpVtbl[9]))((ISession*)Unsafe.AsPointer(ref this), elementType, containerType, outDiagnostics);
    }

    /// <summary>Return a <c>TypeReflection</c> that represents the <c>__Dynamic</c> type. This type can be used as a specialization argument to indicate using dynamic dispatch.</summary>
    [VtblIndex(10)]
    [return: NativeTypeName("slang::TypeReflection *")]
    public TypeReflection* getDynamicType()
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*>)(lpVtbl[10]))((ISession*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Get the mangled name for a type RTTI object.</summary>
    [VtblIndex(11)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeRTTIMangledName([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, ISlangBlob** outNameBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, ISlangBlob**, int>)(lpVtbl[11]))((ISession*)Unsafe.AsPointer(ref this), type, outNameBlob);
    }

    /// <summary>Get the mangled name for a type witness.</summary>
    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeConformanceWitnessMangledName([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, ISlangBlob** outNameBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, ISlangBlob**, int>)(lpVtbl[12]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outNameBlob);
    }

    /// <summary>Get the sequential ID used to identify a type witness in a dynamic object. The sequential ID is part of the RTTI bytes returned by <c>getDynamicObjectRTTIBytes</c>.</summary>
    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int getTypeConformanceWitnessSequentialID([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, [NativeTypeName("uint32_t *")] uint* outId)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, uint*, int>)(lpVtbl[13]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outId);
    }

    /// <summary>Create a request to load/compile front-end code.</summary>
    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int createCompileRequest([NativeTypeName("SlangCompileRequest **")] ICompileRequest** outCompileRequest)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, ICompileRequest**, int>)(lpVtbl[14]))((ISession*)Unsafe.AsPointer(ref this), outCompileRequest);
    }

    /// <summary>Creates a <c>IComponentType</c> that represents a type's conformance to an interface. The retrieved <c>ITypeConformance</c> objects can be included in a composite <c>IComponentType</c> to explicitly specify which implementation types should be included in the final compiled code. For example, if an module defines <c>IMaterial</c> interface and <c>AMaterial</c>, <c>BMaterial</c>, <c>CMaterial</c> types that implements the interface, the user can exclude <c>CMaterial</c> implementation from the resulting shader code by explicitly adding <c>AMaterial:IMaterial</c> and <c>BMaterial:IMaterial</c> conformances to a composite <c>IComponentType</c> and get entry point code from it. The resulting code will not have anything related to <c>CMaterial</c> in the dynamic dispatch logic. If the user does not explicitly include any <c>TypeConformances</c> to an interface type, all implementations to that interface will be included by default. By linking a <c>ITypeConformance</c>, the user is also given the opportunity to specify the dispatch ID of the implementation type. If <c>conformanceIdOverride</c> is -1, there will be no override behavior and Slang will automatically assign IDs to implementation types. The automatically assigned IDs can be queried via <c>ISession::getTypeConformanceWitnessSequentialID</c>.</summary>
    /// <remarks>
    /// <para>Returns SLANG_OK if succeeds, or SLANG_FAIL if <c>type</c> does not conform to <c>interfaceType</c>.</para>
    /// </remarks>
    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int createTypeConformanceComponentType([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, ITypeConformance** outConformance, [NativeTypeName("SlangInt")] long conformanceIdOverride, ISlangBlob** outDiagnostics)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, ITypeConformance**, long, ISlangBlob**, int>)(lpVtbl[15]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outConformance, conformanceIdOverride, outDiagnostics);
    }

    /// <summary>Load a module from a Slang module blob.</summary>
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

    /// <summary>Checks if a precompiled binary module is up-to-date with the current compiler option settings and the source file contents.</summary>
    /// <remarks>
    /// <para>When the module's primary source file cannot be located on the search paths, the binary module is treated as a standalone artifact and reported as up-to-date so that callers distributing precompiled-only modules can load them. In that case the compiler-version and option-set hash carried in the binary are NOT compared. If a later (secondary) dependency is missing the module is still reported as stale.</para>
    /// </remarks>
    [VtblIndex(19)]
    public bool isBinaryModuleUpToDate([NativeTypeName("const char *")] sbyte* modulePath, [NativeTypeName("slang::IBlob *")] ISlangBlob* binaryModuleBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, ISlangBlob*, byte>)(lpVtbl[19]))((ISession*)Unsafe.AsPointer(ref this), modulePath, binaryModuleBlob) != 0;
    }

    /// <summary>Load a module from a string.</summary>
    [VtblIndex(20)]
    [return: NativeTypeName("slang::IModule *")]
    public IModule* loadModuleFromSourceString([NativeTypeName("const char *")] sbyte* moduleName, [NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const char *")] sbyte* @string, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, sbyte*, sbyte*, sbyte*, ISlangBlob**, IModule*>)(lpVtbl[20]))((ISession*)Unsafe.AsPointer(ref this), moduleName, path, @string, outDiagnostics);
    }

    /// <summary>Get the 16-byte RTTI header to fill into a dynamic object. This header is used to identify the type of the object for dynamic dispatch purpose. For example, given the following shader:</summary>
    /// <remarks>
    /// <para>```slang [anyValueSize(32)] dyn interface IFoo { int eval(); } struct Impl : IFoo { int eval() { return 1; } }</para>
    /// <para>ConstantBuffer&lt;dyn IFoo&gt; cb0;</para>
    /// <para>[numthreads(1,1,1) void main() { cb0.eval(); } ```</para>
    /// <para>The constant buffer <c>cb0</c> should be filled with 16+32=48 bytes of data, where the first 16 bytes should be the RTTI bytes returned by calling <c>getDynamicObjectRTTIBytes(type_Impl, type_IFoo)</c>, and the rest 32 bytes should hold the actual data of the dynamic object (in this case, fields in the <c>Impl</c> type).</para>
    /// <para><c>bufferSizeInBytes</c> must be greater than 16.</para>
    /// </remarks>
    [VtblIndex(21)]
    [return: NativeTypeName("SlangResult")]
    public int getDynamicObjectRTTIBytes([NativeTypeName("slang::TypeReflection *")] TypeReflection* type, [NativeTypeName("slang::TypeReflection *")] TypeReflection* interfaceType, [NativeTypeName("uint32_t *")] uint* outRTTIDataBuffer, [NativeTypeName("uint32_t")] uint bufferSizeInBytes)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, TypeReflection*, TypeReflection*, uint*, uint, int>)(lpVtbl[21]))((ISession*)Unsafe.AsPointer(ref this), type, interfaceType, outRTTIDataBuffer, bufferSizeInBytes);
    }

    /// <summary>Read module info (name and version) from a module blob</summary>
    /// <remarks>
    /// <para>The returned pointers are valid for as long as the session.</para>
    /// </remarks>
    [VtblIndex(22)]
    [return: NativeTypeName("SlangResult")]
    public int loadModuleInfoFromIRBlob([NativeTypeName("slang::IBlob *")] ISlangBlob* source, [NativeTypeName("SlangInt &")] long* outModuleVersion, [NativeTypeName("const char *&")] sbyte** outModuleCompilerVersion, [NativeTypeName("const char *&")] sbyte** outModuleName)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, ISlangBlob*, long*, sbyte**, sbyte**, int>)(lpVtbl[22]))((ISession*)Unsafe.AsPointer(ref this), source, outModuleVersion, outModuleCompilerVersion, outModuleName);
    }

    /// <summary>Get the source location of a declaration.</summary>
    /// <remarks>
    /// <para>The returned filePath pointer is valid for as long as the session.</para>
    /// </remarks>
    [VtblIndex(23)]
    [return: NativeTypeName("SlangResult")]
    public int getDeclSourceLocation([NativeTypeName("slang::DeclReflection *")] DeclReflection* decl, [NativeTypeName("slang::SourceLocation *")] SourceLocation* outLocation)
    {
        return ((delegate* unmanaged[Stdcall]<ISession*, DeclReflection*, SourceLocation*, int>)(lpVtbl[23]))((ISession*)Unsafe.AsPointer(ref this), decl, outLocation);
    }
}
