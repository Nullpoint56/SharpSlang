// The Vulkan-shaped result of projecting a reflection snapshot.
//
// These types stop exactly where the Vulkan spec stops and renderer policy begins. They describe
// what the shader requires; they say nothing about how to satisfy it.

using PrismSlang.Native;
using PrismSlang.Spirv;

namespace PrismSlang.Vulkan;

/// <summary>One binding within a descriptor set.</summary>
/// <param name="Binding">The <c>binding = N</c> index.</param>
/// <param name="DescriptorType">Cast directly to <c>VkDescriptorType</c>.</param>
/// <param name="DescriptorCount">
/// Array size; 1 for a scalar binding, and 0 when <paramref name="IsUnbounded"/> — see there
/// before copying this into <c>VkDescriptorSetLayoutBinding::descriptorCount</c>.
/// </param>
/// <param name="StageFlags">Stages that reference this binding. Cast to <c>VkShaderStageFlags</c>.</param>
/// <param name="Name">The shader parameter that produced it, for diagnostics and material binding.</param>
/// <param name="IsUnbounded">
/// Whether the shader declared a runtime-sized array — <c>Texture2D t[];</c>, i.e. bindless.
/// </param>
/// <param name="InputAttachmentIndex">
/// For an input attachment, which entry of <c>VkSubpassDescription::pInputAttachments</c> the
/// shader reads. Null for every other descriptor type.
/// </param>
/// <param name="ImageFormat">
/// The format a storage image was declared with, via <c>[format("rgba8")]</c>.
/// <c>SLANG_IMAGE_FORMAT_unknown</c> when none was declared — see the remarks, because that case
/// carries a device-feature requirement.
/// </param>
/// <remarks>
/// <para>
/// When <paramref name="IsUnbounded"/> the shader has said nothing about how many descriptors
/// there are, and <paramref name="DescriptorCount"/> is 0 rather than a count. The renderer picks
/// the cap, bounded by the device's <c>maxPerStageDescriptor*</c> limits. Slang's own answer for
/// this case is <c>0xFFFFFFFF</c>, which is not a count and is normalised away here so it cannot
/// reach a driver.
/// </para>
/// <para>
/// <strong>Check the binding index before reaching for
/// <c>VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT</c>.</strong> Vulkan allows that flag
/// only on the highest-numbered binding in a set
/// (<c>VUID-VkDescriptorSetLayoutBindingFlagsCreateInfo-pBindingFlags-03004</c>), and Slang does
/// not arrange for the unbounded array to be last: in the corpus's <c>descriptor-arrays</c> shader
/// the unbounded texture array is binding 1 and an ordinary sampler is binding 2. Nothing here is
/// wrong about the shader — the constraint is Vulkan's and the choice is the host's — but a host
/// that maps <c>IsUnbounded</c> straight onto variable count is refused by
/// <c>vkCreateDescriptorSetLayout</c> rather than by a driver at draw time. When the binding is not
/// last, <c>VK_DESCRIPTOR_BINDING_PARTIALLY_BOUND_BIT</c> with a fixed cap is legal anywhere and is
/// what the validation harness does (§9.18).
/// </para>
/// <para>
/// A <see cref="VulkanDescriptorType.StorageImage"/> whose <paramref name="ImageFormat"/> is
/// unknown compiles to a SPIR-V image type with format <c>Unknown</c>, which the shader may only
/// read or write if the device has <c>shaderStorageImageReadWithoutFormat</c> /
/// <c>shaderStorageImageWriteWithoutFormat</c> enabled. Declaring the format in the shader removes
/// that requirement, and lets the renderer check the image view it is about to bind agrees.
/// </para>
/// </remarks>
public sealed record VulkanBinding(
    uint Binding,
    VulkanDescriptorType DescriptorType,
    uint DescriptorCount,
    VulkanShaderStages StageFlags,
    string? Name = null,
    bool IsUnbounded = false,
    uint? InputAttachmentIndex = null,
    SlangImageFormat ImageFormat = SlangImageFormat.SLANG_IMAGE_FORMAT_unknown);

/// <summary>
/// One colour attachment the fragment stage writes, for
/// <c>VkPipelineColorBlendStateCreateInfo</c>.
/// </summary>
/// <param name="Location">The <c>location = N</c> index, i.e. the attachment index.</param>
/// <param name="Format">
/// The format the shader <em>writes</em>. Not the attachment's storage format, which the render
/// pass decides — a shader returning <c>float4</c> writes correctly into an
/// <c>R8G8B8A8_UNORM</c> target.
/// </param>
/// <param name="Name">Field name as declared.</param>
/// <param name="Semantic">The <c>SV_Target</c><i>N</i> semantic, where one was declared.</param>
public sealed record VulkanFragmentOutput(
    uint Location,
    VulkanFormat Format,
    string Name,
    string? Semantic = null);

