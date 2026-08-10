// L2 — a linked program: the thing you can generate code from and reflect over.

using PrismSlang.Interop;
using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Spirv;

namespace PrismSlang;

/// <summary>
/// A composed and linked program. Code generation and reflection both require linking first,
/// because that is when cross-module references and specialisation are resolved.
/// </summary>
public sealed unsafe class LinkedProgram : SlangObject<IComponentType>
{
    private readonly string[] _entryPointNames;
    private readonly IReadOnlyList<TargetOptions> _targets;

    internal LinkedProgram(
        IComponentType* owned,
        string[] entryPointNames,
        SlangSession owner,
        IReadOnlyList<TargetOptions> targets)
        : base(owned, nameof(LinkedProgram), owner)
    {
        _entryPointNames = entryPointNames;
        _targets = targets;
    }

    /// <summary>
    /// Entry point names <em>as declared in Slang</em>, in the order they were linked. Index into
    /// these for code retrieval.
    /// </summary>
    /// <remarks>
    /// Not the names the emitted SPIR-V uses. Those come from the module — see
    /// <see cref="GetSpirvEntryPoint"/> — and by default they are all <c>main</c>.
    /// </remarks>
    public IReadOnlyList<string> EntryPointNames => _entryPointNames;

    /// <summary>
    /// How many generic parameters still need concrete types before this program can generate
    /// code. Non-zero means it was linked without specialisation arguments.
    /// </summary>
    public long SpecializationParameterCount => Ptr->getSpecializationParamCount();

