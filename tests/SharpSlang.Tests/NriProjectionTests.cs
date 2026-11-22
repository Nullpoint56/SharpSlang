// Tests for the NRI projection.
//
// They pin the mappings: which DescriptorType a binding kind becomes, which StageBits a stage is,
// which Format a leaf type gets, and the few places NRI's vocabulary has no word for what Slang
// said and the projection must refuse. None of them needs NRI's native library; NriDeviceTests is
// where a device judges, and ModuleAgreementTests and PipelineLayoutTests hold the projected
// registers and locations to the emitted module.

using System.Reflection;
using SharpNRI;
using SharpNRI.Native;
using SharpSlang.Native;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class NriProjectionTests
{
    /// <summary>One shader with a constant buffer, a texture and sampler, a storage buffer and vertex inputs.</summary>
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;
        RWStructuredBuffer<float4> outBuffer;

        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv, uint4 boneIndices)
        {
            VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv + float(boneIndices.x); return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        {
            outBuffer[0] = float4(1,1,1,1);
            return float4(albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure, 1.0);
        }
        """;

    private static readonly NriProjectionOptions Capped = new() { UnboundedArrayCap = 64 };

    internal static ProgramLayout Reflect(string source, params string[] entryPoints)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { Profile = "spirv_1_5" }],
        });
        using var module = session.LoadModuleFromSource("t", "t.slang", source);

        var eps = entryPoints.Select(module.FindEntryPoint).ToArray();
        try
        {
            using var program = session.Link(module, eps);
            return program.Reflect(0);
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    private static NriPipelineProjection Project(string source = Shader, params string[] entryPoints)
    {
        if (entryPoints.Length == 0) entryPoints = ["vertexMain", "fragmentMain"];
        return Reflect(source, entryPoints).ToNri(Capped);
    }

    // ---------------------------------------------------------------- descriptor sets

    [NativeFact]
    public void Descriptor_types_map_to_the_nri_equivalents()
    {
        var set = Assert.Single(Project().DescriptorSets);
        Assert.Equal(0u, set.SetIndex);
        Assert.Equal(0u, set.RegisterSpace);

        var byName = set.Ranges.Where(r => r.Name is not null).ToDictionary(r => r.Name!);

        Assert.Equal(DescriptorType.CONSTANT_BUFFER, byName["u"].Desc.descriptorType);
        // Slang emits separate images and samplers for SPIR-V, so a bare Texture2D is a TEXTURE
        // and the sampler is its own SAMPLER; NRI has no combined kind to map onto anyway.
        Assert.Equal(DescriptorType.TEXTURE, byName["albedo"].Desc.descriptorType);
        Assert.Equal(DescriptorType.SAMPLER, byName["albedoSampler"].Desc.descriptorType);
        Assert.Equal(DescriptorType.STORAGE_STRUCTURED_BUFFER, byName["outBuffer"].Desc.descriptorType);
    }

    [NativeFact]
    public void Ranges_are_ordered_by_binding_and_indexed_by_position()
    {
        // RangeIndex is what NriDescriptorSet.Update keys on, so it must be the position in the
        // array the desc carries — and that array is ordered by binding, so a host reading the
        // projection and a host reading the desc see the same range at the same index.
        var set = Assert.Single(Project().DescriptorSets);
        var desc = Assert.Single(Project().Layout.DescriptorSets);

        Assert.Equal(set.Ranges.Select(r => r.Desc.baseRegisterIndex).Order(),
                     set.Ranges.Select(r => r.Desc.baseRegisterIndex));
        Assert.Equal(Enumerable.Range(0, set.Ranges.Count).Select(i => (uint)i),
                     set.Ranges.Select(r => r.RangeIndex));
        Assert.Equal(set.Ranges.Select(r => r.Desc), desc.Ranges);
        Assert.All(set.Ranges, r => Assert.Equal(1u, r.Desc.descriptorNum));
        Assert.All(set.Ranges, r => Assert.Equal(DescriptorRangeBits.NONE, r.Desc.flags));
        Assert.All(set.Ranges, r => Assert.False(string.IsNullOrEmpty(r.Name)));
    }

    [NativeFact]
    public void Stage_bits_name_the_stages_that_actually_read_the_binding()
    {
        var projection = Project();
        var set = Assert.Single(projection.DescriptorSets);
        var byName = set.Ranges.ToDictionary(r => r.Name!, r => r.Desc.shaderStages);

        Assert.Equal(StageBits.VERTEX_SHADER | StageBits.FRAGMENT_SHADER, byName["u"]);
        Assert.Equal(StageBits.FRAGMENT_SHADER, byName["albedo"]);
        Assert.Equal(StageBits.FRAGMENT_SHADER, byName["albedoSampler"]);
        Assert.Equal(StageBits.FRAGMENT_SHADER, byName["outBuffer"]);

        // The layout's own mask is every stage the program has — what NRI's validation compares
        // each range against.
        Assert.Equal(StageBits.VERTEX_SHADER | StageBits.FRAGMENT_SHADER, projection.Layout.ShaderStages);
    }

    [NativeFact]
    public void A_binding_no_stage_claims_is_visible_to_the_whole_program_rather_than_nowhere()
    {
        // The direction that matters, and it matters more here than in Vulkan: StageBits.ALL is 0
        // in NRI, so "no bits accumulated" and "visible everywhere" are the same value, and a
        // projection that forgot the fallback would still produce something that creates. The
        // fallback is the program's stages, which NriDeviceTests can check the device accepts.
        const string Unread = """
            Texture2D    urRead;
            Texture2D    urUnread;
            SamplerState urSampler;

            [shader("vertex")]
            float4 vertexMain(float3 p : POSITION) : SV_Position { return float4(p, 1); }

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return urRead.Sample(urSampler, 0); }
            """;

        var set = Assert.Single(Project(Unread, "vertexMain", "fragmentMain").DescriptorSets);
        var byName = set.Ranges.ToDictionary(r => r.Name!, r => r.Desc.shaderStages);

        Assert.Equal(StageBits.FRAGMENT_SHADER, byName["urRead"]);
        Assert.Equal(StageBits.VERTEX_SHADER | StageBits.FRAGMENT_SHADER, byName["urUnread"]);
    }

    [NativeFact]
    public void Parameter_blocks_become_separate_descriptor_sets_in_register_space_order()
    {
        const string Blocks = """
            struct Frame { float4x4 view; };
            struct Material { float4 tint; Texture2D map; };
            ParameterBlock<Frame> frame;
            ParameterBlock<Material> material;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target
            {
                return mul(frame.view, material.tint) + material.map.Load(int3(0,0,0));
            }
            """;

        var projection = Project(Blocks, "fragmentMain");

        Assert.True(projection.DescriptorSets.Count >= 2,
            $"expected a set per parameter block, got {projection.DescriptorSets.Count}");
        Assert.Equal(projection.DescriptorSets.Select(s => s.RegisterSpace).Order(),
                     projection.DescriptorSets.Select(s => s.RegisterSpace));
        Assert.Equal(Enumerable.Range(0, projection.DescriptorSets.Count).Select(i => (uint)i),
                     projection.DescriptorSets.Select(s => s.SetIndex));
        Assert.Equal(projection.DescriptorSets.Select(s => s.RegisterSpace),
                     projection.Layout.DescriptorSets.Select(s => s.RegisterSpace));

        // The texture inside the material block must survive the projection.
        Assert.Contains(projection.DescriptorSets.SelectMany(s => s.Ranges),
            r => r.Desc.descriptorType == DescriptorType.TEXTURE);
    }

    [NativeFact]
    public void A_skipped_register_space_separates_set_index_from_register_space()
    {
        // explicit-spaces declares sets 0 and 2. NRI's descriptor API is indexed by position in
        // the desc's array; the SPIR-V by the register space; the projection reports both and
        // they must not be confused.
        var c = ShaderCorpus.Case("explicit-spaces");
        var projection = Reflect(c.Source, [.. c.EntryPoints]).ToNri(Capped);

        Assert.Equal([0u, 2u], projection.DescriptorSets.Select(s => s.RegisterSpace));
        Assert.Equal([0u, 1u], projection.DescriptorSets.Select(s => s.SetIndex));
        Assert.Equal(2u, projection.Set(2)!.RegisterSpace);
        Assert.Null(projection.Set(1));

        var (set, range) = projection.Range("esFar")!.Value;
        Assert.Equal(1u, set.SetIndex);
        Assert.Equal(3u, range.Desc.baseRegisterIndex);
    }

    // ---------------------------------------------------------------- arrays

    [NativeFact]
    public void A_fixed_array_is_one_range_with_the_array_flag()
    {
        // Without ARRAY, NRI's Vulkan backend reads descriptorNum 4 as four consecutive bindings,
        // which is not what Slang's SPIR-V declares. The flag is what makes it one binding of four.
        var c = ShaderCorpus.Case("descriptor-arrays");
        var projection = Reflect(c.Source, [.. c.EntryPoints]).ToNri(Capped);

        var fixedArray = projection.Range("daFixed")!.Value.Range;
        Assert.Equal(4u, fixedArray.Desc.descriptorNum);
        Assert.Equal(DescriptorRangeBits.ARRAY, fixedArray.Desc.flags);
        Assert.False(fixedArray.IsUnbounded);
    }

    [NativeFact]
    public void An_unbounded_array_takes_the_host_cap_and_is_partially_bound()
    {
        var c = ShaderCorpus.Case("descriptor-arrays");
        var projection = Reflect(c.Source, [.. c.EntryPoints]).ToNri(new NriProjectionOptions { UnboundedArrayCap = 256 });

        var unbounded = projection.Range("daUnbounded")!.Value.Range;
        Assert.True(unbounded.IsUnbounded);
        Assert.Equal(256u, unbounded.Desc.descriptorNum);
        Assert.Equal(DescriptorRangeBits.ARRAY | DescriptorRangeBits.PARTIALLY_BOUND, unbounded.Desc.flags);
    }

    [NativeFact]
    public void An_unbounded_array_without_a_cap_is_refused_by_name()
    {
        // NRI's desc has no spelling for "unbounded", so whatever number went there would be the
        // size of a real binding. The host decides; the projection refuses to.
        var c = ShaderCorpus.Case("descriptor-arrays");
        var layout = Reflect(c.Source, [.. c.EntryPoints]);

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri());
        Assert.Contains("daUnbounded", e.Message);
        Assert.Contains("UnboundedArrayCap", e.Message);
    }

    [NativeFact]
    public void An_append_buffer_names_the_range_index_of_its_counter()
    {
        var c = ShaderCorpus.Case("append-buffers");
        var projection = Reflect(c.Source, [.. c.EntryPoints]).ToNri(Capped);
        var set = Assert.Single(projection.DescriptorSets);

        var append = set.Range("abAppend")!;
        Assert.NotNull(append.CounterRangeIndex);
        var counter = set.Ranges[(int)append.CounterRangeIndex!.Value];
        Assert.Equal(DescriptorType.STORAGE_STRUCTURED_BUFFER, counter.Desc.descriptorType);
        Assert.NotEqual(append.RangeIndex, counter.RangeIndex);
    }

    // ---------------------------------------------------------------- vertex input

    [NativeFact]
    public void Vertex_attributes_get_locations_formats_and_semantics_and_nothing_else()
    {
        var attributes = Project().VertexAttributes;

        var position = Assert.Single(attributes, v => v.Name == "position");
        Assert.Equal(Format.RGB32_SFLOAT, position.Desc.Format);

        var uv = Assert.Single(attributes, v => v.Name == "uv");
        Assert.Equal(Format.RG32_SFLOAT, uv.Desc.Format);

        var bones = Assert.Single(attributes, v => v.Name == "boneIndices");
        Assert.Equal(Format.RGBA32_UINT, bones.Desc.Format);

        Assert.Equal(attributes.Select(v => v.Desc.Location).Order(), attributes.Select(v => v.Desc.Location));
        Assert.Equal(attributes.Count, attributes.Select(v => v.Desc.Location).Distinct().Count());

        // The boundary this package must not cross: how attributes group into streams, at what
        // offsets, is a renderer decision. The desc has fields for both and they are left at zero
        // for the renderer to fill with `with`.
        Assert.All(attributes, v => Assert.Equal(0u, v.Desc.Offset));
        Assert.All(attributes, v => Assert.Equal((ushort)0, v.Desc.StreamIndex));
    }

    [NativeFact]
    public void Vertex_attributes_carry_the_d3d_semantic_and_its_index()
    {
        var c = ShaderCorpus.Case("vertex-interface");
        var attributes = Reflect(c.Source, [.. c.EntryPoints]).ToNri(Capped).VertexAttributes;

        var texcoord = Assert.Single(attributes, v => v.Name == "viTexCoord");
        Assert.Equal("TEXCOORD", texcoord.Desc.SemanticName);
        Assert.Equal(0u, texcoord.Desc.SemanticIndex);

        // A float4x4 is four attributes at consecutive locations, each a float4.
        var matrix = attributes.Where(v => v.Name.StartsWith("viInstance[", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, matrix.Count);
        Assert.All(matrix, v => Assert.Equal(Format.RGBA32_SFLOAT, v.Desc.Format));
        Assert.Equal(matrix.Select(v => v.Desc.Location), Enumerable.Range((int)matrix[0].Desc.Location, 4).Select(i => (uint)i));
    }

    [NativeFact]
    public void A_64_bit_vertex_attribute_is_refused_because_nri_has_no_format_for_it()
    {
        var c = ShaderCorpus.Case("wide-vertex-inputs");
        var layout = Reflect(c.Source, [.. c.EntryPoints]);

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(Capped));
        Assert.Contains("wvScalar", e.Message);
        Assert.Contains("64-bit", e.Message);
    }

    [NativeFact]
    public void Program_without_a_vertex_stage_has_no_vertex_attributes()
    {
        const string ComputeOnly = """
            RWStructuredBuffer<float> data;
            [shader("compute")]
            [numthreads(64, 1, 1)]
            void computeMain(uint3 tid : SV_DispatchThreadID) { data[tid.x] = 1.0; }
            """;

        var projection = Project(ComputeOnly, "computeMain");

        Assert.Empty(projection.VertexAttributes);
        Assert.Equal(StageBits.COMPUTE_SHADER, projection.Layout.ShaderStages);
        Assert.All(projection.DescriptorSets.SelectMany(s => s.Ranges),
            r => Assert.Equal(StageBits.COMPUTE_SHADER, r.Desc.shaderStages));

        var stage = Assert.Single(projection.Stages);
        Assert.Equal((64ul, 1ul, 1ul), stage.ThreadGroupSize!.Value.Resolved);
    }

    // ---------------------------------------------------------------- root constants

    [NativeFact]
    public void Push_constants_become_root_constants_with_slangs_size()
    {
        const string WithPush = """
            struct PushData { float4x4 model; float lodBias; };
            [vk::push_constant] ConstantBuffer<PushData> push;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return push.model[0] * push.lodBias; }
            """;

        var projection = Project(WithPush, "fragmentMain");

        var block = Assert.Single(projection.RootConstants);
        Assert.Equal(0u, block.Index);
        Assert.Equal("push", block.Name);
        Assert.Equal(StageBits.FRAGMENT_SHADER, block.Desc.shaderStages);

        // 80, not the 68 a naive float4x4 + float sum suggests: Slang rounds the struct up to its
        // 16-byte alignment, and the size has to be the one the SPIR-V was laid out against.
        Assert.Equal(80u, block.Desc.size);
        Assert.Equal([block.Desc], projection.Layout.RootConstants);

        // A push constant is not a descriptor; it must not also appear as a range.
        Assert.DoesNotContain(projection.DescriptorSets.SelectMany(s => s.Ranges), r => r.Name == "push");
    }

    [NativeFact]
    public void Naming_a_push_constant_block_as_a_root_constant_parameter_changes_nothing()
    {
        // The no-op that lets one host list serve the SPIR-V and the DXIL build of a shader: on
        // SPIR-V the block is already root constants by Slang's placement.
        const string WithPush = """
            struct PushData { float4 tint; };
            [vk::push_constant] ConstantBuffer<PushData> push;
            ConstantBuffer<PushData> frame;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return push.tint + frame.tint; }
            """;

        var layout = Reflect(WithPush, "fragmentMain");
        var plain = layout.ToNri();
        var named = layout.ToNri(new NriProjectionOptions { RootConstantParameters = ["push"] });

        Assert.Equal(plain.RootConstants, named.RootConstants);
        Assert.Equal(plain.Layout.RootRegisterSpace, named.Layout.RootRegisterSpace);
        Assert.Equal(plain.DescriptorSets.Select(s => s.Ranges.Count), named.DescriptorSets.Select(s => s.Ranges.Count));
    }

    [NativeFact]
    public void Promoting_a_spirv_constant_buffer_is_refused_with_the_attribute_that_does_the_job()
    {
        // On a SPIR-V target the module reads `frame` through its descriptor; root constants
        // there would be a push-constant range the shader never reads. The DXIL build of the same
        // shader is where the name means something — NriDeviceTests measures that one.
        const string WithBuffer = """
            struct FrameData { float4 tint; };
            ConstantBuffer<FrameData> frame;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return frame.tint; }
            """;

        var layout = Reflect(WithBuffer, "fragmentMain");
        var e = Assert.Throws<NotSupportedException>(
            () => layout.ToNri(new NriProjectionOptions { RootConstantParameters = ["frame"] }));

        Assert.Contains("frame", e.Message);
        Assert.Contains("[vk::push_constant]", e.Message);
    }

    [NativeFact]
    public void A_root_constant_parameter_the_program_does_not_declare_is_refused_by_name()
    {
        var layout = Reflect(Shader, "vertexMain", "fragmentMain");
        var e = Assert.Throws<NotSupportedException>(
            () => layout.ToNri(new NriProjectionOptions { RootConstantParameters = ["nothing"] }));

        Assert.Contains("'nothing'", e.Message);
        Assert.Contains("albedo", e.Message);   // the declared names are listed, so the typo is findable
    }

    // ---------------------------------------------------------------- refusals

    [NativeFact]
    public void A_combined_texture_sampler_is_refused_because_nri_cannot_name_one()
    {
        // descriptor-kinds declares a Sampler2D among sixteen other bindings. NRI's DescriptorType
        // has TEXTURE and SAMPLER and nothing for both at once, so the whole program is refused by
        // the construct's name rather than quietly projected sixteen-seventeenths of the way.
        var c = ShaderCorpus.Case("descriptor-kinds");
        var layout = Reflect(c.Source, [.. c.EntryPoints]);

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(Capped));
        Assert.Contains("combined texture sampler", e.Message);
    }

    [NativeFact]
    public void Non_zero_vulkan_binding_offsets_are_refused_up_front()
    {
        var layout = Reflect(Shader, "vertexMain", "fragmentMain");
        var options = new NriProjectionOptions { VkBindingOffsets = new VKBindingOffsets { tRegister = 100 } };

        var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(options));
        Assert.Contains("VkBindingOffsets", e.Message);
        Assert.Contains("t 100", e.Message);
    }

    // ---------------------------------------------------------------- mapping units

    [Theory]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_SAMPLER, DescriptorType.SAMPLER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_TEXTURE, DescriptorType.TEXTURE)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TETURE, DescriptorType.STORAGE_TEXTURE)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, DescriptorType.CONSTANT_BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_PARAMETER_BLOCK, DescriptorType.CONSTANT_BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_RAW_BUFFER, DescriptorType.STRUCTURED_BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_RAW_BUFFER, DescriptorType.STORAGE_STRUCTURED_BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_TYPED_BUFFER, DescriptorType.BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TYPED_BUFFER, DescriptorType.STORAGE_BUFFER)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET, DescriptorType.INPUT_ATTACHMENT)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE, DescriptorType.ACCELERATION_STRUCTURE)]
    public void Binding_type_mapping(SlangBindingType slang, DescriptorType expected) =>
        Assert.Equal(expected, NriProjection.ToDescriptorType(slang));

    [Theory]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER, "combined texture sampler")]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA, "inline uniform block")]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_EXISTENTIAL_VALUE, "EXISTENTIAL_VALUE")]
    public void Unmappable_binding_type_throws_rather_than_guessing(SlangBindingType slang, string named)
    {
        var e = Assert.Throws<NotSupportedException>(() => NriProjection.ToDescriptorType(slang));
        Assert.Contains(named, e.Message);
    }

    [Theory]
    [InlineData(SlangStage.SLANG_STAGE_VERTEX, StageBits.VERTEX_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_HULL, StageBits.TESS_CONTROL_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_DOMAIN, StageBits.TESS_EVALUATION_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_FRAGMENT, StageBits.FRAGMENT_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_COMPUTE, StageBits.COMPUTE_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_GEOMETRY, StageBits.GEOMETRY_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_RAY_GENERATION, StageBits.RAYGEN_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_AMPLIFICATION, StageBits.TASK_SHADER)]
    [InlineData(SlangStage.SLANG_STAGE_MESH, StageBits.MESH_SHADER)]
    public void Stage_mapping(SlangStage slang, StageBits expected) =>
        Assert.Equal(expected, NriProjection.ToStage(slang));

    [Fact]
    public void A_stage_nri_has_no_bit_for_is_refused()
    {
        // NRI has no harmless default: ALL is 0 and NONE is every bit, and a shader desc carrying
        // either is not a shader of any stage.
        Assert.Throws<NotSupportedException>(() => NriProjection.ToStage(SlangStage.SLANG_STAGE_DISPATCH));
    }

    /// <summary>The NRI header revision the projection's rules were measured against.</summary>
    /// <remarks>
    /// Every rule in NriProjection that is NRI's rather than Slang's — a set lands at its register
    /// space with gaps filled, a range without <c>ARRAY</c> is consecutive bindings, push-constant
    /// offsets accumulate in array order, binding offsets are added to every range — was read out
    /// of this revision's Vulkan backend, and the comments cite its line numbers. SharpSlang does
    /// not pin NRI itself because nothing here loads it; SharpNRI.Descs does. This pins what it
    /// says, so a SharpNRI bump that moves NRI fails here with the rules to re-measure named,
    /// instead of passing on tests that never ask a device.
    ///
    /// Re-read for v192 against v191 (NRI's tags, Source/VK/PipelineLayoutVK.hpp): the four rules
    /// are unchanged, six lines further down. What moved is that every stage mask is now also
    /// intersected with the stages the device supports (GetSupportedShaderStageFlags), which drops
    /// only stages the device cannot run; the projection sets explicit stages and is unaffected.
    /// </remarks>
    private const int MeasuredNriVersion = 192;

    [Fact]
    public void The_descs_package_was_generated_from_the_nri_revision_the_projection_was_measured_against()
    {
        // Three readings, because they can disagree. NriHeaders.Version and Nri.NriVersion are
        // consts, compiled into this assembly from the SharpNRI.Descs and SharpNRI this project was
        // built against; the AssemblyMetadata stamp is read from the SharpNRI.Descs that loaded.
        var stamped = typeof(NriPipelineLayoutDesc).Assembly
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NriVersion")?.Value;

        Assert.True(stamped is not null,
            "SharpNRI.Descs no longer stamps NriVersion into its assembly metadata, so nothing says " +
            "which NRI the loaded descs describe. Restore the stamp in SharpNRI before trusting this projection.");

        var readings = new[]
        {
            ("SharpNRI.Descs' NriHeaders.Version", NriHeaders.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("SharpNRI's Nri.NriVersion", Nri.NriVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("the loaded SharpNRI.Descs' NriVersion stamp", stamped!),
        };

        var expected = MeasuredNriVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (what, value) in readings)
        {
            Assert.True(value == expected,
                $"{what} is NRI {value}; the projection's rules were measured against NRI " +
                $"{MeasuredNriVersion}'s Vulkan backend (PipelineLayoutVK.hpp). Re-read the set " +
                $"placement, the ARRAY reading of descriptorNum, the push-constant offset " +
                $"accumulation and the binding-offset addition in the new revision, re-run " +
                $"NriDeviceTests, and update MeasuredNriVersion.");
        }
    }
    [Fact]
    public void The_pipeline_package_does_not_reference_the_nri_package()
    {
        // The whole point of the split, in both directions: the NRI package depends on SharpNRI's
        // descs, and the reader package under it does not.
        Assert.DoesNotContain(typeof(ProgramLayout).Assembly.GetReferencedAssemblies(),
            a => a.Name?.Contains("NRI", StringComparison.OrdinalIgnoreCase) == true);

        // And the NRI package depends on the descs, not on the binding with the native library.
        var nri = typeof(NriPipelineProjection).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Contains("SharpNRI.Descs", nri);
        Assert.DoesNotContain("SharpNRI", nri);
    }

    // ---------------------------------------------------------------- the corpus

    /// <summary>Corpus cases the NRI projection refuses, and the construct each message must name.</summary>
    /// <remarks>
    /// Exact, like NriDeviceTests' own lists: a refusal that stops happening fails the test,
    /// because it means either NRI grew a type or the projection started guessing.
    /// </remarks>
    private static readonly Dictionary<string, string> RefusedByNri = new(StringComparer.Ordinal)
    {
        ["descriptor-kinds"] = "combined texture sampler",
        ["wide-vertex-inputs"] = "64-bit",
    };

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    [NativeTheory]
    [MemberData(nameof(Cases))]
    public void The_projection_states_what_the_layout_states_on_every_corpus_shader(string caseName)
    {
        // The projection only re-spells the API-neutral reading in ProgramLayout and
        // PipelineInterface, which ModuleAgreementTests holds to the emitted module over the same
        // corpus. So every space, register, count, name, root constant, location and stage here
        // has to be that reading's, and a difference is the projection drifting from it.
        var c = ShaderCorpus.Case(caseName);
        var layout = Reflect(c.Source, [.. c.EntryPoints]);

        if (RefusedByNri.TryGetValue(caseName, out var named))
        {
            var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(Capped));
            Assert.Contains(named, e.Message);
            return;
        }

        var nri = layout.ToNri(Capped);

        // Descriptors: same spaces, registers, names, counts and per-range facts, in order.
        var neutral = ModuleAgreementTests.Descriptors(layout).ToList();
        var projected = nri.DescriptorSets.SelectMany(s => s.Ranges.Select(r => (s.RegisterSpace, r))).ToList();
        Assert.Equal(
            neutral.Select(d => (d.Space, d.Range.IndexOffset, d.Name)),
            projected.Select(p => (p.RegisterSpace, p.r.Desc.baseRegisterIndex, p.r.Name)));
        foreach (var (d, p) in neutral.Zip(projected))
        {
            Assert.Equal(d.Range.IsUnbounded, p.r.IsUnbounded);
            Assert.Equal(d.Range.IsUnbounded ? 64u : d.Range.DescriptorCount, p.r.Desc.descriptorNum);
            Assert.Equal(d.Range.InputAttachmentIndex, p.r.InputAttachmentIndex);
            Assert.Equal(d.Range.ImageFormat, p.r.ImageFormat);
            Assert.Equal(d.Range.CounterBinding is not null, p.r.CounterRangeIndex is not null);
        }

        // Root constants: the push-constant blocks, by name and size.
        Assert.Equal(layout.PushConstantBlocks().Select(b => (b.Name, (uint)b.Size)),
                     nri.RootConstants.Select(r => (r.Name, r.Desc.size)));

        // Vertex attributes and fragment outputs: the same ones at the same locations (a matrix is
        // its rows in both).
        Assert.Equal(
            layout.VertexInputs().Where(v => v.LocationsConsumed > 0).Select(v => (v.Name, v.Location)),
            nri.VertexAttributes.Select(a => (a.Name, a.Desc.Location)));
        Assert.Equal(layout.FragmentOutputs().Select(o => (o.Name, o.Location)),
                     nri.FragmentOutputs.Select(o => (o.Name, o.Location)));

        Assert.Equal(layout.EntryPoints.Select(e => e.Name), nri.Stages.Select(s => s.Name));
    }

    [Fact]
    public void Every_declared_refusal_names_a_shader_that_exists()
    {
        var unknown = RefusedByNri.Keys.Where(name => ShaderCorpus.All.All(c => c.Name != name)).ToList();
        Assert.True(unknown.Count == 0, "RefusedByNri names shaders the corpus does not have: " + string.Join(", ", unknown));
    }
}
