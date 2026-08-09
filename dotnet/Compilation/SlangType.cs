// L2 — a handle to a type in a compiled program, for specialisation and conformance.
//
// This is Slang's edge over GLSL: instead of an #ifdef permutation matrix, a shader declares
// `IMaterial` or `MyPass<T>` and the host supplies concrete types at bake time (specialisation) or
// dispatches between implementations at run time (type conformance).
//
// The pointer is BORROWED — owned by the module or program whose reflection produced it, and
// invalid once that is disposed. So this is not a SlangObject: nothing to release, but it does
// hold a managed reference to its owner so the owner cannot be collected first.

using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang;

/// <summary>A named type in a compiled program — a struct, an interface, or a generic.</summary>
public sealed unsafe class SlangType
{
    private readonly TypeReflection* _type;
    private readonly SlangObject _owner;

    internal SlangType(TypeReflection* type, SlangObject owner, string name)
    {
        _type = type;
        _owner = owner;
        Name = name;
    }

    /// <summary>The type's name as declared.</summary>
    public string Name { get; }

    /// <summary>
    /// The borrowed native pointer. Throws if the owner has been disposed, since the pointer is
    /// interior to it and would otherwise be a dangling read.
    /// </summary>
    internal TypeReflection* Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_owner.IsDisposed, $"owner of SlangType('{Name}')");
            return _type;
        }
    }

    internal SlangObject Owner => _owner;

    public override string ToString() => $"SlangType({Name})";
}

/// <summary>
/// A registration that one concrete type implements an interface, so Slang can dispatch to it
/// dynamically. Link it into a program alongside the module and entry points.
/// </summary>
/// <remarks>
/// This is what lets a material system ship several <c>IMaterial</c> implementations in one
/// pipeline and choose between them at run time, rather than compiling a pipeline per material.
/// </remarks>
public sealed unsafe class TypeConformance : SlangObject<ITypeConformance>
{
    internal TypeConformance(ITypeConformance* owned, SlangSession owner, string description)
        : base(owned, description, owner) { }

    internal IComponentType* AsComponent => (IComponentType*)Ptr;
}

/// <summary>Extra components and arguments applied when linking a program.</summary>
public sealed class LinkOptions
{
    /// <summary>
    /// Concrete types substituted for the program's generic parameters, in declaration order.
    /// Supply exactly <see cref="LinkedProgram.SpecializationParameterCount"/> of them.
    /// </summary>
    public IReadOnlyList<SlangType> SpecializationArguments { get; init; } = [];

    /// <summary>
    /// Conformances to link in, making those implementations reachable through dynamic dispatch.
    /// </summary>
    public IReadOnlyList<TypeConformance> TypeConformances { get; init; } = [];

    /// <summary>
    /// Compiler options for this link only. Null links with the session's settings.
    /// </summary>
    /// <remarks>
    /// Lets one session produce debug and release variants of the same shader, rather than needing
    /// a session per configuration.
    /// </remarks>
    public LinkTimeOptions? CompilerOptions { get; init; }
}
