// L2 — slang::TypeReflection: a type in a compiled program, one member per spReflectionType_*
// function, and the handle specialisation and conformance take.
//
// This is Slang's edge over GLSL: instead of an #ifdef permutation matrix, a shader declares
// `IMaterial` or `MyPass<T>` and the host supplies concrete types at bake time (specialisation) or
// dispatches between implementations at run time (type conformance).
//
// The pointer is BORROWED — owned by the module or program whose reflection produced it, and
// invalid once that is disposed (SlangReflectionObject). Nothing to release.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary><c>slang::TypeReflection</c>: a type in a compiled program — a struct, an interface, a generic, a resource.</summary>
public sealed unsafe class SlangType : SlangReflectionObject
{
    private readonly string? _name;

    internal SlangType(TypeReflection* type, SlangObject owner, string name)
        : base(type, owner, $"SlangType({name})") => _name = name;

    private SlangType(SlangReflectionType* type, SlangObject owner)
        : base(type, owner, nameof(SlangType)) { }

    internal static SlangType? From(SlangReflectionType* p, SlangObject owner) =>
        p is null ? null : new SlangType(p, owner);

    internal SlangReflectionType* Ptr => (SlangReflectionType*)Raw;

    /// <summary>
    /// The borrowed native pointer. Throws if the owner has been disposed, since the pointer is
    /// interior to it and would otherwise be a dangling read.
    /// </summary>
    internal TypeReflection* Handle => (TypeReflection*)Raw;

    /// <summary>
    /// The name it was looked up by (<see cref="SlangModule.FindType"/>, <see cref="LinkedProgram.FindType"/>),
    /// or Slang's <c>getName</c> for a type reached through reflection.
    /// </summary>
    public string Name => _name ?? DeclaredName ?? "";

    /// <summary><c>getName</c>: the name without generic arguments (<c>StructuredBuffer</c>).</summary>
    public string? DeclaredName => Utf8.Read(SlangApi.spReflectionType_GetName(Ptr));

    /// <summary>
    /// <c>getFullName</c>: the name with generic arguments, Slang's canonical spelling
    /// (<c>vector&lt;float,3&gt;</c>, <c>StructuredBuffer&lt;vector&lt;float,4&gt;, DefaultDataLayout&gt;</c>), or null.
    /// </summary>
    public string? FullName
    {
        get
        {
            ISlangBlob* blob = null;
            if (SlangResult.Failed(SlangApi.spReflectionType_GetFullName(Ptr, &blob)) || blob is null) return null;
            using var owned = new SlangBlob(blob);
            return owned.AsText();
        }
    }

    /// <summary><c>getKind</c>.</summary>
    public SlangTypeKind Kind => SlangApi.spReflectionType_GetKind(Ptr);

    /// <summary><c>getFieldCount</c>: 0 for anything that is not a struct.</summary>
    public uint FieldCount => SlangApi.spReflectionType_GetFieldCount(Ptr);

    /// <summary><c>getFieldByIndex</c>.</summary>
    public SlangVariable? GetFieldByIndex(uint index) =>
        SlangVariable.From(SlangApi.spReflectionType_GetFieldByIndex(Ptr, index), Owner);

    /// <summary>
    /// <c>getElementCount</c>: an array's length or a vector's component count.
    /// </summary>
    /// <remarks>
    /// <b>Ends the process</b> on an array whose length is a specialization constant (win-x64
    /// <c>SEHException</c>, Linux abort; v2026.21, the prober, all four legs; U1), although
    /// <c>slang.h</c> documents <c>SLANG_UNKNOWN_SIZE</c> for that case. The type alone cannot
    /// tell; its layout can: ask <see cref="SlangTypeLayout.GetSize"/> first and do not call this
    /// when it is unknown (AGENTS.md#sizes-slang-does-not-know). An unbounded array reports 0.
    /// </remarks>
    public ulong ElementCount => SlangApi.spReflectionType_GetElementCount(Ptr);

    /// <summary><c>getElementType</c>: the element of an array, vector or matrix, or null.</summary>
    public SlangType? ElementType => From(SlangApi.spReflectionType_GetElementType(Ptr), Owner);

