using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangFileSystem : ISlangCastable")]
public unsafe partial struct ISlangFileSystem
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystem*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangFileSystem*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystem*, uint>)(lpVtbl[1]))((ISlangFileSystem*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystem*, uint>)(lpVtbl[2]))((ISlangFileSystem*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystem*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangFileSystem*)Unsafe.AsPointer(ref this), guid);
    }

    /// <summary>Load a file from <c>path</c> and return a blob of its contents</summary>
    /// <remarks>
    /// <para>NOTE! This is a *binary* load - the blob should contain the exact same bytes as are found in the backing file.</para>
    /// <para>If load is successful, the implementation should create a blob to hold the file's content, store it to <c>outBlob</c>, and return 0. If the load fails, the implementation should return a failure status (any negative value will do).</para>
    /// </remarks>
    /// <param name="path">The path to load from, as a null-terminated UTF-8 string.</param>
    /// <param name="outBlob">A destination pointer to receive the blob of the file contents.</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure in loading the file.</returns>
    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int loadFile([NativeTypeName("const char *")] sbyte* path, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystem*, sbyte*, ISlangBlob**, int>)(lpVtbl[4]))((ISlangFileSystem*)Unsafe.AsPointer(ref this), path, outBlob);
    }
}
