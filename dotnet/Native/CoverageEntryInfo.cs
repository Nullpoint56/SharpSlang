namespace SharpSlang.Native;

/// <summary>Per-coverage-entry attribution returned by</summary>
/// <remarks>
/// <para><c>ICoverageTracingMetadata::getEntryInfo</c>. Use the leading</para>
/// <para><c>structSize</c> for ABI-versioned struct growth: future revisions</para>
/// <para>may add fields such as column/span information, function identity,</para>
/// <para>branch-arm identity, or coverage-mode-specific metadata at the end</para>
/// <para>without changing the COM interface. Entries are source-location</para>
/// <para>based: the current producers emit one source entry per marker op.</para>
/// <para>If multiple line entries resolve to the same <c>(file, line)</c>, LCOV</para>
/// <para>export aggregates them at report-generation time.</para>
/// </remarks>
public unsafe partial struct CoverageEntryInfo
{
    [NativeTypeName("size_t")]
    public nuint structSize;

    /// <summary>Source file for this coverage entry, or <c>nullptr</c> if the entry</summary>
    /// <remarks>
    /// <para>could not be attributed to a real source file. The returned</para>
    /// <para>pointer is valid for the lifetime of the metadata object.</para>
    /// </remarks>
    [NativeTypeName("const char *")]
    public sbyte* file;

    /// <summary>1-based source line for this coverage entry, or 0 if the entry</summary>
    /// <remarks>
    /// <para>could not be attributed to a real source line. The current</para>
    /// <para>implementation reports line, function, and branch entries;</para>
    /// <para>future revisions may add source-region entries or additional</para>
    /// <para>branch forms.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint line;

    /// <summary>Counter slot used by this entry, or</summary>
    /// <remarks>
    /// <para><c>kInvalidCoverageCounterIndex</c> when the entry has no runtime</para>
    /// <para>counter.</para>
    /// <para>This is NOT unique per entry. Line coverage coalesces entries</para>
    /// <para>that provably execute together (those in one basic block with</para>
    /// <para>nothing between them that can abandon the invocation) onto a</para>
    /// <para>single counter, which is what keeps instrumented shader code</para>
    /// <para>small. Several entries therefore report the same</para>
    /// <para><c>counterIndex</c>, and <c>getCounterCount()</c> is correspondingly</para>
    /// <para>smaller than the entry count.</para>
    /// <para>Read results per entry (<c>counters[entry.counterIndex]</c> for each</para>
    /// <para>entry), never per counter: a counter does not identify one</para>
    /// <para>source location. Sizing a readback buffer from the entry count</para>
    /// <para>rather than the counter count is a bug.</para>
    /// <para>Function and branch entries always use a dedicated counter.</para>
    /// <para>Future coverage modes may use <c>kInvalidCoverageCounterIndex</c></para>
    /// <para>for entries whose count is derived from other counters or</para>
    /// <para>represented through tail-extended fields.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint counterIndex;

    /// <summary>Semantic kind of this source coverage entry.</summary>
    [NativeTypeName("slang::CoverageEntryKind")]
    public CoverageEntryKind kind;

    /// <summary>Runtime accumulation mode for <c>counterIndex</c>. The current</summary>
    /// <remarks>
    /// <para>implementation only defines <c>Count</c>; future concrete modes can</para>
    /// <para>be appended when implemented.</para>
    /// </remarks>
    [NativeTypeName("slang::CoverageCounterMode")]
    public CoverageCounterMode counterMode;

    /// <summary>1-based inclusive start column for this entry, or 0 when</summary>
    /// <remarks>
    /// <para>unavailable.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint startColumn;

    /// <summary>1-based end line for this entry's half-open end coordinate, or</summary>
    /// <remarks>
    /// <para>0 when the exact range is unavailable. Future source-region</para>
    /// <para>coverage can use <c>(line,startColumn)</c> to <c>(endLine,endColumn)</c></para>
    /// <para>for a half-open source range.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint endLine;

    /// <summary>1-based exclusive end column for this entry, or 0 when</summary>
    /// <remarks>
    /// <para>unavailable.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint endColumn;

    /// <summary>Function display name for function coverage entries, or</summary>
    /// <remarks>
    /// <para><c>nullptr</c> when not applicable or unavailable.</para>
    /// </remarks>
    [NativeTypeName("const char *")]
    public sbyte* functionName;

    /// <summary>Stable mangled function name for function coverage entries, or</summary>
    /// <remarks>
    /// <para><c>nullptr</c> when not applicable or unavailable.</para>
    /// </remarks>
    [NativeTypeName("const char *")]
    public sbyte* functionMangledName;

    /// <summary>Stable branch-site identifier within this metadata object, or 0</summary>
    /// <remarks>
    /// <para>when not applicable.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint branchSiteID;

    /// <summary>Stable branch-arm identifier within <c>branchSiteID</c>, or 0 when</summary>
    /// <remarks>
    /// <para>not applicable.</para>
    /// </remarks>
    [NativeTypeName("uint32_t")]
    public uint branchArmID;

    /// <summary>Branch arm semantic for branch coverage entries.</summary>
    [NativeTypeName("slang::CoverageBranchArmKind")]
    public CoverageBranchArmKind branchArmKind;
}
