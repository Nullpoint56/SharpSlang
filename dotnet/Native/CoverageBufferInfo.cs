namespace SharpSlang.Native;

/// <summary>Coverage-buffer descriptor binding info returned by</summary>
/// <remarks>
/// <para><c>ICoverageTracingMetadata::getBufferInfo</c>. This coverage-specific</para>
/// <para>view is kept for ABI compatibility. New host integrations should</para>
/// <para>query <c>ISyntheticResourceMetadata</c> when they need the full hidden-</para>
/// <para>resource binding contract, including CPU/CUDA marshaling fields.</para>
/// <para>Sentinel conventions:</para>
/// <para>- <c>space == -1</c> means no descriptor space is reported for the</para>
/// <para>current target</para>
/// <para>- <c>binding == -1</c> means no descriptor binding is reported for the</para>
/// <para>current target</para>
/// <para><c>0</c> is a valid value for both fields.</para>
/// </remarks>
public partial struct CoverageBufferInfo
{
    [NativeTypeName("size_t")]
    public nuint structSize;

    /// <summary>Register space the coverage buffer is bound to (D3D12</summary>
    /// <remarks>
    /// <para><c>space</c>, Vulkan descriptor set), or -1 if not assigned for</para>
    /// <para>this target.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int space;

    /// <summary>Binding index the coverage buffer is bound at (D3D12</summary>
    /// <remarks>
    /// <para><c>register</c>, Vulkan <c>binding</c>), or -1 if not assigned for</para>
    /// <para>this target.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int binding;

    /// <summary>Byte width of one counter slot in the synthesized buffer:</summary>
    /// <remarks>
    /// <para><c>4</c> for a <c>RWStructuredBuffer&lt;uint&gt;</c>, <c>8</c> for a</para>
    /// <para><c>RWStructuredBuffer&lt;uint64_t&gt;</c>. The host reads back</para>
    /// <para><c>getCounterCount() * elementByteWidth</c> bytes and interprets</para>
    /// <para>each slot as a little-endian unsigned integer of this width.</para>
    /// <para>Mirrored on the JSON sidecar as <c>buffer.element_stride</c>.</para>
    /// <para>A current in-process implementation always writes <c>4</c> or <c>8</c>;</para>
    /// <para>the in-class default <c>4</c> only appears if the caller forgot to</para>
    /// <para>pass the field to <c>getBufferInfo</c>. A sentinel <c>0</c> can only</para>
    /// <para>arise when reading a metadata object from an older compiler</para>
    /// <para>that pre-dates this field; both values should be treated as</para>
    /// <para>the historical uint32 layout.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint elementByteWidth;
}
