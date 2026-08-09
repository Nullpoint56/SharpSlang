namespace PrismSlang.Native;

public partial struct CompileCoreModuleFlag
{

    [NativeTypeName("slang::CompileCoreModuleFlags")]
    public enum Enum : uint
    {
        WriteDocumentation = 0x1,
    }
}
