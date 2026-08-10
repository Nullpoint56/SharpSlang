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

    /// <summary>
    /// The type's fields, by declared name and type, without a layout.
    /// </summary>
    /// <remarks>
    /// The structural view: what a type <em>has</em>, answerable for any named type, including one
    /// that no shader parameter uses and that therefore has no layout anywhere in a
    /// <see cref="Reflection.ProgramLayout"/>. For byte offsets and sizes, lay the type out —
    /// <c>LinkedProgram.SpecializeGeneric</c> returns a layout, and a type used by a parameter has
    /// one in the snapshot already. Empty for anything that is not a struct.
    /// </remarks>
    public IReadOnlyList<(string Name, string Type)> Fields
    {
        get
        {
            var type = (SlangReflectionType*)Handle;
            var count = SlangApi.spReflectionType_GetFieldCount(type);
            var fields = new List<(string, string)>((int)count);

            for (uint i = 0; i < count; i++)
            {
                // Not named `field`: inside a property accessor that is a contextual keyword in
                // C# 14 and binds to a synthesized backing field.
                var member = SlangApi.spReflectionType_GetFieldByIndex(type, i);
                if (member is null) continue;

                fields.Add((
                    Utf8.Read(SlangApi.spReflectionVariable_GetName(member)) ?? "",
                    Reflection.ReflectionWalker.TypeName(SlangApi.spReflectionVariable_GetType(member))));
            }

            return fields;
        }
    }

    /// <summary>
    /// A user attribute on this type's declaration, looked up by name, or null.
    /// </summary>
    /// <remarks>
    /// <c>TypeLayout.Attributes</c> enumerates the same set and is the one to reach for when the
    /// type has a layout. This exists for a type that does not, and because Slang's own by-name
    /// lookup is the second opinion the enumeration is checked against.
    ///
    /// Note the asymmetry in Slang's own API, which is why this takes no session: the type
    /// overload finds an attribute by name alone, while the variable and function overloads
    /// require an <c>IGlobalSession</c>.
    /// </remarks>
    public Reflection.UserAttribute? FindAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        using Utf8.Scoped n = new(name);
        var attribute = SlangApi.spReflectionType_FindUserAttributeByName(
            (SlangReflectionType*)Handle, n);

        return attribute is null ? null : Reflection.ReflectionWalker.Attribute(attribute);
    }

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