    /// <summary>
    /// Finds a named type in this program's reflection, or null. Borrows from the program.
    /// </summary>
    public SlangType? FindType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        using Utf8.Scoped n = new(name);
        var type = SlangApi.spReflection_FindTypeByName((SlangProgramLayout*)layout, n);
        return type is null ? null : new SlangType((TypeReflection*)type, this, name);
    }

    /// <summary>
    /// Supplies concrete arguments to a generic declaration and reports the resulting type and its
    /// layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "What would <c>Wrapper&lt;Lambert&gt;</c> lay out as", asked without writing a shader that
    /// declares one. A material system that has just chosen an implementation needs the field
    /// offsets before it can fill a buffer, and the alternative was to author a shader per
    /// candidate and reflect that.
    /// </para>
    /// <para>
    /// Distinct from <c>LinkOptions.SpecializationArguments</c>, which specializes the
    /// <em>program</em> at link time and changes the code that gets generated. This asks a
    /// question about a type and generates nothing.
    /// </para>
    /// <para>
    /// The route is not the obvious one. <c>spReflection_specializeType</c> takes a type and
    /// argument types and looks like exactly this call; against a generic reached by name it
    /// returns null, with no diagnostics. What works is to take the type's <em>generic
    /// container</em> and specialize that — see §9.20.
    /// </para>
    /// </remarks>
    /// <param name="genericName">The generic's declared name, unqualified — <c>Wrapper</c>.</param>
    /// <param name="arguments">One per generic parameter, in declaration order.</param>
    /// <returns>The specialized type, or null if the program has no such generic.</returns>
    /// <exception cref="ArgumentException">An argument was default-constructed, or none were given.</exception>
    /// <exception cref="SlangException">Slang refused the specialization; its diagnostics are attached.</exception>
    public SpecializedType? SpecializeGeneric(string genericName, params ReadOnlySpan<GenericArgument> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(genericName);
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

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        var shader = (SlangProgramLayout*)layout;

        SlangReflectionType* declared;
        using (Utf8.Scoped n = new(genericName))
        {
            declared = SlangApi.spReflection_FindTypeByName(shader, n);
        }

        if (declared is null) return null;

        // The generic container, not the type. FindTypeByName on `Wrapper` hands back a struct
        // whose full name is `Wrapper<T>` — the declaration with its parameter still symbolic —
        // and that is not what specializeGeneric takes.
        var generic = SlangApi.spReflectionType_GetGenericContainer(declared);
        if (generic is null)
        {
            throw new ArgumentException(
                $"'{genericName}' is a type in this program but not a generic one, so there is "
                + "nothing to specialize.", nameof(genericName));
        }

        RequireArgumentsMatch(genericName, generic, arguments);

        var kinds = new SlangReflectionGenericArgType[arguments.Length];
        var values = new SlangReflectionGenericArg[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            kinds[i] = arguments[i].Kind;
            values[i] = arguments[i].ToNative();
        }

        SlangReflectionGeneric* specialized;
        ISlangBlob* specializeDiagnostics = null;

        fixed (SlangReflectionGenericArgType* pKinds = kinds)
        fixed (SlangReflectionGenericArg* pValues = values)
        {
            specialized = SlangApi.spReflection_specializeGeneric(
                shader, generic, arguments.Length, pKinds, pValues, &specializeDiagnostics);
        }

        Diagnostics.ThrowIfNull(
            specialized, $"specialize '{genericName}'", specializeDiagnostics);

        return ReflectionWalker.ReadSpecialization(shader, specialized);
    }

    /// <summary>
    /// Checks the arguments against the generic's declaration before Slang sees them.
    /// </summary>
    /// <remarks>
    /// Not defensive tidiness — a guard against a crash. Handed an <c>int</c> where a type
    /// parameter belongs, <c>spReflection_specializeGeneric</c> access-violates and takes the
    /// process with it: no diagnostics, no result, nothing to catch. Measured on 2026.14.1 and
    /// asserted from a child process by <c>ProberTests</c>, because it cannot be asserted from
    /// inside the runner it would kill. See §9.20.
    ///
    /// Unlike §9.12's specialization-sized resource array, this one <em>is</em> detectable: the
    /// generic says how many parameters of each kind it takes, and comparing is cheap.
    ///
    /// The residue, stated because it is not covered: for a generic mixing type and value
    /// parameters the two are reported as separate lists, so their interleaving in the declaration
    /// is not recoverable and the counts can match while the order is wrong. That combination is
    /// not guarded here.
    /// </remarks>
    private static void RequireArgumentsMatch(
        string genericName, SlangReflectionGeneric* generic, ReadOnlySpan<GenericArgument> arguments)
    {
        var expectedTypes = (int)SlangApi.spReflectionGeneric_GetTypeParameterCount(generic);
        var expectedValues = (int)SlangApi.spReflectionGeneric_GetValueParameterCount(generic);

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

    /// <summary>
    /// What the compiler knows about the code generated for a target, beyond its interface.
    /// </summary>
    /// <remarks>
    /// Chiefly which parameter locations the emitted code actually reads. See
    /// <see cref="SlangMetadata"/> for why that is not a property of the reflection snapshot.
    /// </remarks>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public SlangMetadata GetTargetMetadata(int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        IMetadata* metadata = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getTargetMetadata(targetIndex, &metadata, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType::getTargetMetadata", diagnostics);
        Diagnostics.ThrowIfNull(metadata, "IComponentType::getTargetMetadata", null);

        return new SlangMetadata(metadata, this);
    }

    /// <summary>
    /// The same, for one entry point — which is the useful granularity.
    /// </summary>
    /// <remarks>
    /// A binding read by the fragment stage and not by the vertex stage is used by the program and
    /// unused by one of its stages, and only this call can tell them apart.
    /// </remarks>
    public SlangMetadata GetEntryPointMetadata(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        IMetadata* metadata = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getEntryPointMetadata(entryPointIndex, targetIndex, &metadata, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType::getEntryPointMetadata", diagnostics);
        Diagnostics.ThrowIfNull(metadata, "IComponentType::getEntryPointMetadata", null);

        return new SlangMetadata(metadata, this);
    }

    /// <summary>
    /// Finds a function by name and returns its signature, or null if the program has no such
    /// function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The complement to walking <see cref="SlangModule.ReflectDeclarations"/>: that enumerates
    /// what exists, this answers about one name. Both return the same
    /// <see cref="FunctionSignature"/> snapshot and neither lets a native pointer escape.
    /// </para>
    /// <para>
    /// This is also the only way to see an <em>overload set</em>. The declaration tree reports two
    /// declarations of the same name as two sibling nodes, each with its own signature, and
    /// <c>IsOverloaded</c> is false on both. Resolved by name, Slang returns one function that says
    /// it is overloaded and carries the real declarations underneath — so
    /// <see cref="FunctionSignature.Overloads"/> is populated here and never there.
    /// </para>
    /// </remarks>
    /// <param name="name">The function's name, unqualified.</param>
    public FunctionSignature? FindFunction(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        using Utf8.Scoped n = new(name);
        var function = SlangApi.spReflection_FindFunctionByName((SlangProgramLayout*)layout, n);
        return function is null ? null : ReflectionWalker.ReadFunctionSignature(function);
    }

    /// <summary>
    /// Finds a method on a type by name and returns its signature, or null.
    /// </summary>
    /// <remarks>
    /// What a material system asks when it has resolved a concrete type and needs to know whether
    /// that type's <c>shade</c> takes the arguments it is about to pass.
    /// </remarks>
    public FunctionSignature? FindFunction(SlangType type, string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        using Utf8.Scoped n = new(name);
        var function = SlangApi.spReflection_FindFunctionByNameInType(
            (SlangProgramLayout*)layout, (SlangReflectionType*)type.Handle, n);

        return function is null ? null : ReflectionWalker.ReadFunctionSignature(function);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a subtype of <paramref name="baseType"/> — i.e. whether
    /// a concrete type satisfies an interface. Use before creating a conformance to fail with a
    /// clear message rather than a Slang diagnostic.
    /// </summary>
    public bool IsSubType(SlangType type, SlangType baseType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(baseType);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        return SlangApi.spReflection_isSubType(
            (SlangProgramLayout*)layout,
            (SlangReflectionType*)type.Handle,
            (SlangReflectionType*)baseType.Handle) != 0;
    }

    /// <summary>Generated code for one entry point, for the given target.</summary>
    /// <param name="entryPointIndex">Index into <see cref="EntryPointNames"/>.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    /// <returns>An owned blob; dispose it, or use <see cref="GetEntryPointCodeBytes"/>.</returns>
    public SlangBlob GetEntryPointCode(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);

        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var operation = $"getEntryPointCode('{_entryPointNames[entryPointIndex]}')";
        var hr = Ptr->getEntryPointCode(entryPointIndex, targetIndex, &code, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, operation, diagnostics);

        if (code is null)
            throw new SlangException(SlangResult.Fail, $"{operation} produced no code", null);

        var blob = new SlangBlob(code, this);
        try
        {
            Verify(blob.Span, targetIndex, operation);
            return blob;
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    /// <summary>Generated code for one entry point, copied into a managed array.</summary>
    public byte[] GetEntryPointCodeBytes(int entryPointIndex, int targetIndex = 0)
    {
        using var blob = GetEntryPointCode(entryPointIndex, targetIndex);
        return blob.ToArray();
    }

    /// <summary>Generated code for one entry point, resolved by name.</summary>
    public byte[] GetEntryPointCodeBytes(string entryPointName, int targetIndex = 0)
    {
        var i = Array.IndexOf(_entryPointNames, entryPointName);
        if (i < 0)
            throw new ArgumentException(
                $"'{entryPointName}' was not linked into this program. Linked: {string.Join(", ", _entryPointNames)}",
                nameof(entryPointName));
        return GetEntryPointCodeBytes(i, targetIndex);
    }

    /// <summary>
    /// Whole-program code for a target, rather than per entry point. Requires the target to have
    /// been created with whole-program generation; most Vulkan use wants per-entry-point instead.
    /// </summary>
    public SlangBlob GetTargetCode(int targetIndex = 0)
    {
        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getTargetCode(targetIndex, &code, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "getTargetCode", diagnostics);

        if (code is null) throw new SlangException(SlangResult.Fail, "getTargetCode produced no code", null);

        var blob = new SlangBlob(code, this);
        try
        {
            Verify(blob.Span, targetIndex, "getTargetCode");
            return blob;
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The entry point's name and stage <em>in the emitted module</em> — what a pipeline builder
    /// puts in <c>VkPipelineShaderStageCreateInfo::pName</c>.
    /// </summary>
    /// <remarks>
    /// Reads it out of the SPIR-V rather than deriving it. Slang names every per-entry-point module
    /// <c>main</c> by default whatever the function was called, and
    /// <see cref="TargetOptions.UseEntryPointNameInSpirv"/> changes that — so neither answer is
    /// safe to hardcode, on either side of the artifact.
    /// </remarks>
    /// <exception cref="InvalidDataException">The target does not emit SPIR-V, or the module is malformed.</exception>
    public SpirvEntryPoint GetSpirvEntryPoint(int entryPointIndex, int targetIndex = 0)
    {
        using var code = GetEntryPointCode(entryPointIndex, targetIndex);
        return SpirvModule.SingleEntryPoint(code.Span);
    }

    /// <summary>
    /// Checks a generated module against the target it was generated for.
    /// </summary>
    /// <remarks>
    /// Only the header: magic, word alignment, and the version. That is enough to turn the two
    /// failures that otherwise surface at <c>vkCreateShaderModule</c> — "this is not SPIR-V" and
    /// "this is a newer SPIR-V than the device accepts" — into an exception naming the target.
    /// </remarks>
    private void Verify(ReadOnlySpan<byte> code, int targetIndex, string operation)
    {
        if (targetIndex < 0 || targetIndex >= _targets.Count) return;

        var target = _targets[targetIndex];
        if (!target.IsSpirv || !target.VerifyEmittedSpirv) return;

        if (!SpirvModule.IsSpirv(code))
            throw new SlangException(SlangResult.Ok, operation,
                $"target {targetIndex} is SPIR-V but produced {code.Length} bytes that are not a " +
                $"SPIR-V module. This is a Slang bug or a corrupted blob, not a shader error.");

        var expected = target.ExpectedSpirvVersion;
        if (expected == SpirvVersion.Unspecified) return;

        var actual = SpirvModule.ReadVersion(code);
        if (actual == expected) return;

        throw new SlangException(SlangResult.Ok, operation,
            $"target {targetIndex} asked for SPIR-V {expected.ToDisplayString()} but Slang emitted " +
            $"{actual.ToDisplayString()}. A module above the device's supported SPIR-V version is " +
            $"rejected by vkCreateShaderModule with no further detail, so it is caught here instead.");
    }

    /// <summary>
    /// The native reflection root for this program, for L3's use only.
    /// </summary>
    /// <remarks>
    /// Borrowed, not ref-counted: owned by this program and invalid once it is disposed. It must
    /// not escape the reflection walk — see docs §4.4. Deliberately internal.
    /// </remarks>
    internal ShaderReflection* GetLayout(int targetIndex = 0)
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(targetIndex, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);
        return layout;
    }
}
