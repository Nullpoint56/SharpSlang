// The NRI-shaped result of projecting a reflection snapshot.
//
// The centrepiece is a PrismNRI NriPipelineLayoutDesc the host can hand straight to
// NriDevice.CreatePipelineLayout. Around it are the things the desc cannot carry and a renderer
// needs anyway: the name each descriptor range came from, which set index and range index to
// pass to NriDescriptorPool.AllocateDescriptorSet and NriDescriptorSet.Update for it, the vertex
// attributes and colour outputs for a graphics pipeline, and the stages for picking shaders.
//
// Named Projected* rather than Nri*, on purpose. PrismNRI already has an NriDescriptorSet (the
// allocated object) and an NriDescriptorSetDesc (the desc), and a consumer with both namespaces
// open should never have to disambiguate the description of a set from the set.

using PrismNRI;
using PrismNRI.Native;
using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.NRI;

/// <summary>What the host decides before a layout can be projected onto NRI.</summary>
public sealed record NriProjectionOptions
{
    /// <summary>
    /// The descriptor count given to a runtime-sized array — <c>Texture2D t[];</c>, i.e. bindless.
    /// </summary>
    /// <remarks>
    /// Required when the shader declares one; <see cref="NriProjection.ToNri"/> refuses the
    /// program otherwise. The shader has said nothing about how many descriptors there are, Slang
    /// reports <c>0xFFFFFFFF</c>, and NRI's <c>DescriptorRangeDesc.descriptorNum</c> has no spelling
    /// for "unbounded": whatever number goes there is the size of the binding the device creates.
    /// The number is the host's, bounded by the device's descriptor limits, and a default here
    /// would be a guess that reaches a driver.
    /// </remarks>
    public uint? UnboundedArrayCap { get; init; }

    /// <summary>
    /// The per-register-class binding shifts the NRI device was created with
    /// (<c>NriDeviceOptions.VkBindingOffsets</c>). Must be all zero.
    /// </summary>
    /// <remarks>
    /// NRI's Vulkan backend adds these to every range's <c>baseRegisterIndex</c> when it builds
    /// the <c>VkDescriptorSetLayout</c>, so the SPIR-V has to have been compiled with the same
    /// shifts. Slang's SPIR-V is compiled with none: the <c>binding</c> decoration is the index
    /// reflection reports. A device created with non-zero offsets would therefore bind every
    /// descriptor at a shifted index the module never reads from, and the projection refuses that
    /// combination at projection time rather than letting the mismatch surface as a wrong texture
    /// at draw time. The field exists so the host states the device's value and the projection can
    /// check it, not so it can be honoured.
    /// </remarks>
    public VKBindingOffsets VkBindingOffsets { get; init; }

    /// <summary>
    /// Constant-buffer parameters to bind as root constants rather than as descriptors, by shader
    /// parameter name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The D3D12 half of push constants. On a SPIR-V target Slang honours
    /// <c>[vk::push_constant]</c> and reports the block as a push-constant buffer, which the
    /// projection turns into root constants on its own. On a DXIL target Slang drops the attribute
    /// and lays the block out as an ordinary constant buffer at a <c>b</c> register, and reflection
    /// keeps no trace of the attribute on either target (measured on 2026.21: the block arrives
    /// with no attributes at all). So on D3D12 the intent has to come from the host, and this is
    /// where it says it.
    /// </para>
    /// <para>
    /// A named parameter must be a constant buffer that occupies a descriptor slot, or a block
    /// Slang already placed in push-constant storage, which makes the name a no-op — so one host
    /// list serves both targets. A name the program does not declare, or that names anything else,
    /// is refused. Each named block becomes a <c>RootConstantDesc</c> at its own register index,
    /// its range leaves the descriptor set, and <c>RootRegisterSpace</c> becomes its register
    /// space; the named blocks must share one space, because NRI has one root space.
    /// </para>
    /// <para>
    /// NRI's D3D12 backend binds root constants at <c>b&lt;registerIndex&gt;</c> of the root space,
    /// which is the register the DXIL reads the block from, so the shader needs no change. Its
    /// Vulkan backend ignores the register and builds a push-constant range, which is what a
    /// SPIR-V-compiled block of the same name is anyway.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> RootConstantParameters { get; init; } = [];
}

