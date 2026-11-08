using System.Runtime.CompilerServices;

namespace SharpSlang.Native;

/// <summary>IComponentType2 is a component type used for getting separate debug data.</summary>
/// <remarks>
/// <para>This interface is used for getting separate debug data, introduced here to avoid breaking backwards compatibility of the IComponentType interface.</para>
/// <para>The <c>getTargetCompileResult</c> and <c>getEntryPointCompileResult</c> functions are used to get the base and debug spirv, and metadata containing the debug build identifier.</para>
/// </remarks>
[NativeTypeName("struct IComponentType2 : ISlangUnknown")]
public unsafe partial struct IComponentType2
{
    public void** lpVtbl;

    [VtblIndex(0)]
    [return: NativeTypeName("SlangResult")]
    public int queryInterface([NativeTypeName("const SlangUUID &")] SlangUUID* uuid, void** outObject)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, SlangUUID*, void**, int>)(lpVtbl[0]))((IComponentType2*)Unsafe.AsPointer(ref this), uuid, outObject);
    }

    [VtblIndex(1)]
    [return: NativeTypeName("uint32_t")]
    public uint addRef()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, uint>)(lpVtbl[1]))((IComponentType2*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(2)]
    [return: NativeTypeName("uint32_t")]
    public uint release()
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, uint>)(lpVtbl[2]))((IComponentType2*)Unsafe.AsPointer(ref this));
    }

    [VtblIndex(3)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetCompileResult([NativeTypeName("SlangInt")] long targetIndex, ICompileResult** outCompileResult, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, long, ICompileResult**, ISlangBlob**, int>)(lpVtbl[3]))((IComponentType2*)Unsafe.AsPointer(ref this), targetIndex, outCompileResult, outDiagnostics);
    }

    [VtblIndex(4)]
    [return: NativeTypeName("SlangResult")]
    public int getEntryPointCompileResult([NativeTypeName("SlangInt")] long entryPointIndex, [NativeTypeName("SlangInt")] long targetIndex, ICompileResult** outCompileResult, [NativeTypeName("IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, long, long, ICompileResult**, ISlangBlob**, int>)(lpVtbl[4]))((IComponentType2*)Unsafe.AsPointer(ref this), entryPointIndex, targetIndex, outCompileResult, outDiagnostics);
    }

    /// <summary>Get functions accessible through the ISlangSharedLibrary interface.</summary>
    /// <remarks>
    /// <para>The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.</para>
    /// <para>NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.</para>
    /// <para><c>targetIndex</c>: The index of the target to get code for (default: zero).</para>
    /// <para><c>outSharedLibrary</c>: A pointer to a ISharedLibrary interface which functions can be queried on.</para>
    /// </remarks>
    /// <returns>A <c>SlangResult</c> to indicate success or failure.</returns>
    [VtblIndex(5)]
    [return: NativeTypeName("SlangResult")]
    public int getTargetHostCallable(int targetIndex, ISlangSharedLibrary** outSharedLibrary, [NativeTypeName("slang::IBlob **")] ISlangBlob** outDiagnostics = null)
    {
        return ((delegate* unmanaged[Stdcall]<IComponentType2*, int, ISlangSharedLibrary**, ISlangBlob**, int>)(lpVtbl[5]))((IComponentType2*)Unsafe.AsPointer(ref this), targetIndex, outSharedLibrary, outDiagnostics);
    }
}
