// The projection itself: a neutral ProgramLayout read as Vulkan pipeline requirements.
//
// Everything here is a mechanical consequence of the Vulkan specification plus what Slang reported.
// Where a mapping is genuinely ambiguous the code says so rather than guessing quietly.

using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.Vulkan;

/// <summary>Reads a <see cref="ProgramLayout"/> as Vulkan pipeline requirements.</summary>
public static class VulkanProjection
{
    /// <summary>Projects a reflection snapshot into descriptor sets, push constants and vertex inputs.</summary>
    public static VulkanPipelineLayout ToVulkan(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var stagesBySpace = StagesPerSpace(layout);

        var sets = new List<VulkanDescriptorSetLayout>();
        foreach (var set in layout.DescriptorSets.OrderBy(s => s.SpaceOffset))
        {
            var stages = stagesBySpace.TryGetValue(set.SpaceOffset, out var s) ? s : AllStages(layout);
            var names = NamesForSpace(layout, set.SpaceOffset);

            var bindings = set.Ranges
                .Where(r => r.BindingType != SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT)
                .Select(r => new VulkanBinding(
                    r.IndexOffset,
                    ToDescriptorType(r.BindingType),
                    r.DescriptorCount,
                    stages,
                    // The range's own name where the walk could attribute one — that is the only
                    // source that reaches inside a ParameterBlock. The scan below is the fallback
                    // for ranges Slang's binding-range view does not cover.
                    r.Name ?? names.GetValueOrDefault(r.IndexOffset),
                    r.IsUnbounded,
                    r.InputAttachmentIndex,
                    r.ImageFormat))
                .OrderBy(b => b.Binding)
                .ToList();

            if (bindings.Count > 0) sets.Add(new VulkanDescriptorSetLayout(set.SpaceOffset, bindings));
        }

        return new VulkanPipelineLayout
        {
            DescriptorSets = sets,
            PushConstants = PushConstants(layout),
            VertexInputs = VertexInputs(layout),
            FragmentOutputs = FragmentOutputs(layout),
            SpecializationConstants = layout.SpecializationMap(),
            Stages = Stages(layout),
        };
    }

    /// <summary>The program's entry points as pipeline stages.</summary>
    /// <remarks>
    /// A specialized workgroup size arrives here as the zeros Slang reports, but carrying the flag
    /// that says so — which is what stops <see cref="VulkanWorkgroupSize.GroupCountFor"/> dividing
    /// by one of them. Use <see cref="VulkanWorkgroupSize.From"/> with the emitted module to get
    /// the declared defaults and the constant ids.
    /// </remarks>
    private static List<VulkanStage> Stages(ProgramLayout layout) => layout.EntryPoints
        .Select(e => new VulkanStage(
            e.Name,
            ToStage(e.Stage),
            e.ThreadGroupSize is { } t
                ? new VulkanWorkgroupSize((uint)t.X, (uint)t.Y, (uint)t.Z, t.IsSpecialized)
                : null))
        .ToList();

