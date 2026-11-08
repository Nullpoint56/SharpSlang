// L3 — slang::IBindlessResourceMetadata, the optional interface behind IMetadata's castAs.
//
// slang.h: "Cast from an artifact-associated IMetadata* using castAs()". castAs hands back an
// interface of the same object without a reference, so this borrows it for the metadata's
// lifetime and checks the metadata on every call, the way SlangReflectionObject borrows.

using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang;

/// <summary>
/// <c>slang::IBindlessResourceMetadata</c>: whether the code generated for a target still uses a
/// bindless descriptor heap after the target's lowering.
/// </summary>
/// <remarks>
/// <see cref="SlangShaderReflection.BindlessSpaceIndex"/> is the front end's reservation, and stays
/// put when lowering removes every use of the heap; this is the code generator's answer
/// (<c>slang.h</c>). A code-generation signal, not a binding policy: a target that lowers
/// descriptor handles to native handles may need no heap binding even when this is true.
/// </remarks>
public sealed unsafe class SlangBindlessResourceMetadataExtension
{
    private readonly SlangMetadata _metadata;
    private readonly IBindlessResourceMetadata* _p;

    private SlangBindlessResourceMetadataExtension(SlangMetadata metadata, IBindlessResourceMetadata* p)
    {
        _metadata = metadata;
        _p = p;
    }

    internal static SlangBindlessResourceMetadataExtension? TryAcquire(SlangMetadata metadata, IMetadata* native)
    {
        var iid = SlangIID.IBindlessResourceMetadata;
        var found = native->castAs((SlangUUID*)&iid);
        return found is null ? null : new SlangBindlessResourceMetadataExtension(metadata, (IBindlessResourceMetadata*)found);
    }

    private IBindlessResourceMetadata* Ptr
    {
        get
        {
            ObjectDisposedException.ThrowIf(_metadata.IsDisposed, "owner of SlangBindlessResourceMetadataExtension");
            return _p;
        }
    }

    /// <summary>The interface pointer (<c>slang::IBindlessResourceMetadata*</c>), borrowed from the metadata. Last resort (AGENTS.md#layers).</summary>
    public nint NativeHandle => (nint)Ptr;

    /// <summary><c>usesBindlessResourceHeap</c>.</summary>
    public bool UsesBindlessResourceHeap => Ptr->usesBindlessResourceHeap();
}
