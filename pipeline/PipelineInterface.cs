// The parts of a layout that every graphics API reads the same way.
//
// Projecting a ProgramLayout onto a pipeline description is mostly API-specific: which descriptor
// type a binding becomes, what a stage is called, which format a float3 attribute has. Underneath
// that is a layer that is not: which leaf attributes the vertex stage reads and at which locations,
// which outputs the fragment stage writes, which blocks are push constants, and what a descriptor
// slot is called. SharpSlang.Vulkan discovered each of those the hard way — a matrix attribute
// spanning four locations, a builtin with no location at all, a push-constant block whose size is
// not knowable — and a second projection would have re-discovered them one by one.
//
// So the discovery lives here, once, and both projections consume it. Nothing in this file names a
// graphics API. A leaf attribute is handed over as the TypeLayout it was declared with, and each
// projection decides whether it has a format for it.

using SharpSlang.Native;

namespace SharpSlang.Reflection;

/// <summary>One leaf vertex attribute, before any API has given it a format.</summary>
/// <param name="Location">The <c>location = N</c> index, from the offset Slang reported.</param>
/// <param name="Type">
/// The leaf type: a <see cref="ScalarLayout"/> or <see cref="VectorLayout"/>. A matrix arrives as
/// one of these per location, synthesised from its element type and width.
/// </param>
/// <param name="LocationsConsumed">
/// Locations this attribute occupies as Slang counted them. A projection with no format for
/// <paramref name="Type"/> must refuse when this is non-zero, because dropping the attribute
/// silently is how a projection ends up one location short for everything after it.
/// </param>
/// <param name="Name">Parameter name as declared, with <c>[i]</c> appended for a matrix or array element.</param>
/// <param name="Semantic">HLSL-style semantic, if the shader declared one.</param>
/// <param name="SemanticIndex">The semantic's index, for APIs that bind by semantic.</param>
public sealed record VertexInput(
    uint Location,
    TypeLayout Type,
    uint LocationsConsumed,
    string Name,
    string? Semantic = null,
    uint SemanticIndex = 0);

/// <summary>One colour attachment the fragment stage writes, before any API has given it a format.</summary>
/// <param name="Location">The <c>location = N</c> index, i.e. the attachment index.</param>
/// <param name="Type">The type the shader writes, not the attachment's storage format.</param>
/// <param name="Name">Field name as declared, or the entry point's name for a bare return.</param>
/// <param name="Semantic">The <c>SV_Target</c><i>N</i> semantic, where one was declared.</param>
public sealed record FragmentOutput(
    uint Location,
    TypeLayout Type,
    string Name,
    string? Semantic = null);

/// <summary>A block Slang placed in push-constant storage.</summary>
/// <param name="Name">
/// The parameter name for an explicit <c>[vk::push_constant]</c> block, or
/// <c>&lt;entry point&gt;.uniforms</c> for an entry point's <c>uniform</c> value parameters.
/// </param>
/// <param name="Size">Byte size of the block's contents.</param>
/// <param name="Stage">
/// The one stage that reads it, for an entry point's uniform parameters. Null for an explicit
/// block, which is visible to every stage of the program.
/// </param>
public sealed record PushConstantBlock(string Name, ulong Size, SlangStage? Stage = null);

/// <summary>Reads the API-neutral parts of a pipeline interface out of a <see cref="ProgramLayout"/>.</summary>
public static class PipelineInterface
{
    // ---------------------------------------------------------------- invariants

