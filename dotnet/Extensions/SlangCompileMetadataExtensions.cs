// L3 — the optional interfaces behind IMetadata's castAs besides IBindlessResourceMetadata:
// slang::ICooperativeTypesMetadata, slang::ISyntheticResourceMetadata and
// slang::ICoverageTracingMetadata.
//
// Each borrows its interface for the metadata's lifetime and checks the metadata on every call, as
// SlangBindlessResourceMetadataExtension does. Records that carry Slang's strings are copied out,
// because Slang's pointers live only as long as the metadata.

using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang;

/// <summary>
/// <c>slang::ICooperativeTypesMetadata</c>: the cooperative matrix and vector types, and the
/// combinations of them, the code generated for a target uses, to compare with what a device
/// reports (Vulkan's cooperative matrix and vector property queries).
/// </summary>
/// <remarks>
/// Collected after the target's lowering (<c>slang.h</c>): a target that lowers cooperative types
/// to ordinary arrays reports empty lists.
/// </remarks>
public sealed unsafe class SlangCooperativeTypesMetadataExtension
{
    private readonly SlangMetadata _metadata;
    private readonly ICooperativeTypesMetadata* _p;

    private SlangCooperativeTypesMetadataExtension(SlangMetadata metadata, ICooperativeTypesMetadata* p)
    {
        _metadata = metadata;
        _p = p;
    }

    internal static SlangCooperativeTypesMetadataExtension? TryAcquire(SlangMetadata metadata, IMetadata* native)
    {
        var iid = SlangIID.ICooperativeTypesMetadata;
        var found = native->castAs((SlangUUID*)&iid);
        return found is null ? null : new SlangCooperativeTypesMetadataExtension(metadata, (ICooperativeTypesMetadata*)found);
    }

    private ICooperativeTypesMetadata* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_metadata.IsDisposed, "owner of SlangCooperativeTypesMetadataExtension");
            return _p;
        }
    }

    /// <summary>The interface pointer, borrowed from the metadata. Last resort (AGENTS.md#layers).</summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary><c>getCooperativeMatrixTypeCount</c> and <c>getCooperativeMatrixTypeByIndex</c>.</summary>
    public CooperativeMatrixType[] CooperativeMatrixTypes
    {
        get
        {
            var result = new CooperativeMatrixType[checked((int)Ptr->getCooperativeMatrixTypeCount())];
            for (var i = 0; i < result.Length; i++)
            {
                var item = Defaults.CooperativeMatrixType();
                Diagnostics.ThrowIfFailed(Ptr->getCooperativeMatrixTypeByIndex((ulong)i, &item), "ICooperativeTypesMetadata::getCooperativeMatrixTypeByIndex", null);
                result[i] = item;
            }
            return result;
        }
    }

    /// <summary><c>getCooperativeMatrixCombinationCount</c> and <c>getCooperativeMatrixCombinationByIndex</c>.</summary>
    public CooperativeMatrixCombination[] CooperativeMatrixCombinations
    {
        get
        {
            var result = new CooperativeMatrixCombination[checked((int)Ptr->getCooperativeMatrixCombinationCount())];
            for (var i = 0; i < result.Length; i++)
            {
                var item = Defaults.CooperativeMatrixCombination();
                Diagnostics.ThrowIfFailed(Ptr->getCooperativeMatrixCombinationByIndex((ulong)i, &item), "ICooperativeTypesMetadata::getCooperativeMatrixCombinationByIndex", null);
                result[i] = item;
            }
            return result;
        }
    }

    /// <summary><c>getCooperativeVectorTypeCount</c> and <c>getCooperativeVectorTypeByIndex</c>.</summary>
    public CooperativeVectorTypeUsageInfo[] CooperativeVectorTypes
    {
        get
        {
            var result = new CooperativeVectorTypeUsageInfo[checked((int)Ptr->getCooperativeVectorTypeCount())];
            for (var i = 0; i < result.Length; i++)
            {
                var item = Defaults.CooperativeVectorTypeUsageInfo();
                Diagnostics.ThrowIfFailed(Ptr->getCooperativeVectorTypeByIndex((ulong)i, &item), "ICooperativeTypesMetadata::getCooperativeVectorTypeByIndex", null);
                result[i] = item;
            }
            return result;
        }
    }

    /// <summary><c>getCooperativeVectorCombinationCount</c> and <c>getCooperativeVectorCombinationByIndex</c>.</summary>
    public CooperativeVectorCombination[] CooperativeVectorCombinations
    {
        get
        {
            var result = new CooperativeVectorCombination[checked((int)Ptr->getCooperativeVectorCombinationCount())];
            for (var i = 0; i < result.Length; i++)
            {
                var item = Defaults.CooperativeVectorCombination();
                Diagnostics.ThrowIfFailed(Ptr->getCooperativeVectorCombinationByIndex((ulong)i, &item), "ICooperativeTypesMetadata::getCooperativeVectorCombinationByIndex", null);
                result[i] = item;
            }
            return result;
        }
    }
}

