namespace SharpSlang.Native;

public partial struct CooperativeMatrixCombination
{
    /// <summary>Number of rows of matrix A and the result.</summary>
    [NativeTypeName("uint32_t")]
    public uint m;

    /// <summary>Number of columns of matrix B and the result.</summary>
    [NativeTypeName("uint32_t")]
    public uint n;

    /// <summary>Shared inner dimension: columns of A and rows of B.</summary>
    [NativeTypeName("uint32_t")]
    public uint k;

    public SlangScalarType componentTypeA;

    public SlangScalarType componentTypeB;

    public SlangScalarType componentTypeC;

    public SlangScalarType componentTypeResult;

    [NativeTypeName("SlangBool")]
    public byte saturate;

    public SlangScope scope;
}
