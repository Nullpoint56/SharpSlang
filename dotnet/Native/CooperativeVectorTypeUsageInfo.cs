namespace PrismSlang.Native;

public partial struct CooperativeVectorTypeUsageInfo
{
    public SlangScalarType componentType;

    [NativeTypeName("uint32_t")]
    public uint maxSize;

    [NativeTypeName("SlangBool")]
    public byte usedForTrainingOp;
}
