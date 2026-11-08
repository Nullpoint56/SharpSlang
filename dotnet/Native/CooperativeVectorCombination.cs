namespace SharpSlang.Native;

public partial struct CooperativeVectorCombination
{
    public SlangScalarType inputType;

    public SlangScalarType inputInterpretation;

    /// <summary>Number of logical elements packed into each physical input element.</summary>
    /// <remarks>
    /// <para>For example, this is 4 when four int8 values are packed into one uint32 input element.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint inputPackingFactor;

    public SlangScalarType matrixInterpretation;

    /// <summary><c>NONE</c> means the operation has no bias operand/matrix.</summary>
    public SlangScalarType biasInterpretation;

    public SlangScalarType resultType;

    [NativeTypeName("SlangBool")]
    public byte transpose;
}