/// <summary>One descriptor range of a set, with everything the desc cannot say about it.</summary>
/// <param name="RangeIndex">
/// Its index within the set's <c>Ranges</c>: the <c>rangeIndex</c> for
/// <c>NriDescriptorSet.Update</c> and <c>UpdateRange</c>.
/// </param>
/// <param name="Desc">
/// The range as NRI wants it. <c>baseRegisterIndex</c> is the shader's binding index;
/// <c>descriptorNum</c> is the array size, 1 for a scalar binding and the host's cap for an
/// unbounded one; <c>flags</c> carries <c>ARRAY</c> for any array and <c>PARTIALLY_BOUND</c> for an
/// unbounded one.
/// </param>
/// <param name="Name">The shader parameter that produced it, for diagnostics and material binding.</param>
/// <param name="IsUnbounded">
/// Whether the shader declared a runtime-sized array, so <c>Desc.descriptorNum</c> is
/// <see cref="NriProjectionOptions.UnboundedArrayCap"/> rather than a count the shader stated.
/// </param>
/// <param name="InputAttachmentIndex">
/// For an input attachment, which attachment of the pass the shader reads. Null for every other
/// descriptor type.
/// </param>
/// <param name="ImageFormat">
/// The format a storage texture was declared with, via <c>[format("rgba8")]</c>;
/// <c>SLANG_IMAGE_FORMAT_unknown</c> when none was declared, which on Vulkan makes the shader
/// depend on the device's read/write-without-format features.
/// </param>
/// <param name="CounterRangeIndex">
/// For an <c>AppendStructuredBuffer</c> or <c>ConsumeStructuredBuffer</c>, the <see cref="RangeIndex"/>
/// in this same set of the storage buffer holding its count — a second descriptor the renderer
/// must create and write. Null for everything else.
/// </param>
/// <remarks>
/// <para>
/// <c>ARRAY</c> is set whenever the shader declared an array, and the flag is load-bearing. NRI's
/// Vulkan backend reads a range without it as <c>descriptorNum</c> <em>consecutive bindings</em>
/// of one descriptor each, starting at <c>baseRegisterIndex</c> — the D3D register-range reading —
/// where Slang's SPIR-V declares one binding whose descriptor count is the array length. With
/// the flag, NRI builds one binding of <c>descriptorNum</c> descriptors, which is what the module
/// has.
/// </para>
/// <para>
/// An unbounded range gets <c>PARTIALLY_BOUND</c> and not <c>VARIABLE_SIZED_ARRAY</c>, because the
/// latter is only legal on the highest-numbered binding of a set
/// (<c>VUID-VkDescriptorSetLayoutBindingFlagsCreateInfo-pBindingFlags-03004</c>) and Slang does
/// not arrange for the unbounded array to be last: in the corpus's <c>descriptor-arrays</c> shader
/// the unbounded texture array is binding 1 and a sampler is binding 2. A host that knows its
/// unbounded range is last may add the flag with <c>with</c> and allocate with a
/// <c>variableDescriptorNum</c>; partially bound with a fixed cap is legal anywhere.
/// </para>
/// </remarks>
public sealed record ProjectedDescriptorRange(
    uint RangeIndex,
    DescriptorRangeDesc Desc,
    string? Name = null,
    bool IsUnbounded = false,
    uint? InputAttachmentIndex = null,
    SlangImageFormat ImageFormat = SlangImageFormat.SLANG_IMAGE_FORMAT_unknown,
    uint? CounterRangeIndex = null);

