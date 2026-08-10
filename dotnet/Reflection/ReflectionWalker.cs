// L3 — the walk itself.
//
// Reads the native reflection tree once and builds the managed snapshot. Every native pointer used
// here is borrowed from the LinkedProgram and must not outlive it, so none of them escape this
// file: the only thing that leaves is immutable managed data.
//
// Cost is not a concern. These are O(1) reads over layout data Slang already computed, a few
// thousand of them at load time — tens of microseconds, once. See docs §4.4 for why that kills the
// argument for batching this behind a C++ shim.

using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang.Reflection;

/// <summary>Builds a <see cref="ProgramLayout"/> from a linked program.</summary>
public static unsafe class ReflectionWalker
{
    /// <summary>Walks the reflection tree of a linked program for one target.</summary>
    /// <param name="program">A linked program. Must not be disposed.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public static ProgramLayout Walk(LinkedProgram program, int targetIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(program);

        var shader = (SlangProgramLayout*)program.GetLayout(targetIndex);
        if (shader is null)
            throw new SlangException(SlangResult.Fail, "getLayout returned no reflection", null);

        // Types are shared across the tree (every `float` is the same TypeLayout*), so cache by
        // pointer. Also stops a cyclic or deeply-shared graph from exploding the walk.
        var cache = new Dictionary<nint, TypeLayout>();

        var parameters = new List<ShaderParameter>();
        var specializationConstants = new List<SpecializationConstant>();
        var paramCount = SlangApi.spReflection_GetParameterCount(shader);
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = SlangApi.spReflection_GetParameterByIndex(shader, i);
            var parameter = ReadParameter(varLayout, cache);
            parameters.Add(parameter);

