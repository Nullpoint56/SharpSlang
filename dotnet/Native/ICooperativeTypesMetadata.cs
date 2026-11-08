using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>Cooperative matrix and vector metadata.</summary>
/// <remarks>
/// <para>This interface reports the cooperative matrix/vector type information that a compiled target uses, including cooperative matrix types, cooperative vector type-usage records, and certain type combinations required to execute some operations (like matrix multiplication).</para>
/// <para>Applications can use this metadata to compare shader requirements against the capabilities exposed by the target API/driver (for example Vulkan cooperative matrix/vector property queries, or analogous APIs on other backends).</para>
/// <para>Metadata is collected from the IR after target-specific lowering, so it only reflects cooperative types that survive as native constructs in the final output. Targets that lower cooperative types into ordinary arrays will report empty lists.</para>
/// <para>Lists are exposed using <c>get*Count()</c> plus <c>get*ByIndex()</c> methods, where the count returns the number of elements currently available and valid indices are in the range <c>[0, count)</c>.</para>
/// <para>Cast from an <c>IMetadata*</c> using <c>castAs()</c>.</para>
/// </remarks>
[NativeTypeName("struct ICooperativeTypesMetadata : ISlangCastable")]
public unsafe partial struct ICooperativeTypesMetadata
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, uint>)(lpVtbl[1]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, uint>)(lpVtbl[2]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangUInt")]
    public ulong getCooperativeMatrixTypeCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong>)(lpVtbl[4]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getCooperativeMatrixTypeByIndex([NativeTypeName("SlangUInt")] ulong index, [NativeTypeName("slang::CooperativeMatrixType *")] CooperativeMatrixType* outType)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong, CooperativeMatrixType*, int>)(lpVtbl[5]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), index, outType);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangUInt")]
    public ulong getCooperativeMatrixCombinationCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong>)(lpVtbl[6]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getCooperativeMatrixCombinationByIndex([NativeTypeName("SlangUInt")] ulong index, [NativeTypeName("slang::CooperativeMatrixCombination *")] CooperativeMatrixCombination* outCombination)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong, CooperativeMatrixCombination*, int>)(lpVtbl[7]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), index, outCombination);
    }

    [VtblIndex(8)]
    [return: NativeTypeName("SlangUInt")]
    public ulong getCooperativeVectorTypeCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong>)(lpVtbl[8]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(9)]
    [return: NativeTypeName("SlangResult")]
    public int getCooperativeVectorTypeByIndex([NativeTypeName("SlangUInt")] ulong index, [NativeTypeName("slang::CooperativeVectorTypeUsageInfo *")] CooperativeVectorTypeUsageInfo* outType)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong, CooperativeVectorTypeUsageInfo*, int>)(lpVtbl[9]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), index, outType);
    }

    [VtblIndex(10)]
    [return: NativeTypeName("SlangUInt")]
    public ulong getCooperativeVectorCombinationCount()
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong>)(lpVtbl[10]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(11)]
    [return: NativeTypeName("SlangResult")]
    public int getCooperativeVectorCombinationByIndex([NativeTypeName("SlangUInt")] ulong index, [NativeTypeName("slang::CooperativeVectorCombination *")] CooperativeVectorCombination* outCombination)
    {
        return ((delegate* unmanaged[Stdcall]<ICooperativeTypesMetadata*, ulong, CooperativeVectorCombination*, int>)(lpVtbl[11]))((ICooperativeTypesMetadata*)Unsafe.AsPointer(ref this), index, outCombination);
    }
}
