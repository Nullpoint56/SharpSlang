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
                    names.GetValueOrDefault(r.IndexOffset)))
                .OrderBy(b => b.Binding)
                .ToList();

            if (bindings.Count > 0) sets.Add(new VulkanDescriptorSetLayout(set.SpaceOffset, bindings));
        }

        return new VulkanPipelineLayout
        {
            DescriptorSets = sets,
            PushConstants = PushConstants(layout),
            VertexInputs = VertexInputs(layout),
            SpecializationConstants = layout.SpecializationMap(),
        };
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

    private static List<VulkanPushConstantRange> PushConstants(ProgramLayout layout)
    {
        var result = new List<VulkanPushConstantRange>();
        var stages = AllStages(layout);

        foreach (var p in layout.Parameters)
        {
            var push = p.Bindings.FirstOrDefault(
                b => b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER);
            if (push == default && !p.Bindings.Any(
                    b => b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER))
            {
                continue;
            }

            // The declared size is the element's uniform footprint, not the wrapper's.
            var size = p.Type is ContainerLayout c ? c.ElementType.UniformSize : p.Type.UniformSize;
            result.Add(new VulkanPushConstantRange((uint)push.Index, (uint)size, stages, p.Name));
        }

        return result;
    }

    private static List<VulkanVertexInput> VertexInputs(ProgramLayout layout)
    {
        var vertex = layout.EntryPoints.FirstOrDefault(e => e.Stage == SlangStage.SLANG_STAGE_VERTEX);
        if (vertex is null) return [];

        var result = new List<VulkanVertexInput>();
        Flatten(vertex.Parameters, result);
        return result.OrderBy(v => v.Location).ToList();

        static void Flatten(IEnumerable<ShaderParameter> parameters, List<VulkanVertexInput> into)
        {
            foreach (var p in parameters)
            {
                var varying = p.Bindings.FirstOrDefault(
                    b => b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_INPUT);
                if (varying == default) continue;

                // A struct parameter contributes one attribute per leaf field, each at its own
                // location, rather than a single attribute for the struct.
                if (p.Type is StructLayout s)
                {
                    var index = varying.Index;
                    foreach (var f in s.Fields)
                    {
                        var format = ToVertexFormat(f.Type);
                        if (format != VulkanFormat.Undefined)
                            into.Add(new VulkanVertexInput(index, format, f.Name, f.Semantic));
                        index++;
                    }
                    continue;
                }

                var scalarFormat = ToVertexFormat(p.Type);
                if (scalarFormat != VulkanFormat.Undefined)
                    into.Add(new VulkanVertexInput(varying.Index, scalarFormat, p.Name, p.Semantic));
            }
        }
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
    /// Specialization constants are skipped, and skipping them is load-bearing rather than tidy.
    /// Slang reports them as top-level parameters whose binding index is really the
    /// <c>constant_id</c> and whose space is always 0, so <c>[vk::constant_id(0)] const int
    /// kLightCount</c> claims exactly the same (space 0, index 0) coordinates as the descriptor at
    /// set 0 binding 0. They also come first in the parameter list, so without this filter the
    /// first-writer-wins <c>TryAdd</c> below labelled real descriptors with constant names.
    /// </remarks>
    private static Dictionary<uint, string> NamesForSpace(ProgramLayout layout, uint space)
    {
        var names = new Dictionary<uint, string>();
        foreach (var p in layout.Parameters)
        {
            if (p.IsSpecializationConstant) continue;
            if (p.DescriptorSet != space) continue;
            var b = p.PrimaryBinding;
            if (b is not null) names.TryAdd(b.Value.Index, p.Name);
        }
        return names;
    }
}