            if (parameter.IsSpecializationConstant)
                specializationConstants.Add(ReadSpecializationConstant(varLayout, parameter));
        }

        var entryPoints = new List<EntryPointLayout>();
        var epCount = SlangApi.spReflection_getEntryPointCount(shader);
        for (ulong i = 0; i < epCount; i++)
            entryPoints.Add(ReadEntryPoint(SlangApi.spReflection_getEntryPointByIndex(shader, i), cache));

        var hashed = new List<string>();
        var hashedCount = SlangApi.spReflection_getHashedStringCount(shader);
        for (ulong i = 0; i < hashedCount; i++)
        {
            nuint len;
            var s = SlangApi.spReflection_getHashedString(shader, (nuint)i, &len);
            if (s is not null) hashed.Add(new string((sbyte*)s, 0, (int)len));
        }

        return new ProgramLayout
        {
            Parameters = parameters,
            EntryPoints = entryPoints,
            DescriptorSets = ReadDescriptorSets(shader),
            GlobalConstantBufferSize = SlangApi.spReflection_getGlobalConstantBufferSize(shader),
            GlobalConstantBufferBinding = (uint)SlangApi.spReflection_getGlobalConstantBufferBinding(shader),
            HashedStrings = hashed,
            SpecializationParameters = ReadSpecializationParameters(shader),
            SpecializationConstants = specializationConstants.OrderBy(c => c.ConstantId).ToList(),
            BindlessSpaceIndex = BindlessSpace(shader),
            SubObjectRanges = ReadSubObjectRanges(shader),
        };
    }

    /// <summary>The space reserved for a bindless heap, or null for the usual "there isn't one".</summary>
    private static uint? BindlessSpace(SlangProgramLayout* shader)
    {
        var index = SlangApi.spReflection_getBindlessSpaceIndex(shader);
        return index < 0 ? null : (uint)index;
    }

    /// <summary>
    /// The global scope's sub-object ranges: which of its binding ranges are objects with bindings
    /// of their own, and which space each took.
    /// </summary>
    private static List<SubObjectRange> ReadSubObjectRanges(SlangProgramLayout* shader)
    {
        var result = new List<SubObjectRange>();

        var globalScope = SlangApi.spReflection_getGlobalParamsTypeLayout(shader);
        if (globalScope is null) return result;

        var count = SlangApi.spReflectionTypeLayout_getSubObjectRangeCount(globalScope);
        for (long i = 0; i < count; i++)
        {
            var bindingRange = SlangApi.spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(globalScope, i);
            if (bindingRange < 0) continue;

            // Not from getSubObjectRangeOffset. That returns a variable layout with no variable
            // behind it — measured on 2026.14.1, every name came back null — so the name comes from
            // the binding range it points at, which is the same object reached the other way.
            var variable = SlangApi.spReflectionTypeLayout_getBindingRangeLeafVariable(
                globalScope, bindingRange);

            // No space here on purpose. getSubObjectRangeSpaceOffset reported 0 for a block
            // occupying set 1 and 0 for one occupying set 2, and getBindingRangeDescriptorSetIndex
            // reports 0 for both — it indexes the enclosing scope's own set list. Emitting either
            // as "the set" would be a wrong number in a field that looks authoritative, so the
            // record carries the name and the caller joins on DescriptorSets. See SubObjectRange.
            result.Add(new SubObjectRange(
                (uint)bindingRange,
                variable is null ? null : Utf8.Read(SlangApi.spReflectionVariable_GetName(variable))));
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
        SlangReflectionVariableLayout* varLayout,
        ShaderParameter parameter)
    {
        var scalar = parameter.Type is ScalarLayout s
            ? s.ScalarType
            : SlangScalarType.SLANG_SCALAR_TYPE_NONE;

        var id = parameter.Bindings[0].Index;

        var variable = SlangApi.spReflectionVariableLayout_GetVariable(varLayout);
        if (variable is null || SlangApi.spReflectionVariable_HasDefaultValue(variable) == 0)
            return new SpecializationConstant(parameter.Name, id, scalar);

        var isFloat = scalar
            is SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64;

        if (isFloat)
        {
            float f;
            return SlangResult.Succeeded(SlangApi.spReflectionVariable_GetDefaultValueFloat(variable, &f))
                ? new SpecializationConstant(parameter.Name, id, scalar, DefaultFloat: f)
                : new SpecializationConstant(parameter.Name, id, scalar);
        }

        long v;
        return SlangResult.Succeeded(SlangApi.spReflectionVariable_GetDefaultValueInt(variable, &v))
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
            : ReadDecl((SlangReflectionDecl*)root, module.Name, depth: 0);
    }

    // A module's tree is shallow in practice, but a generic's inner decl can point back at its
    // container, so cap the recursion rather than trusting the shape.
    private const int MaxDeclarationDepth = 32;

    private static DeclarationNode ReadDecl(SlangReflectionDecl* decl, string? fallbackName, int depth)
    {
        var name = Utf8.Read(SlangApi.spReflectionDecl_getName(decl)) ?? fallbackName ?? "";
        var kind = SlangApi.spReflectionDecl_getKind(decl) switch
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
            var count = SlangApi.spReflectionDecl_getChildrenCount(decl);
            for (uint i = 0; i < count; i++)
            {
                var child = SlangApi.spReflectionDecl_getChild(decl, i);
                if (child is not null) children.Add(ReadDecl(child, null, depth + 1));
            }
        }

        var generics = kind == DeclarationKind.Generic ? ReadGenericParameters(decl) : null;
        var function = kind == DeclarationKind.Func ? ReadFunction(decl) : null;
        return new DeclarationNode(name, kind, children, generics, function);
    }

    /// <summary>
    /// The signature of a function reached by name rather than through the declaration tree.
    /// </summary>
    internal static FunctionSignature ReadFunctionSignature(SlangReflectionFunction* function) =>
        ReadFunction(function, expandOverloads: true);

    /// <summary>The signature of a function declaration.</summary>
    private static FunctionSignature? ReadFunction(SlangReflectionDecl* decl)
    {
        var function = SlangApi.spReflectionDecl_castToFunction(decl);
        return function is null ? null : ReadFunction(function, expandOverloads: true);
    }

    private static FunctionSignature ReadFunction(SlangReflectionFunction* function, bool expandOverloads)
    {
        var parameterCount = SlangApi.spReflectionFunction_GetParameterCount(function);
        var parameters = new List<FunctionParameter>((int)parameterCount);

        for (uint i = 0; i < parameterCount; i++)
        {
            var parameter = SlangApi.spReflectionFunction_GetParameter(function, i);
            if (parameter is null) continue;

            parameters.Add(new FunctionParameter(
                Utf8.Read(SlangApi.spReflectionVariable_GetName(parameter)) ?? "",
                FullTypeName(SlangApi.spReflectionVariable_GetType(parameter)),
                DirectionOf(parameter),
                ReadAttributes(parameter)));
        }

        // Slang reports an overloaded name as one function with the real declarations underneath,
        // and the outer one's own parameter list is whichever declaration it happened to pick.
        // Expanding one level is enough — an overload is a concrete declaration and is not itself
        // overloaded — and the recursion is bounded by that flag rather than by trust.
        List<FunctionSignature>? overloads = null;
        if (expandOverloads && SlangApi.spReflectionFunction_isOverloaded(function) != 0)
        {
            var count = SlangApi.spReflectionFunction_getOverloadCount(function);
            overloads = new List<FunctionSignature>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var overload = SlangApi.spReflectionFunction_getOverload(function, i);
                if (overload is not null) overloads.Add(ReadFunction(overload, expandOverloads: false));
            }
        }

        return new FunctionSignature(
            Utf8.Read(SlangApi.spReflectionFunction_GetName(function)) ?? "",
            FullTypeName(SlangApi.spReflectionFunction_GetResultType(function)),
            parameters,
            ModifiersOf(function),
            ReadFunctionAttributes(function),
            overloads);
    }

    /// <summary>
    /// Which of Slang's declaration modifiers a function carries.
    /// </summary>
    /// <remarks>
    /// There is no "list the modifiers" query — only <c>FindModifier</c>, which answers about one
    /// id — so this asks about each in turn. Eleven calls per function, all of them pointer
    /// comparisons in Slang, and the alternative is not reporting them.
    /// </remarks>
    private static List<string>? ModifiersOf(SlangReflectionFunction* function)
    {
        List<string>? found = null;

        foreach (var id in Enum.GetValues<SlangModifierID>())
        {
            if (SlangApi.spReflectionFunction_FindModifier(function, id) is null) continue;
            (found ??= []).Add(ModifierName(id));
        }

        return found;
    }

    private static ParameterDirection DirectionOf(SlangReflectionVariable* parameter)
    {
        // Asked in this order because `inout` is its own id rather than the two flags together,
        // and a parameter that is `out` is not also `in`.
        if (SlangApi.spReflectionVariable_FindModifier(parameter, SlangModifierID.SLANG_MODIFIER_INOUT) is not null)
            return ParameterDirection.InOut;
        if (SlangApi.spReflectionVariable_FindModifier(parameter, SlangModifierID.SLANG_MODIFIER_OUT) is not null)
            return ParameterDirection.Out;
        return ParameterDirection.In;
    }

    /// <summary><c>SLANG_MODIFIER_NO_DIFF</c> becomes <c>no_diff</c>.</summary>
    private static string ModifierName(SlangModifierID id) =>
        id.ToString().Replace("SLANG_MODIFIER_", "", StringComparison.Ordinal).ToLowerInvariant();

    /// <summary>
    /// A type's full name, generic arguments included.
    /// </summary>
    /// <remarks>
    /// <c>GetFullName</c> writes into a blob the caller owns, unlike <c>GetName</c> which returns
    /// an interior pointer. It is the one worth having: <c>GetName</c> reports
    /// <c>StructuredBuffer</c> where a signature check needs
    /// <c>StructuredBuffer&lt;float4&gt;</c>. Falls back to the short name, then to <c>void</c>,
    /// which is what a null result type means.
    /// </remarks>
    private static string FullTypeName(SlangReflectionType* type)
    {
        if (type is null) return "void";

        ISlangBlob* blob = null;
        if (SlangResult.Succeeded(SlangApi.spReflectionType_GetFullName(type, &blob)) && blob is not null)
        {
            using var owned = new SlangBlob(blob);
            var text = owned.AsText();
            if (!string.IsNullOrEmpty(text)) return text;
        }

        return Utf8.Read(SlangApi.spReflectionType_GetName(type)) ?? "void";
    }

    private static List<UserAttribute>? ReadFunctionAttributes(SlangReflectionFunction* function)
    {
        var count = SlangApi.spReflectionFunction_GetUserAttributeCount(function);
        if (count == 0) return null;

        var attributes = new List<UserAttribute>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var attr = SlangApi.spReflectionFunction_GetUserAttribute(function, i);
            if (attr is not null) attributes.Add(ReadAttribute(attr));
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
    private static List<GenericParameter>? ReadGenericParameters(SlangReflectionDecl* decl)
    {
        var generic = SlangApi.spReflectionDecl_castToGeneric(decl);
        if (generic is null) return null;

        var result = new List<GenericParameter>();

        var typeCount = SlangApi.spReflectionGeneric_GetTypeParameterCount(generic);
        for (uint i = 0; i < typeCount; i++)
        {
            var param = SlangApi.spReflectionGeneric_GetTypeParameter(generic, i);
            if (param is null) continue;

            var constraints = new List<string>();
            var constraintCount =
                SlangApi.spReflectionGeneric_GetTypeParameterConstraintCount(generic, param);
            for (uint k = 0; k < constraintCount; k++)
            {
                var constraint =
                    SlangApi.spReflectionGeneric_GetTypeParameterConstraintType(generic, param, k);
                if (constraint is null) continue;
                var n = Utf8.Read(SlangApi.spReflectionType_GetName(constraint));
                if (n is not null) constraints.Add(n);
            }

            result.Add(new GenericParameter(
                Utf8.Read(SlangApi.spReflectionVariable_GetName(param)) ?? "",
                GenericParameterKind.Type,
                constraints));
        }

        var valueCount = SlangApi.spReflectionGeneric_GetValueParameterCount(generic);
        for (uint i = 0; i < valueCount; i++)
        {
            var param = SlangApi.spReflectionGeneric_GetValueParameter(generic, i);
            if (param is null) continue;

            result.Add(new GenericParameter(
                Utf8.Read(SlangApi.spReflectionVariable_GetName(param)) ?? "",
                GenericParameterKind.Value,
                []));
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// The program's unbound generic parameters and the interfaces they must satisfy.
    /// </summary>
    private static List<SpecializationParameter> ReadSpecializationParameters(SlangProgramLayout* shader)
    {
        var result = new List<SpecializationParameter>();
        var count = SlangApi.spReflection_GetTypeParameterCount(shader);

        for (uint i = 0; i < count; i++)
        {
            var param = SlangApi.spReflection_GetTypeParameterByIndex(shader, i);
            if (param is null) continue;

            var constraints = new List<string>();
            var constraintCount = SlangApi.spReflectionTypeParameter_GetConstraintCount(param);
            for (uint k = 0; k < constraintCount; k++)
            {
                var constraint = SlangApi.spReflectionTypeParameter_GetConstraintByIndex(param, k);
                if (constraint is null) continue;
                var n = Utf8.Read(SlangApi.spReflectionType_GetName(constraint));
                if (n is not null) constraints.Add(n);
            }

            var name = Utf8.Read(SlangApi.spReflectionTypeParameter_GetName(param));

            // Measured against slang-compiler 2026.14.1, because the two cases are not
            // distinguished by any flag:
            //   `type_param T : IFoo;`      -> named,   constraints ["IFoo"]  -> a type parameter
            //   `uniform IFoo param;`       -> unnamed, no constraints        -> an existential
            // The second is an interface-typed shader parameter, where the concrete type arrives
            // at bind time rather than at specialisation time. Name presence is the discriminator.
            result.Add(new SpecializationParameter(
                (int)SlangApi.spReflectionTypeParameter_GetIndex(param),
                name,
                string.IsNullOrEmpty(name)
                    ? SpecializationParameterKind.Existential
                    : SpecializationParameterKind.Type,
                constraints));
        }

        return result;
    }

    // ---------------------------------------------------------------- parameters and entry points

    private static ShaderParameter ReadParameter(
        SlangReflectionVariableLayout* varLayout,
        Dictionary<nint, TypeLayout> cache)
    {
        var variable = SlangApi.spReflectionVariableLayout_GetVariable(varLayout);
        var typeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(varLayout);

        var name = Utf8.Read(SlangApi.spReflectionVariable_GetName(variable)) ?? "<unnamed>";
        var semantic = Utf8.Read(SlangApi.spReflectionVariableLayout_GetSemanticName(varLayout));

        return new ShaderParameter(
            name,
            ReadTypeLayout(typeLayout, cache),
            ReadBindings(varLayout, typeLayout),
            semantic,
            (uint)SlangApi.spReflectionVariableLayout_GetSemanticIndex(varLayout),
            ReadAttributes(variable));
    }

    /// <summary>
    /// A parameter can occupy several categories at once, so read the offset in each one the type
    /// layout reports rather than assuming a single binding.
    /// </summary>
    private static List<ParameterBinding> ReadBindings(
        SlangReflectionVariableLayout* varLayout,
        SlangReflectionTypeLayout* typeLayout)
    {
        var result = new List<ParameterBinding>();
        var categories = SlangApi.spReflectionTypeLayout_GetCategoryCount(typeLayout);

        for (uint c = 0; c < categories; c++)
        {
            var category = SlangApi.spReflectionTypeLayout_GetCategoryByIndex(typeLayout, c);
            result.Add(new ParameterBinding(
                category,
                (uint)SlangApi.spReflectionVariableLayout_GetOffset(varLayout, category),
                (uint)SlangApi.spReflectionVariableLayout_GetSpace(varLayout, category)));
        }

        return result;
    }

    private static EntryPointLayout ReadEntryPoint(
        SlangEntryPointLayout* ep,
        Dictionary<nint, TypeLayout> cache)
    {
        var name = Utf8.Read(SlangApi.spReflectionEntryPoint_getName(ep)) ?? "<unnamed>";
        var stage = SlangApi.spReflectionEntryPoint_getStage(ep);

        var parameters = new List<ShaderParameter>();
        var count = SlangApi.spReflectionEntryPoint_getParameterCount(ep);
        for (ulong i = 0; i < count; i++)
            parameters.Add(ReadParameter(
                SlangApi.spReflectionEntryPoint_getParameterByIndex(ep, (uint)i), cache));

        // Mesh and amplification are dispatched in workgroups too, and declare one with
        // [numthreads] exactly as compute does — the module emits OpExecutionMode LocalSize for all
        // three. Gating this on SLANG_STAGE_COMPUTE reported null for a mesh shader that had said
        // [numthreads(1,1,1)], which the emitted module then confirmed.
        ThreadGroupSize? threadGroup = null;
        if (stage is SlangStage.SLANG_STAGE_COMPUTE
                  or SlangStage.SLANG_STAGE_MESH
                  or SlangStage.SLANG_STAGE_AMPLIFICATION)
        {
            var dims = stackalloc ulong[3];
            SlangApi.spReflectionEntryPoint_getComputeThreadGroupSize(ep, 3, dims);
            threadGroup = new ThreadGroupSize(dims[0], dims[1], dims[2]);
        }

        TypeLayout? resultType = null;
        var resultVar = SlangApi.spReflectionEntryPoint_getResultVarLayout(ep);
        if (resultVar is not null)
        {
            var tl = SlangApi.spReflectionVariableLayout_GetTypeLayout(resultVar);
            if (tl is not null) resultType = ReadTypeLayout(tl, cache);
        }

        // Where this entry point's own parameter block sits in the global numbering, and how much
        // uniform data it carries. Both are needed to place `uniform` entry-point parameters,
        // which report their offsets relative to this block rather than absolutely.
        List<ParameterBinding>? parameterBase = null;
        ulong uniformSize = 0;
        var epVarLayout = SlangApi.spReflectionEntryPoint_getVarLayout(ep);
        if (epVarLayout is not null)
        {
            var epTypeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(epVarLayout);
            if (epTypeLayout is not null)
            {
                parameterBase = ReadBindings(epVarLayout, epTypeLayout);

                // The entry point's block is a constant buffer wrapping a struct, and the wrapper
                // reports a uniform size of 0 — the bytes are on the element. Reading the wrapper
                // says an entry point with `uniform float4 tint` needs no push constants at all.
                var element = SlangApi.spReflectionTypeLayout_GetElementTypeLayout(epTypeLayout);
                uniformSize = SlangApi.spReflectionTypeLayout_GetSize(
                    element is not null ? element : epTypeLayout,
                    SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
            }
        }

        // Slang writes 0 when the entry point declared no [WaveSize(N)], and 0 threads per wave is
        // not a thing — so null is the honest projection, the same reasoning as ThreadGroupSize's
        // specialized dimensions.
        ulong waveSize;
        SlangApi.spReflectionEntryPoint_getComputeWaveSize(ep, &waveSize);

        // Null rather than a repeat of Name. A caller wants to know that the module calls this
        // entry point something else, and an override equal to the name is not that.
        var nameOverride = Utf8.Read(SlangApi.spReflectionEntryPoint_getNameOverride(ep));
        if (string.Equals(nameOverride, name, StringComparison.Ordinal)) nameOverride = null;

        return new EntryPointLayout(
            name,
            stage,
            parameters,
            threadGroup,
            SlangApi.spReflectionEntryPoint_usesAnySampleRateInput(ep) != 0,
            resultType,
            parameterBase,
            uniformSize,
            nameOverride,
            waveSize == 0 ? null : waveSize,
            SlangApi.spReflectionEntryPoint_hasDefaultConstantBuffer(ep) != 0);
    }

    // ---------------------------------------------------------------- descriptor sets

    /// <summary>
    /// Builds the full descriptor-set picture, including sets introduced by parameter blocks.
    /// </summary>
    /// <remarks>
    /// Determined empirically against slang-compiler 2026.14.1, because the shape is not obvious:
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
    private static List<DescriptorSet> ReadDescriptorSets(SlangProgramLayout* shader)
    {
        var bySpace = new SortedDictionary<uint, List<DescriptorRange>>();

        var globalScope = SlangApi.spReflection_getGlobalParamsTypeLayout(shader);
        if (globalScope is not null) CollectSets(globalScope, baseSpace: 0, bySpace);

        CollectEntryPointSets(shader, bySpace);

        var paramCount = SlangApi.spReflection_GetParameterCount(shader);
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = SlangApi.spReflection_GetParameterByIndex(shader, i);
            var typeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(varLayout);
            if (typeLayout is null) continue;

            CollectSpaces(varLayout, typeLayout, parentSpace: 0, bySpace);
        }

        AddUncoveredParameters(shader, bySpace);

        return bySpace.Select(kv => new DescriptorSet(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Collects the descriptor sets of anything that introduces a register space, and of everything
    /// nested inside it.
    /// </summary>
    /// <param name="parentSpace">
    /// The space of whatever encloses this. Sub-element offsets are relative, so the absolute space
    /// is the running sum down the chain.
    /// </param>
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
        SlangReflectionVariableLayout* varLayout,
        SlangReflectionTypeLayout* typeLayout,
        uint parentSpace,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        // No sub-element category means it holds nothing that lives in a space of its own, so
        // there is nothing here for this walk and nothing nested either.
        if (SubElementSpace(varLayout, typeLayout) is not { } offset) return;

        var space = parentSpace + offset;

        if (SlangApi.spReflectionTypeLayout_getKind(typeLayout) == SlangTypeKind.SLANG_TYPE_KIND_STRUCT)
        {
            CollectNestedSpaces(typeLayout, space, bySpace);
            return;
        }

        var element = SlangApi.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);

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
        SlangReflectionTypeLayout* structLayout,
        uint space,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        if (SlangApi.spReflectionTypeLayout_getKind(structLayout) != SlangTypeKind.SLANG_TYPE_KIND_STRUCT)
            return;

        var fields = SlangApi.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint f = 0; f < fields; f++)
        {
            var fieldVar = SlangApi.spReflectionTypeLayout_GetFieldByIndex(structLayout, f);
            if (fieldVar is null) continue;

            var fieldType = SlangApi.spReflectionVariableLayout_GetTypeLayout(fieldVar);
            if (fieldType is not null) CollectSpaces(fieldVar, fieldType, space, bySpace);
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
    /// <c>SLANG_BINDING_TYPE_UNKNOWN</c>, which <c>VulkanProjection.ToDescriptorType</c> refuses
    /// loudly. A hole reported as unknown is a bug report; a hole silently given a plausible type
    /// is the class of defect this whole exercise keeps finding.
    /// </para>
    /// </remarks>
    private static void AddUncoveredParameters(
        SlangProgramLayout* shader,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        const SlangParameterCategory Slot =
            SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT;

        var paramCount = SlangApi.spReflection_GetParameterCount(shader);
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = SlangApi.spReflection_GetParameterByIndex(shader, i);
            var typeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(varLayout);
            if (typeLayout is null) continue;

            // A parameter block lives in its own space; its contents were collected by recursing.
            if (SubElementSpace(varLayout, typeLayout) is not null) continue;

            var slots = SlangApi.spReflectionTypeLayout_GetSize(typeLayout, Slot);
            if (slots == 0) continue;

            var space = (uint)SlangApi.spReflectionVariableLayout_GetSpace(varLayout, Slot);
            var index = (uint)SlangApi.spReflectionVariableLayout_GetOffset(varLayout, Slot);

            if (!bySpace.TryGetValue(space, out var ranges)) bySpace[space] = ranges = [];
            if (ranges.Any(r => Covers(r, index))) continue;

            var type = SlangApi.spReflectionTypeLayout_GetType(typeLayout);
            var shape = type is null
                ? SlangResourceShape.SLANG_RESOURCE_NONE
                : SlangApi.spReflectionType_GetResourceShape(type)
                  & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK;

            var variable = SlangApi.spReflectionVariableLayout_GetVariable(varLayout);

            ranges.Add(new DescriptorRange(
                shape == SlangResourceShape.SLANG_TEXTURE_SUBPASS
                    ? SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET
                    : SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN,
                index,
                (uint)slots,
                Slot,
                Name: variable is null ? null : Utf8.Read(SlangApi.spReflectionVariable_GetName(variable)),
                InputAttachmentIndex: SubpassIndex(varLayout, typeLayout)));
        }

        static bool Covers(DescriptorRange range, uint index) =>
            range.IsUnbounded
                ? index >= range.IndexOffset
                : index >= range.IndexOffset && index < range.IndexOffset + Math.Max(range.DescriptorCount, 1);
    }

    /// <summary>The <c>SUBPASS</c> category offset — the module's input attachment index.</summary>
    private static uint? SubpassIndex(
        SlangReflectionVariableLayout* varLayout,
        SlangReflectionTypeLayout* typeLayout)
    {
        var categories = SlangApi.spReflectionTypeLayout_GetCategoryCount(typeLayout);
        for (uint c = 0; c < categories; c++)
        {
            if (SlangApi.spReflectionTypeLayout_GetCategoryByIndex(typeLayout, c)
                != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUBPASS)
            {
                continue;
            }

            return (uint)SlangApi.spReflectionVariableLayout_GetOffset(
                varLayout, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUBPASS);
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
    /// slang-compiler 2026.14.1: with three global descriptors the entry-point block reports
    /// <c>DESCRIPTOR_TABLE_SLOT off=3</c>, and its two ranges at relative 0 and 1 land at absolute
    /// bindings 3 and 4, exactly matching the module's decorations.
    /// </remarks>
    private static void CollectEntryPointSets(
        SlangProgramLayout* shader,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        var count = SlangApi.spReflection_getEntryPointCount(shader);
        for (ulong e = 0; e < count; e++)
        {
            var ep = SlangApi.spReflection_getEntryPointByIndex(shader, e);
            if (ep is null) continue;

            var varLayout = SlangApi.spReflectionEntryPoint_getVarLayout(ep);
            if (varLayout is null) continue;

            var typeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(varLayout);
            if (typeLayout is null) continue;

            var baseSpace = (uint)SlangApi.spReflectionVariableLayout_GetSpace(
                varLayout, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT);

            CollectSets(typeLayout, baseSpace, bySpace, varLayout);
        }
    }

    /// <param name="rebase">
    /// When set, the variable layout whose per-category offsets each range index is relative to.
    /// Null for the global scope, whose ranges are already absolute.
    /// </param>
    private static void CollectSets(
        SlangReflectionTypeLayout* tl,
        uint baseSpace,
        SortedDictionary<uint, List<DescriptorRange>> bySpace,
        SlangReflectionVariableLayout* rebase = null)
    {
        var leaves = ReadBindingRanges(tl);

        var setCount = SlangApi.spReflectionTypeLayout_getDescriptorSetCount(tl);
        for (long s = 0; s < setCount; s++)
        {
            var space = baseSpace + (uint)SlangApi.spReflectionTypeLayout_getDescriptorSetSpaceOffset(tl, s);
            if (!bySpace.TryGetValue(space, out var ranges))
                bySpace[space] = ranges = [];

            var rangeCount = SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(tl, s);
            for (long r = 0; r < rangeCount; r++)
            {
                var category = SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(tl, s, r);
                var index = (uint)SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(tl, s, r);
                var count = SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(tl, s, r);

                if (rebase is not null)
                    index += (uint)SlangApi.spReflectionVariableLayout_GetOffset(rebase, category);

                // -1 as a SlangInt is how Slang reports "runtime-sized". It is not a count and
                // must not be handed to Vulkan as one.
                var unbounded = count < 0 || (ulong)count >= uint.MaxValue;
                var leaf = leaves.GetValueOrDefault((s, r));

                ranges.Add(new DescriptorRange(
                    SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(tl, s, r),
                    index,
                    unbounded ? 0u : (uint)count,
                    category,
                    Name: leaf.Name,
                    IsUnbounded: unbounded,
                    ImageFormat: leaf.Format));
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
    /// those are not top-level parameters, so the projection's habit of recovering names by
    /// scanning the parameter list reported every binding of Slang's own descriptor-set feature as
    /// unnamed.
    /// </para>
    /// <para>
    /// A binding range with <c>descriptorSetIndex == -1</c> is a parameter block, which lives in a
    /// space of its own rather than in one of this layout's sets; its contents are named when the
    /// walk recurses into the element.
    /// </para>
    /// <para>
    /// <b>The image format call is guarded deliberately.</b>
    /// <c>spReflectionTypeLayout_getBindingRangeImageFormat</c> access-violates — not returns
    /// <c>unknown</c>, crashes the process — when asked about a <c>PARAMETER_BLOCK</c> binding
    /// range, measured against slang-compiler 2026.14.1. Only texture-shaped ranges are asked.
    /// </para>
    /// </remarks>
    private static Dictionary<(long Set, long Range), (string? Name, SlangImageFormat Format)>
        ReadBindingRanges(SlangReflectionTypeLayout* tl)
    {
        var result = new Dictionary<(long, long), (string?, SlangImageFormat)>();

        var count = SlangApi.spReflectionTypeLayout_getBindingRangeCount(tl);
        for (long b = 0; b < count; b++)
        {
            var set = SlangApi.spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(tl, b);
            if (set < 0) continue;

            var type = SlangApi.spReflectionTypeLayout_getBindingRangeType(tl, b);
            var format = type is SlangBindingType.SLANG_BINDING_TYPE_TEXTURE
                             or SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TETURE
                ? SlangApi.spReflectionTypeLayout_getBindingRangeImageFormat(tl, b)
                : SlangImageFormat.SLANG_IMAGE_FORMAT_unknown;

            var variable = SlangApi.spReflectionTypeLayout_getBindingRangeLeafVariable(tl, b);
            var name = variable is null ? null : Utf8.Read(SlangApi.spReflectionVariable_GetName(variable));

            var first = SlangApi.spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(tl, b);
            var spanned = SlangApi.spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(tl, b);
            for (long r = 0; r < spanned; r++) result[(set, first + r)] = (name, format);
        }

        return result;
    }

    /// <summary>
    /// Adds the implicit constant-buffer descriptor a container (parameter block / constant
    /// buffer) occupies for its own uniform data, if it has any.
    /// </summary>
    private static void AddContainerDescriptor(
        SlangReflectionVariableLayout* varLayout,
        SlangReflectionTypeLayout* typeLayout,
        SlangReflectionTypeLayout* element,
        uint space,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        var container = SlangApi.spReflectionTypeLayout_getContainerVarLayout(typeLayout);
        if (container is null) return;

        var containerType = SlangApi.spReflectionVariableLayout_GetTypeLayout(container);
        if (containerType is null) return;

        // No uniform payload means no implicit buffer — the block holds only resources.
        var uniformSize = element is null
            ? 0
            : SlangApi.spReflectionTypeLayout_GetSize(
                element, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
        if (uniformSize == 0) return;

        var categories = SlangApi.spReflectionTypeLayout_GetCategoryCount(containerType);
        for (uint c = 0; c < categories; c++)
        {
            var category = SlangApi.spReflectionTypeLayout_GetCategoryByIndex(containerType, c);
            if (category != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT)
                continue;

            var index = (uint)SlangApi.spReflectionVariableLayout_GetOffset(container, category);

            if (!bySpace.TryGetValue(space, out var ranges))
                bySpace[space] = ranges = [];

            // Only add if nothing already claims that binding — the element's own sets take
            // precedence, and duplicate bindings would produce an invalid set layout.
            if (ranges.All(r => r.IndexOffset != index))
            {
                // Named after the block, which is what the module calls it: a ParameterBlock<Frame>
                // named gFrame decorates its implicit buffer `gFrame`, while its contents get
                // `gFrame.shadow` and so on.
                var variable = SlangApi.spReflectionVariableLayout_GetVariable(varLayout);

                ranges.Add(new DescriptorRange(
                    SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, index, 1, category,
                    Name: variable is null ? null : Utf8.Read(SlangApi.spReflectionVariable_GetName(variable))));
            }
            return;
        }
    }

    /// <summary>
    /// The register space a parameter introduces, if it introduces one. For register-space
    /// categories the *offset* carries the space index; the space field stays 0.
    /// </summary>
    private static uint? SubElementSpace(
        SlangReflectionVariableLayout* varLayout,
        SlangReflectionTypeLayout* typeLayout)
    {
        var categories = SlangApi.spReflectionTypeLayout_GetCategoryCount(typeLayout);
        for (uint c = 0; c < categories; c++)
        {
            var category = SlangApi.spReflectionTypeLayout_GetCategoryByIndex(typeLayout, c);
            if (category is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE
                         or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_REGISTER_SPACE)
            {
                return (uint)SlangApi.spReflectionVariableLayout_GetOffset(varLayout, category);
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- the type tree

    private static TypeLayout ReadTypeLayout(SlangReflectionTypeLayout* tl, Dictionary<nint, TypeLayout> cache)
    {
        if (tl is null) return new OpaqueLayout { Kind = LayoutKind.Unknown };
        if (cache.TryGetValue((nint)tl, out var hit)) return hit;

        // Insert a placeholder before recursing so a self-referential type cannot loop forever.
        cache[(nint)tl] = new OpaqueLayout { Kind = LayoutKind.Unknown, Name = "<recursive>" };

        var built = Build(tl, cache);
        cache[(nint)tl] = built;
        return built;
    }

    private static TypeLayout Build(SlangReflectionTypeLayout* tl, Dictionary<nint, TypeLayout> cache)
    {
        var type = SlangApi.spReflectionTypeLayout_GetType(tl);
        var kind = SlangApi.spReflectionTypeLayout_getKind(tl);
        var name = type is not null ? Utf8.Read(SlangApi.spReflectionType_GetName(type)) : null;
        var sizes = ReadSizes(tl);
        var alignment = SlangApi.spReflectionTypeLayout_getAlignment(
            tl, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);

        switch (kind)
        {
            case SlangTypeKind.SLANG_TYPE_KIND_SCALAR:
                return new ScalarLayout
                {
                    Kind = LayoutKind.Scalar, Name = name, Sizes = sizes, Alignment = alignment,
                    ScalarType = SlangApi.spReflectionType_GetScalarType(type),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_VECTOR:
                return new VectorLayout
                {
                    Kind = LayoutKind.Vector, Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ElementScalar(type),
                    ElementCount = (int)SlangApi.spReflectionType_GetElementCount(type),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_MATRIX:
            {
                var mode = SlangApi.spReflectionTypeLayout_GetMatrixLayoutMode(tl);
                var rows = (int)SlangApi.spReflectionType_GetRowCount(type);
                var columns = (int)SlangApi.spReflectionType_GetColumnCount(type);

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
                };

            case SlangTypeKind.SLANG_TYPE_KIND_ARRAY:
            {
                // Ask about the size first, and only ask for a count if the answer was a number.
                // An array whose length is a specialization constant reports CategorySize.Unknown,
                // and spReflectionType_GetElementCount access-violates on it — as does
                // GetSpecializedElementCount, which the C++ header presents as the resolving
                // version. Measured against slang-compiler 2026.14.1; see ArrayLayout for the
                // resource-array case, which has no such signal and cannot be guarded.
                var specializationSized = sizes.Any(s => !s.IsKnown);

                var count = specializationSized ? 0 : SlangApi.spReflectionType_GetElementCount(type);

                return new ArrayLayout
                {
                    Kind = LayoutKind.Array, Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ReadTypeLayout(SlangApi.spReflectionTypeLayout_GetElementTypeLayout(tl), cache),
                    // 0 is how Slang reports an unbounded array; a real zero-length array is not
                    // expressible in the language, so the mapping is unambiguous.
                    ElementCount = specializationSized || count == 0 ? null : (int)count,
                    IsSpecializationSized = specializationSized,
                    ElementStride = SlangApi.spReflectionTypeLayout_GetElementStride(
                        tl, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM),
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
                var elementLayout = SlangApi.spReflectionTypeLayout_GetElementTypeLayout(tl);

                return new ResourceLayout
                {
                    Kind = LayoutKind.Resource, Name = name, Sizes = sizes, Alignment = alignment,
                    Shape = SlangApi.spReflectionType_GetResourceShape(type),
                    Access = SlangApi.spReflectionType_GetResourceAccess(type),
                    ResultType = ReadResultType(type, cache),
                    ElementType = elementLayout is null ? null : ReadTypeLayout(elementLayout, cache),
                    ElementStride = elementLayout is null
                        ? 0
                        : SlangApi.spReflectionTypeLayout_GetStride(
                            elementLayout, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM),
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
                    ElementType = ReadTypeLayout(SlangApi.spReflectionTypeLayout_GetElementTypeLayout(tl), cache),
                };

            default:
                return new OpaqueLayout
                {
                    Kind = kind switch
                    {
                        SlangTypeKind.SLANG_TYPE_KIND_INTERFACE => LayoutKind.Interface,
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

    private static List<FieldLayout> ReadFields(SlangReflectionTypeLayout* tl, Dictionary<nint, TypeLayout> cache)
    {
        var fields = new List<FieldLayout>();
        var count = SlangApi.spReflectionTypeLayout_GetFieldCount(tl);

        for (uint i = 0; i < count; i++)
        {
            var fieldVarLayout = SlangApi.spReflectionTypeLayout_GetFieldByIndex(tl, i);
            if (fieldVarLayout is null) continue;

            var variable = SlangApi.spReflectionVariableLayout_GetVariable(fieldVarLayout);
            var fieldTypeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(fieldVarLayout);

            var offsets = new List<CategoryOffset>();
            var categories = SlangApi.spReflectionTypeLayout_GetCategoryCount(fieldTypeLayout);
            for (uint c = 0; c < categories; c++)
            {
                var category = SlangApi.spReflectionTypeLayout_GetCategoryByIndex(fieldTypeLayout, c);
                offsets.Add(new CategoryOffset(
                    category,
                    SlangApi.spReflectionVariableLayout_GetOffset(fieldVarLayout, category),
                    (uint)SlangApi.spReflectionVariableLayout_GetSpace(fieldVarLayout, category)));
            }

            fields.Add(new FieldLayout(
                Utf8.Read(SlangApi.spReflectionVariable_GetName(variable)) ?? "<unnamed>",
                ReadTypeLayout(fieldTypeLayout, cache),
                offsets,
                Utf8.Read(SlangApi.spReflectionVariableLayout_GetSemanticName(fieldVarLayout)),
                (uint)SlangApi.spReflectionVariableLayout_GetSemanticIndex(fieldVarLayout),
                ReadAttributes(variable)));
        }

        return fields;
    }

    private static List<CategorySize> ReadSizes(SlangReflectionTypeLayout* tl)
    {
        var sizes = new List<CategorySize>();
        var count = SlangApi.spReflectionTypeLayout_GetCategoryCount(tl);

        for (uint c = 0; c < count; c++)
        {
            var category = SlangApi.spReflectionTypeLayout_GetCategoryByIndex(tl, c);
            sizes.Add(new CategorySize(category, SlangApi.spReflectionTypeLayout_GetSize(tl, category)));
        }

        return sizes;
    }

    private static List<UserAttribute>? ReadAttributes(SlangReflectionVariable* variable)
    {
        if (variable is null) return null;

        var count = SlangApi.spReflectionVariable_GetUserAttributeCount(variable);
        if (count == 0) return null;

        var attributes = new List<UserAttribute>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var attr = SlangApi.spReflectionVariable_GetUserAttribute(variable, i);
            if (attr is not null) attributes.Add(ReadAttribute(attr));
        }

        return attributes.Count > 0 ? attributes : null;
    }

    /// <summary>One user attribute, whatever it is hanging off.</summary>
    private static UserAttribute ReadAttribute(SlangReflectionUserAttribute* attr)
    {
        var name = Utf8.Read(SlangApi.spReflectionUserAttribute_GetName(attr)) ?? "<unnamed>";
        var argCount = SlangApi.spReflectionUserAttribute_GetArgumentCount(attr);
        var args = new List<object?>((int)argCount);

        for (uint a = 0; a < argCount; a++)
        {
            // Slang has no "what type is this argument" query; the accessors report failure
            // for the wrong type, so probe int, then float, then string.
            int intValue;
            float floatValue;
            if (SlangResult.Succeeded(SlangApi.spReflectionUserAttribute_GetArgumentValueInt(attr, a, &intValue)))
                args.Add(intValue);
            else if (SlangResult.Succeeded(SlangApi.spReflectionUserAttribute_GetArgumentValueFloat(attr, a, &floatValue)))
                args.Add(floatValue);
            else
            {
                nuint len;
                var s = SlangApi.spReflectionUserAttribute_GetArgumentValueString(attr, a, &len);
                args.Add(s is not null ? new string((sbyte*)s, 0, (int)len) : null);
            }
        }

        return new UserAttribute(name, args);
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

    private static SlangScalarType ElementScalar(SlangReflectionType* type)
    {
        var element = SlangApi.spReflectionType_GetElementType(type);
        return element is not null
            ? SlangApi.spReflectionType_GetScalarType(element)
            : SlangApi.spReflectionType_GetScalarType(type);
    }

    private static TypeLayout? ReadResultType(SlangReflectionType* type, Dictionary<nint, TypeLayout> cache)
    {
        var result = SlangApi.spReflectionType_GetResourceResultType(type);
        if (result is null) return null;

        // A bare SlangReflectionType* has no layout, so describe it structurally without sizes.
        var kind = SlangApi.spReflectionType_GetKind(result);
        var name = Utf8.Read(SlangApi.spReflectionType_GetName(result));

        return kind switch
        {
            SlangTypeKind.SLANG_TYPE_KIND_SCALAR => new ScalarLayout
            {
                Kind = LayoutKind.Scalar, Name = name,
                ScalarType = SlangApi.spReflectionType_GetScalarType(result),
            },
            SlangTypeKind.SLANG_TYPE_KIND_VECTOR => new VectorLayout
            {
                Kind = LayoutKind.Vector, Name = name,
                ElementType = ElementScalar(result),
                ElementCount = (int)SlangApi.spReflectionType_GetElementCount(result),
            },
            _ => new OpaqueLayout { Kind = LayoutKind.Unknown, Name = name },
        };
    }
}

/// <summary>Convenience entry point for reflecting a linked program.</summary>
public static class LinkedProgramReflectionExtensions
{
    /// <summary>
    /// Walks this program's reflection and returns an immutable snapshot. Safe to keep after the
    /// program is disposed.
    /// </summary>
    public static ProgramLayout Reflect(this LinkedProgram program, int targetIndex = 0) =>
        ReflectionWalker.Walk(program, targetIndex);

    /// <summary>
    /// Walks this module's declaration tree. Independent of linking, so it works on a module that
    /// still has unbound generic parameters.
    /// </summary>
    public static DeclarationNode ReflectDeclarations(this SlangModule module) =>
        ReflectionWalker.WalkDeclarations(module);
}
