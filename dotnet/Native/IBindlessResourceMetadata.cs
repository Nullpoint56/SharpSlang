using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>Bindless resource metadata produced for a compiled target.</summary>
/// <remarks>
/// <para>The bindless space index reported through program reflection is a frontend-predicted reserved descriptor space. It remains stable even when later optimization or target lowering removes all descriptor-handle heap use from the emitted shader. This metadata interface reports the post-lowering usage signal instead.</para>
/// <para><c>usesBindlessResourceHeap()</c> reports whether the final target IR still contains the descriptor-handle/bindless resource path after target-specific lowering. This is a code-generation signal, not a complete cross-target host binding policy: targets that lower descriptor handles to native resource handles or addresses may not require an explicit descriptor-heap binding even when this returns true. Hosts should combine this query with their target binding model when deciding whether to bind a heap.</para>
/// <para>Cast from an artifact-associated <c>IMetadata*</c> using <c>castAs()</c>.</para>
/// </remarks>
[NativeTypeName("struct IBindlessResourceMetadata : ISlangCastable")]
public unsafe partial struct IBindlessResourceMetadata
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, uint>)(lpVtbl[1]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, uint>)(lpVtbl[2]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Returns true when the compiled target IR still contains a bindless</summary>
    /// <remarks>
    /// <para>descriptor-heap/resource-handle path after target-specific lowering. This is a</para>
    /// <para>code-generation signal, not a complete cross-target host binding policy; targets</para>
    /// <para>that lower descriptor handles to native resource handles or addresses may not require</para>
    /// <para>an explicit descriptor-heap binding even when this returns true.</para>
    /// </remarks>
    [VtblIndex(4)]
    public bool usesBindlessResourceHeap()
    {
        return ((delegate* unmanaged[Stdcall]<IBindlessResourceMetadata*, byte>)(lpVtbl[4]))((IBindlessResourceMetadata*)Unsafe.AsPointer(ref this)) != 0;
    }
}
