using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct IMetadata : ISlangCastable")]
public unsafe partial struct IMetadata
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<IMetadata*, SlangUUID*, void**, int>)(lpVtbl[0]))((IMetadata*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IMetadata*, uint>)(lpVtbl[1]))((IMetadata*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IMetadata*, uint>)(lpVtbl[2]))((IMetadata*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<IMetadata*, SlangUUID*, void*>)(lpVtbl[3]))((IMetadata*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Returns whether a resource parameter at the specified binding location is actually being used in the compiled shader.</summary>
    /// <remarks>
    /// <para>is this a <c>t</c> register? <c>s</c> register?</para>
    /// <para><c>space</c> for D3D12, <c>set</c> for Vulkan</para>
    /// <para><c>register</c> for D3D12, <c>binding</c> for Vulkan</para>
    /// </remarks>
    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int isParameterLocationUsed(SlangParameterCategory category, [NativeTypeName("SlangUInt")] ulong spaceIndex, [NativeTypeName("SlangUInt")] ulong registerIndex, [NativeTypeName("bool &")] bool* outUsed)
    {
        return ((delegate* unmanaged[Cdecl]<IMetadata*, SlangParameterCategory, ulong, ulong, bool*, int>)(lpVtbl[4]))((IMetadata*)Unsafe.AsPointer(ref this), category, spaceIndex, registerIndex, outUsed);
    }

    /// <summary>Returns the debug build identifier for a base and debug spirv pair.</summary>
    [VtblIndex(5)]
    [return: NativeTypeName("const char *")]
    public sbyte* getDebugBuildIdentifier()
    {
        return ((delegate* unmanaged[Stdcall]<IMetadata*, sbyte*>)(lpVtbl[5]))((IMetadata*)Unsafe.AsPointer(ref this));
    }
}
