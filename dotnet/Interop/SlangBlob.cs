// L1 — ISlangBlob as an owned, disposable buffer.
//
// Slang returns bytes (SPIR-V, diagnostics, serialized modules) as ISlangBlob: a ref-counted
// object exposing a pointer and a length. The pointer is valid only while the blob is alive, which
// slang.h states in prose and expresses in no type. Handing that pointer upward is how you get a
// use-after-free that presents as corrupt shader bytecode.
//
// So: Span is scoped to the blob's lifetime and never escapes; ToArray copies out.

using System.Text;
using PrismSlang.Native;

namespace PrismSlang.Interop;

/// <summary>An owned reference to a Slang blob. Dispose releases the native buffer.</summary>
public sealed unsafe class SlangBlob : SlangObject<ISlangBlob>
{
    internal SlangBlob(ISlangBlob* owned, SlangObject? owner = null)
        : base(owned, nameof(SlangBlob), owner) { }

    /// <summary>
    /// Takes ownership of a blob out-parameter, or returns null if Slang produced none.
    /// Slang leaves the out-parameter untouched on many paths, so null is normal, not an error.
    /// </summary>
    internal static SlangBlob? Adopt(ISlangBlob* blob, SlangObject? owner = null) =>
        blob is null ? null : new SlangBlob(blob, owner);

    /// <summary>Length in bytes.</summary>
    public int Length => (int)Ptr->getBufferSize();

    /// <summary>
    /// The bytes, without copying. Valid only until this blob is disposed — do not store it.
    /// Use <see cref="ToArray"/> if the data must outlive the blob.
    /// </summary>
    public ReadOnlySpan<byte> Span => new(Ptr->getBufferPointer(), Length);

    /// <summary>A managed copy of the bytes, safe to keep after disposal.</summary>
    public byte[] ToArray() => Span.ToArray();

    /// <summary>
    /// The contents as UTF-8 text. Slang's text blobs are not reliably NUL-terminated, so this
    /// decodes by length rather than scanning for a terminator.
    /// </summary>
    public string AsText()
    {
        var len = Length;
        return len == 0 ? string.Empty : Encoding.UTF8.GetString((byte*)Ptr->getBufferPointer(), len);
    }

    public override string ToString() =>
        IsDisposed ? "SlangBlob (disposed)" : $"SlangBlob ({Length} bytes)";
}
