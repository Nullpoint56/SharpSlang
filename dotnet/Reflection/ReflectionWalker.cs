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
        };
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
        return new DeclarationNode(name, kind, children, generics);
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

        ThreadGroupSize? threadGroup = null;
        if (stage == SlangStage.SLANG_STAGE_COMPUTE)
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

        return new EntryPointLayout(
            name,
            stage,
            parameters,
            threadGroup,
            SlangApi.spReflectionEntryPoint_usesAnySampleRateInput(ep) != 0,
            resultType);
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

        var paramCount = SlangApi.spReflection_GetParameterCount(shader);
        for (uint i = 0; i < paramCount; i++)
        {
            var varLayout = SlangApi.spReflection_GetParameterByIndex(shader, i);
            var typeLayout = SlangApi.spReflectionVariableLayout_GetTypeLayout(varLayout);
            if (typeLayout is null) continue;

            var space = SubElementSpace(varLayout, typeLayout);
            if (space is null) continue;

            var element = SlangApi.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);

            // A parameter block's own uniform data becomes an implicit constant buffer, and that
            // descriptor lives on the *container* var layout rather than in the element's
            // descriptor sets. Miss it and a block of pure uniform data reports no bindings at
            // all — the block's element layout genuinely says descriptorSets=0 in that case.
            AddContainerDescriptor(varLayout, typeLayout, element, space.Value, bySpace);

            if (element is not null) CollectSets(element, space.Value, bySpace);
        }

        return bySpace.Select(kv => new DescriptorSet(kv.Key, kv.Value)).ToList();
    }

    private static void CollectSets(
        SlangReflectionTypeLayout* tl,
        uint baseSpace,
        SortedDictionary<uint, List<DescriptorRange>> bySpace)
    {
        var setCount = SlangApi.spReflectionTypeLayout_getDescriptorSetCount(tl);
        for (long s = 0; s < setCount; s++)
        {
            var space = baseSpace + (uint)SlangApi.spReflectionTypeLayout_getDescriptorSetSpaceOffset(tl, s);
            if (!bySpace.TryGetValue(space, out var ranges))
                bySpace[space] = ranges = [];

            var rangeCount = SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(tl, s);
            for (long r = 0; r < rangeCount; r++)
            {
                ranges.Add(new DescriptorRange(
                    SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(tl, s, r),
                    (uint)SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(tl, s, r),
                    (uint)SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(tl, s, r),
                    SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(tl, s, r)));
            }
        }
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
                ranges.Add(new DescriptorRange(
                    SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, index, 1, category));
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
                return new MatrixLayout
                {
                    Kind = LayoutKind.Matrix, Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ElementScalar(type),
                    RowCount = (int)SlangApi.spReflectionType_GetRowCount(type),
                    ColumnCount = (int)SlangApi.spReflectionType_GetColumnCount(type),
                    Mode = SlangApi.spReflectionTypeLayout_GetMatrixLayoutMode(tl),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_STRUCT:
                return new StructLayout
                {
                    Kind = LayoutKind.Struct, Name = name, Sizes = sizes, Alignment = alignment,
                    Fields = ReadFields(tl, cache),
                };

            case SlangTypeKind.SLANG_TYPE_KIND_ARRAY:
            {
                var count = SlangApi.spReflectionType_GetElementCount(type);
                return new ArrayLayout
                {
                    Kind = LayoutKind.Array, Name = name, Sizes = sizes, Alignment = alignment,
                    ElementType = ReadTypeLayout(SlangApi.spReflectionTypeLayout_GetElementTypeLayout(tl), cache),
                    // 0 is how Slang reports an unbounded array; a real zero-length array is not
                    // expressible in the language, so the mapping is unambiguous.
                    ElementCount = count == 0 ? null : (int)count,
                    ElementStride = SlangApi.spReflectionTypeLayout_GetElementStride(
                        tl, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM),
                };
            }

            case SlangTypeKind.SLANG_TYPE_KIND_RESOURCE:
                return new ResourceLayout
                {
                    Kind = LayoutKind.Resource, Name = name, Sizes = sizes, Alignment = alignment,
                    Shape = SlangApi.spReflectionType_GetResourceShape(type),
                    Access = SlangApi.spReflectionType_GetResourceAccess(type),
                    ResultType = ReadResultType(type, cache),
                };

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
            if (attr is null) continue;

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

            attributes.Add(new UserAttribute(name, args));
        }

        return attributes.Count > 0 ? attributes : null;
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
