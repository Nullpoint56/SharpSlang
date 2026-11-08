// Tests for the Vulkan projection.
//
// The mappings are spec-determined, so most of these pin a value rather than explore behaviour.
// The ones worth reading are the two that guard a boundary: that the projection does not invent
// vertex buffer bindings, and that it refuses rather than guesses on an unmapped binding type.

using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Vulkan;

namespace SharpSlang.Tests;

public class VulkanProjectionTests
{
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

    private static VulkanPipelineLayout Project(string source = Shader, params string[] entryPoints)
    {
        if (entryPoints.Length == 0) entryPoints = ["vertexMain", "fragmentMain"];

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
            return program.Reflect().ToVulkan();
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    // ---------------------------------------------------------------- descriptor sets

    [NativeFact]
    public void Descriptor_types_map_to_the_vulkan_equivalents()
    {
        var set = Assert.Single(Project().DescriptorSets);
        Assert.Equal(0u, set.Set);

        var byName = set.Bindings.Where(b => b.Name is not null).ToDictionary(b => b.Name!);

        Assert.Equal(VulkanDescriptorType.UniformBuffer, byName["u"].DescriptorType);
        // Slang emits separate images and samplers for SPIR-V, so a bare Texture2D is a sampled
        // image rather than a combined image sampler.
        Assert.Equal(VulkanDescriptorType.SampledImage, byName["albedo"].DescriptorType);
        Assert.Equal(VulkanDescriptorType.Sampler, byName["albedoSampler"].DescriptorType);
        Assert.Equal(VulkanDescriptorType.StorageBuffer, byName["outBuffer"].DescriptorType);
    }

    [NativeFact]
    public void Bindings_are_ordered_and_carry_their_names()
    {
        var set = Assert.Single(Project().DescriptorSets);

        Assert.Equal(set.Bindings.Select(b => b.Binding).Order(), set.Bindings.Select(b => b.Binding));
        Assert.All(set.Bindings, b => Assert.Equal(1u, b.DescriptorCount));
        Assert.All(set.Bindings, b => Assert.False(string.IsNullOrEmpty(b.Name)));
    }

    [NativeFact]
    public void Stage_flags_name_the_stages_that_actually_read_the_binding()
    {
        // This test used to assert the opposite, and the comment explained why: Slang's
        // global-scope reflection does not attribute a parameter to specific entry points, so
        // every binding reported every stage in the program. Conservative, safe, and it left a
        // renderer wanting tight stageFlags to work them out itself.
        //
        // IMetadata knows per entry point, and the reflection walk now asks it. In this
        // shader `u` is read by both stages — u.mvp in the vertex, u.tint and u.exposure in the
        // fragment — and the other three are fragment-only.
        var set = Assert.Single(Project().DescriptorSets);
        var byName = set.Bindings.ToDictionary(b => b.Name!, b => b.StageFlags);

        Assert.Equal(VulkanShaderStages.Vertex | VulkanShaderStages.Fragment, byName["u"]);
        Assert.Equal(VulkanShaderStages.Fragment, byName["albedo"]);
        Assert.Equal(VulkanShaderStages.Fragment, byName["albedoSampler"]);
        Assert.Equal(VulkanShaderStages.Fragment, byName["outBuffer"]);
    }

    [NativeFact]
    public void A_binding_no_stage_claims_is_visible_everywhere_rather_than_nowhere()
    {
        // The direction that matters. Vulkan rejects a binding with a non-zero descriptor count
        // and no stage flags, and an omitted stage that really reads the binding is a driver
        // rejection where a surplus one costs nothing — so "nobody claims it" has to widen, not
        // narrow. `unread` is declared, laid out, and touched by no generated code.
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
        var byName = set.Bindings.ToDictionary(b => b.Name!, b => b.StageFlags);

        Assert.Equal(VulkanShaderStages.Fragment, byName["urRead"]);
        Assert.Equal(VulkanShaderStages.Vertex | VulkanShaderStages.Fragment, byName["urUnread"]);
    }

    [NativeFact]
    public void Parameter_blocks_become_separate_descriptor_sets()
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

        var layout = Project(Blocks, "fragmentMain");

        Assert.True(layout.DescriptorSets.Count >= 2,
            $"expected a set per parameter block, got {layout.DescriptorSets.Count}");
        Assert.Equal(layout.DescriptorSets.Select(s => s.Set).Order(),
                     layout.DescriptorSets.Select(s => s.Set));

        // The texture inside the material block must survive the projection.
        Assert.Contains(layout.DescriptorSets.SelectMany(s => s.Bindings),
            b => b.DescriptorType == VulkanDescriptorType.SampledImage);
    }

