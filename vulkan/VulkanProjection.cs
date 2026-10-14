// The projection itself: a neutral ProgramLayout read as Vulkan pipeline requirements.
//
// Everything here is a mechanical consequence of the Vulkan specification plus what Slang reported.
// Where a mapping is genuinely ambiguous the code says so rather than guessing quietly.
//
// The API-neutral half — which leaf attributes the vertex stage reads and where, which outputs
// the fragment stage writes, which blocks are push constants, what a descriptor slot is called,
// and the invariant that every parameter was placed somewhere — lives in
// PrismSlang.Reflection.PipelineInterface, in the Runtime assembly, because PrismSlang.NRI needs
// exactly the same answers. This file gives each of those answers its Vulkan spelling and adds the
// rules that are Vulkan's alone: the descriptor type per binding kind, the stage bits, the
// VkFormat per leaf type, and the two-locations-for-a-wide-64-bit-vector rule the module itself
// gets wrong.

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
            var names = new NameLookup(layout, set.SpaceOffset);

            var bindings = set.Ranges
                .Where(r => r.BindingType != SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT)
                .Select(r => new VulkanBinding(
                    r.IndexOffset,
                    ToDescriptorType(r.BindingType),
                    r.DescriptorCount,
                    VisibleStages(ToDescriptorType(r.BindingType), StagesFor(r, stages)),
                    // The range's own name where the walk could attribute one — that is the only
                    // source that reaches inside a ParameterBlock. DescriptorNames is the fallback
                    // for ranges Slang's binding-range view does not cover.
                    names.NameOf(r),
                    r.IsUnbounded,
                    r.InputAttachmentIndex,
                    r.ImageFormat,
                    r.CounterBinding))
                .OrderBy(b => b.Binding)
                .ToList();

            if (bindings.Count > 0) sets.Add(new VulkanDescriptorSetLayout(set.SpaceOffset, bindings));
        }

        layout.RequireEveryParameterIsPlaced();

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
    /// Which outputs exist, and at which locations, is decided once in
    /// <see cref="PipelineInterface.FragmentOutputs"/>; this only gives each one a format. A
    /// deferred renderer needs the count to size
    /// <c>VkPipelineColorBlendStateCreateInfo::attachmentCount</c>, and the count has to come from
    /// the shader — a mismatch between it and the render pass is a pipeline creation failure.
    ///
    /// The format is what the shader <em>writes</em>, not what the attachment stores. A shader
    /// returning <c>float4</c> writes fine into an <c>R8G8B8A8_UNORM</c> target; the attachment's
    /// real format is the render pass's business.
    /// </remarks>
    private static List<VulkanFragmentOutput> FragmentOutputs(ProgramLayout layout) =>
        layout.FragmentOutputs()
            .Select(o => new VulkanFragmentOutput(o.Location, ToVertexFormat(o.Type), o.Name, o.Semantic))
            .ToList();

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
    /// The blocks themselves come from <see cref="PipelineInterface.PushConstantBlocks"/>. The
    /// offset needs care. Slang counts push-constant <em>buffers</em>, so the offset it reports is
    /// a buffer index, not a byte offset — they coincide at 0, which is why a single range has
    /// always looked right. Rather than convert a quantity we cannot convert, a program with more
    /// than one push-constant buffer is refused: Vulkan wants byte offsets into one block, and
    /// silently emitting a buffer index as one produces a range that overlaps or misses.
    /// </remarks>
    private static List<VulkanPushConstantRange> PushConstants(ProgramLayout layout)
    {
        var stages = AllStages(layout);
        var result = layout.PushConstantBlocks()
            .Select(b => new VulkanPushConstantRange(
                0, (uint)b.Size, b.Stage is { } stage ? ToStage(stage) : stages, b.Name))
            .ToList();

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
    /// Which leaf attributes exist and where — a struct flattened, a matrix split into one
    /// attribute per location, a builtin with no location left out — is decided once in
    /// <see cref="PipelineInterface.VertexInputs"/>, which also records the two location-counting
    /// bugs that used to live here. This gives each leaf a <c>VkFormat</c>, and refuses one it has
    /// no format for rather than dropping it: silently dropping an attribute is how the matrix bug
    /// hid, and if the type occupies locations, refusing is the only honest answer.
    /// </remarks>
    private static List<VulkanVertexInput> VertexInputs(ProgramLayout layout)
    {
        var result = new List<VulkanVertexInput>();

        foreach (var input in layout.VertexInputs())
        {
            var format = ToVertexFormat(input.Type);
            if (format == VulkanFormat.Undefined)
            {
                if (input.LocationsConsumed == 0) continue;

                throw new NotSupportedException(
                    $"vertex input '{input.Name}' is {input.Type.Kind}" +
                    $"{(input.Type.Name is null ? "" : $" ('{input.Type.Name}')")}, which has no Vulkan " +
                    $"attribute format, but it occupies {input.LocationsConsumed} location(s). " +
                    $"PrismSlang.Vulkan will not guess one.");
            }

            result.Add(new VulkanVertexInput(input.Location, format, input.Name, input.Semantic));
        }

        RequireDistinctLocations(result);
        return result;
    }

    /// <summary>
    /// Refuses a vertex layout where two attributes claim the same location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An invariant checked against the Vulkan specification rather than against the module,
    /// because on this one the module is not an oracle: Slang and the emitted SPIR-V agree with
    /// each other and both disagree with the rule.
    /// </para>
    /// <para>
    /// Vulkan's location assignment gives a three- or four-component 64-bit vector <em>two</em>
    /// consecutive locations, where every 32-bit type and a scalar or two-component 64-bit vector
    /// takes one. Measured against slang-compiler 2026.14.1 with
    /// <c>float3, double, double2, double4, float2</c>: Slang assigns 0, 1, 2, 3, 4 and decorates
    /// the module the same way, so the <c>double4</c> at 3 and the <c>float2</c> at 4 overlap. A
    /// pipeline built from that is rejected by the validation layers, and the diagnostic points at
    /// the pipeline rather than at the shader.
    /// </para>
    /// <para>
    /// This started as the one claim in this file taken from the specification rather than from a
    /// measurement, which made it the one that might have been refusing a legal shader. It is not:
    /// built on a real device with <c>VK_LAYER_KHRONOS_validation</c> loaded, the overlapping module
    /// is rejected by <c>vkCreateShaderModule</c> with
    /// <c>VUID-StandaloneSpirv-OpEntryPoint-08721</c>, "conflicting input location assignment at
    /// location 2" — the same location named below. The module itself is invalid SPIR-V, before any
    /// pipeline is built around it, so refusing at cook time is the earliest anything could.
    /// <c>VulkanValidationTests</c> holds that, and docs .
    /// </para>
    /// </remarks>
    private static void RequireDistinctLocations(List<VulkanVertexInput> inputs)
    {
        var claimed = new Dictionary<uint, string>();

        foreach (var input in inputs)
        {
            for (var i = 0u; i < LocationsConsumed(input.Format); i++)
            {
                if (claimed.TryGetValue(input.Location + i, out var other))
                {
                    throw new NotSupportedException(
                        $"vertex attributes '{other}' and '{input.Name}' both occupy location "
                        + $"{input.Location + i}. A three- or four-component 64-bit attribute takes "
                        + $"two consecutive locations, and Slang assigned these as if it took one. "
                        + $"Give the attribute after the 64-bit one an explicit location, or narrow "
                        + $"it to at most two components.");
                }

                claimed[input.Location + i] = input.Name;
            }
        }
    }

    /// <summary>Locations one attribute of this format occupies. Two only for a wide 64-bit vector.</summary>
    private static uint LocationsConsumed(VulkanFormat format) => format switch
    {
        VulkanFormat.R64G64B64Sfloat or VulkanFormat.R64G64B64A64Sfloat => 2,
        _ => 1,
    };

    /// <summary>
    /// The fallback name for a range the walk could not name, looked up within the range's own
    /// register class.
    /// </summary>
    /// <remarks>
    /// By class and not by index alone, because a layout compiled for D3D numbers each register
    /// class from 0 and an index-only lookup lends a constant buffer the name of the texture at
    /// the same number — see <see cref="PipelineInterface.DescriptorNames(ProgramLayout, uint, SlangParameterCategory?)"/>.
    /// A SPIR-V layout has one class per set, so this is the same lookup it always was there.
    /// </remarks>
    private sealed class NameLookup(ProgramLayout layout, uint space)
    {
        private readonly Dictionary<SlangParameterCategory, IReadOnlyDictionary<uint, string>> _byClass = [];

        public string? NameOf(DescriptorRange range)
        {
            if (range.Name is not null) return range.Name;

            if (!_byClass.TryGetValue(range.Category, out var names))
                _byClass[range.Category] = names = layout.DescriptorNames(space, range.Category);

            return names.GetValueOrDefault(range.IndexOffset);
        }
    }

    /// <summary>
    /// Which stages touch each space, as the fallback when a binding does not say.
    /// </summary>
    /// <remarks>
    /// This used to be the whole answer, and the remark here used to explain why: Slang's
    /// global-scope reflection does not attribute a parameter to the entry points that use it, so
    /// every stage in the program was reported on every binding. Conservative and correct, and a
    /// renderer wanting tight <c>stageFlags</c> had to narrow them itself.
    ///
    /// <c>DescriptorRange.Stages</c> now carries the attribution, from the per-entry-point metadata
    /// the reflection walk collects while the program is still alive. This remains the
    /// answer for a range that claims no stage — see <see cref="StagesFor"/>.
    /// </remarks>
    private static Dictionary<uint, VulkanShaderStages> StagesPerSpace(ProgramLayout layout)
    {
        var all = AllStages(layout);
        return layout.DescriptorSets.ToDictionary(s => s.SpaceOffset, _ => all);
    }

    /// <summary>
    /// The stages a binding is visible to: the ones that read it, or all of them if none is known.
    /// </summary>
    /// <remarks>
    /// The empty case is not "no stage". Vulkan rejects a <c>VkDescriptorSetLayoutBinding</c> with
    /// a non-zero descriptor count and no stage flags, and a binding nothing is known to read still
    /// has to be declared somewhere. Widening back is also the only safe direction: an omitted
    /// stage that really reads the binding is a driver rejection, where a surplus one costs
    /// nothing.
    /// </remarks>
    private static VulkanShaderStages StagesFor(DescriptorRange range, VulkanShaderStages fallback)
    {
        if (range.Stages is not { Count: > 0 } stages) return fallback;

        var narrowed = VulkanShaderStages.None;
        foreach (var stage in stages) narrowed |= ToStage(stage);
        return narrowed == VulkanShaderStages.None ? fallback : narrowed;
    }

    private static VulkanShaderStages AllStages(ProgramLayout layout)
    {
        var stages = VulkanShaderStages.None;
        foreach (var e in layout.EntryPoints) stages |= ToStage(e.Stage);
        return stages;
    }

    /// <summary>
    /// The stages a binding of this type may be declared visible to.
    /// </summary>
    /// <remarks>
    /// The conservative "every stage in the program" above is over-permission, which is normally
    /// harmless. For an input attachment it is not: Vulkan requires
    /// <c>VK_DESCRIPTOR_TYPE_INPUT_ATTACHMENT</c> to be fragment-only, and
    /// <c>VUID-VkDescriptorSetLayoutBinding-descriptorType-01510</c> rejects the descriptor set
    /// layout outright — not the pipeline, the set layout, so a host following the projection
    /// literally cannot create the object at all.
    ///
    /// Found by putting the corpus in front of the validation layers: the subpass-inputs
    /// shader projected <c>VERTEX | FRAGMENT</c> onto both of its input attachments, and every
    /// other test in this suite was content, because the only authority that knows this rule is
    /// Vulkan and until then nothing here was asking Vulkan.
    /// </remarks>
    private static VulkanShaderStages VisibleStages(
        VulkanDescriptorType type, VulkanShaderStages stages) =>
        type == VulkanDescriptorType.InputAttachment
            ? stages & VulkanShaderStages.Fragment
            : stages;
}
