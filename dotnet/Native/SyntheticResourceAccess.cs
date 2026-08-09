namespace PrismSlang.Native;

[NativeTypeName("uint32_t")]
public enum SyntheticResourceAccess : uint
{
    Read = 0,
    Write = 1,
    ReadWrite = 2,
}