    /// <summary>
    /// Colour attachments the fragment stage writes, ordered by location.
    /// </summary>
    /// <remarks>
    /// A deferred renderer needs the count to size
    /// <c>VkPipelineColorBlendStateCreateInfo::attachmentCount</c>, and the count has to come from
    /// the shader — a mismatch between it and the render pass is a pipeline creation failure.
    /// Locations are the <c>VARYING_OUTPUT</c> offsets Slang reported, which match the module's
    /// <c>Output</c> location decorations.
    ///
    /// The format is what the shader <em>writes</em>, not what the attachment stores. A shader
    /// returning <c>float4</c> writes fine into an <c>R8G8B8A8_UNORM</c> target; the attachment's
    /// real format is the render pass's business.
    /// </remarks>
    private static List<VulkanFragmentOutput> FragmentOutputs(ProgramLayout layout)
    {
        var fragment = layout.EntryPoints.FirstOrDefault(e => e.Stage == SlangStage.SLANG_STAGE_FRAGMENT);
        if (fragment?.ResultType is not { } result) return [];

        var outputs = new List<VulkanFragmentOutput>();

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

                outputs.Add(new VulkanFragmentOutput(
                    location, ToVertexFormat(f.Type), f.Name, f.Semantic));
            }
        }
        else if (ToVertexFormat(result) != VulkanFormat.Undefined)
        {
            // A bare `float4 fragmentMain() : SV_Target` return: one attachment at location 0.
            outputs.Add(new VulkanFragmentOutput(0, ToVertexFormat(result), fragment.Name, "SV_TARGET"));
        }

        return outputs.OrderBy(o => o.Location).ToList();
    }


    // ---------------------------------------------------------------- descriptor types

    /// <summary>
    /// Slang's binding kind read as a Vulkan descriptor type.
    /// </summary>
    /// <remarks>
    /// The one judgement call is <c>TEXTURE</c>. Slang emits separate images and samplers for
    /// SPIR-V, so a bare texture is a sampled image; <c>COMBINED_TEXTURE_SAMPLER</c> is the case
    /// that maps to a combined image sampler. Mutable variants map to storage rather than sampled.
    /// </remarks>
    public static VulkanDescriptorType ToDescriptorType(SlangBindingType binding)
    {
        var mutable = (binding & SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_FLAG) != 0;
        var basis = binding & SlangBindingType.SLANG_BINDING_TYPE_BASE_MASK;

        return basis switch
        {
            SlangBindingType.SLANG_BINDING_TYPE_SAMPLER => VulkanDescriptorType.Sampler,
            SlangBindingType.SLANG_BINDING_TYPE_TEXTURE =>
                mutable ? VulkanDescriptorType.StorageImage : VulkanDescriptorType.SampledImage,
            SlangBindingType.SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER =>
                VulkanDescriptorType.CombinedImageSampler,
            SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER => VulkanDescriptorType.UniformBuffer,
            SlangBindingType.SLANG_BINDING_TYPE_PARAMETER_BLOCK => VulkanDescriptorType.UniformBuffer,
            SlangBindingType.SLANG_BINDING_TYPE_TYPED_BUFFER =>
                mutable ? VulkanDescriptorType.StorageTexelBuffer : VulkanDescriptorType.UniformTexelBuffer,
            SlangBindingType.SLANG_BINDING_TYPE_RAW_BUFFER => VulkanDescriptorType.StorageBuffer,
            SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET => VulkanDescriptorType.InputAttachment,
            SlangBindingType.SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA => VulkanDescriptorType.InlineUniformBlock,
            SlangBindingType.SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE =>
                VulkanDescriptorType.AccelerationStructureKhr,

            // Only reachable from the reconciliation pass in ReflectionWalker, which fills holes
            // Slang leaves in its own descriptor-set view and types them from the resource shape.
            // A shape it does not recognise arrives here rather than being given a plausible type.
            SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN => throw new NotSupportedException(
                "this shader binds something Slang omitted from its descriptor-set reflection and " +
                "whose resource shape PrismSlang does not recognise, so its descriptor type cannot " +
                "be determined. The binding is real — the module decorates it — so this is a gap in " +
                "PrismSlang rather than a shader error; the parameter name is on the binding. " +
                "See ReflectionWalker.AddUncoveredParameters."),

            _ => throw new NotSupportedException(
                $"Slang binding type {binding} has no Vulkan descriptor type. If this is a real " +
                $"shader construct rather than a Slang internal, PrismSlang.Vulkan needs extending."),
        };
    }

    /// <summary>Slang's stage read as a Vulkan stage flag.</summary>
    public static VulkanShaderStages ToStage(SlangStage stage) => stage switch
    {
        SlangStage.SLANG_STAGE_VERTEX => VulkanShaderStages.Vertex,
        SlangStage.SLANG_STAGE_HULL => VulkanShaderStages.TessellationControl,
        SlangStage.SLANG_STAGE_DOMAIN => VulkanShaderStages.TessellationEvaluation,
        SlangStage.SLANG_STAGE_GEOMETRY => VulkanShaderStages.Geometry,
        SlangStage.SLANG_STAGE_FRAGMENT => VulkanShaderStages.Fragment,
        SlangStage.SLANG_STAGE_COMPUTE => VulkanShaderStages.Compute,
        SlangStage.SLANG_STAGE_RAY_GENERATION => VulkanShaderStages.RaygenKhr,
        SlangStage.SLANG_STAGE_ANY_HIT => VulkanShaderStages.AnyHitKhr,
        SlangStage.SLANG_STAGE_CLOSEST_HIT => VulkanShaderStages.ClosestHitKhr,
        SlangStage.SLANG_STAGE_MISS => VulkanShaderStages.MissKhr,
        SlangStage.SLANG_STAGE_INTERSECTION => VulkanShaderStages.IntersectionKhr,
        SlangStage.SLANG_STAGE_CALLABLE => VulkanShaderStages.CallableKhr,
        SlangStage.SLANG_STAGE_AMPLIFICATION => VulkanShaderStages.TaskExt,
        SlangStage.SLANG_STAGE_MESH => VulkanShaderStages.MeshExt,
        _ => VulkanShaderStages.None,
    };

    /// <summary>
    /// A scalar or vector type read as a vertex attribute format.
    /// </summary>
    /// <remarks>
    /// Covers the 32-bit, 16-bit-float and 64-bit-float families a vertex input can plausibly use.
    /// Packed and normalised formats are not inferable from a Slang declaration — a shader sees
    /// <c>float4</c> whether the buffer holds <c>R8G8B8A8_UNORM</c> or <c>R32G32B32A32_SFLOAT</c>.
    /// That choice is the renderer's, and belongs in the mesh format, not here.
    /// </remarks>
    public static VulkanFormat ToVertexFormat(TypeLayout type)
    {
        var (scalar, count) = type switch
        {
            ScalarLayout s => (s.ScalarType, 1),
            VectorLayout v => (v.ElementType, v.ElementCount),
            _ => (SlangScalarType.SLANG_SCALAR_TYPE_NONE, 0),
        };

        return scalar switch
        {
            SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32 => count switch
            {
                1 => VulkanFormat.R32Sfloat,
                2 => VulkanFormat.R32G32Sfloat,
                3 => VulkanFormat.R32G32B32Sfloat,
                4 => VulkanFormat.R32G32B32A32Sfloat,
                _ => VulkanFormat.Undefined,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_INT32 => count switch
            {
                1 => VulkanFormat.R32Sint,
                2 => VulkanFormat.R32G32Sint,
                3 => VulkanFormat.R32G32B32Sint,
                4 => VulkanFormat.R32G32B32A32Sint,
                _ => VulkanFormat.Undefined,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_UINT32 => count switch
            {
                1 => VulkanFormat.R32Uint,
                2 => VulkanFormat.R32G32Uint,
                3 => VulkanFormat.R32G32B32Uint,
                4 => VulkanFormat.R32G32B32A32Uint,
                _ => VulkanFormat.Undefined,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 => count switch
            {
                1 => VulkanFormat.R16Sfloat,
                2 => VulkanFormat.R16G16Sfloat,
                3 => VulkanFormat.R16G16B16Sfloat,
                4 => VulkanFormat.R16G16B16A16Sfloat,
                _ => VulkanFormat.Undefined,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64 => count switch
            {
                1 => VulkanFormat.R64Sfloat,
                2 => VulkanFormat.R64G64Sfloat,
                3 => VulkanFormat.R64G64B64Sfloat,
                4 => VulkanFormat.R64G64B64A64Sfloat,
                _ => VulkanFormat.Undefined,
            },
            _ => VulkanFormat.Undefined,
        };
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>
    /// Push constant ranges, from explicit <c>[vk::push_constant]</c> blocks and from entry-point
    /// <c>uniform</c> value parameters, which Slang also places in a push-constant buffer.
    /// </summary>
    /// <remarks>
    /// The offset needs care. Slang counts push-constant <em>buffers</em>, so the offset it reports
    /// is a buffer index, not a byte offset — they coincide at 0, which is why a single range has
    /// always looked right. Rather than convert a quantity we cannot convert, a program with more
    /// than one push-constant buffer is refused: Vulkan wants byte offsets into one block, and
    /// silently emitting a buffer index as one produces a range that overlaps or misses.
    /// </remarks>
    private static List<VulkanPushConstantRange> PushConstants(ProgramLayout layout)
    {
        var result = new List<VulkanPushConstantRange>();
        var stages = AllStages(layout);

        foreach (var p in layout.Parameters)
        {
            if (!p.Bindings.Any(b => b.Category
                    == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER))
            {
                continue;
            }

            // The declared size is the element's uniform footprint, not the wrapper's.
            var size = p.Type is ContainerLayout c ? c.ElementType.UniformSize : p.Type.UniformSize;
            result.Add(new VulkanPushConstantRange(0, (uint)size, stages, p.Name));
        }

        // An entry point's `uniform` value parameters become an implicit push-constant buffer.
        // Without this they are in the shader and in no pipeline layout, so vkCmdPushConstants has
        // nowhere to write and the values the shader reads are undefined.
        foreach (var ep in layout.EntryPoints)
        {
            if (ep.UniformSize == 0) continue;
            result.Add(new VulkanPushConstantRange(
                0, (uint)ep.UniformSize, ToStage(ep.Stage), $"{ep.Name}.uniforms"));
        }

        if (result.Count > 1)
        {
            throw new NotSupportedException(
                $"this program has {result.Count} push-constant buffers " +
                $"({string.Join(", ", result.Select(r => r.Name))}). Slang reports their offsets as " +
                $"buffer indices, not byte offsets, so PrismSlang.Vulkan cannot lay them out into " +
                $"the single block Vulkan expects. Merge them into one [vk::push_constant] struct.");
        }

        return result;
    }

    /// <summary>
    /// Vertex attributes of the vertex stage, at the locations the module actually uses.
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
    private static List<VulkanVertexInput> VertexInputs(ProgramLayout layout)
    {
        var vertex = layout.EntryPoints.FirstOrDefault(e => e.Stage == SlangStage.SLANG_STAGE_VERTEX);
        if (vertex is null) return [];

        var result = new List<VulkanVertexInput>();

        foreach (var p in vertex.Parameters)
        {
            if (!Varying(p.Bindings, out var location)) continue;
            Emit(p.Name, p.Type, p.Semantic, location, result);
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
        uint location,
        List<VulkanVertexInput> into)
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
                    Emit(f.Name, f.Type, f.Semantic, location + fieldOffset, into);
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
                        $"{locations} location(s), which does not divide evenly. PrismSlang.Vulkan " +
                        $"cannot say what each location holds; declare the attribute explicitly.");
                }

                var width = (int)(components / locations);
                for (var i = 0u; i < locations; i++)
                {
                    into.Add(new VulkanVertexInput(
                        location + i,
                        VectorFormat(m.ElementType, width, name),
                        $"{name}[{i}]",
                        semantic));
                }
                return;
            }

            case ArrayLayout { ElementCount: { } count } a when count > 0:
            {
                var locations = VaryingLocations(a);
                var stride = locations == 0 ? 1u : locations / (uint)count;
                for (var i = 0u; i < count; i++)
                    Emit($"{name}[{i}]", a.ElementType, semantic, location + (i * stride), into);
                return;
            }

            default:
            {
                var format = ToVertexFormat(type);
                if (format != VulkanFormat.Undefined)
                {
                    into.Add(new VulkanVertexInput(location, format, name, semantic));
                    return;
                }

                // Silently dropping an attribute is how the matrix bug hid. If the type occupies
                // locations, refusing is the only honest answer.
                if (VaryingLocations(type) > 0)
                {
                    throw new NotSupportedException(
                        $"vertex input '{name}' is {type.Kind}" +
                        $"{(type.Name is null ? "" : $" ('{type.Name}')")}, which has no Vulkan " +
                        $"attribute format, but it occupies {VaryingLocations(type)} location(s). " +
                        $"PrismSlang.Vulkan will not guess one.");
                }

                return;
            }
        }
    }

    private static VulkanFormat VectorFormat(SlangScalarType scalar, int count, string name)
    {
        var format = ToVertexFormat(count == 1
            ? new ScalarLayout { Kind = LayoutKind.Scalar, ScalarType = scalar }
            : new VectorLayout { Kind = LayoutKind.Vector, ElementType = scalar, ElementCount = count });

        return format != VulkanFormat.Undefined
            ? format
            : throw new NotSupportedException(
                $"vertex input '{name}' needs a {count}-component {scalar} format, which has no " +
                $"Vulkan equivalent here.");
    }

    /// <summary>
    /// Which stages touch each space.
    /// </summary>
    /// <remarks>
    /// Slang's global-scope reflection does not attribute a parameter to the entry points that use
    /// it, so per-binding stage flags cannot be derived exactly from the snapshot alone. Every
    /// stage in the program is reported, which is conservative: it over-permits visibility rather
    /// than omitting a stage that actually reads the binding, and an omission would be a runtime
    /// validation error. A renderer that wants tighter flags can narrow them per pipeline.
    /// </remarks>
    private static Dictionary<uint, VulkanShaderStages> StagesPerSpace(ProgramLayout layout)
    {
        var all = AllStages(layout);
        return layout.DescriptorSets.ToDictionary(s => s.SpaceOffset, _ => all);
    }

    private static VulkanShaderStages AllStages(ProgramLayout layout)
    {
        var stages = VulkanShaderStages.None;
        foreach (var e in layout.EntryPoints) stages |= ToStage(e.Stage);
        return stages;
    }

    /// <summary>
    /// Names for the bindings of one space, so a descriptor can be reported as "albedo" rather
    /// than "set 0 binding 1".
    /// </summary>
    /// <remarks>
    /// Only parameters that genuinely occupy a descriptor slot are considered, and asking the
    /// question that way round is load-bearing. Slang reports specialization constants and
    /// <c>[vk::push_constant]</c> blocks in the same top-level list, with an index and a space that
    /// look exactly like a descriptor's: <c>[vk::constant_id(0)]</c> and a push-constant block both
    /// claim (space 0, index 0), the same coordinates as the descriptor at set 0 binding 0, and
    /// both sort ahead of it — so the first-writer-wins <c>TryAdd</c> below labelled real
    /// descriptors with their names. That was two separate bugs, found one at a time while
    /// excluding kinds as they turned up. A whitelist cannot grow a third.
    /// </remarks>
    private static Dictionary<uint, string> NamesForSpace(ProgramLayout layout, uint space)
    {
        var names = new Dictionary<uint, string>();
        foreach (var p in layout.Parameters)
        {
            if (!p.OccupiesDescriptorSlot) continue;
            if (p.DescriptorSet != space) continue;
            var b = p.PrimaryBinding;
            if (b is not null) names.TryAdd(b.Value.Index, p.Name);
        }

        // Entry-point `uniform` parameters are descriptors too, numbered from the entry point's
        // own base rather than absolutely. See ReflectionWalker.CollectEntryPointSets.
        foreach (var ep in layout.EntryPoints)
        {
            var baseIndex = ep.BaseOffset(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT);
            foreach (var p in ep.Parameters)
            {
                if (!p.OccupiesDescriptorSlot) continue;
                var b = p.PrimaryBinding;
                if (b is not null && b.Value.Space == space) names.TryAdd(baseIndex + b.Value.Index, p.Name);
            }
        }

        return names;
    }
}
