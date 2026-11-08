// L2 — slang::FunctionReflection, slang::GenericReflection, slang::DeclReflection,
// slang::TypeParameterReflection, slang::UserAttribute and slang::Modifier: one member per
// spReflectionFunction_*, spReflectionGeneric_*, spReflectionDecl_*, spReflectionTypeParameter_*
// and spReflectionUserAttribute_* function, in Slang's argument order. Borrowed
// (SlangReflectionObject).

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary><c>slang::FunctionReflection</c>: a function declaration — its signature, modifiers, attributes and overloads.</summary>
public sealed unsafe class SlangFunction : SlangReflectionObject
{
    private SlangFunction(SlangReflectionFunction* p, SlangObject owner)
        : base(p, owner, nameof(SlangFunction)) { }

    internal static SlangFunction? From(SlangReflectionFunction* p, SlangObject owner) =>
        p is null ? null : new SlangFunction(p, owner);

    internal SlangReflectionFunction* Ptr => (SlangReflectionFunction*)Raw;

    /// <summary><c>getName</c>.</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionFunction_GetName(Ptr));

    /// <summary><c>getReturnType</c>, or null for <c>void</c>.</summary>
    public SlangType? ResultType => SlangType.From(SlangApi.spReflectionFunction_GetResultType(Ptr), Owner);

    /// <summary><c>getParameterCount</c>.</summary>
    public uint ParameterCount => SlangApi.spReflectionFunction_GetParameterCount(Ptr);

    /// <summary><c>getParameterByIndex</c>.</summary>
    public SlangVariable? GetParameter(uint index) =>
        SlangVariable.From(SlangApi.spReflectionFunction_GetParameter(Ptr, index), Owner);

    /// <summary><c>getUserAttributeCount</c>.</summary>
    public uint UserAttributeCount => SlangApi.spReflectionFunction_GetUserAttributeCount(Ptr);

    /// <summary><c>getUserAttributeByIndex</c>.</summary>
    public SlangUserAttribute? GetUserAttribute(uint index) =>
        SlangUserAttribute.From(SlangApi.spReflectionFunction_GetUserAttribute(Ptr, index), Owner);

    /// <summary><c>findUserAttributeByName</c>, which on a function takes the global session; or null.</summary>
    public SlangUserAttribute? FindUserAttributeByName(SlangGlobalSession globalSession, string name)
    {
        ArgumentNullException.ThrowIfNull(globalSession);
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return SlangUserAttribute.From(
            SlangApi.spReflectionFunction_FindUserAttributeByName(Ptr, globalSession.Handle, n), Owner);
    }

    /// <summary><c>findModifier</c>, or null.</summary>
    public SlangModifier? FindModifier(SlangModifierID id) =>
        SlangModifier.From(SlangApi.spReflectionFunction_FindModifier(Ptr, id), Owner);

    /// <summary><c>getGenericContainer</c>, or null.</summary>
    public SlangGeneric? GenericContainer => SlangGeneric.From(SlangApi.spReflectionFunction_GetGenericContainer(Ptr), Owner);

    /// <summary>
    /// <c>applySpecializations</c>, or null. Measured at v2026.21: the parameters still report
    /// <c>T</c>; apply the generic to each parameter and to the result type as well (U15).
    /// </summary>
    public SlangFunction? ApplySpecializations(SlangGeneric generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return From(SlangApi.spReflectionFunction_applySpecializations(Ptr, generic.Ptr), Owner);
    }

    /// <summary><c>specializeWithArgTypes</c>: the overload a call with these argument types resolves to, or null.</summary>
    public SlangFunction? SpecializeWithArgTypes(params ReadOnlySpan<SlangType> argumentTypes)
    {
        var types = new SlangReflectionType*[Math.Max(argumentTypes.Length, 1)];
        for (var i = 0; i < argumentTypes.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(argumentTypes[i]);
            types[i] = argumentTypes[i].Ptr;
        }

        fixed (SlangReflectionType** p = types)
        {
            return From(SlangApi.spReflectionFunction_specializeWithArgTypes(Ptr, argumentTypes.Length, p), Owner);
        }
    }

