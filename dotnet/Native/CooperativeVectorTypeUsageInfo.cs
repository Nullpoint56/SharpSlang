namespace SharpSlang.Native;

public partial struct CooperativeVectorTypeUsageInfo
{
    public SlangScalarType componentType;

    /// <summary>Maximum element count used for this component type in cooperative</summary>
    /// <remarks>
    /// <para>operations (e.g. MatMul).</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint maxSize;

    /// <summary>Whether this component type is used as an accumulation/storage type for</summary>
    /// <remarks>
    /// <para>cooperative training operations (e.g. outer-product accumulation and</para>
    /// <para>reduce-sum accumulation). This flag is independent of <c>maxSize</c>.</para>
    /// </remarks>
    [NativeTypeName("SlangBool")]
    public byte usedForTrainingOp;
}