    // ---------------------------------------------------------------- vertex input

    [NativeFact]
    public void Vertex_inputs_get_locations_and_formats()
    {
        var inputs = Project().VertexInputs;

        var position = Assert.Single(inputs, v => v.Name == "position");
        Assert.Equal(VulkanFormat.R32G32B32Sfloat, position.Format);

        var uv = Assert.Single(inputs, v => v.Name == "uv");
        Assert.Equal(VulkanFormat.R32G32Sfloat, uv.Format);

        var bones = Assert.Single(inputs, v => v.Name == "boneIndices");
        Assert.Equal(VulkanFormat.R32G32B32A32Uint, bones.Format);

        Assert.Equal(inputs.Select(v => v.Location).Order(), inputs.Select(v => v.Location));
        Assert.Equal(inputs.Count, inputs.Select(v => v.Location).Distinct().Count());
    }

    [NativeFact]
    public void Vertex_inputs_do_not_invent_buffer_bindings()
    {
        // The boundary this package must not cross. How attributes group into vertex buffers,
        // their strides, and input rates are renderer decisions the shader does not express.
        // If VulkanVertexInput ever grows a Binding or Stride member, this test should be the
        // thing that makes someone justify it.
        var properties = typeof(VulkanVertexInput).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain("Binding", properties);
        Assert.DoesNotContain("Stride", properties);
        Assert.DoesNotContain("Offset", properties);
        Assert.DoesNotContain("InputRate", properties);
    }

    [NativeFact]
    public void Program_without_a_vertex_stage_has_no_vertex_inputs()
    {
        const string ComputeOnly = """
            RWStructuredBuffer<float> data;
            [shader("compute")]
            [numthreads(64, 1, 1)]
            void computeMain(uint3 tid : SV_DispatchThreadID) { data[tid.x] = 1.0; }
            """;

        var layout = Project(ComputeOnly, "computeMain");

        Assert.Empty(layout.VertexInputs);
        Assert.All(layout.DescriptorSets.SelectMany(s => s.Bindings),
            b => Assert.Equal(VulkanShaderStages.Compute, b.StageFlags));
    }

    // ---------------------------------------------------------------- push constants

    [NativeFact]
    public void Push_constants_are_reported_with_their_size()
    {
        const string WithPush = """
            struct PushData { float4x4 model; float lodBias; };
            [vk::push_constant] ConstantBuffer<PushData> push;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return push.model[0] * push.lodBias; }
            """;

        var layout = Project(WithPush, "fragmentMain");

        var range = Assert.Single(layout.PushConstants);
        Assert.Equal("push", range.Name);
        Assert.Equal(VulkanShaderStages.Fragment, range.StageFlags);

        // 80, not the 68 a naive float4x4 + float sum suggests: Slang rounds the struct up to its
        // 16-byte alignment. The size reported must be Slang's, since it is what the SPIR-V was
        // laid out against — a hand-computed 68 would under-size the VkPushConstantRange.
        Assert.Equal(80u, range.Size);

        // A push constant is not a descriptor; it must not also appear as a binding.
        Assert.DoesNotContain(layout.DescriptorSets.SelectMany(s => s.Bindings), b => b.Name == "push");
    }

    // ---------------------------------------------------------------- mapping units