/// <summary>A resource Slang added to the program itself (<c>slang::SyntheticResourceInfo</c>).</summary>
/// <param name="Id">Slang's opaque, non-zero id for it.</param>
/// <param name="BindingType">What kind of binding it is.</param>
/// <param name="ArraySize">How many resources; <see cref="IsUnbounded"/> when the host decides.</param>
/// <param name="Scope">Global, or one entry point's.</param>
/// <param name="Access">How the shader uses it.</param>
/// <param name="EntryPointIndex">The entry point when scoped to one, else -1.</param>
/// <param name="Space">Register space or descriptor set, or -1 when the target reports none.</param>
/// <param name="Binding">Binding index, or -1 when the target reports none.</param>
/// <param name="UniformOffset">Byte offset in the uniform data for CPU and CUDA targets, or -1.</param>
/// <param name="UniformStride">Byte stride between elements there, or 0.</param>
/// <param name="DebugName">Slang's name for it, or null.</param>
/// <param name="BindlessIndex">This shader's element of an unbounded array, or -1.</param>
public sealed record SlangSyntheticResource(
    uint Id,
    BindingType BindingType,
    uint ArraySize,
    SyntheticResourceScope Scope,
    SyntheticResourceAccess Access,
    int EntryPointIndex,
    int Space,
    int Binding,
    int UniformOffset,
    int UniformStride,
    string? DebugName,
    int BindlessIndex)
{
    /// <summary>The array's length is the host's choice (<c>kUnboundedSyntheticResourceArraySize</c>).</summary>
    public bool IsUnbounded => ArraySize == uint.MaxValue;
}

/// <summary>
/// <c>slang::ISyntheticResourceMetadata</c>: resources Slang added to the program itself, such as
/// the coverage counter buffer, which reflection does not report but the host must bind.
/// </summary>
public sealed unsafe class SlangSyntheticResourceMetadataExtension
{
    private readonly SlangMetadata _metadata;
    private readonly ISyntheticResourceMetadata* _p;

    private SlangSyntheticResourceMetadataExtension(SlangMetadata metadata, ISyntheticResourceMetadata* p)
    {
        _metadata = metadata;
        _p = p;
    }

    internal static SlangSyntheticResourceMetadataExtension? TryAcquire(SlangMetadata metadata, IMetadata* native)
    {
        var iid = SlangIID.ISyntheticResourceMetadata;
        var found = native->castAs((SlangUUID*)&iid);
        return found is null ? null : new SlangSyntheticResourceMetadataExtension(metadata, (ISyntheticResourceMetadata*)found);
    }

    private ISyntheticResourceMetadata* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_metadata.IsDisposed, "owner of SlangSyntheticResourceMetadataExtension");
            return _p;
        }
    }

    /// <summary>The interface pointer, borrowed from the metadata. Last resort (AGENTS.md#layers).</summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary><c>getResourceCount</c>.</summary>
    public int ResourceCount => checked((int)Ptr->getResourceCount());

    /// <summary><c>getResourceInfo</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No resource at <paramref name="index"/>.</exception>
    public SlangSyntheticResource GetResource(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ResourceCount);

        var info = Defaults.SyntheticResourceInfo();
        Diagnostics.ThrowIfFailed(Ptr->getResourceInfo((uint)index, &info), "ISyntheticResourceMetadata::getResourceInfo", null);
        return new SlangSyntheticResource(
            info.id, info.bindingType, info.arraySize, info.scope, info.access, info.entryPointIndex,
            info.space, info.binding, info.uniformOffset, info.uniformStride, Utf8.Read(info.debugName), info.bindlessIndex);
    }

    /// <summary><c>findResourceIndexByID</c>: the index of the resource with Slang's <paramref name="id"/>, or null.</summary>
    public int? FindResourceIndexById(uint id)
    {
        uint index;
        var hr = Ptr->findResourceIndexByID(id, &index);
        if (hr == SlangResult.NotFound) return null;
        Diagnostics.ThrowIfFailed(hr, "ISyntheticResourceMetadata::findResourceIndexByID", null);
        return (int)index;
    }
}

/// <summary>One source location coverage tracing counts (<c>slang::CoverageEntryInfo</c>).</summary>
/// <param name="File">The source file, or null when Slang could not attribute it.</param>
/// <param name="Line">1-based line, or 0.</param>
/// <param name="CounterIndex">Its counter in the coverage buffer, shared by entries that run together; null for none.</param>
/// <param name="Kind">Line, function or branch.</param>
/// <param name="CounterMode">How the counter accumulates.</param>
/// <param name="StartColumn">1-based start column, or 0.</param>
/// <param name="EndLine">1-based end line, or 0.</param>
/// <param name="EndColumn">1-based exclusive end column, or 0.</param>
/// <param name="FunctionName">The function, for a function entry.</param>
/// <param name="FunctionMangledName">Its mangled name, for a function entry.</param>
/// <param name="BranchSiteId">The branch site, for a branch entry, else 0.</param>
/// <param name="BranchArmId">The arm within it, else 0.</param>
/// <param name="BranchArmKind">Which arm.</param>
public sealed record SlangCoverageEntry(
    string? File,
    uint Line,
    uint? CounterIndex,
    CoverageEntryKind Kind,
    CoverageCounterMode CounterMode,
    uint StartColumn,
    uint EndLine,
    uint EndColumn,
    string? FunctionName,
    string? FunctionMangledName,
    uint BranchSiteId,
    uint BranchArmId,
    CoverageBranchArmKind BranchArmKind);

