// L2 — slang::ShaderReflection (ProgramLayout in slang.h): the root of a component type's
// reflection, one member per spReflection_* function, in Slang's argument order.
//
// Borrowed from the component type whose getLayout produced it (SlangReflectionObject). Slang's
// C++ classes in slang.h have no vtable and forward inline to these flat functions, so this is the
// reflection ABI (AGENTS.md#upstream-shape).

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// <c>slang::ShaderReflection</c>: a program's parameters, entry points, type parameters and
/// type lookups, for one target. From <see cref="LinkedProgram.GetLayout"/>.
/// </summary>
public sealed unsafe class SlangShaderReflection : SlangReflectionObject
{
    internal SlangShaderReflection(SlangProgramLayout* p, SlangObject owner)
        : base(p, owner, nameof(SlangShaderReflection)) { }

    internal SlangProgramLayout* Ptr => (SlangProgramLayout*)Raw;

    /// <summary><c>getParameterCount</c>: the global shader parameters.</summary>
    public uint ParameterCount => SlangApi.spReflection_GetParameterCount(Ptr);

    /// <summary><c>getParameterByIndex</c>.</summary>
    public SlangVariableLayout? GetParameterByIndex(uint index) =>
        SlangVariableLayout.From(SlangApi.spReflection_GetParameterByIndex(Ptr, index), Owner);

    /// <summary><c>getTypeParameterCount</c>: the program's unbound generic parameters.</summary>
    public uint TypeParameterCount => SlangApi.spReflection_GetTypeParameterCount(Ptr);

    /// <summary><c>getTypeParameterByIndex</c>.</summary>
    public SlangTypeParameter? GetTypeParameterByIndex(uint index) =>
        SlangTypeParameter.From(SlangApi.spReflection_GetTypeParameterByIndex(Ptr, index), Owner);

