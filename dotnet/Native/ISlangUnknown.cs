using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>Base interface for components exchanged through the API.</summary>
/// <remarks>
/// <para>This interface definition is compatible with the COM <c>IUnknown</c>, and uses the same UUID, but Slang does not require applications to use or initialize COM.</para>
/// </remarks>
public unsafe partial struct ISlangUnknown
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangUnknown*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangUnknown*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangUnknown*, uint>)(lpVtbl[1]))((ISlangUnknown*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangUnknown*, uint>)(lpVtbl[2]))((ISlangUnknown*)Unsafe.AsPointer(ref this));
    }
}