    /// <summary><c>isOverloaded</c>: an overload set reached by name.</summary>
    public bool IsOverloaded => SlangApi.spReflectionFunction_isOverloaded(Ptr) != 0;

    /// <summary><c>getOverloadCount</c>.</summary>
    public uint OverloadCount => SlangApi.spReflectionFunction_getOverloadCount(Ptr);

    /// <summary><c>getOverload</c>.</summary>
    public SlangFunction? GetOverload(uint index) => From(SlangApi.spReflectionFunction_getOverload(Ptr, index), Owner);

    /// <summary><c>asDecl</c>, or null.</summary>
    public SlangDecl? AsDecl() => SlangDecl.From(SlangApi.spReflectionFunction_asDecl(Ptr), Owner);
}

/// <summary><c>slang::GenericReflection</c>: a generic declaration, specialized or not.</summary>
public sealed unsafe class SlangGeneric : SlangReflectionObject
{
    internal SlangGeneric(SlangReflectionGeneric* p, SlangObject owner)
        : base(p, owner, nameof(SlangGeneric)) { }

    internal static SlangGeneric? From(SlangReflectionGeneric* p, SlangObject owner) =>
        p is null ? null : new SlangGeneric(p, owner);

    internal SlangReflectionGeneric* Ptr => (SlangReflectionGeneric*)Raw;

    /// <summary><c>asDecl</c>, or null.</summary>
    public SlangDecl? AsDecl() => SlangDecl.From(SlangApi.spReflectionGeneric_asDecl(Ptr), Owner);

    /// <summary><c>getName</c>.</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionGeneric_GetName(Ptr));

    /// <summary><c>getTypeParameterCount</c>.</summary>
    public uint TypeParameterCount => SlangApi.spReflectionGeneric_GetTypeParameterCount(Ptr);

    /// <summary><c>getTypeParameter</c>.</summary>
    public SlangVariable? GetTypeParameter(uint index) =>
        SlangVariable.From(SlangApi.spReflectionGeneric_GetTypeParameter(Ptr, index), Owner);

    /// <summary><c>getValueParameterCount</c>.</summary>
    public uint ValueParameterCount => SlangApi.spReflectionGeneric_GetValueParameterCount(Ptr);

    /// <summary><c>getValueParameter</c>.</summary>
    public SlangVariable? GetValueParameter(uint index) =>
        SlangVariable.From(SlangApi.spReflectionGeneric_GetValueParameter(Ptr, index), Owner);

    /// <summary><c>getTypeParameterConstraintCount</c>.</summary>
    public uint GetTypeParameterConstraintCount(SlangVariable typeParameter)
    {
        ArgumentNullException.ThrowIfNull(typeParameter);
        return SlangApi.spReflectionGeneric_GetTypeParameterConstraintCount(Ptr, typeParameter.Ptr);
    }

    /// <summary><c>getTypeParameterConstraintType</c>.</summary>
    public SlangType? GetTypeParameterConstraintType(SlangVariable typeParameter, uint index)
    {
        ArgumentNullException.ThrowIfNull(typeParameter);
        return SlangType.From(SlangApi.spReflectionGeneric_GetTypeParameterConstraintType(Ptr, typeParameter.Ptr, index), Owner);
    }

    /// <summary><c>getInnerKind</c>: what the generic wraps.</summary>
    public SlangDeclKind InnerKind => SlangApi.spReflectionGeneric_GetInnerKind(Ptr);

    /// <summary><c>getInnerDecl</c>, or null.</summary>
    public SlangDecl? InnerDecl => SlangDecl.From(SlangApi.spReflectionGeneric_GetInnerDecl(Ptr), Owner);

    /// <summary><c>getOuterGenericContainer</c>, or null.</summary>
    public SlangGeneric? OuterGenericContainer => From(SlangApi.spReflectionGeneric_GetOuterGenericContainer(Ptr), Owner);

