// The Vulkan-shaped result of projecting a reflection snapshot.
//
// These types stop exactly where the Vulkan spec stops and renderer policy begins. They describe
// what the shader requires; they say nothing about how to satisfy it.

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
/// <remarks>
/// When <paramref name="IsUnbounded"/> the shader has said nothing about how many descriptors
/// there are, and <paramref name="DescriptorCount"/> is 0 rather than a count. The renderer picks
/// the cap and declares it with <c>VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT</c>, bounded
/// by the device's <c>maxPerStageDescriptor*</c> limits; only the last binding in a set may be
/// unbounded. Slang's own answer for this case is <c>0xFFFFFFFF</c>, which is not a count and is
/// normalised away here so it cannot reach a driver.
/// </remarks>
public sealed record VulkanBinding(
    uint Binding,
    VulkanDescriptorType DescriptorType,
    uint DescriptorCount,
    VulkanShaderStages StageFlags,
    string? Name = null,
    bool IsUnbounded = false);

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
}