/// <summary>Where the coverage counter buffer is bound (<c>slang::CoverageBufferInfo</c>).</summary>
/// <param name="Space">Register space or descriptor set, or -1 when the target reports none.</param>
/// <param name="Binding">Binding index, or -1 when the target reports none.</param>
/// <param name="ElementByteWidth">Bytes per counter: 4 or 8.</param>
public readonly record struct SlangCoverageBuffer(int Space, int Binding, uint ElementByteWidth);

/// <summary>
/// <c>slang::ICoverageTracingMetadata</c>: what a shader compiled with coverage tracing
/// (<c>-trace-coverage</c>) counts, and where its counter buffer goes.
/// </summary>
/// <remarks>
/// Read the counters back as <see cref="CounterCount"/> slots of
/// <see cref="SlangCoverageBuffer.ElementByteWidth"/> bytes, then attribute them per entry
/// (<c>counters[entry.CounterIndex]</c>): several entries share a counter (<c>slang.h</c>).
/// </remarks>
public sealed unsafe class SlangCoverageTracingMetadataExtension
{
    private readonly SlangMetadata _metadata;
    private readonly ICoverageTracingMetadata* _p;

    private SlangCoverageTracingMetadataExtension(SlangMetadata metadata, ICoverageTracingMetadata* p)
    {
        _metadata = metadata;
        _p = p;
    }

    internal static SlangCoverageTracingMetadataExtension? TryAcquire(SlangMetadata metadata, IMetadata* native)
    {
        var iid = SlangIID.ICoverageTracingMetadata;
        var found = native->castAs((SlangUUID*)&iid);
        return found is null ? null : new SlangCoverageTracingMetadataExtension(metadata, (ICoverageTracingMetadata*)found);
    }

    private ICoverageTracingMetadata* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_metadata.IsDisposed, "owner of SlangCoverageTracingMetadataExtension");
            return _p;
        }
    }

    /// <summary>The interface pointer, borrowed from the metadata. Last resort (AGENTS.md#layers).</summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary><c>getCounterCount</c>: counter slots in the buffer; size the readback from this.</summary>
    public int CounterCount => checked((int)Ptr->getCounterCount());

    /// <summary><c>getEntryCount</c>: source entries, at least <see cref="CounterCount"/>.</summary>
    public int EntryCount => checked((int)Ptr->getEntryCount());

    /// <summary><c>getEntryInfo</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No entry at <paramref name="index"/>.</exception>
    public SlangCoverageEntry GetEntry(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, EntryCount);

        var e = Defaults.CoverageEntryInfo();
        Diagnostics.ThrowIfFailed(Ptr->getEntryInfo((uint)index, &e), "ICoverageTracingMetadata::getEntryInfo", null);
        return new SlangCoverageEntry(
            Utf8.Read(e.file), e.line, e.counterIndex == SlangApi.kInvalidCoverageCounterIndex ? null : e.counterIndex,
            e.kind, e.counterMode, e.startColumn, e.endLine, e.endColumn,
            Utf8.Read(e.functionName), Utf8.Read(e.functionMangledName), e.branchSiteID, e.branchArmID, e.branchArmKind);
    }

    /// <summary><c>getBufferInfo</c>.</summary>
    public SlangCoverageBuffer Buffer
    {
        get
        {
            var info = Defaults.CoverageBufferInfo();
            Diagnostics.ThrowIfFailed(Ptr->getBufferInfo(&info), "ICoverageTracingMetadata::getBufferInfo", null);
            return new SlangCoverageBuffer(info.space, info.binding, info.elementByteWidth);
        }
    }

    /// <summary>
    /// <c>slang_writeCoverageManifestJson</c>: the entries and the buffer as Slang's JSON manifest,
    /// the sidecar its coverage tools read.
    /// </summary>
    public string ToManifestJson()
    {
        ISlangBlob* blob = null;
        var hr = SlangApi.slang_writeCoverageManifestJson(Ptr, &blob);
        if (SlangResult.Failed(hr) || blob is null)
        {
            if (blob is not null) blob->release();
            throw new SlangException(SlangResult.Failed(hr) ? hr : SlangResult.Fail, "slang_writeCoverageManifestJson", Diagnostics.NewInternalError());
        }

        using var owned = new SlangBlob(blob);
        return owned.AsText();
    }
}
