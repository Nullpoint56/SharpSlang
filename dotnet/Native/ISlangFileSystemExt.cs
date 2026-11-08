using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>An extended file system abstraction.</summary>
/// <remarks>
/// <para>Implementing and using this interface over ISlangFileSystem gives much more control over how paths are managed, as well as how it is determined if two files 'are the same'.</para>
/// <para>All paths as input char*, or output as ISlangBlobs are always encoded as UTF-8 strings. Blobs that contain strings are always zero terminated.</para>
/// </remarks>
[NativeTypeName("struct ISlangFileSystemExt : ISlangFileSystem")]
public unsafe partial struct ISlangFileSystemExt
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, SlangUUID*, void**, int>)(lpVtbl[0]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, uint>)(lpVtbl[1]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, uint>)(lpVtbl[2]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this));
    }

    /// <summary>Can be used to cast to interfaces without reference counting.</summary>
    /// <remarks>
    /// <para>Also provides access to internal implementations, when they provide a guid</para>
    /// <para>Can simulate a 'generated' interface as long as kept in scope by cast from.</para>
    /// </remarks>
    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), guid);
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
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, sbyte*, ISlangBlob**, int>)(lpVtbl[4]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), path, outBlob);
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
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, sbyte*, ISlangBlob**, int>)(lpVtbl[5]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), path, outUniqueIdentity);
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
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, SlangPathType, sbyte*, sbyte*, ISlangBlob**, int>)(lpVtbl[6]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), fromPathType, fromPath, path, pathOut);
    }

    /// <summary>Gets the type of path that path is on the file system.</summary>
    /// <returns>SLANG_OK if located and type is known, else an error. SLANG_E_NOT_FOUND if not found.</returns>
    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getPathType([NativeTypeName("const char *")] sbyte* path, SlangPathType* pathTypeOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, sbyte*, SlangPathType*, int>)(lpVtbl[7]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), path, pathTypeOut);
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
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, PathKind, sbyte*, ISlangBlob**, int>)(lpVtbl[8]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), kind, path, outPath);
    }

    /// <summary>Clears any cached information</summary>
    [VtblIndex(9)]
    public void clearCache()
    {
        ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, void>)(lpVtbl[9]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this));
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
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, sbyte*, delegate* unmanaged[Cdecl]<SlangPathType, sbyte*, void*, void>, void*, int>)(lpVtbl[10]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this), path, callback, userData);
    }

    /// <summary>Returns how paths map to the OS file system</summary>
    /// <returns>OSPathKind that describes how paths map to the Operating System file system</returns>
    [VtblIndex(11)]
    public OSPathKind getOSPathKind()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangFileSystemExt*, OSPathKind>)(lpVtbl[11]))((ISlangFileSystemExt*)Unsafe.AsPointer(ref this));
    }
}
