using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>An interface to provide a mechanism to cast, that doesn't require ref counting and doesn't have to return a pointer to a ISlangUnknown derived class</summary>
[NativeTypeName("struct ISlangCastable : ISlangUnknown")]
public unsafe partial struct ISlangCastable
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangCastable*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangCastable*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangCastable*, uint>)(lpVtbl[1]))((ISlangCastable*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangCastable*, uint>)(lpVtbl[2]))((ISlangCastable*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangCastable*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangCastable*)Unsafe.AsPointer(ref this), guid);
    }
}
