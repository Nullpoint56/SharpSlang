namespace SharpSlang.Native;

public unsafe partial struct PreprocessorMacroDesc
{
    [NativeTypeName("const char *")]
    public sbyte* name;

    [NativeTypeName("const char *")]
    public sbyte* value;
}
