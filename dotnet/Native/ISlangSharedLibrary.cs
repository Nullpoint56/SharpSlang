using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>An interface that can be used to encapsulate access to a shared library. An implementation does not have to implement the library as a shared library</summary>
[NativeTypeName("struct ISlangSharedLibrary : ISlangCastable")]
public unsafe partial struct ISlangSharedLibrary
{
    public void** lpVtbl;

    /// <summary>Get a function by name. If the library is unloaded will only return nullptr.</summary>
    /// <param name="name">The name of the function</param>
    /// <returns>The function pointer related to the name or nullptr if not found</returns>
    [return: NativeTypeName("SlangFuncPtr")]
    public delegate* unmanaged[Cdecl]<void> findFuncByName([NativeTypeName("const char *")] sbyte* name)
    {
        return (delegate* unmanaged[Cdecl]<void>)(findSymbolAddressByName(name));
    }

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, uint>)(lpVtbl[1]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, uint>)(lpVtbl[2]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Get a symbol by name. If the library is unloaded will only return nullptr.</summary>
    /// <param name="name">The name of the symbol</param>
    /// <returns>The pointer related to the name or nullptr if not found</returns>
    [VtblIndex(4)]
    public void* findSymbolAddressByName([NativeTypeName("const char *")] sbyte* name)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangSharedLibrary*, sbyte*, void*>)(lpVtbl[4]))((ISlangSharedLibrary*)Unsafe.AsPointer(ref this), name);
    }
}