    /// <summary><c>getRowCount</c>.</summary>
    public uint RowCount => SlangApi.spReflectionType_GetRowCount(Ptr);

    /// <summary><c>getColumnCount</c>.</summary>
    public uint ColumnCount => SlangApi.spReflectionType_GetColumnCount(Ptr);

    /// <summary><c>getScalarType</c>.</summary>
    public SlangScalarType ScalarType => SlangApi.spReflectionType_GetScalarType(Ptr);

    /// <summary><c>getResourceShape</c>.</summary>
    public SlangResourceShape ResourceShape => SlangApi.spReflectionType_GetResourceShape(Ptr);

    /// <summary><c>getResourceAccess</c>.</summary>
    public SlangResourceAccess ResourceAccess => SlangApi.spReflectionType_GetResourceAccess(Ptr);

    /// <summary><c>getResourceResultType</c>: what a resource yields, or null.</summary>
    public SlangType? ResourceResultType => From(SlangApi.spReflectionType_GetResourceResultType(Ptr), Owner);

    /// <summary><c>getUserAttributeCount</c>.</summary>
    public uint UserAttributeCount => SlangApi.spReflectionType_GetUserAttributeCount(Ptr);

    /// <summary><c>getUserAttributeByIndex</c>.</summary>
    public SlangUserAttribute? GetUserAttribute(uint index) =>
        SlangUserAttribute.From(SlangApi.spReflectionType_GetUserAttribute(Ptr, index), Owner);

    /// <summary>
    /// <c>findUserAttributeByName</c>, or null.
    /// </summary>
    /// <remarks>
    /// The type overload takes no global session, unlike the variable and function ones
    /// (<see cref="SlangVariable.FindUserAttributeByName"/>), which is Slang's asymmetry.
    /// </remarks>
    public SlangUserAttribute? FindUserAttributeByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return SlangUserAttribute.From(SlangApi.spReflectionType_FindUserAttributeByName(Ptr, n), Owner);
    }

    /// <summary><c>getGenericContainer</c>: the generic this type is declared in, or null.</summary>
    public SlangGeneric? GenericContainer => SlangGeneric.From(SlangApi.spReflectionType_GetGenericContainer(Ptr), Owner);

    /// <summary><c>applySpecializations</c>: this type with <paramref name="generic"/>'s arguments substituted, or null.</summary>
    public SlangType? ApplySpecializations(SlangGeneric generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return From(SlangApi.spReflectionType_applySpecializations(Ptr, generic.Ptr), Owner);
    }

    /// <summary>
    /// <c>getSpecializedTypeArgCount</c>. Measured 0 on every path at v2026.21, including a type
    /// written <c>Wrapper&lt;Lambert&gt;</c> in source (U15); read arguments from <see cref="FullName"/>.
    /// </summary>
    public long SpecializedTypeArgCount => SlangApi.spReflectionType_getSpecializedTypeArgCount(Ptr);

    /// <summary><c>getSpecializedTypeArgType</c>.</summary>
    public SlangType? GetSpecializedTypeArgType(long index) =>
        From(SlangApi.spReflectionType_getSpecializedTypeArgType(Ptr, index), Owner);

    /// <summary>
    /// The type's fields, by declared name and full type name, without a layout.
    /// </summary>
    /// <remarks>
    /// <see cref="FieldCount"/> and <see cref="GetFieldByIndex"/> read into one list, for a type
    /// that has no layout anywhere (one no shader parameter uses). Empty for anything that is not
    /// a struct.
    /// </remarks>
    public IReadOnlyList<(string Name, string Type)> Fields
    {
        get
        {
            var count = FieldCount;
            var fields = new List<(string, string)>((int)count);

            for (uint i = 0; i < count; i++)
            {
                // Not named `field`: inside a property accessor that is a contextual keyword in
                // C# 14 and binds to a synthesized backing field.
                var member = GetFieldByIndex(i);
                if (member is null) continue;

                var type = member.Type;
                var full = type?.FullName;
                fields.Add((member.Name ?? "", string.IsNullOrEmpty(full) ? type?.DeclaredName ?? "void" : full));
            }

            return fields;
        }
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
public sealed unsafe class TypeConformance : SlangObject<ITypeConformance>, ISlangComponentType
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