/// <summary>A descriptor set: one entry of <c>NriPipelineLayoutDesc.DescriptorSets</c>.</summary>
/// <param name="SetIndex">
/// Its position in <c>NriPipelineLayoutDesc.DescriptorSets</c>: the <c>setIndex</c> for
/// <c>NriDescriptorPool.AllocateDescriptorSet</c> and <c>NriCommandBuffer.SetDescriptorSet</c>.
/// </param>
/// <param name="RegisterSpace">
/// The shader's <c>set = N</c> index, which is what <c>NriDescriptorSetDesc.RegisterSpace</c>
/// carries. NRI's Vulkan backend places this set's <c>VkDescriptorSetLayout</c> at index
/// <c>RegisterSpace</c> of the pipeline layout and fills any gap with an empty layout, so a shader
/// using sets 0 and 2 binds exactly where its module says.
/// </param>
/// <param name="Ranges">The ranges, ordered by binding index.</param>
/// <remarks>
/// <see cref="SetIndex"/> and <see cref="RegisterSpace"/> differ as soon as the shader skips a
/// space: <c>explicit-spaces</c> in the corpus declares sets 0 and 2, which become descriptor
/// sets 0 and 1 in the desc. NRI's descriptor API is indexed by the first, the SPIR-V by the
/// second, and conflating them binds the wrong set without any validation layer noticing.
/// </remarks>
public sealed record ProjectedDescriptorSet(
    uint SetIndex,
    uint RegisterSpace,
    IReadOnlyList<ProjectedDescriptorRange> Ranges)
{
    /// <summary>Finds a range by the shader parameter name that produced it, or null.</summary>
    /// <remarks>
    /// A loop, not LINQ: this is the one kind of call an engine might make after load, and the
    /// LINQ form allocated a closure and an enumerator per call — 80 bytes, measured. Zero now,
    /// and AllocationTests keeps it there. Cache the result anyway; a lookup by name is still a
    /// string comparison per range.
    /// </remarks>
    public ProjectedDescriptorRange? Range(string name)
    {
        for (var i = 0; i < Ranges.Count; i++)
        {
            var r = Ranges[i];
            if (string.Equals(r.Name, name, StringComparison.Ordinal)) return r;
        }

        return null;
    }
}

/// <summary>A root-constant block: one entry of <c>NriPipelineLayoutDesc.RootConstants</c>.</summary>
/// <param name="Index">
/// Its position in <c>NriPipelineLayoutDesc.RootConstants</c>: the <c>rootConstantIndex</c> for
/// <c>NriCommandBuffer.SetRootConstants</c>.
/// </param>
/// <param name="Desc">
/// The block as NRI wants it. <c>size</c> is the block's uniform footprint as Slang laid it out,
/// which is what the SPIR-V reads; <c>registerIndex</c> is 0, which the Vulkan backend ignores
/// for push constants and which a D3D12 backend reads as <c>b0</c> in the root register space.
/// </param>
/// <param name="Name">
/// The parameter name of an explicit <c>[vk::push_constant]</c> block, or
/// <c>&lt;entry point&gt;.uniforms</c> for an entry point's <c>uniform</c> value parameters.
/// </param>
public sealed record ProjectedRootConstants(uint Index, RootConstantDesc Desc, string Name);

/// <summary>One vertex attribute, for <c>NriVertexInputDesc.Attributes</c>.</summary>
/// <param name="Name">Parameter name as declared, with <c>[i]</c> appended for a matrix or array element.</param>
/// <param name="Desc">
/// The attribute with its location, format and D3D semantic filled in, and <c>Offset</c> 0 and
/// <c>StreamIndex</c> 0 left for the renderer. Which stream an attribute is read from and how
/// attributes interleave are renderer decisions the shader does not express; fill them with
/// <c>with</c>.
/// </param>
public sealed record ProjectedVertexAttribute(string Name, NriVertexAttributeDesc Desc);

/// <summary>One colour attachment the fragment stage writes, for <c>NriOutputMergerDesc.Colors</c>.</summary>
/// <param name="Location">The <c>location = N</c> index, i.e. the attachment index.</param>
/// <param name="Format">
/// The format the shader <em>writes</em>, as a vertex-attribute-style <c>Format</c>. Not the
/// attachment's storage format, which the pass decides — a shader returning <c>float4</c> writes
/// correctly into an <c>RGBA8_UNORM</c> target. <c>UNKNOWN</c> for a type NRI has no format for.
/// </param>
/// <param name="Name">Field name as declared, or the entry point's name for a bare return.</param>
/// <param name="Semantic">The <c>SV_Target</c><i>N</i> semantic, where one was declared.</param>
public sealed record ProjectedFragmentOutput(uint Location, Format Format, string Name, string? Semantic = null);