    /// <summary>
    /// Refuses a program holding a shader parameter that Slang placed nowhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An invariant rather than a comparison, and it is here because comparing against the emitted
    /// module cannot catch this one: when Slang cannot place a parameter it drops the parameter
    /// <em>and</em> emits no descriptor for it, so reflection and the module agree by being equally
    /// silent while the shader still reads the thing.
    /// </para>
    /// <para>
    /// Measured cause, against slang-compiler 2026.21: a buffer whose contents include an array
    /// whose length is a specialization constant. <c>ConstantBuffer&lt;Push&gt;</c> and
    /// <c>[[vk::push_constant]] ConstantBuffer&lt;Push&gt;</c> both come back with no bindings at
    /// all, no descriptor range and no push-constant range, and a renderer built from that layout
    /// binds nothing where the shader expects a buffer.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">A parameter has no binding of any kind.</exception>
    public static void RequireEveryParameterIsPlaced(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        foreach (var p in layout.Parameters)
        {
            if (p.Bindings.Count > 0) continue;

            var cause = p.Type is ContainerLayout c && !c.ElementType.IsUniformSizeKnown
                ? " Its size is not known: it holds an array whose length is a specialization "
                  + "constant, and the pipeline layout needs the size when it is built, which "
                  + "is before any specialization value exists. Give the array a fixed length."
                : "";

            throw new NotSupportedException(
                $"shader parameter '{p.Name}' ({p.Type.Kind}) has no binding of any kind, so it is "
                + $"in the shader and in no part of the pipeline layout.{cause}");
        }
    }

    // ---------------------------------------------------------------- push constants

    /// <summary>
    /// The blocks Slang placed in push-constant storage: explicit <c>[vk::push_constant]</c>
    /// blocks, and each entry point's <c>uniform</c> value parameters, which Slang also places
    /// there.
    /// </summary>
    /// <remarks>
    /// Slang reports the offset of a push-constant block as a <em>buffer index</em>, not a byte
    /// offset, so none is reported here: with one block the offset is 0, and a program with more
    /// than one cannot be laid out into the single block a pipeline layout expects without
    /// converting a quantity that does not convert. Each projection decides what to do with a
    /// second block; both of this repository's refuse.
    /// </remarks>
    /// <exception cref="NotSupportedException">A block's size is not fixed at compile time.</exception>
    public static IReadOnlyList<PushConstantBlock> PushConstantBlocks(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var result = new List<PushConstantBlock>();

        foreach (var p in layout.Parameters)
        {
            if (!p.Bindings.Any(b => b.Category
                    == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER))
            {
                continue;
            }

            // The declared size is the element's uniform footprint, not the wrapper's.
            var block = p.Type is ContainerLayout c ? c.ElementType : p.Type;

            // Unreachable for every shader measured — a block with an unknown size loses its
            // bindings entirely and RequireEveryParameterIsPlaced catches it first. Kept because
            // the alternative is casting a marker near ulong.MaxValue to uint, producing a
            // 0xFFFFFFFE push-constant range that fails inside the driver with nothing pointing
            // back at the shader.
            if (!block.IsUniformSizeKnown)
            {
                throw new NotSupportedException(
                    $"push-constant block '{p.Name}' has no fixed size: it contains an array whose " +
                    $"length is a specialization constant, so its footprint is not known until the " +
                    $"pipeline is created. The pipeline layout needs the size when it is built, " +
                    $"which is earlier. Give the array a fixed length.");
            }

            result.Add(new PushConstantBlock(p.Name, block.UniformSize));
        }

        // An entry point's `uniform` value parameters become an implicit push-constant buffer.
        // Without this they are in the shader and in no pipeline layout, so the push has nowhere
        // to write and the values the shader reads are undefined.
        //
        // On a SPIR-V target, that is. The entry point's base offsets say where Slang put them:
        // measured on 2026.21, entry-point-uniforms reports PUSH_CONSTANT_BUFFER at its base
        // for SPIR-V and CONSTANT_BUFFER — an ordinary `b` register, with a range in the
        // descriptor-set view to match — for DXIL. Reporting a push-constant block there too put
        // the same register in the layout twice, and D3D12 refused the root signature: "Shader
        // register range of type CBV … overlaps with another shader register range". So the block
        // is reported only where the entry point's uniforms are in push-constant storage.
        foreach (var ep in layout.EntryPoints)
        {
            if (ep.UniformSize == 0) continue;
            if (ep.ParameterBase?.Any(b => b.Category
                    == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER) != true)
            {
                continue;
            }

            result.Add(new PushConstantBlock($"{ep.Name}.uniforms", ep.UniformSize, ep.Stage));
        }

        return result;
    }

    // ---------------------------------------------------------------- vertex inputs

