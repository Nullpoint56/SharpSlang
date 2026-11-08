// Which SPIR-V a Vulkan device will accept.
//
// This is the "target environment" that DXC spelled -fspv-target-env=vulkan1.3. Slang has no such
// flag; it pins the SPIR-V version through the target profile and says nothing about Vulkan. So the
// Vulkan half of the mapping lives here, where it is a fact about the specification rather than a
// guess about the compiler.

using SharpSlang.Spirv;

namespace SharpSlang.Vulkan;

/// <summary>A Vulkan API version, as <c>VkApplicationInfo::apiVersion</c> names it.</summary>
public enum VulkanApiVersion
{
    V1_0,
    V1_1,
    V1_2,
    V1_3,
    V1_4,
}

/// <summary>Maps a Vulkan version to the SPIR-V it accepts.</summary>
public static class VulkanEnvironment
{
    /// <summary>
    /// The highest SPIR-V version a device of this Vulkan version is required to accept.
    /// </summary>
    /// <remarks>
    /// Straight from the specification's version-correspondence table. Vulkan 1.4 raises no ceiling
    /// over 1.3 — both cap at SPIR-V 1.6, which is why "the device is 1.4" does not on its own say
    /// which SPIR-V to emit.
    ///
    /// This is a ceiling, not a target. Emitting <em>below</em> it is always accepted; emitting
    /// above it is rejected by <c>vkCreateShaderModule</c>. Pick deliberately and pin it in
    /// <c>TargetOptions.SpirvVersion</c>.
    /// </remarks>
    public static SpirvVersion MaxSpirvVersion(VulkanApiVersion vulkan) => vulkan switch
    {
        VulkanApiVersion.V1_0 => SpirvVersion.V1_0,
        VulkanApiVersion.V1_1 => SpirvVersion.V1_3,
        VulkanApiVersion.V1_2 => SpirvVersion.V1_5,
        VulkanApiVersion.V1_3 => SpirvVersion.V1_6,
        VulkanApiVersion.V1_4 => SpirvVersion.V1_6,
        _ => throw new ArgumentOutOfRangeException(nameof(vulkan), vulkan, "unknown Vulkan version"),
    };

    /// <summary>Whether a module of this SPIR-V version can be given to a device of this Vulkan version.</summary>
    public static bool Accepts(VulkanApiVersion vulkan, SpirvVersion spirv) =>
        spirv != SpirvVersion.Unspecified && spirv <= MaxSpirvVersion(vulkan);

    /// <summary>
    /// Throws unless the module's SPIR-V version is within what the device accepts.
    /// </summary>
    /// <remarks>
    /// For a loader to call on a baked package before <c>vkCreateShaderModule</c>, which reports
    /// the same condition as an opaque failure.
    /// </remarks>
    /// <exception cref="ArgumentException">The module is too new for the device.</exception>
    public static void Require(VulkanApiVersion vulkan, SpirvVersion spirv, string what)
    {
        if (Accepts(vulkan, spirv)) return;

        throw new ArgumentException(
            $"{what} is SPIR-V {spirv.ToDisplayString()}, but Vulkan {vulkan.ToString()[1..].Replace('_', '.')} " +
            $"accepts at most {MaxSpirvVersion(vulkan).ToDisplayString()}. " +
            $"Set TargetOptions.SpirvVersion no higher than that and rebake.",
            nameof(spirv));
    }
}
