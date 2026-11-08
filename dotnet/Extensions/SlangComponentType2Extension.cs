// L3 — slang::IComponentType2, the optional interface behind a component type's queryInterface,
// and the ICompileResult it returns.
//
// Acquired at most once per program, on first use (LinkedProgram.TryComponentType2), as SharpNRI
// acquires its extensions: a program whose implementation lacks the interface answers null to the
// Try form and throws from the other. The interface pointer carries a reference of its own, which
// the program releases before its own (LinkedProgram), so the object is never destroyed after its
// session.

using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang;

/// <summary>
/// <c>slang::IComponentType2</c>: a program's generated code as a compile result, which carries
/// every artifact of an entry point (the code, and with separate debug information the debug
/// module) and the metadata that came with it.
/// </summary>
public sealed unsafe class SlangComponentType2Extension
{
    private readonly LinkedProgram _program;
    private readonly IComponentType2* _p;

    private SlangComponentType2Extension(LinkedProgram program, IComponentType2* p)
    {
        _program = program;
        _p = p;
    }

    internal static SlangComponentType2Extension? TryAcquire(LinkedProgram program, IComponentType* component)
    {
        void* found = null;
        var iid = SlangIID.IComponentType2;
        var hr = component->queryInterface((SlangUUID*)&iid, &found);
        return SlangResult.Failed(hr) || found is null ? null : new SlangComponentType2Extension(program, (IComponentType2*)found);
    }

    /// <summary>Releases the reference <c>queryInterface</c> took; the program calls it before its own release.</summary>
    internal void Release() => ((ISlangUnknown*)_p)->release();

    private IComponentType2* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_program.IsDisposed, "owner of SlangComponentType2Extension");
            return _p;
        }
    }

    /// <summary>
    /// The interface pointer (<c>slang::IComponentType2*</c>), for calling its vtable where this
    /// wrapper has no member. Valid while the program lives. Last resort (AGENTS.md#layers).
    /// </summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary><c>getTargetCompileResult</c>: whole-program code for a target, as a compile result.</summary>
    /// <exception cref="SlangException">Slang refused; its diagnostics are attached.</exception>
    public SlangCompileResult GetTargetCompileResult(long targetIndex)
    {
        ICompileResult* result = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getTargetCompileResult(targetIndex, &result, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType2::getTargetCompileResult", diagnostics);
        Diagnostics.ThrowIfNull(result, "IComponentType2::getTargetCompileResult", null);
        return new SlangCompileResult(result, _program);
    }

    /// <summary><c>getEntryPointCompileResult</c>: one entry point's code for a target, as a compile result.</summary>
    /// <exception cref="SlangException">Slang refused; its diagnostics are attached.</exception>
    public SlangCompileResult GetEntryPointCompileResult(long entryPointIndex, long targetIndex)
    {
        ICompileResult* result = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getEntryPointCompileResult(entryPointIndex, targetIndex, &result, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType2::getEntryPointCompileResult", diagnostics);
        Diagnostics.ThrowIfNull(result, "IComponentType2::getEntryPointCompileResult", null);
        return new SlangCompileResult(result, _program);
    }
}

/// <summary>
/// <c>slang::ICompileResult</c>: the artifacts of one compile — item 0 the code, and with
/// separate debug information the debug module after it — and their metadata. Dispose it before
/// the program.
/// </summary>
public sealed unsafe class SlangCompileResult : SlangObject<ICompileResult>
{
    internal SlangCompileResult(ICompileResult* owned, LinkedProgram owner)
        : base(owned, nameof(SlangCompileResult), owner) { }

    /// <summary><c>getItemCount</c>.</summary>
    public uint ItemCount => Ptr->getItemCount();

    /// <summary><c>getItemData</c>: one artifact's bytes. An owned blob; dispose it before this result.</summary>
    /// <exception cref="SlangException">No such item.</exception>
    public SlangBlob GetItemData(uint index)
    {
        ISlangBlob* blob = null;
        var hr = Ptr->getItemData(index, &blob);
        Diagnostics.ThrowIfFailed(hr, $"ICompileResult::getItemData({index})", null);
        Diagnostics.ThrowIfNull(blob, $"ICompileResult::getItemData({index})", null);
        return new SlangBlob(blob, this);
    }

    /// <summary><c>getMetadata</c>: the metadata that came with the artifacts. Dispose it before this result.</summary>
    /// <exception cref="SlangException">Slang has none for this compile.</exception>
    public SlangMetadata GetMetadata()
    {
        IMetadata* metadata = null;
        var hr = Ptr->getMetadata(&metadata);
        Diagnostics.ThrowIfFailed(hr, "ICompileResult::getMetadata", null);
        Diagnostics.ThrowIfNull(metadata, "ICompileResult::getMetadata", null);
        return new SlangMetadata(metadata, this);
    }
}
