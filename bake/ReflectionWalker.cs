// The walk: a linked program's reflection read once into the immutable managed snapshot.
//
// Built on SharpSlang's public L2 reflection classes only (SlangShaderReflection, SlangTypeLayout,
// SlangVariableLayout, ...; AGENTS.md#layers). Each of them borrows from the LinkedProgram and
// throws once it is disposed, so none of them escapes this file: the only thing that leaves is
// immutable managed data (AGENTS.md#reflection-snapshot).
//
// Cost is not a concern. These are O(1) reads over layout data Slang already computed, a few
// thousand of them at cook time, once per shader.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Reflection;

/// <summary>Builds a <see cref="ProgramLayout"/> from a linked program.</summary>
public static class ReflectionWalker
{
    /// <summary>Walks the reflection tree of a linked program for one target.</summary>
    /// <param name="program">A linked program. Must not be disposed.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public static ProgramLayout Walk(LinkedProgram program, int targetIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(program);

        var shader = program.GetLayout(targetIndex);

        // Types are shared across the tree (every `float` is the same TypeLayout*), so cache by
        // pointer. Also stops a cyclic or deeply-shared graph from exploding the walk.
        var cache = new Dictionary<nint, TypeLayout>();

        var parameters = new List<ShaderParameter>();
        var specializationConstants = new List<SpecializationConstant>();
        var paramCount = shader.ParameterCount;
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = shader.GetParameterByIndex(i)!;
            var parameter = ReadParameter(varLayout, cache);
            parameters.Add(parameter);