    [Theory]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_SAMPLER, VulkanDescriptorType.Sampler)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_TEXTURE, VulkanDescriptorType.SampledImage)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TETURE, VulkanDescriptorType.StorageImage)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER, VulkanDescriptorType.CombinedImageSampler)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, VulkanDescriptorType.UniformBuffer)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_RAW_BUFFER, VulkanDescriptorType.StorageBuffer)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_TYPED_BUFFER, VulkanDescriptorType.UniformTexelBuffer)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_MUTABLE_TYPED_BUFFER, VulkanDescriptorType.StorageTexelBuffer)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_INPUT_RENDER_TARGET, VulkanDescriptorType.InputAttachment)]
    [InlineData(SlangBindingType.SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE, VulkanDescriptorType.AccelerationStructureKhr)]
    public void Binding_type_mapping(SlangBindingType slang, VulkanDescriptorType expected) =>
        Assert.Equal(expected, VulkanProjection.ToDescriptorType(slang));

    [Fact]
    public void Unmapped_binding_type_throws_rather_than_guessing()
    {
        // Silently mapping an unknown binding to "uniform buffer" would produce a pipeline that
        // validates and renders wrongly. Better to fail at bake time with a nameable cause.
        var ex = Assert.Throws<NotSupportedException>(
            () => VulkanProjection.ToDescriptorType(SlangBindingType.SLANG_BINDING_TYPE_EXISTENTIAL_VALUE));
        Assert.Contains("EXISTENTIAL_VALUE", ex.Message);
    }

    [Theory]
    [InlineData(SlangStage.SLANG_STAGE_VERTEX, VulkanShaderStages.Vertex)]
    [InlineData(SlangStage.SLANG_STAGE_FRAGMENT, VulkanShaderStages.Fragment)]
    [InlineData(SlangStage.SLANG_STAGE_COMPUTE, VulkanShaderStages.Compute)]
    [InlineData(SlangStage.SLANG_STAGE_GEOMETRY, VulkanShaderStages.Geometry)]
    [InlineData(SlangStage.SLANG_STAGE_RAY_GENERATION, VulkanShaderStages.RaygenKhr)]
    [InlineData(SlangStage.SLANG_STAGE_MESH, VulkanShaderStages.MeshExt)]
    public void Stage_mapping(SlangStage slang, VulkanShaderStages expected) =>
        Assert.Equal(expected, VulkanProjection.ToStage(slang));

    [Fact]
    public void Enum_values_match_the_vulkan_specification()
    {
        // These are cast straight to Vortice/Silk types by consumers, so a wrong number is a
        // silently wrong descriptor rather than a compile error.
        Assert.Equal(0, (int)VulkanDescriptorType.Sampler);
        Assert.Equal(1, (int)VulkanDescriptorType.CombinedImageSampler);
        Assert.Equal(6, (int)VulkanDescriptorType.UniformBuffer);
        Assert.Equal(7, (int)VulkanDescriptorType.StorageBuffer);
        Assert.Equal(1000150000, (int)VulkanDescriptorType.AccelerationStructureKhr);

        Assert.Equal(0x01, (int)VulkanShaderStages.Vertex);
        Assert.Equal(0x10, (int)VulkanShaderStages.Fragment);
        Assert.Equal(0x20, (int)VulkanShaderStages.Compute);

        Assert.Equal(106, (int)VulkanFormat.R32G32B32Sfloat);
        Assert.Equal(109, (int)VulkanFormat.R32G32B32A32Sfloat);
    }

    [Fact]
    public void Core_package_does_not_reference_the_vulkan_package()
    {
        // The whole point of the split: a D3D12 or Metal consumer must be able to take SharpSlang
        // without Vulkan concepts coming along.
        var core = typeof(ProgramLayout).Assembly;
        Assert.DoesNotContain(core.GetReferencedAssemblies(),
            a => a.Name?.Contains("Vulkan", StringComparison.OrdinalIgnoreCase) == true);
    }
}
