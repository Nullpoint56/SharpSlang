namespace SharpSlang.Native;

public unsafe partial struct SourceLocation
{
    [NativeTypeName("const char *")]
    public sbyte* filePath;

    [NativeTypeName("SlangInt")]
    public long line;

    [NativeTypeName("SlangInt")]
    public long column;
}
