// Vulkan enum values, mirrored rather than referenced.
//
// Taking a dependency on Vortice.Vulkan (or Silk.NET, or any other binding) would couple this
// package to one consumer's choice of Vulkan binding, and drag a large dependency behind a small
// mapping. The numeric values below are fixed by the Vulkan specification and will not change, so
// mirroring them cannot drift.
//
// It can still be wrong, and four of these were. "Cannot change" is an argument about drift and
// says nothing about transcription, and a mistyped VkFormat is silent — it is structurally valid,
// so validation passes and the driver simply reinterprets the bytes. The values are now checked
// member by member against Vortice.Vulkan, which is generated from the Khronos vk.xml registry, by
// VulkanEnumTests. That dependency is on the test project alone; this package still has none, which
// was the point of mirroring in the first place.
//
// the renderer casts directly:
//     var type = (VkDescriptorType)binding.DescriptorType;

namespace SharpSlang.Vulkan;

/// <summary>Mirrors <c>VkDescriptorType</c>.</summary>
public enum VulkanDescriptorType
{
    Sampler = 0,
    CombinedImageSampler = 1,
    SampledImage = 2,
    StorageImage = 3,
    UniformTexelBuffer = 4,
    StorageTexelBuffer = 5,
    UniformBuffer = 6,
    StorageBuffer = 7,
    UniformBufferDynamic = 8,
    StorageBufferDynamic = 9,
    InputAttachment = 10,

    /// <summary><c>VK_DESCRIPTOR_TYPE_INLINE_UNIFORM_BLOCK</c>.</summary>
    InlineUniformBlock = 1000138000,

    /// <summary><c>VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR</c>.</summary>
    AccelerationStructureKhr = 1000150000,
}

/// <summary>Mirrors <c>VkShaderStageFlagBits</c>.</summary>
[Flags]
public enum VulkanShaderStages
{
    None = 0,
    Vertex = 0x00000001,
    TessellationControl = 0x00000002,
    TessellationEvaluation = 0x00000004,
    Geometry = 0x00000008,
    Fragment = 0x00000010,
    Compute = 0x00000020,
    AllGraphics = 0x0000001F,
    RaygenKhr = 0x00000100,
    AnyHitKhr = 0x00000200,
    ClosestHitKhr = 0x00000400,
    MissKhr = 0x00000800,
    IntersectionKhr = 0x00001000,
    CallableKhr = 0x00002000,
    TaskExt = 0x00000040,
    MeshExt = 0x00000080,
}

/// <summary>
/// Mirrors the subset of <c>VkFormat</c> reachable from a shader's vertex input declarations.
/// </summary>
public enum VulkanFormat
{
    Undefined = 0,

    R32Uint = 98,
    R32Sint = 99,
    R32Sfloat = 100,
    R32G32Uint = 101,
    R32G32Sint = 102,
    R32G32Sfloat = 103,
    R32G32B32Uint = 104,
    R32G32B32Sint = 105,
    R32G32B32Sfloat = 106,
    R32G32B32A32Uint = 107,
    R32G32B32A32Sint = 108,
    R32G32B32A32Sfloat = 109,

    // Each width in this part of VkFormat is a UINT/SINT/SFLOAT triple, so the float is the *third*
    // of its group and not the first. These four were transcribed as 110/113/116/119 — the group
    // starts, which are R64_UINT, R64G64_UINT and so on. A `double` vertex attribute was therefore
    // declared to the driver as an unsigned integer of the same width: eight bytes read and
    // reinterpreted, passing validation, producing geometry that is wrong without anything saying
    // so. Caught by VulkanEnumTests on the first run, which is the entire argument for having it.
    R64Sfloat = 112,
    R64G64Sfloat = 115,
    R64G64B64Sfloat = 118,
    R64G64B64A64Sfloat = 121,

    R16Sfloat = 76,
    R16G16Sfloat = 83,
    R16G16B16Sfloat = 90,
    R16G16B16A16Sfloat = 97,
}