/// <summary>
/// One entry point of the program, read as a pipeline stage.
/// </summary>
/// <param name="Name">The Slang function name — how to find this stage in a baked package.</param>
/// <param name="Stage">Cast to <c>VkShaderStageFlagBits</c> for <c>VkPipelineShaderStageCreateInfo</c>.</param>
/// <param name="WorkgroupSize">
/// Threads per group, for a compute-like stage; null for the raster and ray-tracing stages.
/// </param>
/// <remarks>
/// Deliberately not carrying the module entry-point name for <c>pName</c>: that is a property of
/// the emitted bytes, not of the layout, and it lives on <c>BakedEntryPoint</c> beside them.
/// </remarks>
public sealed record VulkanStage(
    string Name,
    VulkanShaderStages Stage,
    VulkanWorkgroupSize? WorkgroupSize = null);

/// <summary>
/// A compute entry point's workgroup dimensions, and the arithmetic for dispatching them.
/// </summary>
/// <param name="X">Threads per group in X.</param>
/// <param name="Y">Threads per group in Y.</param>
/// <param name="Z">Threads per group in Z.</param>
/// <param name="IsSpecialized">
/// Whether a dimension is decided by a specialization constant at pipeline creation.
/// </param>
/// <remarks>
/// When <paramref name="IsSpecialized"/>, how much of the size is filled in depends on where this
/// came from. Projected from a <c>ProgramLayout</c>, a specialized dimension is 0 — Slang reports
/// no value for it, and inventing one would be worse than saying nothing. Built with
/// <see cref="From"/> from a module that was actually emitted, it is the default the shader
/// declared, along with the constant id that overrides it. Either way
/// <see cref="GroupCountFor"/> refuses to divide.
/// </remarks>
public sealed record VulkanWorkgroupSize(uint X, uint Y, uint Z, bool IsSpecialized = false)
{
    /// <summary>
    /// The workgroup size an emitted module declares, resolved against the specialization values
    /// the host is about to supply.
    /// </summary>
    /// <param name="size">From <c>SpirvModule.Read(code).EntryPoints[i].WorkgroupSize</c>.</param>
    /// <param name="specializationValues">
    /// The <c>VkSpecializationInfo</c> this pipeline will be created with, as constant id to value.
    /// A constant that is absent keeps the shader's declared default, because that is what Vulkan
    /// does with one the host does not supply — so an empty dictionary is a meaningful answer
    /// ("I supply nothing") and produces a size that can be dispatched.
    /// </param>
    /// <remarks>
    /// Passing null is different from passing an empty dictionary, and the difference is the point.
    /// Null says nothing has been decided, so the result stays specialized and
    /// <see cref="GroupCountFor"/> keeps refusing. Any dictionary, empty or not, says these are the
    /// values, which determines every dimension.
    /// </remarks>
    public static VulkanWorkgroupSize From(
        SpirvWorkgroupSize size,
        IReadOnlyDictionary<uint, uint>? specializationValues = null)
    {
        ArgumentNullException.ThrowIfNull(size);

        var (x, y, z) = size.Resolve(specializationValues);
        return new VulkanWorkgroupSize(x, y, z, specializationValues is null && size.IsSpecialized);
    }

    /// <summary>
    /// Groups to dispatch to cover a domain of <paramref name="width"/> x <paramref name="height"/>
    /// x <paramref name="depth"/> threads — the arguments for <c>vkCmdDispatch</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A dimension is 0, or is decided by a specialization constant this layout cannot see. Both
    /// mean the group count is not knowable from the layout alone, and rounding up against a
    /// guessed divisor dispatches the wrong number of groups — a shader that quietly processes part
    /// of its domain, which reads as a content bug rather than a dispatch bug.
    /// </exception>
    public (uint X, uint Y, uint Z) GroupCountFor(uint width, uint height = 1, uint depth = 1)
    {
        if (IsSpecialized)
            throw new InvalidOperationException(
                "this workgroup size is decided by a specialization constant, so the group count " +
                "depends on the values you are about to supply. Resolve it against them first: " +
                "SpirvModule.Read(code).EntryPoints[i].WorkgroupSize.Resolve(values).");

        if (X == 0 || Y == 0 || Z == 0)
            throw new InvalidOperationException(
                $"workgroup size [{X}, {Y}, {Z}] has a zero dimension, so there is no group count.");

        return (Ceil(width, X), Ceil(height, Y), Ceil(depth, Z));

        static uint Ceil(uint total, uint per) => (total + per - 1) / per;
    }

