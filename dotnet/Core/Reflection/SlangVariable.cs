// L2 — slang::VariableReflection and slang::VariableLayoutReflection, one member per
// spReflectionVariable_* and spReflectionVariableLayout_* function, in Slang's argument order.
// Borrowed (SlangReflectionObject).

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary><c>slang::VariableReflection</c>: a declared variable — a parameter, a field, a generic parameter.</summary>
public sealed unsafe class SlangVariable : SlangReflectionObject
{
    private SlangVariable(SlangReflectionVariable* p, SlangObject owner)
        : base(p, owner, nameof(SlangVariable)) { }

    internal static SlangVariable? From(SlangReflectionVariable* p, SlangObject owner) =>
        p is null ? null : new SlangVariable(p, owner);

    internal SlangReflectionVariable* Ptr => (SlangReflectionVariable*)Raw;

    /// <summary><c>getName</c>.</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionVariable_GetName(Ptr));

    /// <summary><c>getType</c>, or null.</summary>
    public SlangType? Type => SlangType.From(SlangApi.spReflectionVariable_GetType(Ptr), Owner);

    /// <summary><c>findModifier</c>: the modifier if the declaration carries it, or null.</summary>
    public SlangModifier? FindModifier(SlangModifierID id) =>
        SlangModifier.From(SlangApi.spReflectionVariable_FindModifier(Ptr, id), Owner);

    /// <summary><c>getUserAttributeCount</c>.</summary>
    public uint UserAttributeCount => SlangApi.spReflectionVariable_GetUserAttributeCount(Ptr);

    /// <summary><c>getUserAttributeByIndex</c>.</summary>
    public SlangUserAttribute? GetUserAttribute(uint index) =>
        SlangUserAttribute.From(SlangApi.spReflectionVariable_GetUserAttribute(Ptr, index), Owner);

    /// <summary><c>findUserAttributeByName</c>, which on a variable takes the global session; or null.</summary>
    public SlangUserAttribute? FindUserAttributeByName(SlangGlobalSession globalSession, string name)
    {
        ArgumentNullException.ThrowIfNull(globalSession);
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return SlangUserAttribute.From(
            SlangApi.spReflectionVariable_FindUserAttributeByName(Ptr, globalSession.Handle, n), Owner);
    }

    /// <summary><c>hasDefaultValue</c>.</summary>
    public bool HasDefaultValue => SlangApi.spReflectionVariable_HasDefaultValue(Ptr) != 0;

    /// <summary>
    /// <c>getDefaultValueInt</c>. Succeeds on a floating-point default too; read the type first
    /// (measured, v2026.21: <c>uint kFlags = 7</c> answers both).
    /// </summary>
    public bool TryGetDefaultValueInt(out long value)
    {
        long v;
        var ok = SlangResult.Succeeded(SlangApi.spReflectionVariable_GetDefaultValueInt(Ptr, &v));
        value = ok ? v : 0;
        return ok;
    }

    /// <summary><c>getDefaultValueFloat</c>. Succeeds on an integral default too; read the type first.</summary>
    public bool TryGetDefaultValueFloat(out float value)
    {
        float v;
        var ok = SlangResult.Succeeded(SlangApi.spReflectionVariable_GetDefaultValueFloat(Ptr, &v));
        value = ok ? v : 0;
        return ok;
    }

    /// <summary><c>getGenericContainer</c>, or null.</summary>
    public SlangGeneric? GenericContainer => SlangGeneric.From(SlangApi.spReflectionVariable_GetGenericContainer(Ptr), Owner);

    /// <summary><c>applySpecializations</c>: this variable with <paramref name="generic"/>'s arguments substituted, or null.</summary>
    public SlangVariable? ApplySpecializations(SlangGeneric generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return From(SlangApi.spReflectionVariable_applySpecializations(Ptr, generic.Ptr), Owner);
    }
}

/// <summary><c>slang::VariableLayoutReflection</c>: a variable as laid out — its offsets, spaces and semantic.</summary>
public sealed unsafe class SlangVariableLayout : SlangReflectionObject
{
    private SlangVariableLayout(SlangReflectionVariableLayout* p, SlangObject owner)
        : base(p, owner, nameof(SlangVariableLayout)) { }

    internal static SlangVariableLayout? From(SlangReflectionVariableLayout* p, SlangObject owner) =>
        p is null ? null : new SlangVariableLayout(p, owner);

    internal SlangReflectionVariableLayout* Ptr => (SlangReflectionVariableLayout*)Raw;

    /// <summary><c>getVariable</c>, or null (a sub-object range's offset has none).</summary>
    public SlangVariable? Variable => SlangVariable.From(SlangApi.spReflectionVariableLayout_GetVariable(Ptr), Owner);

    /// <summary><c>getTypeLayout</c>, or null.</summary>
    public SlangTypeLayout? TypeLayout => SlangTypeLayout.From(SlangApi.spReflectionVariableLayout_GetTypeLayout(Ptr), Owner);

    /// <summary>
    /// <c>getOffset</c>: the offset in <paramref name="category"/>. For a register-space category
    /// the offset is the space index, and <see cref="GetSpace"/> stays 0 (AGENTS.md#reflection-snapshot).
    /// </summary>
    public ulong GetOffset(SlangParameterCategory category = SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) =>
        SlangApi.spReflectionVariableLayout_GetOffset(Ptr, category);

    /// <summary><c>getBindingSpace</c> for a category (the flat <c>GetSpace</c>).</summary>
    public ulong GetSpace(SlangParameterCategory category) =>
        SlangApi.spReflectionVariableLayout_GetSpace(Ptr, category);

    /// <summary><c>getImageFormat</c>: the <c>[format(...)]</c> a storage texture was declared with.</summary>
    public SlangImageFormat ImageFormat => SlangApi.spReflectionVariableLayout_GetImageFormat(Ptr);

    /// <summary><c>getSemanticName</c>, or null.</summary>
    public string? SemanticName => Utf8.Read(SlangApi.spReflectionVariableLayout_GetSemanticName(Ptr));

    /// <summary><c>getSemanticIndex</c>.</summary>
    public ulong SemanticIndex => SlangApi.spReflectionVariableLayout_GetSemanticIndex(Ptr);

    /// <summary><c>getStage</c>: the stage a varying belongs to, <c>NONE</c> for a global.</summary>
    public SlangStage Stage => SlangApi.spReflectionVariableLayout_getStage(Ptr);

    /// <summary><c>getPendingDataLayout</c>, or null.</summary>
    public SlangVariableLayout? PendingDataLayout => From(SlangApi.spReflectionVariableLayout_getPendingDataLayout(Ptr), Owner);

    /// <summary><c>spReflectionParameter_GetBindingIndex</c>: the offset in the parameter's own category.</summary>
    public uint BindingIndex => SlangApi.spReflectionParameter_GetBindingIndex(Ptr);

    /// <summary><c>spReflectionParameter_GetBindingSpace</c>: the space in the parameter's own category.</summary>
    public uint BindingSpace => SlangApi.spReflectionParameter_GetBindingSpace(Ptr);
}
