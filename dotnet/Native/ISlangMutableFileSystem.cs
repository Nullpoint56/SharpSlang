using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangMutableFileSystem : ISlangFileSystemExt")]
public unsafe partial struct ISlangMutableFileSystem
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, uint>)(lpVtbl[1]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, uint>)(lpVtbl[2]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), guid);
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
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob**, int>)(lpVtbl[4]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, outBlob);
    }

    /// <summary>Get a uniqueIdentity which uniquely identifies an object of the file system.</summary>
    /// <remarks>
    /// <para>Given a path, returns a 'uniqueIdentity' which ideally is the same value for the same object on the file system.</para>
    /// <para>The uniqueIdentity is used to compare if two paths are the same - which amongst other things allows Slang to cache source contents internally. It is also used for #pragma once functionality.</para>
    /// <para>A *requirement* is for any implementation is that two paths can only return the same uniqueIdentity if the contents of the two files are *identical*. If an implementation breaks this constraint it can produce incorrect compilation. If an implementation cannot *strictly* identify *the same* files, this will only have an effect on #pragma once behavior.</para>
    /// <para>The string for the uniqueIdentity is held zero terminated in the ISlangBlob of outUniqueIdentity.</para>
    /// <para>Note that there are many ways a uniqueIdentity may be generated for a file. For example it could be the 'canonical path' - assuming it is available and unambiguous for a file system. Another possible mechanism could be to store the filename combined with the file date time to uniquely identify it.</para>
    /// <para>The client must ensure the blob be released when no longer used, otherwise memory will leak.</para>
    /// <para>NOTE! Ideally this method would be called 'getPathUniqueIdentity' but for historical reasons and backward compatibility it's name remains with 'File' even though an implementation should be made to work with directories too.</para>
    /// </remarks>
    /// <returns>A <c>SlangResult</c> to indicate success or failure getting the uniqueIdentity.</returns>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getFileUniqueIdentity([NativeTypeName("const char *")] sbyte* path, ISlangBlob** outUniqueIdentity)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob**, int>)(lpVtbl[5]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, outUniqueIdentity);
    }

    /// <summary>Calculate a path combining the 'fromPath' with 'path'</summary>
    /// <remarks>
    /// <para>The client must ensure the blob be released when no longer used, otherwise memory will leak.</para>
    /// </remarks>
    /// <param name="fromPathType">How to interpret the from path - as a file or a directory.</param>
    /// <param name="fromPath">The from path.</param>
    /// <param name="path">Path to be determined relative to the fromPath</param>
    /// <param name="pathOut">Holds the string which is the relative path. The string is held in the blob zero terminated.</param>
    /// <returns>A <c>SlangResult</c> to indicate success or failure in loading the file.</returns>
    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int calcCombinedPath(SlangPathType fromPathType, [NativeTypeName("const char *")] sbyte* fromPath, [NativeTypeName("const char *")] sbyte* path, ISlangBlob** pathOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, SlangPathType, sbyte*, sbyte*, ISlangBlob**, int>)(lpVtbl[6]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), fromPathType, fromPath, path, pathOut);
    }

    /// <summary>Gets the type of path that path is on the file system.</summary>
    /// <returns>SLANG_OK if located and type is known, else an error. SLANG_E_NOT_FOUND if not found.</returns>
    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getPathType([NativeTypeName("const char *")] sbyte* path, SlangPathType* pathTypeOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, SlangPathType*, int>)(lpVtbl[7]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, pathTypeOut);
    }

    /// <summary>Get a path based on the kind.</summary>
    /// <param name="kind">The kind of path wanted</param>
    /// <param name="path">The input path</param>
    /// <param name="outPath">The output path held in a blob</param>
    /// <returns>SLANG_OK if successfully simplified the path (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other error code)</returns>
    [VtblIndex(8)]
    [return: NativeTypeName("SlangResult")]
    public int getPath(PathKind kind, [NativeTypeName("const char *")] sbyte* path, ISlangBlob** outPath)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, PathKind, sbyte*, ISlangBlob**, int>)(lpVtbl[8]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), kind, path, outPath);
    }

    /// <summary>Clears any cached information</summary>
    [VtblIndex(9)]
    public void clearCache()
    {
        ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, void>)(lpVtbl[9]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Enumerate the contents of the path</summary>
    /// <remarks>
    /// <para>Note that for normal Slang operation it isn't necessary to enumerate contents this can return SLANG_E_NOT_IMPLEMENTED.</para>
    /// <para><c>The</c>: path to enumerate</para>
    /// <para><c>callback</c>: This callback is called for each entry in the path.</para>
    /// <para><c>userData</c>: This is passed to the callback</para>
    /// </remarks>
    /// <returns>SLANG_OK if successful</returns>
    [VtblIndex(10)]
    [return: NativeTypeName("SlangResult")]
    public int enumeratePathContents([NativeTypeName("const char *")] sbyte* path, [NativeTypeName("FileSystemContentsCallBack")] delegate* unmanaged[Cdecl]<SlangPathType, sbyte*, void*, void> callback, void* userData)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, delegate* unmanaged[Cdecl]<SlangPathType, sbyte*, void*, void>, void*, int>)(lpVtbl[10]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, callback, userData);
    }

    /// <summary>Returns how paths map to the OS file system</summary>
    /// <returns>OSPathKind that describes how paths map to the Operating System file system</returns>
    [VtblIndex(11)]
    public OSPathKind getOSPathKind()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, OSPathKind>)(lpVtbl[11]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Write data to the specified path.</summary>
    /// <param name="path">The path for data to be saved to</param>
    /// <param name="data">The data to be saved</param>
    /// <param name="size">The size of the data in bytes</param>
    /// <returns>SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other error code)</returns>
    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int saveFile([NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, void*, nuint, int>)(lpVtbl[12]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, data, size);
    }

    /// <summary>Write data in the form of a blob to the specified path.</summary>
    /// <remarks>
    /// <para>Depending on the implementation writing a blob might be faster/use less memory. It is assumed the blob is *immutable* and that an implementation can reference count it.</para>
    /// <para>It is not guaranteed loading the same file will return the *same* blob - just a blob with same contents.</para>
    /// </remarks>
    /// <param name="path">The path for data to be saved to</param>
    /// <param name="dataBlob">The data to be saved</param>
    /// <returns>SLANG_OK if successful (SLANG_E_NOT_IMPLEMENTED if not implemented, or some other error code)</returns>
    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int saveFileBlob([NativeTypeName("const char *")] sbyte* path, ISlangBlob* dataBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob*, int>)(lpVtbl[13]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, dataBlob);
    }

    /// <summary>Remove the entry in the path (directory of file). Will only delete an empty directory, if not empty will return an error.</summary>
    /// <param name="path">The path to remove</param>
    /// <returns>SLANG_OK if successful</returns>
    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int remove([NativeTypeName("const char *")] sbyte* path)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, int>)(lpVtbl[14]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path);
    }

    /// <summary>Create a directory.</summary>
    /// <remarks>
    /// <para>The path to the directory must exist</para>
    /// </remarks>
    /// <param name="path">To the directory to create. The parent path *must* exist otherwise will return an error.</param>
    /// <returns>SLANG_OK if successful</returns>
    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int createDirectory([NativeTypeName("const char *")] sbyte* path)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, int>)(lpVtbl[15]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path);
    }
}
