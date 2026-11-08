namespace SharpSlang.Native;

public unsafe partial struct SyntheticResourceInfo
{
    [NativeTypeName("size_t")]
    public nuint structSize;

    /// <summary>Stable, opaque, non-zero synthetic resource identifier within</summary>
    /// <remarks>
    /// <para>the compiled program. <c>0</c> is reserved as the default</para>
    /// <para>"unassigned" sentinel; <c>findResourceIndexByID(0, ...)</c> always</para>
    /// <para>returns <c>SLANG_E_NOT_FOUND</c>. Non-zero ids are assigned from the</para>
    /// <para>compiler's internal synthetic-resource registry; hosts should</para>
    /// <para>treat the numeric value as opaque and use it only to correlate</para>
    /// <para>metadata and runtime binding helpers.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint id;

    /// <summary>The Slang binding kind represented by this synthetic</summary>
    /// <remarks>
    /// <para>resource.</para>
    /// </remarks>
    [NativeTypeName("slang::BindingType")]
    public BindingType bindingType;

    /// <summary>Number of logical resources in the synthetic binding. Most</summary>
    /// <remarks>
    /// <para>instrumentation resources are scalar (<c>1</c>).</para>
    /// <para><c>kUnboundedSyntheticResourceArraySize</c> when the resource is an</para>
    /// <para>unbounded descriptor array whose descriptor count is a host</para>
    /// <para>runtime decision the compiler cannot see -- the coverage buffer</para>
    /// <para>under <c>-trace-coverage-bindless-index</c> is declared this way</para>
    /// <para>deliberately, so that a shader does not constrain how many</para>
    /// <para>shaders the host binds alongside it. A host sizing a descriptor</para>
    /// <para>array must not read this as a count.</para>
    /// <para>For coverage, this sentinel and <c>bindlessIndex &gt;= 0</c> are set</para>
    /// <para>together and always agree, so testing either one identifies the</para>
    /// <para>bindless form. They are separate fields because they answer</para>
    /// <para>different questions -- <c>arraySize</c> describes the binding's shape</para>
    /// <para>for any synthetic resource, while <c>bindlessIndex</c> says which</para>
    /// <para>element this shader uses -- and a future synthetic resource</para>
    /// <para>could be an unbounded array without having a per-shader index.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint arraySize;

    /// <summary>Whether the resource is global/root-scoped or attached to a</summary>
    /// <remarks>
    /// <para>specific entry point. Coverage currently reports a global</para>
    /// <para>resource; entry-point scoped resources are reserved for future</para>
    /// <para>synthetic-resource producers.</para>
    /// </remarks>
    [NativeTypeName("slang::SyntheticResourceScope")]
    public SyntheticResourceScope scope;

    /// <summary>Intended access pattern for the resource.</summary>
    [NativeTypeName("slang::SyntheticResourceAccess")]
    public SyntheticResourceAccess access;

    /// <summary>Entry point index when <c>scope == EntryPoint</c>, else <c>-1</c>.</summary>
    /// <remarks>
    /// <para>No current coverage resource uses entry-point scope.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int entryPointIndex;

    /// <summary>Sentinel conventions:</summary>
    /// <remarks>
    /// <para>- <c>space == -1</c> means the target does not report a descriptor</para>
    /// <para>space for this resource</para>
    /// <para>- <c>binding == -1</c> means descriptor binding is unavailable for</para>
    /// <para>this target</para>
    /// <para>- <c>uniformOffset == -1</c> means CPU/CUDA-style marshaling</para>
    /// <para>location is unavailable for this target</para>
    /// <para>- <c>uniformStride == 0</c> means CPU/CUDA-style marshaling</para>
    /// <para>location is unavailable or no stride applies; when</para>
    /// <para><c>uniformOffset &gt;= 0</c>, a non-zero stride may be reported</para>
    /// <para>even for scalar resources</para>
    /// <para><c>0</c> is a valid value for <c>space</c>, <c>binding</c>, and</para>
    /// <para><c>uniformOffset</c>.</para>
    /// <para>Descriptor-facing location for backends that bind synthetic</para>
    /// <para>resources via <c>(space, binding)</c>.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int space;

    [NativeTypeName("int32_t")]
    public int binding;

    /// <summary>CPU/CUDA-style marshaling location in generated uniform /</summary>
    /// <remarks>
    /// <para>wrapper parameter data, in bytes.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int uniformOffset;

    /// <summary>Byte stride between adjacent logical elements when</summary>
    /// <remarks>
    /// <para>CPU/CUDA-style marshaling is reported.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int uniformStride;

    /// <summary>Optional stable debug name for the synthetic resource. The</summary>
    /// <remarks>
    /// <para>returned pointer is valid for the lifetime of the metadata</para>
    /// <para>object.</para>
    /// </remarks>
    [NativeTypeName("const char *")]
    public sbyte* debugName;

    /// <summary>Index of this shader's element within the synthetic resource's</summary>
    /// <remarks>
    /// <para>descriptor array, or <c>-1</c> when the resource is bound as a single</para>
    /// <para>descriptor rather than as an element of an array.</para>
    /// <para>Coverage sets this from <c>-trace-coverage-bindless-index</c>. In that</para>
    /// <para>form <c>__slang_coverage</c> is an unbounded array of buffers, so a</para>
    /// <para>single <c>(space, binding)</c> serves every shader in a pipeline and</para>
    /// <para>each shader accesses <c>__slang_coverage[bindlessIndex]</c>. Reading it</para>
    /// <para>back here saves a host from having to track the value it passed at</para>
    /// <para>compile time in order to report on the result.</para>
    /// <para>This field is past the v1 struct size, so it is only written when</para>
    /// <para>the caller's <c>structSize</c> covers it. A caller compiled against an</para>
    /// <para>older header keeps its own struct layout and never sees it.</para>
    /// </remarks>
    [NativeTypeName("int32_t")]
    public int bindlessIndex;
}