    public override string ToString() =>
        IsSpecialized ? $"[{X}, {Y}, {Z}] (specialized)" : $"[{X}, {Y}, {Z}]";
}

/// <summary>A descriptor set: everything needed for one <c>VkDescriptorSetLayoutCreateInfo</c>.</summary>
/// <param name="Set">The <c>set = N</c> index.</param>
/// <param name="Bindings">Bindings, ordered by index.</param>
public sealed record VulkanDescriptorSetLayout(uint Set, IReadOnlyList<VulkanBinding> Bindings);

/// <summary>A push constant range, for <c>VkPushConstantRange</c>.</summary>
public sealed record VulkanPushConstantRange(uint Offset, uint Size, VulkanShaderStages StageFlags, string? Name = null);

/// <summary>
/// One vertex input attribute, for <c>VkVertexInputAttributeDescription</c>.
/// </summary>
/// <remarks>
/// There is deliberately no buffer binding or stride here. Which buffer an attribute is read from,
/// and how attributes are interleaved, is a renderer decision — the shader does not express it and
/// this package will not invent it. Renderer Core supplies <c>binding</c> and <c>offset</c> when it
/// builds the <c>VkVertexInputAttributeDescription</c>.
/// </remarks>
/// <param name="Location">The <c>location = N</c> index.</param>
/// <param name="Format">Cast directly to <c>VkFormat</c>.</param>
/// <param name="Name">Parameter name as declared.</param>
/// <param name="Semantic">HLSL-style semantic, if the shader declared one.</param>
public sealed record VulkanVertexInput(uint Location, VulkanFormat Format, string Name, string? Semantic = null);

/// <summary>
/// A reflection snapshot read as Vulkan pipeline requirements. Immutable and serializable, so it
/// can be baked offline alongside the SPIR-V.
/// </summary>
public sealed record VulkanPipelineLayout
{
    /// <summary>Descriptor sets, ordered by set index.</summary>
    public required IReadOnlyList<VulkanDescriptorSetLayout> DescriptorSets { get; init; }

    /// <summary>Push constant ranges. Empty when the shader declares none.</summary>
    public required IReadOnlyList<VulkanPushConstantRange> PushConstants { get; init; }

    /// <summary>
    /// Vertex attributes of the vertex stage, ordered by location. Empty for a program with no
    /// vertex entry point.
    /// </summary>
    public required IReadOnlyList<VulkanVertexInput> VertexInputs { get; init; }

    /// <summary>
    /// Colour attachments the fragment stage writes, ordered by location. Empty for a program with
    /// no fragment entry point.
    /// </summary>
    /// <remarks>
    /// The count sizes <c>VkPipelineColorBlendStateCreateInfo::attachmentCount</c>, which must
    /// agree with the render pass or pipeline creation fails.
    /// </remarks>
    public IReadOnlyList<VulkanFragmentOutput> FragmentOutputs { get; init; } = [];

    /// <summary>
    /// The program's entry points as pipeline stages, in link order.
    /// </summary>
    /// <remarks>
    /// Present so a compute pipeline can be built from the layout alone. Without it the descriptor
    /// sets and push constants were here but the workgroup size was not, so dispatching meant going
    /// back to the neutral <c>ProgramLayout</c> and doing the ceil-divide by hand — see
    /// <see cref="VulkanWorkgroupSize.GroupCountFor"/>.
    /// </remarks>
    public IReadOnlyList<VulkanStage> Stages { get; init; } = [];

    /// <summary>
    /// Specialization constants the shader declares, at their natural offsets, ordered by
    /// constant id. Empty when it declares none.
    /// </summary>
    /// <remarks>
    /// The map only. To also pack the values, call
    /// <c>ProgramLayout.Pack(values)</c> — offsets there are computed the same way, so a host can
    /// use either and get the same layout.
    /// </remarks>
    public IReadOnlyList<VulkanSpecializationMapEntry> SpecializationConstants { get; init; } = [];

    /// <summary>Finds a descriptor set by index, or null.</summary>
    public VulkanDescriptorSetLayout? Set(uint index) =>
        DescriptorSets.FirstOrDefault(s => s.Set == index);

    /// <summary>Finds a stage by its Slang entry-point name, or null.</summary>
    public VulkanStage? Stage(string name) =>
        Stages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
}
