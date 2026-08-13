// What has never been tried.
//
// ModuleAgreementTests proves that what the corpus covers is reported correctly. It says nothing
// about what the corpus does not cover, and that is where every defect so far has lived: subpass
// inputs, nested parameter blocks, specialized workgroup sizes, non-square matrices. None of them
// was wrong in a way anyone could see — they were simply never compiled.
//
// So this walks Slang's own enumerations and requires every value to be either exercised by the
// corpus or listed below with a reason. It does not find bugs. It converts "nobody tried that" from
// invisible into a failing test, which is the only way the first kind of gap becomes visible before
// a consumer finds it.
//
// A value that becomes exercised must be removed from its list, and a value Slang adds in a future
// release arrives unexercised and undeclared. Both fail.

using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class SurfaceCoverageTests
{
    // ---------------------------------------------------------------- what is knowingly untried

    /// <summary>Stages the corpus does not compile, and why.</summary>
    private static readonly Dictionary<SlangStage, string> UnexercisedStages = new()
    {
        // SLANG_STAGE_PIXEL is not listed: it is the same value as SLANG_STAGE_FRAGMENT, so the
        // corpus covers it by covering fragment shaders.
        [SlangStage.SLANG_STAGE_NONE] = "not a stage; Slang's zero value",
        [SlangStage.SLANG_STAGE_DISPATCH] = "a work-graph stage; PrismEngine is Vulkan-only and "
            + "Vulkan has no work graphs",
        [SlangStage.SLANG_STAGE_NODE] = "as above — a work-graph node",
        [SlangStage.SLANG_STAGE_COUNT] = "not a stage; the enum's cardinality",
    };

    /// <summary>Binding kinds the corpus does not produce, and why.</summary>
    private static readonly Dictionary<SlangBindingType, string> UnexercisedBindings = new()
    {
        [SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN] = "the marker the descriptor reconciliation "
            + "uses for a hole it cannot type; producing one on purpose would need a shape Slang "
            + "drops that this build does not know about",
        [SlangBindingType.SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA] = "no Slang syntax found that "
            + "produces one; VK_EXT_inline_uniform_block is reachable only through explicit layout",
        [SlangBindingType.SLANG_BINDING_TYPE_VARYING_INPUT] = "a varying, not a descriptor. Covered "
            + "as vertex inputs by the agreement harness rather than as a binding",
        [SlangBindingType.SLANG_BINDING_TYPE_VARYING_OUTPUT] = "as above, covered as fragment outputs",
        [SlangBindingType.SLANG_BINDING_TYPE_EXISTENTIAL_VALUE] = "interface-typed parameters "
            + "resolved at bind time; PrismEngine specialises at cook time instead. Docs ",
        [SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT] = "produced by the push-constants case, "
            + "but filtered out of the descriptor sets before the projection sees it",
        [SlangBindingType.SLANG_BINDING_TYPE_PARAMETER_BLOCK] = "measured never to reach a "
            + "descriptor range on SPIR-V: a ParameterBlock takes a register space of its own, and "
            + "the descriptor it does occupy is reported as CONSTANT_BUFFER for the implicit "
            + "uniform buffer. It appears only as a *binding* range at global scope, with "
            + "descriptorSetIndex -1. VulkanProjection maps it to UniformBuffer anyway; that arm "
            + "is unreachable for this target and this test is the record of that",
    };

    /// <summary>Resource shapes the corpus does not declare, and why.</summary>
    private static readonly Dictionary<SlangResourceShape, string> UnexercisedShapes = new()
    {
        [SlangResourceShape.SLANG_RESOURCE_NONE] = "the absence of a resource",
        [SlangResourceShape.SLANG_TEXTURE_1D] = "no Vulkan renderer path uses 1D textures, and the "
            + "descriptor type does not distinguish dimensionality",
        [SlangResourceShape.SLANG_RESOURCE_UNKNOWN] = "Slang's marker for a shape it cannot report",
    };

    /// <summary>
    /// Shape values that are flags, masks or combinations rather than base shapes.
    /// </summary>
    /// <remarks>
    /// Coverage is measured after masking with <c>SLANG_RESOURCE_BASE_SHAPE_MASK</c>, so only the
    /// low nibble is ever observed and requiring the rest would be requiring the impossible. The
    /// flags themselves are exercised — <c>Texture2DArray</c> and <c>Texture2DMS</c> are in the
    /// corpus — they simply are not what this enumeration counts.
    /// </remarks>
    private static bool IsBaseShape(SlangResourceShape shape) =>
        shape == (shape & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK)
        && shape != SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK;

    /// <summary>Type kinds the corpus does not produce, and why.</summary>
    private static readonly Dictionary<SlangTypeKind, string> UnexercisedTypeKinds = new()
    {
        [SlangTypeKind.SLANG_TYPE_KIND_NONE] = "the zero value",
        [SlangTypeKind.SLANG_TYPE_KIND_GENERIC_TYPE_PARAMETER] = "an unspecialised generic; "
            + "GenericsTests covers the specialisation API directly",
        [SlangTypeKind.SLANG_TYPE_KIND_INTERFACE] = "as above — and Slang reports an interface "
            + "declaration as UNSUPPORTED_FOR_REFLECTION anyway. Docs ",
        [SlangTypeKind.SLANG_TYPE_KIND_OUTPUT_STREAM] = "the geometry shader's TriangleStream is a "
            + "parameter Slang does not surface in the layout",
        [SlangTypeKind.SLANG_TYPE_KIND_SPECIALIZED] = "produced by link-time specialisation, "
            + "covered by GenericsTests rather than by a corpus shader",
        [SlangTypeKind.SLANG_TYPE_KIND_FEEDBACK] = "D3D12 sampler feedback; no Vulkan equivalent",
        [SlangTypeKind.SLANG_TYPE_KIND_POINTER] = "a buffer-device-address pointer; unused by "
            + "PrismEngine so far and it occupies no descriptor",
        [SlangTypeKind.SLANG_TYPE_KIND_DYNAMIC_RESOURCE] = "bindless-by-handle, which the engine "
            + "does with unbounded arrays instead",
        [SlangTypeKind.SLANG_TYPE_KIND_TEXTURE_BUFFER] = "the D3D tbuffer; Vulkan shaders use a "
            + "constant buffer or a structured buffer",
        [SlangTypeKind.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER] = "produced by GLSL-style buffer "
            + "blocks, which Slang source does not write",
        [SlangTypeKind.SLANG_TYPE_KIND_COUNT] = "not a kind; the enum's cardinality",
        [SlangTypeKind.SLANG_TYPE_KIND_ENUM] = "an enum is a declaration, not a parameter type — "
            + "it reaches the layout as the scalar it is backed by",
        [SlangTypeKind.SLANG_TYPE_KIND_MESH_OUTPUT] = "the mesh case declares `out vertices` and "
            + "`out indices`, and Slang reports neither as an entry-point parameter, so the kind "
            + "never reaches the layout. A renderer sizes mesh output from the shader source, not "
            + "from reflection",
    };

    // ---------------------------------------------------------------- the tests

    [NativeFact]
    public void Every_stage_is_exercised_or_declared_untried() =>
        AssertCovered(Observed().Stages, UnexercisedStages, "stage");

    [NativeFact]
    public void Every_binding_kind_is_exercised_or_declared_untried() =>
        AssertCovered(Observed().Bindings, UnexercisedBindings, "binding kind", IsBaseBindingType);

    [NativeFact]
    public void Every_resource_shape_is_exercised_or_declared_untried() =>
        AssertCovered(Observed().Shapes, UnexercisedShapes, "resource shape", IsBaseShape);

    [NativeFact]
    public void Every_type_kind_is_exercised_or_declared_untried() =>
        AssertCovered(Observed().TypeKinds, UnexercisedTypeKinds, "type kind");

    [NativeFact]
    public void Nothing_is_declared_untried_that_the_corpus_actually_covers()
    {
        // The other half of the ratchet. Without it a declaration outlives the gap it described,
        // and the list stops being a true statement about what has never been compiled.
        var observed = Observed();

        AssertNotObserved(observed.Stages, UnexercisedStages, "stage");
        AssertNotObserved(observed.Shapes, UnexercisedShapes, "resource shape");
        AssertNotObserved(observed.TypeKinds, UnexercisedTypeKinds, "type kind");

        // Binding kinds are the exception: PUSH_CONSTANT is declared because the projection filters
        // it out, though the walk does see it.
        AssertNotObserved(
            observed.Bindings.Where(b => b != SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT),
            UnexercisedBindings,
            "binding kind");
    }

    private static void AssertCovered<T>(
        IEnumerable<T> observed,
        Dictionary<T, string> declared,
        string label,
        Func<T, bool>? relevant = null)
        where T : struct, Enum
    {
        var seen = observed.ToHashSet();
        var missing = Enum.GetValues<T>()
            .Where(v => relevant?.Invoke(v) ?? true)
            .Distinct()
            .Where(v => !seen.Contains(v) && !declared.ContainsKey(v))
            .Order()
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} {label}(s) are neither exercised by ShaderCorpus nor declared "
            + $"untried in SurfaceCoverageTests:\n"
            + string.Join("", missing.Select(v => $"    {v}\n"))
            + "Add a corpus shader that produces it, or add it to the list with the reason it "
            + "will not be.");
    }

    private static void AssertNotObserved<T>(
        IEnumerable<T> observed,
        Dictionary<T, string> declared,
        string label)
        where T : struct, Enum
    {
        var stale = observed.Where(declared.ContainsKey).Distinct().Order().ToList();

        Assert.True(stale.Count == 0,
            $"{stale.Count} {label}(s) are declared untried but the corpus now produces them. "
            + $"Remove them from SurfaceCoverageTests:\n"
            + string.Join("", stale.Select(v => $"    {v} — was: {declared[v]}\n")));
    }

    /// <summary>
    /// Flags and masks are not binding kinds. Only the base values are things a shader can declare.
    /// </summary>
    private static bool IsBaseBindingType(SlangBindingType type) =>
        type is not (SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_FLAG
                  or SlangBindingType.SLANG_BINDING_TYPE_BASE_MASK
                  or SlangBindingType.SLANG_BINDING_TYPE_EXT_MASK)
        && (type & SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_FLAG) == 0;

    // ---------------------------------------------------------------- what the corpus produces

    private sealed record Coverage(
        HashSet<SlangStage> Stages,
        HashSet<SlangBindingType> Bindings,
        HashSet<SlangResourceShape> Shapes,
        HashSet<SlangTypeKind> TypeKinds);

    private static Coverage? _cached;

    /// <summary>
    /// Everything the corpus produces, compiled once. Cached because four tests want it and
    /// compiling twenty programs is the expensive part.
    /// </summary>
    private static Coverage Observed()
    {
        if (_cached is not null) return _cached;

        var coverage = new Coverage([], [], [], []);

        using var global = SlangGlobalSession.Create();
        foreach (var shaderCase in ShaderCorpus.All)
        {
            using var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions()],
            });
            using var module = session.LoadModuleFromSource(
                shaderCase.Name, $"{shaderCase.Name}.slang", shaderCase.Source);

            var entryPoints = shaderCase.EntryPoints.Select(module.FindEntryPoint).ToList();
            try
            {
                using var program = session.Link(module, [.. entryPoints]);
                Collect(program.Reflect(), coverage);
            }
            finally
            {
                foreach (var e in entryPoints) e.Dispose();
            }
        }

        return _cached = coverage;
    }

    private static void Collect(ProgramLayout layout, Coverage into)
    {
        foreach (var e in layout.EntryPoints)
        {
            into.Stages.Add(e.Stage);
            foreach (var p in e.Parameters) Walk(p.Type, into, []);
        }

        foreach (var set in layout.DescriptorSets)
            foreach (var r in set.Ranges)
                into.Bindings.Add(r.BindingType & SlangBindingType.SLANG_BINDING_TYPE_BASE_MASK);

        foreach (var p in layout.Parameters)
        {
            Walk(p.Type, into, []);
            foreach (var b in p.Bindings)
            {
                if (b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER)
                    into.Bindings.Add(SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT);
            }
        }
    }

    private static void Walk(TypeLayout? type, Coverage into, HashSet<TypeLayout> seen)
    {
        if (type is null || !seen.Add(type)) return;

        if (ToTypeKind(type.Kind) is { } kind) into.TypeKinds.Add(kind);

        switch (type)
        {
            case ResourceLayout r:
                into.Shapes.Add(r.Shape & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK);
                Walk(r.ElementType, into, seen);
                Walk(r.ResultType, into, seen);
                return;

            case StructLayout s:
                foreach (var f in s.Fields) Walk(f.Type, into, seen);
                return;

            case ContainerLayout c: Walk(c.ElementType, into, seen); return;
            case ArrayLayout a: Walk(a.ElementType, into, seen); return;
            default: return;
        }
    }

    /// <summary>
    /// The Slang kind a projected <see cref="LayoutKind"/> came from.
    /// </summary>
    /// <remarks>
    /// The walker collapses several Slang kinds into <c>LayoutKind</c>, so this is the inverse of
    /// that mapping. <c>LayoutKind.Unknown</c> inverts to nothing rather than to
    /// <c>SLANG_TYPE_KIND_NONE</c>: it means the walker could not say, and folding it into a real
    /// enum value would mark that value covered by a type nobody identified — coverage satisfied by
    /// ignorance.
    /// </remarks>
    private static SlangTypeKind? ToTypeKind(LayoutKind kind) => kind switch
    {
        LayoutKind.Scalar => SlangTypeKind.SLANG_TYPE_KIND_SCALAR,
        LayoutKind.Vector => SlangTypeKind.SLANG_TYPE_KIND_VECTOR,
        LayoutKind.Matrix => SlangTypeKind.SLANG_TYPE_KIND_MATRIX,
        LayoutKind.Struct => SlangTypeKind.SLANG_TYPE_KIND_STRUCT,
        LayoutKind.Array => SlangTypeKind.SLANG_TYPE_KIND_ARRAY,
        LayoutKind.Resource => SlangTypeKind.SLANG_TYPE_KIND_RESOURCE,
        LayoutKind.SamplerState => SlangTypeKind.SLANG_TYPE_KIND_SAMPLER_STATE,
        LayoutKind.ConstantBuffer => SlangTypeKind.SLANG_TYPE_KIND_CONSTANT_BUFFER,
        LayoutKind.ParameterBlock => SlangTypeKind.SLANG_TYPE_KIND_PARAMETER_BLOCK,
        LayoutKind.TextureBuffer => SlangTypeKind.SLANG_TYPE_KIND_TEXTURE_BUFFER,
        LayoutKind.ShaderStorageBuffer => SlangTypeKind.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER,
        LayoutKind.Interface => SlangTypeKind.SLANG_TYPE_KIND_INTERFACE,
        LayoutKind.Specialized => SlangTypeKind.SLANG_TYPE_KIND_SPECIALIZED,
        LayoutKind.Feedback => SlangTypeKind.SLANG_TYPE_KIND_FEEDBACK,
        LayoutKind.Pointer => SlangTypeKind.SLANG_TYPE_KIND_POINTER,
        LayoutKind.DynamicResource => SlangTypeKind.SLANG_TYPE_KIND_DYNAMIC_RESOURCE,
        _ => null,
    };
}
