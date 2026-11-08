// The projection itself: a neutral ProgramLayout read as NRI pipeline requirements.
//
// Everything here is a mechanical consequence of NRI's desc vocabulary plus what Slang reported,
// with one authority behind it: NRI's own Vulkan backend (Source/VK/PipelineLayoutVK.hpp at NRI's
// v191 tag, the revision SharpNRI.Descs is generated from), which is what turns the desc into the
// VkPipelineLayout the SPIR-V is bound against. Each rule below names the line of that backend it
// answers to. Where a mapping is genuinely ambiguous the code says so rather than guessing quietly.

using SharpNRI;
using SharpNRI.Native;
using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Pipeline.NRI;

/// <summary>Reads a <see cref="ProgramLayout"/> as NRI pipeline requirements.</summary>
public static class NriProjection
{
    /// <summary>
    /// Projects a reflection snapshot into a SharpNRI pipeline layout desc, root constants,
    /// vertex attributes and fragment outputs.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The shader uses something NRI's descs cannot express — a combined texture sampler, an
    /// inline uniform block, a 64-bit or three-component half vertex attribute, more than one
    /// push-constant block — or declares an unbounded array without
    /// <see cref="NriProjectionOptions.UnboundedArrayCap"/>, or the options name non-zero Vulkan
    /// binding offsets. Each message names the construct.
    /// </exception>
    public static NriPipelineProjection ToNri(this ProgramLayout layout, NriProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        options ??= new NriProjectionOptions();

        // Refused before anything is projected, because nothing projected would be right. The
        // backend adds the offsets to every baseRegisterIndex (PipelineLayoutVK.hpp line 118 and
        // 351) on the assumption the shader compiler was told the same shifts; Slang was not.
        if (options.VkBindingOffsets is not { sRegister: 0, tRegister: 0, bRegister: 0, uRegister: 0 })
        {
            var o = options.VkBindingOffsets;
            throw new NotSupportedException(
                $"NriProjectionOptions.VkBindingOffsets is (s {o.sRegister}, t {o.tRegister}, " +
                $"b {o.bRegister}, u {o.uRegister}), but Slang compiles SPIR-V with no register " +
                $"shifts: the binding decoration is the index reflection reports. NRI's Vulkan " +
                $"backend would add these to every binding, so the pipeline layout and the module " +
                $"would disagree about where each descriptor lives. Create the NRI device with " +
                $"zero VkBindingOffsets for shaders compiled by Slang.");
        }

        layout.RequireEveryParameterIsPlaced();

        var allStages = AllStages(layout);
        var promoted = PromotedConstantBuffers(layout, options);
        var sets = DescriptorSets(layout, options, allStages, promoted);
        var rootConstants = RootConstants(layout, allStages, promoted);

        return new NriPipelineProjection
        {
            Layout = new NriPipelineLayoutDesc
            {
                // The space of the constant buffers the host promoted to root constants, which
                // is where NRI's D3D12 backend binds them (b<registerIndex> of this space), and 0
                // when there are none: NRI's own default for a layout with no root descriptors or
                // samplers, and this projection emits neither — Slang has no construct that maps
                // to a root descriptor, and a sampler in Slang is a descriptor like any other. The
                // Vulkan backend reserves a set index for the root space only when something is
                // in it (PipelineLayoutVK.hpp line 145), so an unused 0 costs nothing.
                RootRegisterSpace = promoted.Count > 0 ? promoted[0].Space : 0,
                RootConstants = rootConstants.Select(r => r.Desc).ToArray(),
                DescriptorSets = sets
                    .Select(s => new NriDescriptorSetDesc(s.RegisterSpace, s.Ranges.Select(r => r.Desc).ToArray()))
                    .ToArray(),
                // Every stage the program runs as, never left at ALL. NRI v191's header says that
                // for descriptor and root visibility "ALL" and "ALL_SHADERS" resolve to this field
                // (NRIDescs.h, StageBits), and its validation refuses a range whose stages are not
                // within it and a pipeline shader whose stage it does not enable
                // (DeviceVal.hpp lines 568 and 774); on D3D12 it also decides which stages the
                // root signature denies root access (PipelineLayoutD3D12.hpp lines 25 to 59). The
                // ranges and root constants below carry explicit stages, so nothing resolves
                // through it, but the program's union is the one mask that satisfies all three
                // and denies D3D12 nothing the program reads.
                ShaderStages = allStages,
            },
            DescriptorSets = sets,
            RootConstants = rootConstants,
            VertexAttributes = VertexAttributes(layout),
            FragmentOutputs = FragmentOutputs(layout),
            Stages = Stages(layout),
        };
    }

