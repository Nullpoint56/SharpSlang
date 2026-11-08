using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ICoverageTracingMetadata : ISlangCastable")]
public unsafe partial struct ICoverageTracingMetadata
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[1]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[2]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Number of runtime counter slots in the synthesized coverage</summary>
    /// <remarks>
    /// <para>buffer. Never larger than <c>getEntryCount()</c>, and smaller when</para>
    /// <para>line coverage shares one counter across the source entries of a</para>
    /// <para>straight-line region. Function and branch entries always take a</para>
    /// <para>dedicated counter, so the two counts are equal for a compile that</para>
    /// <para>enables only those modes, or for line coverage where every marker</para>
    /// <para>lands in its own basic block. Size the counter readback buffer</para>
    /// <para>from this value, never from the entry count.</para>
    /// </remarks>
    [VtblIndex(4)]
    [return: NativeTypeName("uint32_t")]
    public uint getCounterCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[4]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Populate <c>outInfo</c> with attribution info for source coverage</summary>
    /// <remarks>
    /// <para>entry <c>index</c>. The valid range is <c>[0, getEntryCount())</c>. The</para>
    /// <para>caller must pre-set <c>outInfo-&gt;structSize</c> to the size of the</para>
    /// <para><c>CoverageEntryInfo</c> definition it was compiled against. The</para>
    /// <para>implementation accepts any prefix-compatible size at or above</para>
    /// <para>the v1 minimum (<c>file</c> and <c>line</c>), and only writes tail fields</para>
    /// <para>that fit in the caller-provided struct. Returns <c>SLANG_OK</c> on</para>
    /// <para>success, <c>SLANG_E_INVALID_ARG</c> for null <c>outInfo</c>, too-small</para>
    /// <para><c>structSize</c>, or out-of-range <c>index</c>.</para>
    /// </remarks>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryInfo([NativeTypeName("uint32_t")] uint index, [NativeTypeName("slang::CoverageEntryInfo *")] CoverageEntryInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint, CoverageEntryInfo*, int>)(lpVtbl[5]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), index, outInfo);
    }

    /// <summary>Populate <c>outInfo</c> with the coverage buffer's descriptor</summary>
    /// <remarks>
    /// <para>binding info. The caller should default-initialize <c>outInfo</c></para>
    /// <para>(for example <c>CoverageBufferInfo info;</c>) so <c>structSize</c> is</para>
    /// <para>set to <c>sizeof(CoverageBufferInfo)</c>.</para>
    /// <para>Returns <c>SLANG_OK</c> on success, <c>SLANG_E_INVALID_ARG</c> for null</para>
    /// <para><c>outInfo</c> or mismatched <c>structSize</c>.</para>
    /// <para>This is the coverage-specific compatibility view of the hidden</para>
    /// <para>buffer. Hosts that want the generic hidden-resource contract</para>
    /// <para>should query <c>ISyntheticResourceMetadata</c> from the same</para>
    /// <para><c>IMetadata</c> object.</para>
    /// </remarks>
    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int getBufferInfo([NativeTypeName("slang::CoverageBufferInfo *")] CoverageBufferInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, CoverageBufferInfo*, int>)(lpVtbl[6]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this), outInfo);
    }

    /// <summary>Number of source coverage entries available through</summary>
    /// <remarks>
    /// <para><c>getEntryInfo</c>. Every coverage marker produces one entry, so</para>
    /// <para>this is NOT the number of runtime counters: several entries may</para>
    /// <para>name the same <c>counterIndex</c>. Use <c>getCounterCount()</c> to size</para>
    /// <para>the readback buffer.</para>
    /// </remarks>
    [VtblIndex(7)]
    [return: NativeTypeName("uint32_t")]
    public uint getEntryCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICoverageTracingMetadata*, uint>)(lpVtbl[7]))((ICoverageTracingMetadata*)Unsafe.AsPointer(ref this));
    }
}