/// <summary>One entry point of the program, read as a pipeline stage.</summary>
/// <param name="Name">The Slang function name — how to find this stage in a baked package.</param>
/// <param name="Stage">The one <c>StageBits</c> bit for <c>NriShaderDesc.Stage</c>.</param>
/// <param name="ThreadGroupSize">
/// Threads per group, for a compute-like stage; null for the raster and ray-tracing stages. A
/// dimension decided by a specialization constant arrives as 0 — see
/// <see cref="Reflection.ThreadGroupSize.IsSpecialized"/> — and NRI has no specialization
/// constants to resolve it with; specialize such a shader at link time instead.
/// </param>
/// <remarks>
/// Deliberately not carrying the module entry-point name: that is a property of the emitted
/// bytes, not of the layout, and <see cref="NriProjection.ToNriShaderDesc"/> reads it from the
/// <c>BakedEntryPoint</c> beside them.
/// </remarks>
public sealed record ProjectedStage(string Name, StageBits Stage, ThreadGroupSize? ThreadGroupSize = null);

/// <summary>
/// A reflection snapshot read as NRI pipeline requirements. Immutable, so it can be computed once
/// per shader and shared.
/// </summary>
public sealed record NriPipelineProjection
{
    /// <summary>
    /// The pipeline layout desc, ready for <c>NriDevice.CreatePipelineLayout</c>. Its
    /// <c>DescriptorSets</c> and <c>RootConstants</c> are the same arrays
    /// <see cref="DescriptorSets"/> and <see cref="RootConstants"/> describe, in the same order.
    /// </summary>
    public required NriPipelineLayoutDesc Layout { get; init; }

    /// <summary>Descriptor sets, in the order they appear in <see cref="Layout"/>; by register space.</summary>
    public required IReadOnlyList<ProjectedDescriptorSet> DescriptorSets { get; init; }

    /// <summary>Root-constant blocks, in the order they appear in <see cref="Layout"/>. Empty when the shader declares none.</summary>
    public required IReadOnlyList<ProjectedRootConstants> RootConstants { get; init; }

    /// <summary>
    /// Vertex attributes of the vertex stage, ordered by location. Empty for a program with no
    /// vertex entry point.
    /// </summary>
    public required IReadOnlyList<ProjectedVertexAttribute> VertexAttributes { get; init; }

    /// <summary>
    /// Colour attachments the fragment stage writes, ordered by location. Empty for a program with
    /// no fragment entry point.
    /// </summary>
    public IReadOnlyList<ProjectedFragmentOutput> FragmentOutputs { get; init; } = [];

    /// <summary>The program's entry points as pipeline stages, in link order.</summary>
    public IReadOnlyList<ProjectedStage> Stages { get; init; } = [];

    // The three lookups below are loops rather than LINQ for the reason ProjectedDescriptorSet
    // .Range records: they are what an engine calls after load, and a closure plus an enumerator
    // per call is an allocation on a path that otherwise has none. Measured at 80 to 120 bytes
    // per call before; zero after, pinned by AllocationTests.

    /// <summary>Finds a descriptor set by the shader's <c>set = N</c> index, or null.</summary>
    public ProjectedDescriptorSet? Set(uint registerSpace)
    {
        for (var i = 0; i < DescriptorSets.Count; i++)
        {
            var s = DescriptorSets[i];
            if (s.RegisterSpace == registerSpace) return s;
        }

        return null;
    }

    /// <summary>Finds a descriptor range by the shader parameter name that produced it, in any set, or null.</summary>
    public (ProjectedDescriptorSet Set, ProjectedDescriptorRange Range)? Range(string name)
    {
        for (var i = 0; i < DescriptorSets.Count; i++)
        {
            var set = DescriptorSets[i];
            if (set.Range(name) is { } range) return (set, range);
        }

        return null;
    }

    /// <summary>Finds a stage by its Slang entry-point name, or null.</summary>
    public ProjectedStage? Stage(string name)
    {
        for (var i = 0; i < Stages.Count; i++)
        {
            var s = Stages[i];
            if (string.Equals(s.Name, name, StringComparison.Ordinal)) return s;
        }

        return null;
    }
}