    /// <summary>
    /// A baked entry point as the shader desc a SharpNRI pipeline wants: the stage, the bytes, and
    /// the entry-point name the module actually declares.
    /// </summary>
    /// <remarks>
    /// The name is <c>BakedEntryPoint.ModuleEntryPointName</c>, never <c>Name</c>. Slang calls
    /// every per-entry-point module <c>main</c> unless <c>TargetOptions.UseEntryPointNameInSpirv</c>
    /// was set, and NRI hands the name to <c>VkPipelineShaderStageCreateInfo::pName</c>, which
    /// must match the bytes. The thread-group dimensions are left at zero: they are Metal-only in
    /// NRI, and the SPIR-V carries its own.
    /// </remarks>
    public static NriShaderDesc ToNriShaderDesc(this BakedEntryPoint entryPoint)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        return new NriShaderDesc(ToStage(entryPoint.Stage), entryPoint.Code, entryPoint.ModuleEntryPointName);
    }

    // ---------------------------------------------------------------- descriptor sets

    private static List<ProjectedDescriptorSet> DescriptorSets(
        ProgramLayout layout, NriProjectionOptions options, StageBits allStages,
        IReadOnlyList<PromotedBlock> promoted)
    {
        var sets = new List<ProjectedDescriptorSet>();

        foreach (var set in layout.DescriptorSets.OrderBy(s => s.SpaceOffset))
        {
            var names = new NameLookup(layout, set.SpaceOffset);

            // Ordered by binding before indices are assigned, so RangeIndex is the position in
            // the array NRI receives — which is the only thing NriDescriptorSet.Update keys on.
            // A block the host promoted to root constants leaves the set here: on D3D12 the root
            // signature binds it at its register directly, and a descriptor range at the same
            // register would be a second binding of the same b register.
            var ranges = set.Ranges
                .Where(r => r.BindingType != SlangBindingType.SLANG_BINDING_TYPE_PUSH_CONSTANT)
                .Where(r => !promoted.Any(p => ReferenceEquals(p.Range, r)))
                .OrderBy(r => r.IndexOffset)
                .ToList();

            if (ranges.Count == 0) continue;

            var projected = new List<ProjectedDescriptorRange>(ranges.Count);
            for (var i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                var type = ToDescriptorType(r.BindingType);

                // The counter of an append/consume buffer is its own range in the same set, which
                // Slang identifies by binding index. The host updates it by range index, so the
                // index is resolved here, where both numberings are in hand.
                uint? counter = null;
                if (r.CounterBinding is { } binding)
                {
                    var found = ranges.FindIndex(x => x.IndexOffset == binding);
                    if (found >= 0) counter = (uint)found;
                }

                projected.Add(new ProjectedDescriptorRange(
                    (uint)i,
                    new DescriptorRangeDesc
                    {
                        baseRegisterIndex = r.IndexOffset,
                        descriptorNum = DescriptorCount(r, options, names.NameOf(r)),
                        descriptorType = type,
                        shaderStages = VisibleStages(type, StagesFor(r, allStages)),
                        flags = RangeFlags(r),
                    },
                    // The range's own name where the walk could attribute one — that is the only
                    // source that reaches inside a ParameterBlock. DescriptorNames is the fallback
                    // for ranges Slang's binding-range view does not cover, looked up within the
                    // range's register class: a DXIL layout numbers b, t, s and u from 0 each.
                    names.NameOf(r),
                    r.IsUnbounded,
                    r.InputAttachmentIndex,
                    r.ImageFormat,
                    counter));
            }

            sets.Add(new ProjectedDescriptorSet((uint)sets.Count, set.SpaceOffset, projected));
        }

        return sets;
    }

    /// <summary>
    /// The count NRI builds the binding with: the shader's, or the host's cap for an unbounded one.
    /// </summary>
    private static uint DescriptorCount(DescriptorRange range, NriProjectionOptions options, string? resolvedName)
    {
        if (!range.IsUnbounded) return range.DescriptorCount;

        if (options.UnboundedArrayCap is { } cap && cap > 0) return cap;

        var name = resolvedName ?? $"binding {range.IndexOffset}";
        throw new NotSupportedException(
            $"descriptor '{name}' is a runtime-sized array and NRI needs a count to build the " +
            $"binding with: DescriptorRangeDesc.descriptorNum has no spelling for \"unbounded\". " +
            $"The shader does not say how many, so the number is yours — set " +
            $"NriProjectionOptions.UnboundedArrayCap within the device's descriptor limits.");
    }

    /// <summary>
    /// <c>ARRAY</c> for any array, plus <c>PARTIALLY_BOUND</c> for an unbounded one.
    /// </summary>
    /// <remarks>
    /// The flag is not decoration. NRI's Vulkan backend reads a range without <c>ARRAY</c> as
    /// <c>descriptorNum</c> consecutive bindings of one descriptor each, from
    /// <c>baseRegisterIndex</c> upward (PipelineLayoutVK.hpp lines 359 to 367: <c>descriptorNum
    /// = range.descriptorNum</c> only when <c>isArray</c> is false, and a binding per
    /// <c>j</c>) — the D3D register-range reading. Slang's SPIR-V declares <c>Texture2D t[4]</c>
    /// as one binding with a descriptor count of 4, and only with the flag does NRI build that.
    /// <c>VARIABLE_SIZED_ARRAY</c> is left to the host; see <see cref="ProjectedDescriptorRange"/>.
    /// </remarks>
    private static DescriptorRangeBits RangeFlags(DescriptorRange range)
    {
        if (range.IsUnbounded) return DescriptorRangeBits.ARRAY | DescriptorRangeBits.PARTIALLY_BOUND;
        return range.DescriptorCount > 1 ? DescriptorRangeBits.ARRAY : DescriptorRangeBits.NONE;
    }

    /// <summary>
    /// Slang's binding kind read as an NRI descriptor type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one judgement call is <c>TEXTURE</c>. Slang emits separate images
    /// and samplers for SPIR-V, so a bare texture is <c>TEXTURE</c> (a sampled image in NRI's
    /// Vulkan backend), a mutable one <c>STORAGE_TEXTURE</c>. NRI has no combined image sampler
    /// at all, so <c>COMBINED_TEXTURE_SAMPLER</c> — Slang's <c>Sampler2D</c> — is refused rather
    /// than split into a texture and a sampler the module does not declare.
    /// </para>
    /// <para>
    /// <c>RAW_BUFFER</c> is <c>STRUCTURED_BUFFER</c> when read-only and
    /// <c>STORAGE_STRUCTURED_BUFFER</c> when writable. On Vulkan both are
    /// <c>VK_DESCRIPTOR_TYPE_STORAGE_BUFFER</c>, so the distinction costs nothing there and is
    /// what a D3D12 backend needs to tell an SRV from a UAV. <c>TYPED_BUFFER</c> splits the same
    /// way into <c>BUFFER</c> and <c>STORAGE_BUFFER</c>, NRI's names for the texel buffers.
    /// </para>
    /// </remarks>
    public static DescriptorType ToDescriptorType(SlangBindingType binding)
    {
        var mutable = (binding & SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_FLAG) != 0;
        var basis = binding & SlangBindingType.SLANG_BINDING_TYPE_BASE_MASK;

        return basis switch
        {
            SlangBindingType.SLANG_BINDING_TYPE_SAMPLER => DescriptorType.SAMPLER,
            SlangBindingType.SLANG_BINDING_TYPE_TEXTURE =>
                mutable ? DescriptorType.STORAGE_TEXTURE : DescriptorType.TEXTURE,
            SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER => DescriptorType.CONSTANT_BUFFER,
            SlangBindingType.SLANG_BINDING_TYPE_PARAMETER_BLOCK => DescriptorType.CONSTANT_BUFFER,
            SlangBindingType.SLANG_BINDING_TYPE_TYPED_BUFFER =>
                mutable ? DescriptorType.STORAGE_BUFFER : DescriptorType.BUFFER,
            SlangBindingType.SLANG_BINDING_TYPE_RAW_BUFFER =>
                mutable ? DescriptorType.STORAGE_STRUCTURED_BUFFER : DescriptorType.STRUCTURED_BUFFER,
            SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET => DescriptorType.INPUT_ATTACHMENT,
            SlangBindingType.SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE =>
                DescriptorType.ACCELERATION_STRUCTURE,

            SlangBindingType.SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER => throw new NotSupportedException(
                "this shader declares a combined texture sampler (Sampler2D and friends), and NRI " +
                "has no descriptor type for one: its DescriptorType names a TEXTURE and a SAMPLER " +
                "separately. Declare a Texture2D and a SamplerState instead, which is what Slang " +
                "emits for SPIR-V anyway."),

            SlangBindingType.SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA => throw new NotSupportedException(
                "this shader uses an inline uniform block, which NRI's DescriptorType cannot " +
                "express. Use a ConstantBuffer or a [vk::push_constant] block."),

            // Only reachable from the reconciliation pass in ReflectionWalker, which fills holes
            // Slang leaves in its own descriptor-set view and types them from the resource shape.
            // A shape it does not recognise arrives here rather than being given a plausible type.
            SlangBindingType.SLANG_BINDING_TYPE_UNKNOWN => throw new NotSupportedException(
                "this shader binds something Slang omitted from its descriptor-set reflection and " +
                "whose resource shape SharpSlang does not recognise, so its descriptor type cannot " +
                "be determined. The binding is real — the module decorates it — so this is a gap in " +
                "SharpSlang rather than a shader error; the parameter name is on the binding. " +
                "See ReflectionWalker.AddUncoveredParameters."),

            _ => throw new NotSupportedException(
                $"Slang binding type {binding} has no NRI descriptor type. If this is a real " +
                $"shader construct rather than a Slang internal, SharpSlang.Pipeline.NRI needs extending."),
        };
    }

    /// <summary>Slang's stage read as the one NRI stage bit.</summary>
    /// <exception cref="NotSupportedException">The stage has no NRI bit — a work-graph stage.</exception>
    /// <remarks>
    /// Refuses rather than returning a default, because NRI's <c>StageBits</c> has no harmless
    /// default: <c>ALL</c> is 0 and <c>NONE</c> is every bit, and a shader desc carrying either
    /// is not a shader of any stage.
    /// </remarks>
    public static StageBits ToStage(SlangStage stage) => stage switch
    {
        SlangStage.SLANG_STAGE_VERTEX => StageBits.VERTEX_SHADER,
        SlangStage.SLANG_STAGE_HULL => StageBits.TESS_CONTROL_SHADER,
        SlangStage.SLANG_STAGE_DOMAIN => StageBits.TESS_EVALUATION_SHADER,
        SlangStage.SLANG_STAGE_GEOMETRY => StageBits.GEOMETRY_SHADER,
        SlangStage.SLANG_STAGE_FRAGMENT => StageBits.FRAGMENT_SHADER,
        SlangStage.SLANG_STAGE_COMPUTE => StageBits.COMPUTE_SHADER,
        SlangStage.SLANG_STAGE_RAY_GENERATION => StageBits.RAYGEN_SHADER,
        SlangStage.SLANG_STAGE_ANY_HIT => StageBits.ANY_HIT_SHADER,
        SlangStage.SLANG_STAGE_CLOSEST_HIT => StageBits.CLOSEST_HIT_SHADER,
        SlangStage.SLANG_STAGE_MISS => StageBits.MISS_SHADER,
        SlangStage.SLANG_STAGE_INTERSECTION => StageBits.INTERSECTION_SHADER,
        SlangStage.SLANG_STAGE_CALLABLE => StageBits.CALLABLE_SHADER,
        SlangStage.SLANG_STAGE_AMPLIFICATION => StageBits.TASK_SHADER,
        SlangStage.SLANG_STAGE_MESH => StageBits.MESH_SHADER,
        _ => throw new NotSupportedException(
            $"Slang stage {stage} has no NRI stage bit. NRI has no pipeline that runs it."),
    };

    /// <summary>
    /// A scalar or vector type read as a vertex attribute format, or <c>UNKNOWN</c> if NRI has none.
    /// </summary>
    /// <remarks>
    /// The 32-bit and 16-bit-float families, minus what NRI's
    /// <c>Format</c> lacks: there is no 64-bit format at all and no three-component 16-bit one, so
    /// a <c>double</c> or <c>half3</c> attribute comes back <c>UNKNOWN</c> and
    /// <see cref="ToNri"/> refuses it. Packed and normalised formats are not inferable from a
    /// Slang declaration — a shader sees <c>float4</c> whether the buffer holds
    /// <c>RGBA8_UNORM</c> or <c>RGBA32_SFLOAT</c>. That choice is the renderer's, and belongs in
    /// the mesh format, not here.
    /// </remarks>
    public static Format ToVertexFormat(TypeLayout type)
    {
        ArgumentNullException.ThrowIfNull(type);

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
                1 => Format.R32_SFLOAT,
                2 => Format.RG32_SFLOAT,
                3 => Format.RGB32_SFLOAT,
                4 => Format.RGBA32_SFLOAT,
                _ => Format.UNKNOWN,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_INT32 => count switch
            {
                1 => Format.R32_SINT,
                2 => Format.RG32_SINT,
                3 => Format.RGB32_SINT,
                4 => Format.RGBA32_SINT,
                _ => Format.UNKNOWN,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_UINT32 => count switch
            {
                1 => Format.R32_UINT,
                2 => Format.RG32_UINT,
                3 => Format.RGB32_UINT,
                4 => Format.RGBA32_UINT,
                _ => Format.UNKNOWN,
            },
            SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 => count switch
            {
                1 => Format.R16_SFLOAT,
                2 => Format.RG16_SFLOAT,
                4 => Format.RGBA16_SFLOAT,
                _ => Format.UNKNOWN,
            },
            _ => Format.UNKNOWN,
        };
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>
    /// Root constants, from explicit <c>[vk::push_constant]</c> blocks and from entry-point
    /// <c>uniform</c> value parameters, which Slang also places in a push-constant buffer.
    /// </summary>
    /// <remarks>
    /// NRI's Vulkan backend lays root constants out as one push-constant range each, at byte
    /// offsets that accumulate in array order (PipelineLayoutVK.hpp lines 124 to 134). That is a
    /// real layout, and it is still not one this projection can use for a second block: Slang
    /// reports a push-constant block's offset as a buffer index, not a byte offset, and the
    /// SPIR-V for two blocks is two push-constant blocks each starting at byte 0, which Vulkan
    /// does not allow in one entry point. So a program with
    /// more than one push-constant buffer is refused with the blocks named.
    /// </remarks>
    private static List<ProjectedRootConstants> RootConstants(
        ProgramLayout layout, StageBits allStages, IReadOnlyList<PromotedBlock> promoted)
    {
        var blocks = layout.PushConstantBlocks();

        if (blocks.Count > 1)
        {
            throw new NotSupportedException(
                $"this program has {blocks.Count} push-constant buffers " +
                $"({string.Join(", ", blocks.Select(b => b.Name))}). Slang reports their offsets as " +
                $"buffer indices, not byte offsets, so SharpSlang.Pipeline.NRI cannot lay them out into " +
                $"the single block a Vulkan entry point may use. Merge them into one " +
                $"[vk::push_constant] struct.");
        }

        var result = blocks
            .Select(b => new ProjectedRootConstants(
                0,
                new RootConstantDesc
                {
                    registerIndex = 0,
                    size = checked((uint)b.Size),
                    shaderStages = b.Stage is { } stage ? ToStage(stage) : allStages,
                },
                b.Name))
            .ToList();

        // The host's promoted constant buffers follow, in the order the host listed them, each at
        // the b register the DXIL reads it from. A push-constant block never coexists with these:
        // Slang only places one on a SPIR-V target, where promotion is refused, so the index is
        // simply the position.
        foreach (var p in promoted)
        {
            result.Add(new ProjectedRootConstants(
                (uint)result.Count,
                new RootConstantDesc
                {
                    registerIndex = p.Range.IndexOffset,
                    size = checked((uint)p.Size),
                    shaderStages = StagesFor(p.Range, allStages),
                },
                p.Name));
        }

        for (var i = 0; i < result.Count; i++) result[i] = result[i] with { Index = (uint)i };
        return result;
    }

    /// <summary>
    /// The fallback name for a range the walk could not name, looked up within the range's own
    /// register class — see <see cref="PipelineInterface.DescriptorNames(ProgramLayout, uint, SlangParameterCategory?)"/>
    /// for why the class matters on a DXIL layout.
    /// </summary>
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

    /// <summary>A constant buffer the host named in <see cref="NriProjectionOptions.RootConstantParameters"/>, resolved.</summary>
    private sealed record PromotedBlock(string Name, uint Space, DescriptorRange Range, ulong Size);

    /// <summary>
    /// Resolves each name in <see cref="NriProjectionOptions.RootConstantParameters"/> to the
    /// constant-buffer range it promotes, refusing anything the name cannot mean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target decides whether promotion is meaningful, and the layout records the target in
    /// its categories: a DXIL layout places a constant buffer in the <c>CONSTANT_BUFFER</c>
    /// category at a <c>b</c> register, which NRI's D3D12 root constants bind directly; a SPIR-V
    /// layout places it in <c>DESCRIPTOR_TABLE_SLOT</c>, and the module reads it as a uniform
    /// buffer through a descriptor — root constants there would be a push-constant range the
    /// SPIR-V never reads. So a SPIR-V constant buffer is refused with the attribute that does
    /// the job on that target, and a block Slang already placed in push-constant storage is the
    /// no-op that lets one host list serve both.
    /// </para>
    /// <para>
    /// Matched through the parameter list rather than by range name, because the per-space name
    /// table is keyed by register index alone and D3D numbers each register class from 0: on a
    /// DXIL layout <c>b0</c>, <c>t0</c>, <c>s0</c> and <c>u0</c> share the index, and a name
    /// lookup by index would promote whichever came first. The parameter's primary binding has
    /// the category as well as the index, and the range is found by both.
    /// </para>
    /// </remarks>
    private static List<PromotedBlock> PromotedConstantBuffers(ProgramLayout layout, NriProjectionOptions options)
    {
        var result = new List<PromotedBlock>();

        foreach (var name in options.RootConstantParameters)
        {
            var parameter = layout.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                ?? throw new NotSupportedException(
                    $"NriProjectionOptions.RootConstantParameters names '{name}', which this program " +
                    $"does not declare as a shader parameter. Declared: " +
                    $"{string.Join(", ", layout.Parameters.Select(p => p.Name))}.");

            // Already root constants by Slang's own placement: naming it is the no-op that lets
            // the same list cover the SPIR-V build of this shader.
            if (parameter.Bindings.Any(b => b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER))
                continue;

            var binding = parameter.PrimaryBinding ?? throw new NotSupportedException(
                $"'{name}' has no binding, so it cannot be promoted to root constants.");

            if (binding.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT)
            {
                throw new NotSupportedException(
                    $"'{name}' is a descriptor in a layout compiled for SPIR-V, where the module reads " +
                    $"it as a uniform buffer through its descriptor. Root constants there would be a " +
                    $"push-constant range the shader never reads. On a SPIR-V target, declare the " +
                    $"block [vk::push_constant] and the projection emits root constants on its own; " +
                    $"RootConstantParameters is for the DXIL build of the same shader.");
            }

            if (binding.Category != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER)
            {
                throw new NotSupportedException(
                    $"'{name}' is bound in the {binding.Category} category, not as a constant buffer, " +
                    $"so it cannot be root constants: NRI's D3D12 backend binds root constants at a " +
                    $"b register, and this parameter has none.");
            }

            var range = layout.DescriptorSets
                .Where(s => s.SpaceOffset == binding.Space)
                .SelectMany(s => s.Ranges)
                .FirstOrDefault(r =>
                    r.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER
                    && r.IndexOffset == binding.Index
                    && (r.BindingType & SlangBindingType.SLANG_BINDING_TYPE_BASE_MASK)
                        == SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER)
                ?? throw new NotSupportedException(
                    $"'{name}' is a constant buffer at b{binding.Index} in space {binding.Space}, but " +
                    $"no descriptor range of the layout is at that register. The parameter and the " +
                    $"descriptor-set view disagree, which is a gap in SharpSlang rather than in the " +
                    $"shader.");

            if (result.Count > 0 && result[0].Space != binding.Space)
            {
                throw new NotSupportedException(
                    $"'{name}' is in register space {binding.Space} and '{result[0].Name}' in space " +
                    $"{result[0].Space}. NRI has one root register space, so every promoted block " +
                    $"must share it; put them in one space or promote only one.");
            }

            // The declared size is the element's uniform footprint, not the wrapper's — the same
            // reading PipelineInterface.PushConstantBlocks gives a push-constant block.
            var block = parameter.Type is ContainerLayout c ? c.ElementType : parameter.Type;
            if (!block.IsUniformSizeKnown)
            {
                throw new NotSupportedException(
                    $"'{name}' has no fixed size: it contains an array whose length is a " +
                    $"specialization constant, so it cannot be root constants, whose size is part " +
                    $"of the pipeline layout.");
            }

            result.Add(new PromotedBlock(name, binding.Space, range, block.UniformSize));
        }

        return result;
    }

    /// <summary>
    /// Vertex attributes of the vertex stage, at the locations the module actually uses.
    /// </summary>
    /// <remarks>
    /// Which leaf attributes exist and where is decided once in
    /// <see cref="PipelineInterface.VertexInputs"/>. This gives each leaf an NRI <c>Format</c>
    /// and its D3D semantic, and refuses one it has no format for rather than dropping it: a
    /// dropped attribute leaves every location after it one short, silently. No distinct-location
    /// check is needed here: the only attributes that take two locations
    /// are the wide 64-bit vectors, and NRI has no format for those, so they are refused first.
    /// </remarks>
    private static List<ProjectedVertexAttribute> VertexAttributes(ProgramLayout layout)
    {
        var result = new List<ProjectedVertexAttribute>();

        foreach (var input in layout.VertexInputs())
        {
            var format = ToVertexFormat(input.Type);
            if (format == Format.UNKNOWN)
            {
                if (input.LocationsConsumed == 0) continue;

                throw new NotSupportedException(
                    $"vertex input '{input.Name}' is {input.Type.Kind}" +
                    $"{(input.Type.Name is null ? "" : $" ('{input.Type.Name}')")}, which has no NRI " +
                    $"vertex format (NRI's Format has no 64-bit types and no three-component 16-bit " +
                    $"one), but it occupies {input.LocationsConsumed} location(s). SharpSlang.Pipeline.NRI " +
                    $"will not guess one; narrow or widen the attribute.");
            }

            result.Add(new ProjectedVertexAttribute(
                input.Name,
                new NriVertexAttributeDesc(
                    SemanticName: input.Semantic,
                    SemanticIndex: input.SemanticIndex,
                    Location: input.Location,
                    Offset: 0,
                    Format: format,
                    StreamIndex: 0)));
        }

        return result;
    }

    /// <summary>Colour attachments the fragment stage writes, ordered by location.</summary>
    private static List<ProjectedFragmentOutput> FragmentOutputs(ProgramLayout layout) =>
        layout.FragmentOutputs()
            .Select(o => new ProjectedFragmentOutput(o.Location, ToVertexFormat(o.Type), o.Name, o.Semantic))
            .ToList();

    /// <summary>The program's entry points as pipeline stages.</summary>
    private static List<ProjectedStage> Stages(ProgramLayout layout) => layout.EntryPoints
        .Select(e => new ProjectedStage(e.Name, ToStage(e.Stage), e.ThreadGroupSize))
        .ToList();

    // ---------------------------------------------------------------- stages

    /// <summary>
    /// The stages a binding is visible to: the ones that read it, or every stage in the program
    /// if none is known.
    /// </summary>
    /// <remarks>
    /// The empty case is not "no stage": Vulkan rejects a binding with a non-zero descriptor count
    /// and no stage flags, and widening is the only safe direction — an omitted stage that really
    /// reads the binding is a driver rejection, where a surplus one costs nothing. The union of
    /// the program's stages rather than <c>StageBits.ALL</c>, so the two projections of one
    /// shader say the same thing and so NRI's validation sees a stage mask the pipeline has.
    /// </remarks>
    private static StageBits StagesFor(DescriptorRange range, StageBits fallback)
    {
        if (range.Stages is not { Count: > 0 } stages) return fallback;

        var narrowed = StageBits.ALL;    // 0: nothing accumulated yet
        foreach (var stage in stages) narrowed |= ToStage(stage);
        return narrowed == StageBits.ALL ? fallback : narrowed;
    }

    /// <summary>Every stage any entry point of the program runs as.</summary>
    /// <remarks>
    /// Starts from <c>StageBits.ALL</c>, which is 0 in NRI, so a program with no entry points
    /// projects to a layout visible everywhere — the same conservative answer as a binding no
    /// stage claims. <c>NONE</c> is the one with every bit set and is never emitted here.
    /// </remarks>
    private static StageBits AllStages(ProgramLayout layout)
    {
        var stages = StageBits.ALL;
        foreach (var e in layout.EntryPoints) stages |= ToStage(e.Stage);
        return stages;
    }

    /// <summary>
    /// The stages a binding of this type may be declared visible to: fragment only for an input
    /// attachment, which Vulkan enforces on the descriptor set layout
    /// (<c>VUID-VkDescriptorSetLayoutBinding-descriptorType-01510</c>) — found by the Vulkan
    /// projection's corpus run, and the same rule reaches NRI's Vulkan backend unchanged.
    /// </summary>
    private static StageBits VisibleStages(DescriptorType type, StageBits stages) =>
        type == DescriptorType.INPUT_ATTACHMENT ? stages & StageBits.FRAGMENT_SHADER : stages;
}
