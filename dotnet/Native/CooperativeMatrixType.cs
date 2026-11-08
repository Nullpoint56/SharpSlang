namespace SharpSlang.Native;

public partial struct CooperativeMatrixType
{
    /// <summary>Component type <c>NONE</c> means this type is not valid.</summary>
    public SlangScalarType componentType;

    public SlangScope scope;

    [NativeTypeName("uint32_t")]
    public uint rowCount;

    [NativeTypeName("uint32_t")]
    public uint columnCount;

    public SlangCooperativeMatrixUse use;
}