    /// <summary><c>getConcreteType</c>: on a specialized generic, the type bound to <paramref name="typeParameter"/>.</summary>
    public SlangType? GetConcreteType(SlangVariable typeParameter)
    {
        ArgumentNullException.ThrowIfNull(typeParameter);
        return SlangType.From(SlangApi.spReflectionGeneric_GetConcreteType(Ptr, typeParameter.Ptr), Owner);
    }

    /// <summary><c>getConcreteIntVal</c>: on a specialized generic, the value bound to <paramref name="valueParameter"/>.</summary>
    public long GetConcreteIntVal(SlangVariable valueParameter)
    {
        ArgumentNullException.ThrowIfNull(valueParameter);
        return SlangApi.spReflectionGeneric_GetConcreteIntVal(Ptr, valueParameter.Ptr);
    }

    /// <summary><c>applySpecializations</c>, or null.</summary>
    public SlangGeneric? ApplySpecializations(SlangGeneric generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return From(SlangApi.spReflectionGeneric_applySpecializations(Ptr, generic.Ptr), Owner);
    }
}

/// <summary><c>slang::DeclReflection</c>: a node of a module's declaration tree.</summary>
public sealed unsafe class SlangDecl : SlangReflectionObject
{
    private SlangDecl(SlangReflectionDecl* p, SlangObject owner)
        : base(p, owner, nameof(SlangDecl)) { }

    internal static SlangDecl? From(SlangReflectionDecl* p, SlangObject owner) =>
        p is null ? null : new SlangDecl(p, owner);

    internal SlangReflectionDecl* Ptr => (SlangReflectionDecl*)Raw;

    /// <summary><c>getName</c>.</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionDecl_getName(Ptr));

    /// <summary><c>getKind</c>.</summary>
    public SlangDeclKind Kind => SlangApi.spReflectionDecl_getKind(Ptr);

    /// <summary><c>getChildrenCount</c>.</summary>
    public uint ChildrenCount => SlangApi.spReflectionDecl_getChildrenCount(Ptr);

    /// <summary><c>getChild</c>.</summary>
    public SlangDecl? GetChild(uint index) => From(SlangApi.spReflectionDecl_getChild(Ptr, index), Owner);

    /// <summary><c>getParent</c>, or null at the root.</summary>
    public SlangDecl? Parent => From(SlangApi.spReflectionDecl_getParent(Ptr), Owner);

    /// <summary><c>getType</c> (the flat <c>spReflection_getTypeFromDecl</c>), or null.</summary>
    public SlangType? Type => SlangType.From(SlangApi.spReflection_getTypeFromDecl(Ptr), Owner);

    /// <summary><c>asVariable</c>, or null.</summary>
    public SlangVariable? CastToVariable() => SlangVariable.From(SlangApi.spReflectionDecl_castToVariable(Ptr), Owner);

    /// <summary><c>asFunction</c>, or null.</summary>
    public SlangFunction? CastToFunction() => SlangFunction.From(SlangApi.spReflectionDecl_castToFunction(Ptr), Owner);

    /// <summary><c>asGeneric</c>, or null.</summary>
    public SlangGeneric? CastToGeneric() => SlangGeneric.From(SlangApi.spReflectionDecl_castToGeneric(Ptr), Owner);

    /// <summary><c>findModifier</c>, or null.</summary>
    public SlangModifier? FindModifier(SlangModifierID id) =>
        SlangModifier.From(SlangApi.spReflectionDecl_findModifier(Ptr, id), Owner);
}

/// <summary><c>slang::TypeParameterReflection</c>: one of a program's unbound generic parameters.</summary>
public sealed unsafe class SlangTypeParameter : SlangReflectionObject
{
    private SlangTypeParameter(SlangReflectionTypeParameter* p, SlangObject owner)
        : base(p, owner, nameof(SlangTypeParameter)) { }

