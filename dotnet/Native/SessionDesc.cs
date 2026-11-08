namespace SharpSlang.Native;

public unsafe partial struct SessionDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    [NativeTypeName("size_t")]
    public nuint structureSize;

    /// <summary>Code generation targets to include in the session.</summary>
    [NativeTypeName("const TargetDesc *")]
    public TargetDesc* targets;

    [NativeTypeName("SlangInt")]
    public long targetCount;

    /// <summary>Flags to configure the session.</summary>
    [NativeTypeName("slang::SessionFlags")]
    public uint flags;

    /// <summary>Default layout to assume for variables with matrix types.</summary>
    public SlangMatrixLayoutMode defaultMatrixLayoutMode;

    /// <summary>Paths to use when searching for <c>#include</c>d or <c>import</c>ed files.</summary>
    [NativeTypeName("const char *const *")]
    public sbyte** searchPaths;

    [NativeTypeName("SlangInt")]
    public long searchPathCount;

    [NativeTypeName("const PreprocessorMacroDesc *")]
    public PreprocessorMacroDesc* preprocessorMacros;

    [NativeTypeName("SlangInt")]
    public long preprocessorMacroCount;

    public ISlangFileSystem* fileSystem;

    [NativeTypeName("bool")]
    public byte enableEffectAnnotations;

    [NativeTypeName("bool")]
    public byte allowGLSLSyntax;

    /// <summary>Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount.</summary>
    [NativeTypeName("const CompilerOptionEntry *")]
    public CompilerOptionEntry* compilerOptionEntries;

    /// <summary>Number of additional compiler option entries.</summary>
    [NativeTypeName("uint32_t")]
    public uint compilerOptionEntryCount;

    /// <summary>Whether to skip SPIRV validation.</summary>
    [NativeTypeName("bool")]
    public byte skipSPIRVValidation;
}
