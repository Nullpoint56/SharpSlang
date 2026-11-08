// L1 — lifetime for Slang's borrowed reflection pointers.
//
// SlangObject is the owned, ref-counted population. This is the other one (see SlangObject's
// header): ShaderReflection, TypeLayoutReflection, VariableReflection and the rest are interior
// pointers into the IComponentType or IModule whose getLayout produced them. They have no
// reference count, nothing to release, and are valid exactly as long as that owner is
// (AGENTS.md#reflection-snapshot).
//
// So a wrapper holds the pointer and the owner's wrapper, and every read checks the owner first: a
// use after the owner's Dispose is an ObjectDisposedException naming what was read, never a read
// of freed memory. Holding the owner's wrapper also keeps it from being collected while a
// reflection wrapper is reachable. Disposing nothing, these do not count as the owner's children:
// the owner refuses Dispose only for owned objects (SlangObject), because a borrowed pointer has
// nothing of its own to give back first.

namespace SharpSlang.Interop;

/// <summary>
/// Base of the wrappers over Slang's reflection objects: a borrowed pointer, valid while the
/// object that produced it is not disposed.
/// </summary>
/// <remarks>
/// Two wrappers are equal when they wrap the same native object, so a walk can recognise a type
/// layout it has seen before. Every member throws <see cref="ObjectDisposedException"/> once the
/// owner (the <see cref="LinkedProgram"/>, <see cref="SlangModule"/> or
/// <see cref="SlangEntryPoint"/> whose reflection this is) has been disposed.
/// </remarks>
public abstract unsafe class SlangReflectionObject : IEquatable<SlangReflectionObject>
{
    private readonly nint _handle;
    private readonly SlangObject _owner;
    private readonly string _what;

    private protected SlangReflectionObject(void* handle, SlangObject owner, string what)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(owner);
        _handle = (nint)handle;
        _owner = owner;
        _what = what;
    }

    /// <summary>
    /// The native reflection pointer (<c>slang::TypeLayoutReflection*</c> and so on), for calling
    /// <see cref="Native.SlangApi"/>'s <c>spReflection*</c> functions where this wrapper has no
    /// member. Borrowed: valid while the owner lives, never released. The second escape hatch
    /// (AGENTS.md#layers); the binding sees none of the calls made through it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner has been disposed.</exception>
    public nint NativeHandle => (nint)Raw;

    /// <summary>The owned object this reflection belongs to.</summary>
    public SlangObject Owner => _owner;

    /// <summary>The pointer, after checking the owner is alive.</summary>
    private protected void* Raw
    {
        get
        {
            ObjectDisposedException.ThrowIf(_owner.IsDisposed, $"owner of {_what}");
            return (void*)_handle;
        }
    }

    public bool Equals(SlangReflectionObject? other) =>
        other is not null && other.GetType() == GetType() && other._handle == _handle;

    public override bool Equals(object? obj) => obj is SlangReflectionObject other && Equals(other);

    public override int GetHashCode() => _handle.GetHashCode();

    public override string ToString() => _owner.IsDisposed ? $"{_what} (owner disposed)" : _what;
}
