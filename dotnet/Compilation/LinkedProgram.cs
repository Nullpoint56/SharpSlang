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
    /// container</em> and specialize that —
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

        SlangReflectionType* declaredType;
        SlangReflectionFunction* declaredFunction;
        using (Utf8.Scoped n = new(genericName))
        {
            declaredType = SlangApi.spReflection_FindTypeByName(shader, n);
            declaredFunction = SlangApi.spReflection_FindFunctionByName(shader, n);
        }

        // Both are asked, because a generic is a declaration of some kind with parameters attached
        // and Slang looks the two up through different tables. `struct Wrapper<T>` is only findable
        // as a type; `float3 apply<T>(…)` is only findable as a function, and looking for the
        // second among types is how this returned null for every generic function.
        //
        // The generic *container*, not the declaration. FindTypeByName on `Wrapper` hands back a
        // struct whose full name is `Wrapper<T>` — the declaration with its parameter still
        // symbolic — and that is not what specializeGeneric takes.
        var generic =
            declaredType is not null ? SlangApi.spReflectionType_GetGenericContainer(declaredType) : null;

        if (generic is null && declaredFunction is not null)
            generic = SlangApi.spReflectionFunction_GetGenericContainer(declaredFunction);

        if (declaredType is null && declaredFunction is null) return null;

        if (generic is null)
        {
            throw new ArgumentException(
                $"'{genericName}' is a {(declaredType is not null ? "type" : "function")} in this "
                + "program but not a generic one, so there is nothing to specialize.",
                nameof(genericName));
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
    /// Not defensive tidiness, and load-bearing on every platform for two different reasons.
    /// Handed an argument list that does not match the generic's parameters,
    /// <c>spReflection_specializeGeneric</c> behaves three ways on 2026.14.1, and the same on 2026.21
    /// where re-measured, which is win-x64 and linux-x64 so far:
    /// <list type="bullet">
    /// <item>win-x64 — <c>0xC0000005</c>, which the CLR does not convert into anything catchable.</item>
    /// <item>linux-x64 and linux-arm64 — an access violation, both architectures.</item>
    /// <item>osx-arm64 — <c>error[E30060]: expected a type</c>, and a <b>non-null</b> generic
    /// returned alongside it. Not a crash, and arguably worse: a caller checking only for null
    /// proceeds with a specialization Slang has already rejected.</item>
    /// </list>
    /// So the guard prevents a dead process on two platforms and a silently bogus result on the
    /// third. Asserted from a child process by <c>ProberTests</c>, because two of the three cannot
    /// be asserted from inside the runner they would kill.
    ///
    /// Unlike 's specialization-sized resource array, this one <em>is</em> detectable: the
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
    /// Picks the overload of <paramref name="name"/> that a call with these argument types would
    /// resolve to, or null if none matches.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="FindFunction(string)"/>, which reports that a name is
    /// overloaded and hands back the candidates. This answers which one a call site gets — the
    /// question a material system asks before binding a hook to a function it did not write.
    ///
    /// Two routes exist and both are used, because neither alone is enough. Slang's
    /// <c>specializeWithArgTypes</c> resolves against a candidate set on the function itself;
    /// <c>TryResolveOverloadedFunction</c> resolves across a list the caller assembles. The first is
    /// tried, and the second picks up the case where an overloaded name does not narrow to one.
    /// </remarks>
    /// <param name="name">The function's name, unqualified.</param>
    /// <param name="argumentTypes">The call's argument types, in order.</param>
    public FunctionSignature? ResolveOverload(string name, params ReadOnlySpan<SlangType> argumentTypes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        var shader = (SlangProgramLayout*)layout;

        SlangReflectionFunction* function;
        using (Utf8.Scoped n = new(name))
        {
            function = SlangApi.spReflection_FindFunctionByName(shader, n);
        }

        if (function is null) return null;

        var types = new SlangReflectionType*[Math.Max(argumentTypes.Length, 1)];
        for (var i = 0; i < argumentTypes.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(argumentTypes[i]);
            types[i] = (SlangReflectionType*)argumentTypes[i].Handle;
        }

        SlangReflectionFunction* resolved;
        fixed (SlangReflectionType** pTypes = types)
        {
            resolved = SlangApi.spReflectionFunction_specializeWithArgTypes(
                function, argumentTypes.Length, pTypes);
        }

        // Where the name is an overload set and the call did not narrow it, the candidates can be
        // handed back for Slang to arbitrate. Only reached when the direct route declined.
        if (resolved is null && SlangApi.spReflectionFunction_isOverloaded(function) != 0)
        {
            var count = SlangApi.spReflectionFunction_getOverloadCount(function);
            var candidates = new SlangReflectionFunction*[Math.Max((int)count, 1)];
            for (uint i = 0; i < count; i++)
                candidates[i] = SlangApi.spReflectionFunction_getOverload(function, i);

            fixed (SlangReflectionFunction** pCandidates = candidates)
            {
                resolved = SlangApi.spReflection_TryResolveOverloadedFunction(
                    shader, count, pCandidates);
            }
        }

        return resolved is null ? null : ReflectionWalker.ReadFunctionSignature(resolved);
    }

    /// <summary>
    /// Slang's own reflection dump for this program, as JSON.
    /// </summary>
    /// <remarks>
    /// The same text <c>slangc -reflection-json</c> writes. Not the shape anything here reads —
    /// <see cref="ProgramLayout"/> is — but a second opinion in a bug report, and the only view
    /// that is Slang's own formatting rather than this package's reading of it.
    ///
    /// <c>spReflection_ToJson</c> takes an <c>ICompileRequest</c>, which belongs to the legacy API
    /// this binding does not bind (§5, Tier 4). Null is passed, and measured to work; a release
    /// that starts dereferencing it makes this return null rather than crash, because the result is
    /// checked rather than assumed.
    /// </remarks>
    /// <returns>The JSON, or null if Slang produced none.</returns>
    public string? ToJson()
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        ISlangBlob* json = null;
        var hr = SlangApi.spReflection_ToJson((SlangProgramLayout*)layout, null, &json);
        if (SlangResult.Failed(hr) || json is null) return null;

        using var blob = new SlangBlob(json);
        return blob.AsText();
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

    /// <summary>
    /// Slang's identity hash for one entry point: the source and the options it was compiled
    /// with, which is <b>not</b> a key for "is this the same compiled code".
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>getEntryPointHash</c>, and it was left unbound for a long time because the obvious
    /// use for it was the wrong one. Measured on 2026.14.1, the hash was stable across runs and
    /// changed with the source, but was <b>identical across optimization levels</b> while the
    /// emitted SPIR-V was not, so a pipeline cache keyed on it served stale code with no error.
    /// </para>
    /// <para>
    /// Measured on 2026.21, it follows the options too: every link-time and target option tried
    /// moved it, the optimisation level included. And it follows the options rather than the code.
    /// Naming the default optimisation level explicitly, <c>High</c> against <c>Maximal</c>, and a
    /// link-time <c>DebugInformation</c> each emit byte-identical SPIR-V under a different hash. So
    /// it no longer answers "same shader" across option sets, and as a cache key it errs towards a
    /// miss; nothing measured says it covers the compiler build. Pinned both ways by
    /// <c>LinkOptionsTests</c>, so the next change of meaning is a failing test.
    /// </para>
    /// <para>To key a pipeline cache, hash the bytes from <see cref="GetEntryPointCodeBytes"/>.</para>
    /// </remarks>
    /// <param name="entryPointIndex">Index into <see cref="EntryPointNames"/>.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public byte[] GetEntryPointIdentityHash(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);

        ISlangBlob* hash = null;
        Ptr->getEntryPointHash(entryPointIndex, targetIndex, &hash);

        // Returns void, so there is no result to check and null is the only failure signal there
        // is. Slang produces a hash for every entry point measured; an empty one would mean the
        // identity key silently degrades to "all shaders are the same shader".
        if (hash is null)
        {
            throw new SlangException(
                SlangResult.Fail,
                $"getEntryPointHash('{_entryPointNames[entryPointIndex]}') produced no hash",
                null);
        }

        using var blob = new SlangBlob(hash, this);
        var bytes = blob.ToArray();

        if (bytes.Length == 0)
        {
            throw new SlangException(
                SlangResult.Fail,
                $"getEntryPointHash('{_entryPointNames[entryPointIndex]}') produced an empty hash",
                null);
        }

        return bytes;
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