    /// <summary>
    /// Leaf vertex attributes of the vertex stage, at the locations the module actually uses,
    /// ordered by location. Empty for a program with no vertex entry point.
    /// </summary>
    /// <remarks>
    /// Every location here comes from the offset Slang reported for that field, never from
    /// counting. The counting version of this function was wrong in two ways at once, and both
    /// were invisible until the emitted module was read back:
    ///
    /// <list type="bullet">
    /// <item>A field can span more than one location. A <c>float4x4</c> occupies four, so every
    /// attribute declared after one was reported four locations too low — the shader read
    /// whichever buffer the renderer had bound at the wrong index.</item>
    /// <item>A field whose format did not map was skipped but still advanced the counter by one,
    /// so the matrix itself vanished from the layout entirely. Instanced rendering with a
    /// per-instance transform simply had no transform.</item>
    /// </list>
    ///
    /// Measured for <c>struct VIn { float3 position; float4x4 instanceXform; float2 uv; uint4
    /// bones; }</c>: the module decorates 0, 1, 5, 6. The old code produced 0, 2, 3 and dropped
    /// the matrix.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// An attribute occupies locations but is of a kind no projection could name: a matrix whose
    /// location count does not divide its components, or a type that is neither scalar, vector,
    /// matrix, array nor struct.
    /// </exception>
    public static IReadOnlyList<VertexInput> VertexInputs(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var vertex = layout.EntryPoints.FirstOrDefault(e => e.Stage == SlangStage.SLANG_STAGE_VERTEX);
        if (vertex is null) return [];

        var result = new List<VertexInput>();

        foreach (var p in vertex.Parameters)
        {
            if (!Varying(p.Bindings, out var location)) continue;
            Emit(p.Name, p.Type, p.Semantic, p.SemanticIndex, location, result);
        }

        return result.OrderBy(v => v.Location).ToList();
    }

    /// <summary>The VARYING_INPUT offset, if the parameter has one at all.</summary>
    /// <remarks>
    /// Presence is checked rather than defaulted. A system-value input such as
    /// <c>SV_VertexID</c> is a builtin with no location, and defaulting its absent offset to 0
    /// would collide it with the attribute genuinely at location 0.
    /// </remarks>
    private static bool Varying(IReadOnlyList<ParameterBinding> bindings, out uint location)
    {
        foreach (var b in bindings)
        {
            if (b.Category != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_INPUT) continue;
            location = b.Index;
            return true;
        }

        location = 0;
        return false;
    }

    /// <summary>
    /// A field's offset in one category, and whether it has one at all.
    /// </summary>
    /// <remarks>
    /// Presence is the interesting half. A field with no offset in a varying category is a
    /// builtin — <c>SV_VertexID</c> on the way in, <c>SV_Depth</c> on the way out — and defaulting
    /// its absent offset to 0 would put it at the location of a real attribute.
    /// </remarks>
    private static bool Offset(
        IReadOnlyList<CategoryOffset> offsets,
        SlangParameterCategory category,
        out uint offset)
    {
        foreach (var o in offsets)
        {
            if (o.Category != category) continue;
            offset = (uint)o.Offset;
            return true;
        }

        offset = 0;
        return false;
    }

    /// <summary>Locations this type consumes, as Slang counted them.</summary>
    private static uint VaryingLocations(TypeLayout type) =>
        (uint)type.Sizes
            .FirstOrDefault(s => s.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_INPUT)
            .Size;

