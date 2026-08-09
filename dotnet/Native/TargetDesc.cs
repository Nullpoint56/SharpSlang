namespace PrismSlang.Native;

public unsafe partial struct TargetDesc
{
    [NativeTypeName("size_t")]
    public nuint structureSize;

    public SlangCompileTarget format;

    public SlangProfileID profile;

    [NativeTypeName("SlangTargetFlags")]
    public uint flags;

    public SlangFloatingPointMode floatingPointMode;

    public SlangLineDirectiveMode lineDirectiveMode;

    [NativeTypeName("bool")]
    public byte forceGLSLScalarBufferLayout;

    [NativeTypeName("const CompilerOptionEntry *")]
    public CompilerOptionEntry* compilerOptionEntries;

    [NativeTypeName("uint32_t")]
    public uint compilerOptionEntryCount;
}
