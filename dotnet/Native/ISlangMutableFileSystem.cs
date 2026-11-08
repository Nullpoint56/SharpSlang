using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

[NativeTypeName("struct ISlangMutableFileSystem : ISlangFileSystemExt")]
public unsafe partial struct ISlangMutableFileSystem
{
    public void** lpVtbl;

    [return: NativeTypeName("SlangResult")]
    public int QueryInterface([NativeTypeName("const struct _GUID &")] Guid* uuid, void** outObject)
    {
        return queryInterface(unchecked((SlangUUID*)(uuid)), outObject);
    }

    [return: NativeTypeName("uint32_t")]
    public uint AddRef()
    {
        return addRef();
    }

    [return: NativeTypeName("uint32_t")]
    public uint Release()
    {
        return release();
    }

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

    [VtblIndex(3)]
    public void* castAs([NativeTypeName("const SlangUUID &")] SlangUUID* guid)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, SlangUUID*, void*>)(lpVtbl[3]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), guid);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int loadFile([NativeTypeName("const char *")] sbyte* path, ISlangBlob** outBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob**, int>)(lpVtbl[4]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, outBlob);
    }

    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getFileUniqueIdentity([NativeTypeName("const char *")] sbyte* path, ISlangBlob** outUniqueIdentity)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob**, int>)(lpVtbl[5]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, outUniqueIdentity);
    }

    [VtblIndex(6)]
    [return: NativeTypeName("SlangResult")]
    public int calcCombinedPath(SlangPathType fromPathType, [NativeTypeName("const char *")] sbyte* fromPath, [NativeTypeName("const char *")] sbyte* path, ISlangBlob** pathOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, SlangPathType, sbyte*, sbyte*, ISlangBlob**, int>)(lpVtbl[6]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), fromPathType, fromPath, path, pathOut);
    }

    [VtblIndex(7)]
    [return: NativeTypeName("SlangResult")]
    public int getPathType([NativeTypeName("const char *")] sbyte* path, SlangPathType* pathTypeOut)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, SlangPathType*, int>)(lpVtbl[7]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, pathTypeOut);
    }

    [VtblIndex(8)]
    [return: NativeTypeName("SlangResult")]
    public int getPath(PathKind kind, [NativeTypeName("const char *")] sbyte* path, ISlangBlob** outPath)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, PathKind, sbyte*, ISlangBlob**, int>)(lpVtbl[8]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), kind, path, outPath);
    }

    [VtblIndex(9)]
    public void clearCache()
    {
        ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, void>)(lpVtbl[9]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(10)]
    [return: NativeTypeName("SlangResult")]
    public int enumeratePathContents([NativeTypeName("const char *")] sbyte* path, [NativeTypeName("FileSystemContentsCallBack")] delegate* unmanaged[Cdecl]<SlangPathType, sbyte*, void*, void> callback, void* userData)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, delegate* unmanaged[Cdecl]<SlangPathType, sbyte*, void*, void>, void*, int>)(lpVtbl[10]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, callback, userData);
    }

    [VtblIndex(11)]
    public OSPathKind getOSPathKind()
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, OSPathKind>)(lpVtbl[11]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(12)]
    [return: NativeTypeName("SlangResult")]
    public int saveFile([NativeTypeName("const char *")] sbyte* path, [NativeTypeName("const void *")] void* data, [NativeTypeName("size_t")] nuint size)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, void*, nuint, int>)(lpVtbl[12]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, data, size);
    }

    [VtblIndex(13)]
    [return: NativeTypeName("SlangResult")]
    public int saveFileBlob([NativeTypeName("const char *")] sbyte* path, ISlangBlob* dataBlob)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, ISlangBlob*, int>)(lpVtbl[13]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path, dataBlob);
    }

    [VtblIndex(14)]
    [return: NativeTypeName("SlangResult")]
    public int remove([NativeTypeName("const char *")] sbyte* path)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, int>)(lpVtbl[14]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path);
    }

    [VtblIndex(15)]
    [return: NativeTypeName("SlangResult")]
    public int createDirectory([NativeTypeName("const char *")] sbyte* path)
    {
        return ((delegate* unmanaged[Stdcall]<ISlangMutableFileSystem*, sbyte*, int>)(lpVtbl[15]))((ISlangMutableFileSystem*)Unsafe.AsPointer(ref this), path);
    }
}