    private static void Emit(
        string name,
        TypeLayout type,
        string? semantic,
        uint semanticIndex,
        uint location,
        List<VertexInput> into)
    {
        switch (type)
        {
            case StructLayout s:
                foreach (var f in s.Fields)
                {
                    if (!Offset(f.Offsets, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_INPUT,
                            out var fieldOffset))
                    {
                        continue;
                    }
                    Emit(f.Name, f.Type, f.Semantic, f.SemanticIndex, location + fieldOffset, into);
                }
                return;

            // A matrix is several attributes at consecutive locations — the standard way to hand a
            // per-instance transform to a vertex shader. How many, and how wide each is, is derived
            // from the location count Slang reported rather than from an assumption about whether
            // rows or columns come first, which depends on the session's matrix layout mode.
            case MatrixLayout m:
            {
                var locations = VaryingLocations(m);
                var components = m.RowCount * m.ColumnCount;
                if (locations == 0 || components % locations != 0)
                {
                    throw new NotSupportedException(
                        $"vertex input '{name}' is a {m.RowCount}x{m.ColumnCount} matrix occupying " +
                        $"{locations} location(s), which does not divide evenly. SharpSlang " +
                        $"cannot say what each location holds; declare the attribute explicitly.");
                }

                var width = (int)(components / locations);
                TypeLayout piece = width == 1
                    ? new ScalarLayout { Kind = LayoutKind.Scalar, ScalarType = m.ElementType }
                    : new VectorLayout { Kind = LayoutKind.Vector, ElementType = m.ElementType, ElementCount = width };

                // The semantic index advances with the row. HLSL numbers a matrix attribute's rows
                // INSTANCE0 to INSTANCE3, and D3D12 binds vertex attributes by semantic name and
                // index: four rows all declared INSTANCE0 are refused by CreateInputLayout with
                // "have the same Semantic". Found by the D3D12 device leg on vertex-interface;
                // Vulkan binds by location and never looked at the index.
                for (var i = 0u; i < locations; i++)
                    into.Add(new VertexInput(location + i, piece, 1, $"{name}[{i}]", semantic, semanticIndex + i));
                return;
            }

            case ArrayLayout { ElementCount: { } count } a when count > 0:
            {
                var locations = VaryingLocations(a);
                var stride = locations == 0 ? 1u : locations / (uint)count;
                // The semantic index advances by what each element consumes, for the same reason
                // as the matrix rows above: `float2 uv[3] : TEXCOORD0` is TEXCOORD0, 1, 2 in HLSL,
                // and an array of matrices steps by the rows of each.
                for (var i = 0u; i < count; i++)
                    Emit($"{name}[{i}]", a.ElementType, semantic, semanticIndex + (i * stride), location + (i * stride), into);
                return;
            }

            case ScalarLayout or VectorLayout:
                into.Add(new VertexInput(location, type, VaryingLocations(type), name, semantic, semanticIndex));
                return;

            default:
                // Silently dropping an attribute is how the matrix bug hid. If the type occupies
                // locations, refusing is the only honest answer.
                if (VaryingLocations(type) > 0)
                {
                    throw new NotSupportedException(
                        $"vertex input '{name}' is {type.Kind}" +
                        $"{(type.Name is null ? "" : $" ('{type.Name}')")}, which is not a scalar, " +
                        $"vector, matrix, array or struct, but it occupies " +
                        $"{VaryingLocations(type)} location(s). SharpSlang will not guess what it holds.");
                }

                return;
        }
    }

    // ---------------------------------------------------------------- fragment outputs

    /// <summary>
    /// Colour attachments the fragment stage writes, ordered by location. Empty for a program
    /// with no fragment entry point.
    /// </summary>
    /// <remarks>
    /// A deferred renderer needs the count to size its colour-blend state, and the count has to
    /// come from the shader — a mismatch between it and the render pass is a pipeline creation
    /// failure. Locations are the <c>VARYING_OUTPUT</c> offsets Slang reported, which match the
    /// module's <c>Output</c> location decorations.
    ///
    /// The type is what the shader <em>writes</em>, not what the attachment stores. A shader
    /// returning <c>float4</c> writes fine into an <c>R8G8B8A8_UNORM</c> target; the attachment's
    /// real format is the render pass's business.
    /// </remarks>
    public static IReadOnlyList<FragmentOutput> FragmentOutputs(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var fragment = layout.EntryPoints.FirstOrDefault(e => e.Stage == SlangStage.SLANG_STAGE_FRAGMENT);
        if (fragment?.ResultType is not { } result) return [];

        var outputs = new List<FragmentOutput>();

        if (result is StructLayout s)
        {
            foreach (var f in s.Fields)
            {
                // Presence of the category, not the name of the semantic, is what separates a
                // colour attachment from a system value. Measured: `float depth : SV_Depth`
                // reports no VARYING_OUTPUT category at all and is decorated BuiltIn FragDepth
                // with no location, so it drops out here without needing a list of semantic names
                // to keep up to date.
                if (!Offset(f.Offsets, SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT,
                        out var location))
                {
                    continue;
                }

                outputs.Add(new FragmentOutput(location, f.Type, f.Name, f.Semantic));
            }
        }
        else if (result is ScalarLayout or VectorLayout)
        {
            // A bare `float4 fragmentMain() : SV_Target` return: one attachment at location 0.
            outputs.Add(new FragmentOutput(0, result, fragment.Name, "SV_TARGET"));
        }

        return outputs.OrderBy(o => o.Location).ToList();
    }