    internal static SlangTypeParameter? From(SlangReflectionTypeParameter* p, SlangObject owner) =>
        p is null ? null : new SlangTypeParameter(p, owner);

    private SlangReflectionTypeParameter* Ptr => (SlangReflectionTypeParameter*)Raw;

    /// <summary><c>getName</c>: null or empty for an interface-typed shader parameter (an existential).</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionTypeParameter_GetName(Ptr));

    /// <summary><c>getIndex</c>.</summary>
    public uint Index => SlangApi.spReflectionTypeParameter_GetIndex(Ptr);

    /// <summary><c>getConstraintCount</c>.</summary>
    public uint ConstraintCount => SlangApi.spReflectionTypeParameter_GetConstraintCount(Ptr);

    /// <summary><c>getConstraintByIndex</c>.</summary>
    public SlangType? GetConstraintByIndex(uint index) =>
        SlangType.From(SlangApi.spReflectionTypeParameter_GetConstraintByIndex(Ptr, index), Owner);
}

/// <summary><c>slang::UserAttribute</c>: a <c>[MyAttribute(...)]</c> on a declaration.</summary>
/// <remarks>
/// Slang has no accessor for an argument's value kind: each typed getter reports failure for the
/// wrong kind, and <see cref="GetArgumentType"/> names the declared type.
/// </remarks>
public sealed unsafe class SlangUserAttribute : SlangReflectionObject
{
    private SlangUserAttribute(SlangReflectionUserAttribute* p, SlangObject owner)
        : base(p, owner, nameof(SlangUserAttribute)) { }

    internal static SlangUserAttribute? From(SlangReflectionUserAttribute* p, SlangObject owner) =>
        p is null ? null : new SlangUserAttribute(p, owner);

    private SlangReflectionUserAttribute* Ptr => (SlangReflectionUserAttribute*)Raw;

    /// <summary><c>getName</c>.</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionUserAttribute_GetName(Ptr));

    /// <summary><c>getArgumentCount</c>.</summary>
    public uint ArgumentCount => SlangApi.spReflectionUserAttribute_GetArgumentCount(Ptr);

    /// <summary><c>getArgumentType</c>, or null.</summary>
    public SlangType? GetArgumentType(uint index) =>
        SlangType.From(SlangApi.spReflectionUserAttribute_GetArgumentType(Ptr, index), Owner);

    /// <summary><c>getArgumentValueInt</c>.</summary>
    public bool TryGetArgumentValueInt(uint index, out int value)
    {
        int v;
        var ok = SlangResult.Succeeded(SlangApi.spReflectionUserAttribute_GetArgumentValueInt(Ptr, index, &v));
        value = ok ? v : 0;
        return ok;
    }

    /// <summary><c>getArgumentValueFloat</c>.</summary>
    public bool TryGetArgumentValueFloat(uint index, out float value)
    {
        float v;
        var ok = SlangResult.Succeeded(SlangApi.spReflectionUserAttribute_GetArgumentValueFloat(Ptr, index, &v));
        value = ok ? v : 0;
        return ok;
    }

    /// <summary><c>getArgumentValueString</c>, decoded by the length Slang reports; null when the argument is not a string.</summary>
    public string? GetArgumentValueString(uint index)
    {
        nuint length;
        var s = SlangApi.spReflectionUserAttribute_GetArgumentValueString(Ptr, index, &length);
        return s is null ? null : new string(s, 0, checked((int)length), System.Text.Encoding.UTF8);
    }
}

/// <summary>
/// <c>slang::Modifier</c>: a modifier a declaration carries, as <c>findModifier</c> returns it.
/// Slang's API has no call on a modifier; its presence is the answer.
/// </summary>
public sealed unsafe class SlangModifier : SlangReflectionObject
{
    private SlangModifier(SlangReflectionModifier* p, SlangObject owner)
        : base(p, owner, nameof(SlangModifier)) { }

    internal static SlangModifier? From(SlangReflectionModifier* p, SlangObject owner) =>
        p is null ? null : new SlangModifier(p, owner);
}