            if (parameter.IsSpecializationConstant)
                specializationConstants.Add(ReadSpecializationConstant(varLayout, parameter));
        }

        var entryPoints = new List<EntryPointLayout>();
        var epCount = shader.EntryPointCount;
        for (ulong i = 0; i < epCount; i++)
            entryPoints.Add(ReadEntryPoint(shader.GetEntryPointByIndex(i)!, cache));

        var hashed = new List<string>();
        var hashedCount = shader.HashedStringCount;
        for (ulong i = 0; i < hashedCount; i++)
        {
            if (shader.GetHashedString(i) is { } s) hashed.Add(s);
        }

        return new ProgramLayout
        {
            Parameters = parameters,
            EntryPoints = entryPoints,
            DescriptorSets = MarkImplicitGlobalBuffer(
                WithStageUsage(ReadDescriptorSets(shader), program, targetIndex, entryPoints),
                shader.GlobalConstantBufferSize,
                (uint)shader.GlobalConstantBufferBinding),
            GlobalConstantBufferSize = (nuint)shader.GlobalConstantBufferSize,
            GlobalConstantBufferBinding = (uint)shader.GlobalConstantBufferBinding,
            HashedStrings = hashed,
            SpecializationParameters = ReadSpecializationParameters(shader),
            SpecializationConstants = specializationConstants.OrderBy(c => c.ConstantId).ToList(),
            BindlessSpaceIndex = BindlessSpace(shader),
            SubObjectRanges = ReadSubObjectRanges(shader),
        };
    }

    /// <summary>
    /// Labels the range that is the implicit buffer for loose global uniforms, when there is one.
    /// </summary>
    /// <remarks>
    /// It arrives with a null name, because reflection attributes it to no declaration — so a host
    /// iterating ranges to build descriptors meets one it cannot identify and has to correlate it
    /// against <see cref="ProgramLayout.GlobalConstantBufferBinding"/> by index. This does that
    /// correlation once, here.
    /// <para>
    /// Deliberately narrow. The gate is the *size*: with no loose globals Slang reports a binding
    /// of 0 as well as a size of 0, and matching on the binding alone would label whatever real
    /// descriptor happens to sit at binding 0 on nearly every shader in the corpus. The name and
    /// binding type are checked too, so a shader that does have loose globals cannot have some
    /// other range at the same index mislabelled.
    /// </para>
    /// </remarks>
    private static List<DescriptorSet> MarkImplicitGlobalBuffer(
        List<DescriptorSet> sets,
        ulong size,
        uint binding)
    {
        if (size == 0) return sets;

        for (var s = 0; s < sets.Count; s++)
        {
            var ranges = sets[s].Ranges;
            for (var r = 0; r < ranges.Count; r++)
            {
                var range = ranges[r];
                if (range.Name is not null
                    || range.IndexOffset != binding
                    || range.BindingType is not SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER)
                {
                    continue;
                }

                var updated = new List<DescriptorRange>(ranges);
                updated[r] = range with { IsImplicitGlobalBuffer = true };
                sets[s] = sets[s] with { Ranges = updated };
                return sets;
            }
        }

        return sets;
    }

    /// <summary>
    /// Fills in which stages read each descriptor range, from the per-entry-point metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one fact in this snapshot that reflection cannot supply. Slang's global-scope view does
    /// not attribute a parameter to the entry points that use it. <c>IMetadata</c> knows, per entry
    /// point, whether the generated code touches a location — so asking it once per stage per
    /// range produces the attribution the layout view lacks.
    /// </para>
    /// <para>
    /// Done here rather than in the projection because the projection reads a snapshot and the
    /// compiler is gone by then, and because a baked package should carry the answer rather than
    /// recompute it.
    /// </para>
    /// <para>
    /// A range no stage claims keeps an empty list, which the projection reads as "every stage" —
    /// see <c>DescriptorRange.Stages</c>. Failing to ask at all, on a target whose backend does not
    /// track usage, does the same. Neither case narrows anything, which is the only safe direction:
    /// omitting a stage that really reads the binding is a driver rejection.
    /// </para>
    /// </remarks>
    private static List<DescriptorSet> WithStageUsage(
        List<DescriptorSet> sets,
        LinkedProgram program,
        int targetIndex,
        List<EntryPointLayout> entryPoints)
    {
        if (sets.Count == 0 || entryPoints.Count == 0) return sets;

        var metadata = new List<(SlangStage Stage, SlangMetadata Metadata)>(entryPoints.Count);
        try
        {
            for (var i = 0; i < entryPoints.Count; i++)
            {
                try
                {
                    metadata.Add((entryPoints[i].Stage, program.GetEntryPointMetadata(i, targetIndex)));
                }
                catch (SlangException)
                {
                    // This target cannot answer. Leaving the lists empty means "every stage".
                    return sets;
                }
            }

            return [.. sets.Select(set => set with
            {
                Ranges = [.. set.Ranges.Select(range => range with
                {
                    Stages = StagesUsing(metadata, set.SpaceOffset, range),
                })],
            })];
        }
        finally
        {
            foreach (var (_, m) in metadata) m.Dispose();
        }
    }

    private static List<SlangStage> StagesUsing(
        List<(SlangStage Stage, SlangMetadata Metadata)> metadata, uint space, DescriptorRange range)
    {
        var stages = new List<SlangStage>();

        foreach (var (stage, m) in metadata)
        {
            try
            {
                if (m.IsUsed(range.Category, space, range.IndexOffset) && !stages.Contains(stage))
                    stages.Add(stage);
            }
            catch (SlangException)
            {
                // One unanswerable location must not narrow the rest. Reported as "nobody claims
                // it", which the projection widens back to every stage.
                return [];
            }
        }

        return stages;
    }

    /// <summary>The space reserved for a bindless heap, or null for the usual "there isn't one".</summary>
    private static uint? BindlessSpace(SlangShaderReflection shader)
    {
        var index = shader.BindlessSpaceIndex;
        return index < 0 ? null : (uint)index;
    }

    /// <summary>
    /// The global scope's sub-object ranges: which of its binding ranges are objects with bindings
    /// of their own.
    /// </summary>
    private static List<SubObjectRange> ReadSubObjectRanges(SlangShaderReflection shader)
    {
        var result = new List<SubObjectRange>();

        var globalScope = shader.GlobalParamsTypeLayout;
        if (globalScope is null) return result;

        var count = globalScope.SubObjectRangeCount;
        for (long i = 0; i < count; i++)
        {
            var bindingRange = globalScope.GetSubObjectRangeBindingRangeIndex(i);
            if (bindingRange < 0) continue;

            // Not from getSubObjectRangeOffset. That returns a variable layout with no variable
            // behind it — measured on 2026.21, every name came back null — so the name comes from
            // the binding range it points at, which is the same object reached the other way.
            var variable = globalScope.GetBindingRangeLeafVariable(bindingRange);

            // No space here on purpose. getSubObjectRangeSpaceOffset reported 0 for a block
            // occupying set 1 and 0 for one occupying set 2, and getBindingRangeDescriptorSetIndex
            // reports 0 for both — it indexes the enclosing scope's own set list. Emitting either
            // as "the set" would be a wrong number in a field that looks authoritative, so the
            // record carries the name and the caller joins on DescriptorSets. See SubObjectRange.
            result.Add(new SubObjectRange((uint)bindingRange, variable?.Name));
        }

        return result;
    }

    /// <summary>
    /// Turns a specialization-constant parameter into its usable form: id, type, default.
    /// </summary>
    /// <remarks>
    /// The id is the parameter's binding <em>offset</em> in the <c>SPECIALIZATION_CONSTANT</c>
    /// category. Verified against the emitted module's <c>SpecId</c> decorations for explicit,
    /// non-contiguous and Slang-assigned ids alike — see
    /// <c>SpecializationConstantTests.Reflected_ids_match_the_emitted_SpecId_decorations</c>.
    ///
    /// The default value is read type-first rather than by probing. <c>GetDefaultValueFloat</c>
    /// succeeds on an integral constant too (measured: <c>uint kFlags = 7</c> returns SLANG_OK from
    /// both getters), so probing in the wrong order silently turns integers into floats.
    /// </remarks>
    private static SpecializationConstant ReadSpecializationConstant(
        SlangVariableLayout varLayout,
        ShaderParameter parameter)
    {
        var scalar = parameter.Type is ScalarLayout s
            ? s.ScalarType
            : SlangScalarType.SLANG_SCALAR_TYPE_NONE;

        var id = parameter.Bindings[0].Index;

        var variable = varLayout.Variable;
        if (variable is null || !variable.HasDefaultValue)
            return new SpecializationConstant(parameter.Name, id, scalar);

        var isFloat = scalar
            is SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64;

        if (isFloat)
        {
            return variable.TryGetDefaultValueFloat(out var f)
                ? new SpecializationConstant(parameter.Name, id, scalar, DefaultFloat: f)
                : new SpecializationConstant(parameter.Name, id, scalar);
        }

        return variable.TryGetDefaultValueInt(out var v)
            ? new SpecializationConstant(parameter.Name, id, scalar, DefaultInt: v)
            : new SpecializationConstant(parameter.Name, id, scalar);
    }

    /// <summary>
    /// Walks a module's declaration tree — its structs, interfaces, functions and generics.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Walk"/> because it hangs off <c>IModule</c>, not off a linked
    /// program's reflection, and because a renderer never needs it: this is for tooling and for a
    /// material system discovering which types implement which interfaces.
    /// </remarks>
    public static DeclarationNode WalkDeclarations(SlangModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var root = module.GetModuleReflection();
        return root is null
            ? new DeclarationNode(module.Name, DeclarationKind.Module, [])
            : ReadDecl(root, module.Name, depth: 0);
    }

    // A module's tree is shallow in practice, but a generic's inner decl can point back at its
    // container, so cap the recursion rather than trusting the shape.
    private const int MaxDeclarationDepth = 32;

    private static DeclarationNode ReadDecl(SlangDecl decl, string? fallbackName, int depth)
    {
        var name = decl.Name ?? fallbackName ?? "";
        var kind = decl.Kind switch
        {
            SlangDeclKind.SLANG_DECL_KIND_MODULE => DeclarationKind.Module,
            SlangDeclKind.SLANG_DECL_KIND_STRUCT => DeclarationKind.Struct,
            SlangDeclKind.SLANG_DECL_KIND_FUNC => DeclarationKind.Func,
            SlangDeclKind.SLANG_DECL_KIND_GENERIC => DeclarationKind.Generic,
            SlangDeclKind.SLANG_DECL_KIND_VARIABLE => DeclarationKind.Variable,
            SlangDeclKind.SLANG_DECL_KIND_NAMESPACE => DeclarationKind.Namespace,
            SlangDeclKind.SLANG_DECL_KIND_ENUM => DeclarationKind.Enum,
            _ => DeclarationKind.Unsupported,
        };

        var children = new List<DeclarationNode>();
        if (depth < MaxDeclarationDepth)
        {
            var count = decl.ChildrenCount;
            for (uint i = 0; i < count; i++)
            {
                if (decl.GetChild(i) is { } child) children.Add(ReadDecl(child, null, depth + 1));
            }
        }

        var generics = kind == DeclarationKind.Generic ? ReadGenericParameters(decl) : null;
        var function = kind == DeclarationKind.Func ? ReadFunction(decl) : null;

        // The declared type of a `let`/`static` at namespace or struct scope. castToVariable is
        // the only way in: a variable declaration is a decl node like any other and its type is
        // not on the node.
        string? variableType = null;
        if (kind == DeclarationKind.Variable && decl.CastToVariable() is { } variable)
            variableType = FullTypeName(variable.Type);

        // Nesting, for a generic inside a generic.
        string? outerGeneric = null;
        if (kind == DeclarationKind.Generic && decl.CastToGeneric()?.OuterGenericContainer is { } outer)
            outerGeneric = outer.Name;

        return new DeclarationNode(
            name, kind, children, generics, function,
            QualifiedNameOf(decl, name), ModifiersOfDecl(decl), variableType, outerGeneric);
    }

    /// <summary>
    /// The declaration's name with its enclosing declarations, dotted.
    /// </summary>
    /// <remarks>
    /// Walked upward with <c>getParent</c> rather than joined on the way down, so it reports the
    /// compiler's nesting rather than this walk's. The module node at the root is dropped: every
    /// name would otherwise begin with it, which is noise.
    /// </remarks>
    private static string QualifiedNameOf(SlangDecl decl, string name)
    {
        var parts = new List<string> { name };

        var parent = decl.Parent;
        for (var depth = 0; parent is not null && depth < MaxDeclarationDepth; depth++)
        {
            if (parent.Kind == SlangDeclKind.SLANG_DECL_KIND_MODULE) break;

            var parentName = parent.Name;
            if (!string.IsNullOrEmpty(parentName)) parts.Add(parentName);

            parent = parent.Parent;
        }

        parts.Reverse();
        return string.Join('.', parts);
    }

    /// <summary>Which of Slang's declaration modifiers this declaration carries.</summary>
    /// <remarks>
    /// There is no "list the modifiers" query, only one that answers about a single id, so each
    /// is asked in turn.
    /// </remarks>
    private static List<string>? ModifiersOfDecl(SlangDecl decl)
    {
        List<string>? found = null;

        foreach (var id in Enum.GetValues<SlangModifierID>())
        {
            if (decl.FindModifier(id) is null) continue;
            (found ??= []).Add(ModifierName(id));
        }

        return found;
    }

    /// <summary>
    /// The signature of a function reached by name rather than through the declaration tree.
    /// </summary>
    internal static FunctionSignature ReadFunctionSignature(SlangFunction function) =>
        ReadFunction(function, expandOverloads: true);

    /// <summary>
    /// What a specialized generic came out as: the bindings, the name, and the layout.
    /// </summary>
    /// <remarks>
    /// The bindings come from asking the <em>specialized</em> generic for its parameters and then
    /// for each parameter's concrete argument — <c>GetConcreteType</c> / <c>GetConcreteIntVal</c>,
    /// which only mean anything on a generic that has been specialized.
    ///
    /// The type comes the long way round: the generic's inner declaration, turned back into a type,
    /// with the specialization applied. <c>getSpecializedTypeArgCount</c> looks like the shortcut
    /// and reports 0 here — as it does everywhere else this project has asked it, including on
    /// types written as <c>Wrapper&lt;Lambert&gt;</c> in source.
    /// </remarks>
    internal static SpecializedType ReadSpecialization(SlangShaderReflection shader, SlangGeneric specialized)
    {
        var bindings = new List<GenericArgumentBinding>();

        var typeCount = specialized.TypeParameterCount;
        for (uint i = 0; i < typeCount; i++)
        {
            var parameter = specialized.GetTypeParameter(i);
            if (parameter is null) continue;

            var concrete = specialized.GetConcreteType(parameter);
            bindings.Add(new GenericArgumentBinding(
                parameter.Name ?? "",
                GenericParameterKind.Type,
                Type: concrete is null ? null : FullTypeName(concrete)));
        }

        var valueCount = specialized.ValueParameterCount;
        for (uint i = 0; i < valueCount; i++)
        {
            var parameter = specialized.GetValueParameter(i);
            if (parameter is null) continue;

            bindings.Add(new GenericArgumentBinding(
                parameter.Name ?? "",
                GenericParameterKind.Value,
                Value: specialized.GetConcreteIntVal(parameter)));
        }

        var name = specialized.Name ?? "<unnamed>";
        var inner = specialized.InnerDecl;

        // What the generic wraps. `struct Wrapper<T>` and `float3 shade<T>(…)` are both generics,
        // and the caller specialized one by name — so it has to be told which it got, rather than
        // being handed a null layout and left to infer.
        var kind = specialized.InnerKind switch
        {
            SlangDeclKind.SLANG_DECL_KIND_STRUCT => DeclarationKind.Struct,
            SlangDeclKind.SLANG_DECL_KIND_FUNC => DeclarationKind.Func,
            SlangDeclKind.SLANG_DECL_KIND_VARIABLE => DeclarationKind.Variable,
            SlangDeclKind.SLANG_DECL_KIND_ENUM => DeclarationKind.Enum,
            SlangDeclKind.SLANG_DECL_KIND_NAMESPACE => DeclarationKind.Namespace,
            SlangDeclKind.SLANG_DECL_KIND_MODULE => DeclarationKind.Module,
            SlangDeclKind.SLANG_DECL_KIND_GENERIC => DeclarationKind.Generic,
            _ => DeclarationKind.Unsupported,
        };

        if (kind == DeclarationKind.Func)
        {
            // A generic function's parameter types come back substituted rather than symbolic —
            // `shade(SgLambert m, vector<float,3> a)` rather than `shade(T m, vector<float,3> a)`.
            // That is the whole reason to specialize a function you are not going to call.
            var function = inner?.CastToFunction();
            var applied = function?.ApplySpecializations(specialized);

            // applySpecializations where it works, the unspecialized declaration where it does not
            // — a symbolic signature is more use than none, and the bindings say what it was given.
            var resolved = applied ?? function;
            var signature = resolved is null
                ? null
                : ReadFunction(resolved, expandOverloads: false, substitute: specialized);

            return new SpecializedType(signature?.ToString() ?? name, bindings, null, kind, signature);
        }

        var innerType = inner?.Type;
        var appliedType = innerType?.ApplySpecializations(specialized);

        if (appliedType is null) return new SpecializedType(name, bindings, null, kind);

        var typeLayout = shader.GetTypeLayout(appliedType, SlangLayoutRules.SLANG_LAYOUT_RULES_DEFAULT);

        return new SpecializedType(
            FullTypeName(appliedType),
            bindings,
            typeLayout is null ? null : ReadTypeLayout(typeLayout, new Dictionary<nint, TypeLayout>()),
            kind);
    }

    /// <summary>The signature of a function declaration.</summary>
    private static FunctionSignature? ReadFunction(SlangDecl decl) =>
        decl.CastToFunction() is { } function ? ReadFunction(function, expandOverloads: true) : null;

    /// <param name="function">The function to read.</param>
    /// <param name="expandOverloads">Whether an overloaded name is read as each of its overloads.</param>
    /// <param name="substitute">
    /// A specialized generic whose arguments should be applied to each parameter and to the result
    /// type, or null to report the declaration as written.
    /// </param>
    /// <remarks>
    /// The substitution has to be pushed through each node rather than applied once to the
    /// function. <c>spReflectionFunction_applySpecializations</c> returns a function whose
    /// parameters still report <c>T</c>; running the same generic through each parameter's
    /// <em>variable</em> and through the result <em>type</em> is what turns
    /// <c>sgApply(T m, ...)</c> into <c>sgApply(SgEmissive m, ...)</c>. Measured, after the obvious
    /// call left the signature symbolic.
    /// </remarks>
    private static FunctionSignature ReadFunction(
        SlangFunction function,
        bool expandOverloads,
        SlangGeneric? substitute = null)
    {
        var parameterCount = function.ParameterCount;
        var parameters = new List<FunctionParameter>((int)parameterCount);

        for (uint i = 0; i < parameterCount; i++)
        {
            var parameter = function.GetParameter(i);
            if (parameter is null) continue;

            if (substitute is not null && parameter.ApplySpecializations(substitute) is { } appliedVariable)
                parameter = appliedVariable;

            parameters.Add(new FunctionParameter(
                parameter.Name ?? "",
                FullTypeName(Substituted(parameter.Type, substitute)),
                DirectionOf(parameter),
                ReadAttributes(parameter)));
        }

        // Slang reports an overloaded name as one function with the real declarations underneath,
        // and the outer one's own parameter list is whichever declaration it happened to pick.
        // Expanding one level is enough — an overload is a concrete declaration and is not itself
        // overloaded — and the recursion is bounded by that flag rather than by trust.
        List<FunctionSignature>? overloads = null;
        if (expandOverloads && function.IsOverloaded)
        {
            var count = function.OverloadCount;
            overloads = new List<FunctionSignature>((int)count);
            for (uint i = 0; i < count; i++)
            {
                if (function.GetOverload(i) is { } overload) overloads.Add(ReadFunction(overload, expandOverloads: false));
            }
        }

        return new FunctionSignature(
            function.Name ?? "",
            FullTypeName(Substituted(function.ResultType, substitute)),
            parameters,
            ModifiersOf(function),
            ReadFunctionAttributes(function),
            overloads);
    }

    /// <summary>The type with a specialization applied, where there is one to apply.</summary>
    private static SlangType? Substituted(SlangType? type, SlangGeneric? substitute)
    {
        if (type is null || substitute is null) return type;
        return type.ApplySpecializations(substitute) ?? type;
    }

    /// <summary>
    /// Which of Slang's declaration modifiers a function carries.
    /// </summary>
    /// <remarks>
    /// There is no "list the modifiers" query — only <c>FindModifier</c>, which answers about one
    /// id — so this asks about each in turn. Eleven calls per function, all of them pointer
    /// comparisons in Slang, and the alternative is not reporting them.
    /// </remarks>
    private static List<string>? ModifiersOf(SlangFunction function)
    {
        List<string>? found = null;

        foreach (var id in Enum.GetValues<SlangModifierID>())
        {
            if (function.FindModifier(id) is null) continue;
            (found ??= []).Add(ModifierName(id));
        }

        return found;
    }

    private static ParameterDirection DirectionOf(SlangVariable parameter)
    {
        // Asked in this order because `inout` is its own id rather than the two flags together,
        // and a parameter that is `out` is not also `in`.
        if (parameter.FindModifier(SlangModifierID.SLANG_MODIFIER_INOUT) is not null) return ParameterDirection.InOut;
        if (parameter.FindModifier(SlangModifierID.SLANG_MODIFIER_OUT) is not null) return ParameterDirection.Out;
        return ParameterDirection.In;
    }

    /// <summary><c>SLANG_MODIFIER_NO_DIFF</c> becomes <c>no_diff</c>.</summary>
    private static string ModifierName(SlangModifierID id) =>
        id.ToString().Replace("SLANG_MODIFIER_", "", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// A type's full name, generic arguments included.
    /// </summary>
    /// <remarks>
    /// <c>GetFullName</c> is the one worth having: <c>GetName</c> reports
    /// <c>StructuredBuffer</c> where a signature check needs
    /// <c>StructuredBuffer&lt;float4&gt;</c>. Falls back to the short name, then to <c>void</c>,
    /// which is what a null result type means.
    /// </remarks>
    internal static string FullTypeName(SlangType? type)
    {
        if (type is null) return "void";

        var text = type.FullName;
        if (!string.IsNullOrEmpty(text)) return text;

        return type.DeclaredName ?? "void";
    }

    private static List<UserAttribute>? ReadFunctionAttributes(SlangFunction function)
    {
        var count = function.UserAttributeCount;
        if (count == 0) return null;

        var attributes = new List<UserAttribute>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (function.GetUserAttribute(i) is { } attr) attributes.Add(ReadAttribute(attr));
        }

        return attributes.Count > 0 ? attributes : null;
    }

    /// <summary>
    /// The type and value parameters of a generic declaration.
    /// </summary>
    /// <remarks>
    /// The only place value generics are visible. Slang's program-level specialisation API reports
    /// type parameters exclusively, so <c>struct Buffer&lt;let N : int&gt;</c> is invisible there
    /// and reachable only through the generic's own reflection.
    /// </remarks>
    private static List<GenericParameter>? ReadGenericParameters(SlangDecl decl)
    {
        var generic = decl.CastToGeneric();
        if (generic is null) return null;

        var result = new List<GenericParameter>();

        var typeCount = generic.TypeParameterCount;
        for (uint i = 0; i < typeCount; i++)
        {
            var param = generic.GetTypeParameter(i);
            if (param is null) continue;

            var constraints = new List<string>();
            var constraintCount = generic.GetTypeParameterConstraintCount(param);
            for (uint k = 0; k < constraintCount; k++)
            {
                if (generic.GetTypeParameterConstraintType(param, k)?.DeclaredName is { } n) constraints.Add(n);
            }

            result.Add(new GenericParameter(param.Name ?? "", GenericParameterKind.Type, constraints));
        }

        var valueCount = generic.ValueParameterCount;
        for (uint i = 0; i < valueCount; i++)
        {
            var param = generic.GetValueParameter(i);
            if (param is null) continue;

            result.Add(new GenericParameter(param.Name ?? "", GenericParameterKind.Value, []));
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// The program's unbound generic parameters and the interfaces they must satisfy.
    /// </summary>
    private static List<SpecializationParameter> ReadSpecializationParameters(SlangShaderReflection shader)
    {
        var result = new List<SpecializationParameter>();
        var count = shader.TypeParameterCount;

        for (uint i = 0; i < count; i++)
        {
            var param = shader.GetTypeParameterByIndex(i);
            if (param is null) continue;

            var constraints = new List<string>();
            var constraintCount = param.ConstraintCount;
            for (uint k = 0; k < constraintCount; k++)
            {
                if (param.GetConstraintByIndex(k)?.DeclaredName is { } n) constraints.Add(n);
            }

            var name = param.Name;

            // Measured against slang-compiler 2026.21, because the two cases are not
            // distinguished by any flag:
            //   `type_param T : IFoo;`      -> named,   constraints ["IFoo"]  -> a type parameter
            //   `uniform IFoo param;`       -> unnamed, no constraints        -> an existential
            // The second is an interface-typed shader parameter, where the concrete type arrives
            // at bind time rather than at specialisation time. Name presence is the discriminator.
            result.Add(new SpecializationParameter(
                (int)param.Index,
                name,
                string.IsNullOrEmpty(name)
                    ? SpecializationParameterKind.Existential
                    : SpecializationParameterKind.Type,
                constraints));
        }

        return result;
    }

    // ---------------------------------------------------------------- parameters and entry points

    private static ShaderParameter ReadParameter(SlangVariableLayout varLayout, Dictionary<nint, TypeLayout> cache)
    {
        var variable = varLayout.Variable;
        var typeLayout = varLayout.TypeLayout;

        var name = variable?.Name ?? "<unnamed>";

        return new ShaderParameter(
            name,
            ReadTypeLayout(typeLayout, cache),
            ReadBindings(varLayout, typeLayout),
            varLayout.SemanticName,
            (uint)varLayout.SemanticIndex,
            ReadAttributes(variable),
            varLayout.Stage);
    }

    /// <summary>
    /// A parameter can occupy several categories at once, so read the offset in each one the type
    /// layout reports rather than assuming a single binding.
    /// </summary>
    private static List<ParameterBinding> ReadBindings(SlangVariableLayout varLayout, SlangTypeLayout? typeLayout)
    {
        var result = new List<ParameterBinding>();
        if (typeLayout is null) return result;

        var categories = typeLayout.CategoryCount;
        for (uint c = 0; c < categories; c++)
        {
            var category = typeLayout.GetCategoryByIndex(c);
            result.Add(new ParameterBinding(
                category,
                (uint)varLayout.GetOffset(category),
                (uint)varLayout.GetSpace(category)));
        }

        return result;
    }

    private static EntryPointLayout ReadEntryPoint(SlangEntryPointReflection ep, Dictionary<nint, TypeLayout> cache)
    {
        var name = ep.Name ?? "<unnamed>";
        var stage = ep.Stage;

        var parameters = new List<ShaderParameter>();
        var count = ep.ParameterCount;
        for (uint i = 0; i < count; i++)
            parameters.Add(ReadParameter(ep.GetParameterByIndex(i)!, cache));

        // Mesh and amplification are dispatched in workgroups too, and declare one with
        // [numthreads] exactly as compute does — the module emits OpExecutionMode LocalSize for all
        // three. Gating this on SLANG_STAGE_COMPUTE reported null for a mesh shader that had said
        // [numthreads(1,1,1)], which the emitted module then confirmed.
        ThreadGroupSize? threadGroup = null;
        if (stage is SlangStage.SLANG_STAGE_COMPUTE
                  or SlangStage.SLANG_STAGE_MESH
                  or SlangStage.SLANG_STAGE_AMPLIFICATION)
        {
            Span<ulong> dims = stackalloc ulong[3];
            ep.GetComputeThreadGroupSize(dims);
            threadGroup = new ThreadGroupSize(dims[0], dims[1], dims[2]);
        }

        TypeLayout? resultType = null;
        if (ep.ResultVarLayout?.TypeLayout is { } tl) resultType = ReadTypeLayout(tl, cache);

        // Where this entry point's own parameter block sits in the global numbering, and how much
        // uniform data it carries. Both are needed to place `uniform` entry-point parameters,
        // which report their offsets relative to this block rather than absolutely.
        List<ParameterBinding>? parameterBase = null;
        ulong uniformSize = 0;
        var epVarLayout = ep.VarLayout;
        if (epVarLayout?.TypeLayout is { } epTypeLayout)
        {
            parameterBase = ReadBindings(epVarLayout, epTypeLayout);

            // The entry point's block is a constant buffer wrapping a struct, and the wrapper
            // reports a uniform size of 0 — the bytes are on the element. Reading the wrapper
            // says an entry point with `uniform float4 tint` needs no push constants at all.
            var element = epTypeLayout.ElementTypeLayout ?? epTypeLayout;
            uniformSize = element.GetSize(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
        }

        // Slang writes 0 when the entry point declared no [WaveSize(N)], and 0 threads per wave is
        // not a thing — so null is the honest projection, the same reasoning as ThreadGroupSize's
        // specialized dimensions.
        var waveSize = ep.ComputeWaveSize;

        // Null rather than a repeat of Name. A caller wants to know that the module calls this
        // entry point something else, and an override equal to the name is not that.
        var nameOverride = ep.NameOverride;
        if (string.Equals(nameOverride, name, StringComparison.Ordinal)) nameOverride = null;

        return new EntryPointLayout(
            name,
            stage,
            parameters,
            threadGroup,
            ep.UsesAnySampleRateInput,
            resultType,
            parameterBase,
            uniformSize,
            nameOverride,
            waveSize == 0 ? null : waveSize,
            ep.HasDefaultConstantBuffer,
            ep.Function is { } function ? ReadFunction(function, expandOverloads: false) : null);
    }

    // ---------------------------------------------------------------- descriptor sets

    /// <summary>
    /// Builds the full descriptor-set picture, including sets introduced by parameter blocks.
    /// </summary>
    /// <remarks>
    /// Determined empirically against slang-compiler 2026.14.1, because the shape is not obvious, and
    /// the same on 2026.21:
    /// <list type="bullet">
    /// <item>The global scope's type layout reports only the sets for things bound *directly* in
    /// it. A <c>ParameterBlock</c> appears there as a binding range with
    /// <c>descriptorSetIndex == -1</c>, meaning "not in one of my sets".</item>
    /// <item>A parameter block reports its own register space as the *offset* of its
    /// <c>SUB_ELEMENT_REGISTER_SPACE</c> category — not in the space field, which stays 0.</item>
    /// <item>Its contents live in the <em>element</em> type layout's descriptor sets, whose
    /// <c>spaceOffset</c> is relative to the block's space.</item>
    /// </list>
    /// So the walk has to recurse and add the offsets. Reading only the global scope silently
    /// loses every binding inside a parameter block.
    /// </remarks>
    private static List<DescriptorSet> ReadDescriptorSets(SlangShaderReflection shader)
    {
        var bySpace = new SortedDictionary<uint, List<DescriptorRange>>();

        if (shader.GlobalParamsTypeLayout is { } globalScope) CollectSets(globalScope, baseSpace: 0, bySpace);

        CollectEntryPointSets(shader, bySpace);

        foreach (var (varLayout, typeLayout) in Parameters(shader))
            CollectSpaces(varLayout, typeLayout, parentSpace: 0, bySpace);

        RelocateExplicitlySpacedParameters(shader, bySpace);
        AddUncoveredParameters(shader, bySpace);

        return bySpace
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new DescriptorSet(kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>The global parameters that have a type layout, with it.</summary>
    private static IEnumerable<(SlangVariableLayout Variable, SlangTypeLayout Type)> Parameters(SlangShaderReflection shader)
    {
        var paramCount = shader.ParameterCount;
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = shader.GetParameterByIndex(i)!;
            if (varLayout.TypeLayout is { } typeLayout) yield return (varLayout, typeLayout);
        }
    }

    /// <summary>
    /// Moves a top-level parameter's range to the register space the parameter itself reports,
    /// where Slang's descriptor-set view put it somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against slang-compiler 2026.21 with
    /// <c>[[vk::binding(0, 1)]] ConstantBuffer&lt;T&gt; spA : register(b0, space1)</c> beside a
    /// texture declared the same way: the parameter reports space 1 for both, the module (and
    /// Slang's own "explicit binding overlap" warning) place both in space 1, and the
    /// descriptor-set view puts the texture in space 1 and the constant buffer in space 0. The
    /// same on a DXIL target. So a renderer built from the set view bound the buffer at set 0
    /// where the shader read set 1 — and on SPIR-V the reconciliation pass below then added an
    /// <c>UNKNOWN</c> range at the real slot, which the projections refuse, so the symptom there
    /// was a refusal naming a gap in SharpSlang; on DXIL nothing was added, and D3D12 refused the
    /// pipeline because its root signature did not cover the register.
    /// </para>
    /// <para>
    /// The parameter list is the half of Slang's reflection that matches the module, which is the
    /// same judgement <see cref="AddUncoveredParameters"/> rests on, so the range follows the
    /// parameter. Matched by name, category and index, and only for a top-level parameter that
    /// does not introduce a space of its own: a parameter block's contents are placed by the
    /// recursion above and are not in question. A parameter whose range is already where it says
    /// is a no-op, which is every parameter without an explicit space.
    /// </para>
    /// </remarks>
    private static void RelocateExplicitlySpacedParameters(
        SlangShaderReflection shader,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        foreach (var (varLayout, typeLayout) in Parameters(shader))
        {
            if (SubElementSpace(varLayout, typeLayout) is not null) continue;

            var name = varLayout.Variable?.Name;
            if (name is null) continue;

            var categories = typeLayout.CategoryCount;
            for (uint c = 0; c < categories; c++)
            {
                var category = typeLayout.GetCategoryByIndex(c);
                if (category is not (SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT
                                     or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER
                                     or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE
                                     or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS
                                     or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SAMPLER_STATE))
                {
                    continue;
                }

                var space = (uint)varLayout.GetSpace(category);
                var index = (uint)varLayout.GetOffset(category);

                foreach (var (otherSpace, ranges) in bySpace)
                {
                    if (otherSpace == space) continue;

                    var misplaced = ranges.FindIndex(r =>
                        r.Name == name && r.Category == category && r.IndexOffset == index
                        && r.BindingType != SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN);
                    if (misplaced < 0) continue;

                    var range = ranges[misplaced];
                    ranges.RemoveAt(misplaced);
                    if (!bySpace.TryGetValue(space, out var target)) bySpace[space] = target = [];
                    target.Add(range);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Collects the descriptor sets of anything that introduces a register space, and of everything
    /// nested inside it.
    /// </summary>
    /// <param name="varLayout">The variable whose sub-objects are walked.</param>
    /// <param name="typeLayout">Its type layout.</param>
    /// <param name="parentSpace">
    /// The space of whatever encloses this. Sub-element offsets are relative, so the absolute space
    /// is the running sum down the chain.
    /// </param>
    /// <param name="bySpace">The ranges found so far, by space, added to in place.</param>
    /// <remarks>
    /// <para>
    /// Recursive because the nesting is, and the recursion used to stop after one level.
    /// <c>struct Outer { float4 c; ParameterBlock&lt;Inner&gt; inner; Texture2D o; }</c> puts
    /// <c>inner</c> in a space of its own, one past the outer block's, and the module decorates
    /// <c>gOuter.inner.t</c> accordingly — while the layout reported the outer set and simply
    /// omitted the inner one. Not a wrong binding: a missing descriptor set, the same failure the
    /// subpass inputs had.
    /// </para>
    /// <para>
    /// A plain struct in the chain is not a container and has no element layout, but it does carry
    /// an offset — measured with <c>Root { float4 c; Mid mid; Texture2D o; }</c> and
    /// <c>Mid { float2 pad; ParameterBlock&lt;Leaf&gt; leaf; }</c>: <c>mid</c> reports 1,
    /// <c>leaf</c> reports 0, and the module puts <c>gRoot.mid.leaf.t</c> in set 1. So the offset
    /// has to accumulate through the struct even though the struct owns no set of its own. Taking
    /// the offset from <c>getSubObjectRangeSpaceOffset</c> instead does not work; it reports 0 for
    /// the same block.
    /// </para>
    /// </remarks>
    private static void CollectSpaces(
        SlangVariableLayout varLayout,
        SlangTypeLayout typeLayout,
        uint parentSpace,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        // No sub-element category means it holds nothing that lives in a space of its own, so
        // there is nothing here for this walk and nothing nested either.
        if (SubElementSpace(varLayout, typeLayout) is not { } offset) return;

        var space = parentSpace + offset;

        if (typeLayout.Kind == SlangTypeKind.SLANG_TYPE_KIND_STRUCT)
        {
            CollectNestedSpaces(typeLayout, space, bySpace);
            return;
        }

        var element = typeLayout.ElementTypeLayout;

        // A parameter block's own uniform data becomes an implicit constant buffer, and that
        // descriptor lives on the *container* var layout rather than in the element's descriptor
        // sets. Miss it and a block of pure uniform data reports no bindings at all — the block's
        // element layout genuinely says descriptorSets=0 in that case.
        AddContainerDescriptor(varLayout, typeLayout, element, space, bySpace);

        if (element is null) return;
        CollectSets(element, space, bySpace);
        CollectNestedSpaces(element, space, bySpace);
    }

    /// <summary>Walks a struct's fields for anything that introduces a space of its own.</summary>
    private static void CollectNestedSpaces(
        SlangTypeLayout structLayout,
        uint space,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        if (structLayout.Kind != SlangTypeKind.SLANG_TYPE_KIND_STRUCT) return;

        var fields = structLayout.FieldCount;
        for (uint f = 0; f < fields; f++)
        {
            var fieldVar = structLayout.GetFieldByIndex(f);
            if (fieldVar?.TypeLayout is { } fieldType) CollectSpaces(fieldVar, fieldType, space, bySpace);
        }
    }

    /// <summary>
    /// Adds a range for any top-level parameter that occupies a descriptor slot no range covers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reconciliation pass rather than more plumbing, because the two halves of Slang's own
    /// reflection disagree and the parameter list is the half that matches the module.
    /// <c>SubpassInput</c> is reported as a normal parameter with a
    /// <c>DESCRIPTOR_TABLE_SLOT</c> offset — and is absent from both the descriptor-set view and
    /// the binding-range view, while still consuming its index. Measured with
    /// <c>gA</c>, <c>gB</c>, <c>gOrdinary</c>: the module decorates bindings 0, 1 and 2, and
    /// reflection offered a single range, at 2. A deferred renderer built the descriptor set layout
    /// from that and got one binding where the shader reads three.
    /// </para>
    /// <para>
    /// Coverage is checked rather than assumed so this cannot double-count: a range starting at
    /// index <c>i</c> with <c>n</c> descriptors covers <c>i</c>..<c>i+n-1</c>, and an unbounded one
    /// covers everything from its start. Only genuine holes are filled.
    /// </para>
    /// <para>
    /// The binding type comes from the resource shape. Only the subpass case is mapped, because
    /// only the subpass case is known to go missing; anything else lands as
    /// <c>SLANG_BINDING_TYPE_UNKNOWN</c>, which <c>NriProjection.ToDescriptorType</c> refuses
    /// loudly. A hole reported as unknown is a bug report; a hole silently given a plausible type
    /// is the class of defect this whole exercise keeps finding.
    /// </para>
    /// </remarks>
    private static void AddUncoveredParameters(
        SlangShaderReflection shader,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        const SlangParameterCategory Slot =
            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT;

        foreach (var (varLayout, typeLayout) in Parameters(shader))
        {
            // A parameter block lives in its own space; its contents were collected by recursing.
            if (SubElementSpace(varLayout, typeLayout) is not null) continue;

            var slots = typeLayout.GetSize(Slot);
            if (slots == 0) continue;

            var space = (uint)varLayout.GetSpace(Slot);
            var index = (uint)varLayout.GetOffset(Slot);

            if (!bySpace.TryGetValue(space, out var ranges)) bySpace[space] = ranges = [];
            if (ranges.Any(r => Covers(r, index))) continue;

            var type = typeLayout.Type;
            var shape = type is null
                ? SlangResourceShape.SLANG_RESOURCE_NONE
                : type.ResourceShape & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK;

            ranges.Add(new DescriptorRange(
                shape == SlangResourceShape.SLANG_TEXTURE_SUBPASS
                    ? SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET
                    : SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN,
                index,
                (uint)slots,
                Slot,
                Name: varLayout.Variable?.Name,
                InputAttachmentIndex: SubpassIndex(varLayout, typeLayout)));
        }

        static bool Covers(DescriptorRange range, uint index) =>
            range.IsUnbounded
                ? index >= range.IndexOffset
                : index >= range.IndexOffset && index < range.IndexOffset + Math.Max(range.DescriptorCount, 1);
    }

    /// <summary>The <c>SUBPASS</c> category offset — the module's input attachment index.</summary>
    private static uint? SubpassIndex(SlangVariableLayout varLayout, SlangTypeLayout typeLayout)
    {
        var categories = typeLayout.CategoryCount;
        for (uint c = 0; c < categories; c++)
        {
            if (typeLayout.GetCategoryByIndex(c) != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUBPASS) continue;
            return (uint)varLayout.GetOffset(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUBPASS);
        }
        return null;
    }

    /// <summary>
    /// Adds the descriptors an entry point's own <c>uniform</c> parameters occupy.
    /// </summary>
    /// <remarks>
    /// Necessary, not thorough-for-its-own-sake. <c>uniform Texture2D extra</c> on an entry point
    /// is a real descriptor — the emitted module decorates it with a set and a binding — but it is
    /// invisible in the global scope, so a layout built only from
    /// <c>getGlobalParamsTypeLayout</c> is missing a binding the shader will read. That produces a
    /// descriptor set layout the driver rejects, or worse, silently accepts against the wrong slot.
    ///
    /// The rebasing is the fiddly part. An entry point's parameters and descriptor ranges are
    /// numbered from zero within the entry point's own block, and the block's position in the
    /// global numbering is the offset on <c>getVarLayout</c> — per category. Measured against
    /// slang-compiler 2026.21: with three global descriptors the entry-point block reports
    /// <c>DESCRIPTOR_TABLE_SLOT off=3</c>, and its two ranges at relative 0 and 1 land at absolute
    /// bindings 3 and 4, exactly matching the module's decorations.
    /// </remarks>
    private static void CollectEntryPointSets(
        SlangShaderReflection shader,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        var count = shader.EntryPointCount;
        for (ulong e = 0; e < count; e++)
        {
            var varLayout = shader.GetEntryPointByIndex(e)?.VarLayout;
            if (varLayout?.TypeLayout is not { } typeLayout) continue;

            var baseSpace = (uint)varLayout.GetSpace(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT);

            CollectSets(typeLayout, baseSpace, bySpace, varLayout);
        }
    }

    /// <param name="tl">The type layout whose binding ranges are read.</param>
    /// <param name="baseSpace">The space its ranges are offset from.</param>
    /// <param name="bySpace">The ranges found so far, by space, added to in place.</param>
    /// <param name="rebase">
    /// When set, the variable layout whose per-category offsets each range index is relative to.
    /// Null for the global scope, whose ranges are already absolute.
    /// </param>
    private static void CollectSets(
        SlangTypeLayout tl,
        uint baseSpace,
        SortedDictionary<uint, List<DescriptorRange>> bySpace,
        SlangVariableLayout? rebase = null)
    {
        var leaves = ReadBindingRanges(tl);

        var setCount = tl.DescriptorSetCount;
        for (long s = 0; s < setCount; s++)
        {
            var space = baseSpace + (uint)tl.GetDescriptorSetSpaceOffset(s);
            if (!bySpace.TryGetValue(space, out var ranges))
                bySpace[space] = ranges = [];

            var rangeCount = tl.GetDescriptorSetDescriptorRangeCount(s);
            for (long r = 0; r < rangeCount; r++)
            {
                var category = tl.GetDescriptorSetDescriptorRangeCategory(s, r);
                var index = (uint)tl.GetDescriptorSetDescriptorRangeIndexOffset(s, r);
                var count = tl.GetDescriptorSetDescriptorRangeDescriptorCount(s, r);

                if (rebase is not null)
                    index += (uint)rebase.GetOffset(category);

                // -1 as a SlangInt is how Slang reports "runtime-sized". It is not a count and
                // must not be handed to Vulkan as one.
                var unbounded = count < 0 || (ulong)count >= uint.MaxValue;
                var leaf = leaves.GetValueOrDefault((s, r));

                ranges.Add(new DescriptorRange(
                    tl.GetDescriptorSetDescriptorRangeType(s, r),
                    index,
                    unbounded ? 0u : (uint)count,
                    category,
                    Name: leaf.Name,
                    IsUnbounded: unbounded,
                    ImageFormat: leaf.Format,
                    CounterBinding: leaf.CounterOffset is { } counter ? index + counter : null,
                    IsSpecializable: leaf.IsSpecializable));
            }
        }
    }

    /// <summary>
    /// The declaration behind each descriptor range of <paramref name="tl"/>, keyed by
    /// (descriptor set, range) — a name, and an image format where there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slang models the same bindings twice. The descriptor-set view says where a range sits; the
    /// binding-range view says what declared it, and links the two through
    /// <c>getBindingRangeDescriptorSetIndex</c> plus <c>getBindingRangeFirstDescriptorRangeIndex</c>.
    /// Joining them is the only way to name a binding <em>inside</em> a <c>ParameterBlock</c>:
    /// those are not top-level parameters, so recovering names by scanning the parameter list
    /// reports every binding of Slang's own descriptor-set feature as unnamed.
    /// </para>
    /// <para>
    /// A binding range with <c>descriptorSetIndex == -1</c> is a parameter block, which lives in a
    /// space of its own rather than in one of this layout's sets; its contents are named when the
    /// walk recurses into the element.
    /// </para>
    /// <para>
    /// <b>The image format call is asked of textures only, a guard that has outlived its reason.</b>
    /// <c>getBindingRangeImageFormat</c> on a <c>PARAMETER_BLOCK</c> binding range was once recorded
    /// as access-violating; at 2026.14.1 and 2026.21 it returns <c>unknown</c> for every non-texture
    /// shape a shader can produce. The guard stays, because dropping it would trade a free branch
    /// for a class of failure that ends the process; <c>ProberTests</c> runs the call across every
    /// binding-range shape in a child process, so a crash would be a red test rather than a dead
    /// runner.
    /// </para>
    /// </remarks>
    private static Dictionary<(long Set, long Range), BindingRangeLeaf> ReadBindingRanges(SlangTypeLayout tl)
    {
        var result = new Dictionary<(long, long), BindingRangeLeaf>();

        var count = tl.BindingRangeCount;
        for (long b = 0; b < count; b++)
        {
            var set = tl.GetBindingRangeDescriptorSetIndex(b);
            if (set < 0) continue;

            var type = tl.GetBindingRangeType(b);
            var format = type is SlangBindingType.SLANG_BINDING_TYPE_TEXTURE
                             or SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TETURE
                ? tl.GetBindingRangeImageFormat(b)
                : SlangImageFormat.SLANG_IMAGE_FORMAT_unknown;

            var name = tl.GetBindingRangeLeafVariable(b)?.Name;

            var first = tl.GetBindingRangeFirstDescriptorRangeIndex(b);
            var spanned = tl.GetBindingRangeDescriptorRangeCount(b);
            var counter = CounterOffset(tl, b);
            var specializable = tl.IsBindingRangeSpecializable(b);
            for (long r = 0; r < spanned; r++)
                result[(set, first + r)] = new(name, format, counter, specializable);
        }

        return result;
    }

    /// <summary>What a binding range's leaf tells us that its descriptor range does not.</summary>
    private readonly record struct BindingRangeLeaf(
        string? Name, SlangImageFormat Format, uint? CounterOffset, bool IsSpecializable);

    /// <summary>
    /// How far past a buffer its own counter sits, in descriptor slots, or null if it has none.
    /// </summary>
    /// <remarks>
    /// An <c>AppendStructuredBuffer</c> costs <em>two</em> Vulkan descriptors: the data and a
    /// separate storage buffer holding the count. Slang reports the second as an ordinary binding
    /// range named <c>counter</c>, so a shader with an append and a consume buffer produces two
    /// bindings both called <c>counter</c> and nothing to tell them apart — and the parameter list
    /// omits both, because neither is a declared parameter. Measured on 2026.21: three declared
    /// buffers, five bindings.
    ///
    /// The offset is relative to the owning buffer's own descriptor index, so the caller adds it to
    /// the absolute index it already has.
    /// </remarks>
    private static uint? CounterOffset(SlangTypeLayout tl, long bindingRange)
    {
        var counter = tl.GetBindingRangeLeafTypeLayout(bindingRange)?.ExplicitCounter;
        if (counter is null) return null;

        return (uint)counter.GetOffset(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT);
    }

    /// <summary>
    /// Adds the implicit constant-buffer descriptor a container (parameter block / constant
    /// buffer) occupies for its own uniform data, if it has any.
    /// </summary>
    private static void AddContainerDescriptor(
        SlangVariableLayout varLayout,
        SlangTypeLayout typeLayout,
        SlangTypeLayout? element,
        uint space,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        var container = typeLayout.ContainerVarLayout;
        if (container?.TypeLayout is not { } containerType) return;

        // No uniform payload means no implicit buffer — the block holds only resources.
        var uniformSize = element?.GetSize(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) ?? 0;
        if (uniformSize == 0) return;

        var categories = containerType.CategoryCount;
        for (uint c = 0; c < categories; c++)
        {
            // The category the target files the buffer under: DESCRIPTOR_TABLE_SLOT for SPIR-V,
            // CONSTANT_BUFFER — a `b` register — for D3D. This used to accept the first only, and
            // on a DXIL layout a ParameterBlock with uniform data therefore had no constant buffer
            // at all: its textures were in their space and the block itself was nowhere. Found by
            // the D3D12 device leg, which refuses a pipeline whose root signature does not cover a
            // register the shader reads: "Shader CBV descriptor range (BaseShaderRegister=0,
            // NumDescriptors=1, RegisterSpace=1) is not fully bound in root signature", on
            // parameter-blocks and buffer-arrays.
            var category = containerType.GetCategoryByIndex(c);
            if (category is not (SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT
                                 or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER))
                continue;

            var index = (uint)container.GetOffset(category);

            if (!bySpace.TryGetValue(space, out var ranges))
                bySpace[space] = ranges = [];

            // Only add if nothing already claims that register — the element's own sets take
            // precedence, and duplicate bindings would produce an invalid set layout. The register
            // is the index within its category: on D3D a texture at t0 and this buffer at b0 share
            // the index and are different registers.
            if (ranges.All(r => r.IndexOffset != index || r.Category != category))
            {
                // Named after the block, which is what the module calls it: a ParameterBlock<Frame>
                // named gFrame decorates its implicit buffer `gFrame`, while its contents get
                // `gFrame.shadow` and so on.
                ranges.Add(new DescriptorRange(
                    SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, index, 1, category,
                    Name: varLayout.Variable?.Name));
            }
            return;
        }
    }

    /// <summary>
    /// The register space a parameter introduces, if it introduces one. For register-space
    /// categories the *offset* carries the space index; the space field stays 0.
    /// </summary>
    private static uint? SubElementSpace(SlangVariableLayout varLayout, SlangTypeLayout typeLayout)
    {
        var categories = typeLayout.CategoryCount;
        for (uint c = 0; c < categories; c++)
        {
            var category = typeLayout.GetCategoryByIndex(c);
            if (category is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE
                         or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_REGISTER_SPACE)
            {
                return (uint)varLayout.GetOffset(category);
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- the type tree

    private static TypeLayout ReadTypeLayout(SlangTypeLayout? tl, Dictionary<nint, TypeLayout> cache)
    {
        if (tl is null) return new OpaqueLayout { Kind = LayoutKind.Unknown };

        var key = tl.NativeHandle;
        if (cache.TryGetValue(key, out var hit)) return hit;

        // Insert a placeholder before recursing so a self-referential type cannot loop forever.
        cache[key] = new OpaqueLayout { Kind = LayoutKind.Unknown, Name = "<recursive>" };

        // The two facts every kind of layout can carry, applied once here rather than threaded
        // through eight construction sites. `with` on an abstract record clones the concrete one.
        var genericIndex = tl.GenericParamIndex;
        var built = Build(tl, cache) with
        {
            GenericParameterIndex = genericIndex < 0 ? null : (uint)genericIndex,
            HasPendingData = tl.PendingDataTypeLayout is not null,
        };

        cache[key] = built;
        return built;
    }

    private static TypeLayout Build(SlangTypeLayout tl, Dictionary<nint, TypeLayout> cache)
    {
        var type = tl.Type;
        var kind = tl.Kind;
        var name = type?.DeclaredName;
        var sizes = ReadSizes(tl);
        var alignment = tl.GetAlignment(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);

        switch (kind)
        {
            case SlangTypeKind.SLANG_TYPE_KIND_SCALAR:
                return new ScalarLayout
                {
                    Kind = LayoutKind.Scalar, Name = name, Sizes = sizes, Alignment = alignment,
                    ScalarType = type?.ScalarType ?? SlangScalarType.SLANG_SCALAR_TYPE_NONE,
                };

            case SlangTypeKind.SLANG_TYPE_KIND_VECTOR:
                return new VectorLayout
                {
                    Kind = LayoutKind.Vector, Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ElementScalar(type),
                    ElementCount = (int)(type?.ElementCount ?? 0),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_MATRIX:
                {
                    var mode = tl.MatrixLayoutMode;
                    var rows = (int)(type?.RowCount ?? 0);
                    var columns = (int)(type?.ColumnCount ?? 0);

                    return new MatrixLayout
                    {
                        Kind = LayoutKind.Matrix, Name = name, Sizes = sizes, Alignment = alignment,
                        ElementType = ElementScalar(type),
                        RowCount = rows,
                        ColumnCount = columns,
                        Mode = mode,
                        Stride = MatrixStride(sizes, mode, rows, columns),
                    };
                }

            case SlangTypeKind.SLANG_TYPE_KIND_STRUCT:
                return new StructLayout
                {
                    Kind = LayoutKind.Struct, Name = name, Sizes = sizes, Alignment = alignment,
                    Fields = ReadFields(tl, cache),
                    // Only on struct: an attribute is written on a declaration, and the other
                    // kinds are built-in types nobody can attach one to. Asked of every kind it
                    // would be a call per node of the tree for an answer that is always null.
                    Attributes = ReadTypeAttributes(type),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_ARRAY:
                {
                    // Ask about the size first, and only ask for a count if the answer was a number.
                    // An array whose length is a specialization constant reports CategorySize.Unknown,
                    // and getElementCount ends the process on it (U1; SlangType.ElementCount) — as
                    // does getSpecializedElementCount, which the C++ header presents as the resolving
                    // version. See ArrayLayout for the resource-array case, which has no such signal
                    // and cannot be guarded.
                    var specializationSized = sizes.Any(s => !s.IsKnown);

                    var count = specializationSized ? 0 : type?.ElementCount ?? 0;

                    return new ArrayLayout
                    {
                        Kind = LayoutKind.Array, Name = name, Sizes = sizes, Alignment = alignment,
                        ElementType = ReadTypeLayout(tl.ElementTypeLayout, cache),
                        // 0 is how Slang reports an unbounded array; a real zero-length array is not
                        // expressible in the language, so the mapping is unambiguous.
                        ElementCount = specializationSized || count == 0 ? null : (int)count,
                        IsSpecializationSized = specializationSized,
                        ElementStride = (nuint)tl.GetElementStride(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM),
                    };
                }

            case SlangTypeKind.SLANG_TYPE_KIND_RESOURCE:
                {
                    // A StructuredBuffer<T> keeps T's *layout* here — offsets, sizes, alignment — while
                    // GetResourceResultType hands back a bare type with none of it. Reading only the
                    // latter is how a consumer ends up knowing that a buffer holds an Instance and not
                    // how many bytes one is. Measured null on textures, samplers, byte-address and
                    // typed buffers, subpass inputs and acceleration structures, so no shape test is
                    // needed: the resources that have an element layout are exactly the ones that
                    // return one.
                    var elementLayout = tl.ElementTypeLayout;

                    return new ResourceLayout
                    {
                        Kind = LayoutKind.Resource, Name = name, Sizes = sizes, Alignment = alignment,
                        Shape = type?.ResourceShape ?? SlangResourceShape.SLANG_RESOURCE_NONE,
                        Access = type?.ResourceAccess ?? SlangResourceAccess.SLANG_RESOURCE_ACCESS_NONE,
                        ResultType = ReadResultType(type),
                        ElementType = elementLayout is null ? null : ReadTypeLayout(elementLayout, cache),
                        ElementStride = elementLayout is null
                            ? 0
                            : (nuint)elementLayout.GetStride(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM),
                    };
                }

            case SlangTypeKind.SLANG_TYPE_KIND_SAMPLER_STATE:
                return new OpaqueLayout
                {
                    Kind = LayoutKind.SamplerState, Name = name, Sizes = sizes, Alignment = alignment,
                };

            case SlangTypeKind.SLANG_TYPE_KIND_CONSTANT_BUFFER:
            case SlangTypeKind.SLANG_TYPE_KIND_PARAMETER_BLOCK:
            case SlangTypeKind.SLANG_TYPE_KIND_TEXTURE_BUFFER:
            case SlangTypeKind.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER:
                return new ContainerLayout
                {
                    Kind = kind switch
                    {
                        SlangTypeKind.SLANG_TYPE_KIND_CONSTANT_BUFFER => LayoutKind.ConstantBuffer,
                        SlangTypeKind.SLANG_TYPE_KIND_PARAMETER_BLOCK => LayoutKind.ParameterBlock,
                        SlangTypeKind.SLANG_TYPE_KIND_TEXTURE_BUFFER => LayoutKind.TextureBuffer,
                        _ => LayoutKind.ShaderStorageBuffer,
                    },
                    Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ReadTypeLayout(tl.ElementTypeLayout, cache),
                };

            default:
                return new OpaqueLayout
                {
                    Kind = kind switch
                    {
                        SlangTypeKind.SLANG_TYPE_KIND_INTERFACE => LayoutKind.Interface,
                        SlangTypeKind.SLANG_TYPE_KIND_GENERIC_TYPE_PARAMETER => LayoutKind.GenericTypeParameter,
                        SlangTypeKind.SLANG_TYPE_KIND_SPECIALIZED => LayoutKind.Specialized,
                        SlangTypeKind.SLANG_TYPE_KIND_FEEDBACK => LayoutKind.Feedback,
                        SlangTypeKind.SLANG_TYPE_KIND_POINTER => LayoutKind.Pointer,
                        SlangTypeKind.SLANG_TYPE_KIND_DYNAMIC_RESOURCE => LayoutKind.DynamicResource,
                        _ => LayoutKind.Unknown,
                    },
                    Name = name, Sizes = sizes, Alignment = alignment,
                };
        }
    }

    private static List<FieldLayout> ReadFields(SlangTypeLayout tl, Dictionary<nint, TypeLayout> cache)
    {
        var fields = new List<FieldLayout>();
        var count = tl.FieldCount;

        for (uint i = 0; i < count; i++)
        {
            var fieldVarLayout = tl.GetFieldByIndex(i);
            if (fieldVarLayout is null) continue;

            var variable = fieldVarLayout.Variable;
            var fieldTypeLayout = fieldVarLayout.TypeLayout;

            var offsets = new List<CategoryOffset>();
            var categories = fieldTypeLayout?.CategoryCount ?? 0;
            for (uint c = 0; c < categories; c++)
            {
                var category = fieldTypeLayout!.GetCategoryByIndex(c);
                offsets.Add(new CategoryOffset(
                    category,
                    fieldVarLayout.GetOffset(category),
                    (uint)fieldVarLayout.GetSpace(category)));
            }

            fields.Add(new FieldLayout(
                variable?.Name ?? "<unnamed>",
                ReadTypeLayout(fieldTypeLayout, cache),
                offsets,
                fieldVarLayout.SemanticName,
                (uint)fieldVarLayout.SemanticIndex,
                ReadAttributes(variable)));
        }

        return fields;
    }

    private static List<CategorySize> ReadSizes(SlangTypeLayout tl)
    {
        var sizes = new List<CategorySize>();
        var count = tl.CategoryCount;

        for (uint c = 0; c < count; c++)
        {
            var category = tl.GetCategoryByIndex(c);
            sizes.Add(new CategorySize(category, tl.GetSize(category)));
        }

        return sizes;
    }

    private static List<UserAttribute>? ReadAttributes(SlangVariable? variable)
    {
        if (variable is null) return null;

        var count = variable.UserAttributeCount;
        if (count == 0) return null;

        var attributes = new List<UserAttribute>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (variable.GetUserAttribute(i) is { } attr) attributes.Add(ReadAttribute(attr));
        }

        return attributes.Count > 0 ? attributes : null;
    }

    /// <summary>User attributes on a type's own declaration, or null.</summary>
    private static List<UserAttribute>? ReadTypeAttributes(SlangType? type)
    {
        if (type is null) return null;

        var count = type.UserAttributeCount;
        if (count == 0) return null;

        var attributes = new List<UserAttribute>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (type.GetUserAttribute(i) is { } attr) attributes.Add(ReadAttribute(attr));
        }

        return attributes.Count > 0 ? attributes : null;
    }

    /// <summary>One user attribute, whatever it is hanging off.</summary>
    internal static UserAttribute ReadAttribute(SlangUserAttribute attr)
    {
        var name = attr.Name ?? "<unnamed>";
        var argCount = attr.ArgumentCount;
        var args = new List<object?>((int)argCount);
        var types = new List<string>((int)argCount);

        for (uint a = 0; a < argCount; a++)
        {
            // The declared type, alongside the probed value. Slang has no variant accessor, so the
            // value still has to be guessed at by trying each; this at least records what it was
            // supposed to be.
            var declared = attr.GetArgumentType(a);
            types.Add(declared is null ? "<unknown>" : declared.DeclaredName ?? "<unnamed>");
        }

        for (uint a = 0; a < argCount; a++)
        {
            // Slang has no "what type is this argument" query; the accessors report failure
            // for the wrong type, so probe int, then float, then string.
            if (attr.TryGetArgumentValueInt(a, out var intValue))
                args.Add(intValue);
            else if (attr.TryGetArgumentValueFloat(a, out var floatValue))
                args.Add(floatValue);
            else
                args.Add(attr.GetArgumentValueString(a));
        }

        return new UserAttribute(name, args, types.Count > 0 ? types : null);
    }

    /// <summary>
    /// Bytes from one row to the next (row-major) or one column to the next (column-major).
    /// </summary>
    /// <remarks>
    /// Derived, because Slang will not say: <c>GetStride</c> on a matrix returns the whole matrix's
    /// footprint and <c>GetElementStride</c> returns 0. Measured against the module's
    /// <c>MatrixStride</c> decoration for 4x4, 3x3, 2x4 and 4x2 under both layout modes — always
    /// the total divided by the number of entries along the major axis, and always 16 in practice
    /// for a float matrix in a constant buffer.
    /// </remarks>
    private static ulong MatrixStride(
        IReadOnlyList<CategorySize> sizes,
        SlangMatrixLayoutMode mode,
        int rows,
        int columns)
    {
        var total = sizes
            .FirstOrDefault(s => s.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM)
            .Size;

        var major = mode == SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR ? columns : rows;
        return major <= 0 ? 0 : total / (ulong)major;
    }

    private static SlangScalarType ElementScalar(SlangType? type)
    {
        if (type is null) return SlangScalarType.SLANG_SCALAR_TYPE_NONE;
        return type.ElementType is { } element ? element.ScalarType : type.ScalarType;
    }

    private static TypeLayout? ReadResultType(SlangType? type)
    {
        var result = type?.ResourceResultType;
        if (result is null) return null;

        // A bare type has no layout, so describe it structurally without sizes.
        var name = result.DeclaredName;

        return result.Kind switch
        {
            SlangTypeKind.SLANG_TYPE_KIND_SCALAR => new ScalarLayout
            {
                Kind = LayoutKind.Scalar, Name = name,
                ScalarType = result.ScalarType,
            },
            SlangTypeKind.SLANG_TYPE_KIND_VECTOR => new VectorLayout
            {
                Kind = LayoutKind.Vector, Name = name,
                ElementType = ElementScalar(result),
                ElementCount = (int)result.ElementCount,
            },
            SlangTypeKind.SLANG_TYPE_KIND_GENERIC_TYPE_PARAMETER =>
                new OpaqueLayout { Kind = LayoutKind.GenericTypeParameter, Name = name },

            _ => new OpaqueLayout { Kind = LayoutKind.Unknown, Name = name },
        };
    }
}