    // ---------------------------------------------------------------- descriptor names

    /// <summary>
    /// Names for the descriptor slots of one space, so a descriptor can be reported as "albedo"
    /// rather than "set 0 binding 1". Keyed by binding index within the space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback for a <see cref="DescriptorRange"/> whose <c>Name</c> is null. A range's own
    /// name is the better source where the walk could attribute one — that is the only source
    /// that reaches inside a <c>ParameterBlock</c>.
    /// </para>
    /// <para>
    /// Only parameters that genuinely occupy a descriptor slot are considered, and asking the
    /// question that way round is load-bearing. Slang reports specialization constants and
    /// <c>[vk::push_constant]</c> blocks in the same top-level list, with an index and a space that
    /// look exactly like a descriptor's: <c>[vk::constant_id(0)]</c> and a push-constant block both
    /// claim (space 0, index 0), the same coordinates as the descriptor at set 0 binding 0, and
    /// both sort ahead of it — so the first-writer-wins <c>TryAdd</c> below labelled real
    /// descriptors with their names. That was two separate bugs, found one at a time while
    /// excluding kinds as they turned up. A whitelist cannot grow a third.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<uint, string> DescriptorNames(this ProgramLayout layout, uint space) =>
        layout.DescriptorNames(space, category: null);

    /// <summary>
    /// <see cref="DescriptorNames(ProgramLayout, uint)"/> for one register class, keyed by the
    /// index within that class.
    /// </summary>
    /// <remarks>
    /// The overload a projection should use, passing the range's own
    /// <see cref="DescriptorRange.Category"/>. On a SPIR-V layout every descriptor of a set is in
    /// <c>DESCRIPTOR_TABLE_SLOT</c> and the index is unique within the set, so the class changes
    /// nothing. On a DXIL layout each register class is numbered from 0 — <c>b0</c>, <c>t0</c>,
    /// <c>s0</c> and <c>u0</c> all have index 0 — and a lookup by index alone lends a constant
    /// buffer the name of whichever texture or UAV sits at the same number. Measured by the D3D12
    /// device leg on three corpus shaders: the implicit buffer of <c>global-uniforms</c> was
    /// reported as <c>guTexture</c>, and a host binding by name would have put a texture where a
    /// constant buffer is read. Null means any class, which is the index-only reading.
    /// </remarks>
    public static IReadOnlyDictionary<uint, string> DescriptorNames(
        this ProgramLayout layout, uint space, SlangParameterCategory? category)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var names = new Dictionary<uint, string>();

        foreach (var p in layout.Parameters)
        {
            if (!p.OccupiesDescriptorSlot) continue;
            if (p.DescriptorSet != space) continue;
            var b = p.PrimaryBinding;
            if (b is null) continue;
            if (category is { } wanted && b.Value.Category != wanted) continue;
            names.TryAdd(b.Value.Index, p.Name);
        }

        // Entry-point `uniform` parameters are descriptors too, numbered from the entry point's
        // own base rather than absolutely. See ReflectionWalker.CollectEntryPointSets.
        foreach (var ep in layout.EntryPoints)
        {
            foreach (var p in ep.Parameters)
            {
                if (!p.OccupiesDescriptorSlot) continue;
                var b = p.PrimaryBinding;
                if (b is null || b.Value.Space != space) continue;
                if (category is { } wanted && b.Value.Category != wanted) continue;
                names.TryAdd(ep.BaseOffset(b.Value.Category) + b.Value.Index, p.Name);
            }
        }

        return names;
    }
}