    /// <summary><c>findTypeParameter</c>, or null.</summary>
    public SlangTypeParameter? FindTypeParameter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangTypeParameter.From(SlangApi.spReflection_FindTypeParameter(Ptr, n), Owner);
    }

    /// <summary><c>getEntryPointCount</c>.</summary>
    public ulong EntryPointCount => SlangApi.spReflection_getEntryPointCount(Ptr);

    /// <summary><c>getEntryPointByIndex</c>.</summary>
    public SlangEntryPointReflection? GetEntryPointByIndex(ulong index) =>
        SlangEntryPointReflection.From(SlangApi.spReflection_getEntryPointByIndex(Ptr, index), Owner);

    /// <summary><c>findEntryPointByName</c>, or null.</summary>
    public SlangEntryPointReflection? FindEntryPointByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangEntryPointReflection.From(SlangApi.spReflection_findEntryPointByName(Ptr, n), Owner);
    }

    /// <summary>
    /// <c>getGlobalConstantBufferBinding</c>: where the implicit buffer for loose global uniforms
    /// sits. Slang reports 0 whether or not one exists; ask
    /// <see cref="GlobalConstantBufferSize"/> first (AGENTS.md#reflection-snapshot).
    /// </summary>
    public ulong GlobalConstantBufferBinding => SlangApi.spReflection_getGlobalConstantBufferBinding(Ptr);

    /// <summary><c>getGlobalConstantBufferSize</c>: 0 when there are no loose global uniforms.</summary>
    public ulong GlobalConstantBufferSize => SlangApi.spReflection_getGlobalConstantBufferSize(Ptr);

    /// <summary><c>findTypeByName</c>, or null. Does not find a generic function; see <see cref="FindFunctionByName"/>.</summary>
    public SlangType? FindTypeByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangType.From(SlangApi.spReflection_FindTypeByName(Ptr, n), Owner);
    }

    /// <summary><c>findFunctionByName</c>, or null. An overloaded name comes back as one function with <see cref="SlangFunction.IsOverloaded"/> set.</summary>
    public SlangFunction? FindFunctionByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangFunction.From(SlangApi.spReflection_FindFunctionByName(Ptr, n), Owner);
    }

    /// <summary><c>findFunctionByNameInType</c>: a method of <paramref name="type"/>, or null.</summary>
    public SlangFunction? FindFunctionByNameInType(SlangType type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangFunction.From(SlangApi.spReflection_FindFunctionByNameInType(Ptr, type.Ptr, n), Owner);
    }

    /// <summary><c>findVarByNameInType</c>: a field or static of <paramref name="type"/>, or null.</summary>
    public SlangVariable? FindVarByNameInType(SlangType type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        using Utf8.Scoped n = new(name);
        return SlangVariable.From(SlangApi.spReflection_FindVarByNameInType(Ptr, type.Ptr, n), Owner);
    }

    /// <summary><c>getTypeLayout</c>: <paramref name="type"/> laid out under <paramref name="rules"/>, or null.</summary>
    public SlangTypeLayout? GetTypeLayout(SlangType type, SlangLayoutRules rules = SlangLayoutRules.SLANG_LAYOUT_RULES_DEFAULT)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SlangTypeLayout.From(SlangApi.spReflection_GetTypeLayout(Ptr, type.Ptr, rules), Owner);
    }

    /// <summary>
    /// <c>specializeGeneric</c>: <paramref name="generic"/> with concrete arguments, in declaration
    /// order.
    /// </summary>
    /// <remarks>
    /// Checked against the generic's own parameter counts before Slang sees them, because Slang
    /// does not report a mismatch: an <c>int</c> where a type parameter belongs, or two type
    /// arguments for one parameter, access-violates on win-x64, linux-x64 and linux-arm64, and on
    /// osx-arm64 reports <c>E30060</c> and still returns a non-null generic (v2026.21, the prober,
    /// all four legs; U3, AGENTS.md#reflection-snapshot). The residue: a generic mixing type and
    /// value parameters reports them as two lists, so their order is not checkable.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// No arguments, a default-constructed one, or a count of type or value arguments the generic
    /// does not take.
    /// </exception>
    /// <exception cref="SlangException">Slang refused the specialization; its diagnostics are attached.</exception>
    public SlangGeneric SpecializeGeneric(SlangGeneric generic, params ReadOnlySpan<GenericArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(generic);
        if (arguments.Length == 0)
            throw new ArgumentException("a generic needs at least one argument", nameof(arguments));

        for (var i = 0; i < arguments.Length; i++)
        {
            if (!arguments[i].IsSet)
            {
                throw new ArgumentException(
                    $"argument {i} was default-constructed; build one with GenericArgument.OfType, "
                    + "OfInt or OfBool", nameof(arguments));
            }
        }

        var name = generic.Name ?? "<unnamed>";
        RequireArgumentsMatch(name, generic, arguments);

        var kinds = new SlangReflectionGenericArgType[arguments.Length];
        var values = new SlangReflectionGenericArg[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            kinds[i] = arguments[i].Kind;
            values[i] = arguments[i].ToNative();
        }

        SlangReflectionGeneric* specialized;
        ISlangBlob* diagnostics = null;
        fixed (SlangReflectionGenericArgType* pKinds = kinds)
        fixed (SlangReflectionGenericArg* pValues = values)
        {
            specialized = SlangApi.spReflection_specializeGeneric(
                Ptr, generic.Ptr, arguments.Length, pKinds, pValues, &diagnostics);
        }

        Diagnostics.ThrowIfNull(specialized, $"specialize '{name}'", diagnostics);
        return new SlangGeneric(specialized, Owner);
    }

    private static void RequireArgumentsMatch(
        string genericName, SlangGeneric generic, ReadOnlySpan<GenericArgument> arguments)
    {
        var expectedTypes = (int)generic.TypeParameterCount;
        var expectedValues = (int)generic.ValueParameterCount;

        var givenTypes = 0;
        foreach (var argument in arguments)
        {
            if (argument.Kind == SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE) givenTypes++;
        }

        var givenValues = arguments.Length - givenTypes;
        if (givenTypes == expectedTypes && givenValues == expectedValues) return;

        throw new ArgumentException(
            $"'{genericName}' takes {Describe(expectedTypes, expectedValues)}, and was given "
            + $"{Describe(givenTypes, givenValues)}. Slang does not report this — it "
            + "access-violates and ends the process — so it is refused here.",
            nameof(arguments));

        static string Describe(int types, int values) => (types, values) switch
        {
            (0, 0) => "no arguments",
            (_, 0) => $"{types} type argument(s)",
            (0, _) => $"{values} value argument(s)",
            _ => $"{types} type and {values} value argument(s)",
        };
    }

    /// <summary><c>isSubType</c>: whether <paramref name="subType"/> conforms to <paramref name="superType"/>.</summary>
    public bool IsSubType(SlangType subType, SlangType superType)
    {
        ArgumentNullException.ThrowIfNull(subType);
        ArgumentNullException.ThrowIfNull(superType);
        return SlangApi.spReflection_isSubType(Ptr, subType.Ptr, superType.Ptr) != 0;
    }

    /// <summary><c>getHashedStringCount</c>: the string literals the program passes to <c>getStringHash</c>.</summary>
    public ulong HashedStringCount => SlangApi.spReflection_getHashedStringCount(Ptr);

    /// <summary><c>getHashedString</c>, decoded by the length Slang reports; null for none.</summary>
    public string? GetHashedString(ulong index)
    {
        nuint length;
        var s = SlangApi.spReflection_getHashedString(Ptr, index, &length);
        return s is null ? null : new string(s, 0, checked((int)length), System.Text.Encoding.UTF8);
    }

    /// <summary><c>getGlobalParamsTypeLayout</c>: the global scope as one type, with its binding and descriptor-set ranges.</summary>
    public SlangTypeLayout? GlobalParamsTypeLayout =>
        SlangTypeLayout.From(SlangApi.spReflection_getGlobalParamsTypeLayout(Ptr), Owner);

    /// <summary><c>getGlobalParamsVarLayout</c>: the global scope as one variable.</summary>
    public SlangVariableLayout? GlobalParamsVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflection_getGlobalParamsVarLayout(Ptr), Owner);

    /// <summary><c>getBindlessSpaceIndex</c>: the register space reserved for a bindless heap, or a negative value when there is none.</summary>
    public long BindlessSpaceIndex => SlangApi.spReflection_getBindlessSpaceIndex(Ptr);

    /// <summary>
    /// <c>spReflection_ToJson</c>: Slang's own reflection dump, the text <c>slangc
    /// -reflection-json</c> writes, or null if Slang produced none.
    /// </summary>
    /// <remarks>
    /// The function takes an <c>ICompileRequest</c>, which belongs to the legacy API this binding
    /// leaves unbound (AGENTS.md#unbound-surface). Null is passed, and measured to work; a release
    /// that stops accepting it makes this return null rather than crash.
    /// </remarks>
    public string? ToJson()
    {
        ISlangBlob* json = null;
        var hr = SlangApi.spReflection_ToJson(Ptr, null, &json);
        if (SlangResult.Failed(hr) || json is null) return null;

        using var blob = new SlangBlob(json);
        return blob.AsText();
    }

    /// <summary>
    /// <c>tryResolveOverloadedFunction</c>: the candidate a call would resolve to, or null when
    /// the candidates do not narrow to one.
    /// </summary>
    public SlangFunction? TryResolveOverloadedFunction(params ReadOnlySpan<SlangFunction> candidates)
    {
        var pointers = new SlangReflectionFunction*[Math.Max(candidates.Length, 1)];
        for (var i = 0; i < candidates.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(candidates[i]);
            pointers[i] = candidates[i].Ptr;
        }

        fixed (SlangReflectionFunction** p = pointers)
        {
            return SlangFunction.From(
                SlangApi.spReflection_TryResolveOverloadedFunction(Ptr, (uint)candidates.Length, p), Owner);
        }
    }
}
