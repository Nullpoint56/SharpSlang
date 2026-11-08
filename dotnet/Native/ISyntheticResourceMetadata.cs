using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISyntheticResourceMetadata : ISlangCastable")]
public unsafe partial struct ISyntheticResourceMetadata
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[1]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[2]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Number of synthetic bindable resources reported by this</summary>
    /// <remarks>
    /// <para>metadata object.</para>
    /// </remarks>
    [VtblIndex(4)]
    [return: NativeTypeName("uint32_t")]
    public uint getResourceCount()
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint>)(lpVtbl[4]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Populate <c>outInfo</c> with the metadata for synthetic resource</summary>
    /// <remarks>
    /// <para><c>index</c>. The caller should default-initialize <c>outInfo</c> (for</para>
    /// <para>example <c>SyntheticResourceInfo info;</c>) so <c>structSize</c> is set</para>
    /// <para>to <c>sizeof(SyntheticResourceInfo)</c>.</para>
    /// <para>Returns <c>SLANG_OK</c> on success,</para>
    /// <para><c>SLANG_E_INVALID_ARG</c> for null <c>outInfo</c>, mismatched</para>
    /// <para><c>structSize</c>, or out-of-range <c>index</c>.</para>
    /// </remarks>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getResourceInfo([NativeTypeName("uint32_t")] uint index, [NativeTypeName("slang::SyntheticResourceInfo *")] SyntheticResourceInfo* outInfo)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint, SyntheticResourceInfo*, int>)(lpVtbl[5]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), index, outInfo);
    }

    /// <summary>Look up the resource index for a stable synthetic resource</summary>
    /// <remarks>
    /// <para>identifier. Returns <c>SLANG_OK</c> on success,</para>
    /// <para><c>SLANG_E_NOT_FOUND</c> when no resource with that id exists, and</para>
    /// <para><c>SLANG_E_INVALID_ARG</c> for null <c>outIndex</c>.</para>
    /// </remarks>
    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int findResourceIndexByID([NativeTypeName("uint32_t")] uint id, [NativeTypeName("uint32_t *")] uint* outIndex)
    {
        return ((delegate* unmanaged[Stdcall]<ISyntheticResourceMetadata*, uint, uint*, int>)(lpVtbl[6]))((ISyntheticResourceMetadata*)Unsafe.AsPointer(ref this), id, outIndex);
    }
}
