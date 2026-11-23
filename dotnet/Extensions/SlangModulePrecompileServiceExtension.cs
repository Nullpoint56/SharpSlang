// L3 — slang::IModulePrecompileService_Experimental, the optional interface behind a module's
// queryInterface.
//
// Acquired at most once per module, on first use (SlangModule.TryPrecompileService), as
// SlangComponentType2Extension is. The interface pointer carries a reference of its own, which the
// module releases before its own.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// <c>slang::IModulePrecompileService_Experimental</c>: compiles a module for a target ahead of
/// time and embeds the target code in it, so a serialized module carries its SPIR-V or DXIL.
/// </summary>
/// <remarks>
/// Experimental in <c>slang.h</c>, and not thread-safe: it mutates the module. Use it only while
/// nothing else uses the module or its session.
/// </remarks>
public sealed unsafe class SlangModulePrecompileServiceExtension
{
    private readonly SlangModule _module;
    private readonly IModulePrecompileService_Experimental* _p;

    private SlangModulePrecompileServiceExtension(SlangModule module, IModulePrecompileService_Experimental* p)
    {
        _module = module;
        _p = p;
    }

    internal static SlangModulePrecompileServiceExtension? TryAcquire(SlangModule module, IModule* native)
    {
        void* found = null;
        var iid = SlangIID.IModulePrecompileService_Experimental;
        var hr = native->queryInterface((SlangUUID*)&iid, &found);
        return SlangResult.Failed(hr) || found is null
            ? null
            : new SlangModulePrecompileServiceExtension(module, (IModulePrecompileService_Experimental*)found);
    }

    internal void Release() => ((ISlangUnknown*)_p)->release();

    private IModulePrecompileService_Experimental* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_module.IsDisposed, "owner of SlangModulePrecompileServiceExtension");
            return _p;
        }
    }

    /// <summary>The interface pointer, owned by the module's wrapper. Last resort (AGENTS.md#layers).</summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary>
    /// <c>precompileForTarget</c>: compiles the module's exported code for <paramref name="target"/>
    /// and embeds it in the module. A second call for the same target does nothing.
    /// </summary>
    /// <exception cref="SlangException">The compile failed; Slang's diagnostics are attached.</exception>
    public void PrecompileForTarget(SlangCompileTarget target)
    {
        ISlangBlob* diagnostics = null;
        var hr = Ptr->precompileForTarget(target, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, $"IModulePrecompileService_Experimental::precompileForTarget({target})", diagnostics);
    }

    /// <summary>
    /// <c>getPrecompiledTargetCode</c>: the code <see cref="PrecompileForTarget"/> embedded for
    /// <paramref name="target"/>, or null when there is none.
    /// </summary>
    public byte[]? GetPrecompiledTargetCode(SlangCompileTarget target)
    {
        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getPrecompiledTargetCode(target, &code, &diagnostics);
        Diagnostics.Consume(diagnostics);
        if (code is null) return null;

        using var owned = new SlangBlob(code);
        return SlangResult.Failed(hr) ? null : owned.ToArray();
    }

    /// <summary><c>getModuleDependencyCount</c>. A module's own service reports 0 (Slang's <c>Module</c>).</summary>
    public int ModuleDependencyCount => checked((int)Ptr->getModuleDependencyCount());

    /// <summary><c>getModuleDependency</c>: a module this one depends on, as a new wrapper the caller disposes.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No dependency at <paramref name="index"/>.</exception>
    public SlangModule GetModuleDependency(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ModuleDependencyCount);

        IModule* module = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getModuleDependency(index, &module, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IModulePrecompileService_Experimental::getModuleDependency", diagnostics);
        Diagnostics.ThrowIfNull(module, "IModulePrecompileService_Experimental::getModuleDependency", null);

        // Owned (addRef'd in ComponentType::getModuleDependency); SlangModule takes another, so
        // this one is given back.
        var wrapper = new SlangModule(module, Utf8.Read(module->getName()) ?? $"#{index}", _module.Session);
        module->release();
        return wrapper;
    }
}
