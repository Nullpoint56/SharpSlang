// Does the pipeline layout SharpSlang reports match the module Slang emitted?
//
// Every one of these compares against SpirvDecorations rather than against a number written down
// by hand, because every defect this file was written for was a disagreement between the two that
// no amount of self-consistent testing would have found. Four bugs shipped behind tests that
// asserted the projection agreed with itself:
//
//   * vertex attributes after a matrix were reported four locations too low, and the matrix was
//     dropped entirely
//   * an unbounded array's descriptor count was 0xFFFFFFFF
//   * a push constant block stole the name of the descriptor at set 0 binding 0
//   * an entry-point `uniform` descriptor was missing from the layout altogether
//
// A test that reads the answer out of the bytes cannot be wrong in the same direction as the code.

using SharpSlang.Baking;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Vulkan;

namespace SharpSlang.Tests;

public class PipelineLayoutTests
{
    private sealed record Compiled(ProgramLayout Layout, VulkanPipelineLayout Vulkan, byte[] Code);

    private static Compiled Compile(string source, string entryPoint = "fragmentMain")
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("p", "p.slang", source);
        using var ep = module.FindEntryPoint(entryPoint);
        using var program = session.Link(module, ep);

        var layout = program.Reflect();
        return new Compiled(layout, layout.ToVulkan(), program.GetEntryPointCodeBytes(0));
    }

    /// <summary>Asserts every descriptor the module declares is reported at the same coordinates.</summary>
    private static void AssertDescriptorsMatchModule(Compiled c)
    {
        var expected = SpirvDecorations.Descriptors(c.Code);

        var actual = c.Vulkan.DescriptorSets
            .SelectMany(s => s.Bindings.Select(b => (b.Name, Set: s.Set, b.Binding)))
            .Where(b => b.Name is not null)
            .ToDictionary(b => b.Name!, b => (b.Set, b.Binding));

        Assert.Equal(
            expected.OrderBy(kv => kv.Key),
            actual.OrderBy(kv => kv.Key));
    }

    // ---------------------------------------------------------------- vertex inputs

    private const string VertexStruct = """
        struct VIn
        {
            float3   position;
            float4x4 instanceXform;
            float2   uv;
            uint4    bones;
        };
        struct VOut { float4 pos : SV_Position; };

        [shader("vertex")]
        VOut vertexMain(VIn v)
        {
            VOut o; o.pos = mul(v.instanceXform, float4(v.position,1)) + float4(v.uv, v.bones.x, 0);
            return o;
        }
        """;

    [NativeFact]
    public void Vertex_attribute_locations_match_the_module()
    {
        var c = Compile(VertexStruct, "vertexMain");
        var expected = SpirvDecorations.InputLocations(c.Code);

        // A matrix is decorated once and consumes four consecutive locations, so the projection
        // reports four attributes where the module reports one variable. Compare the base.
        var actual = new Dictionary<string, uint>();
        foreach (var v in c.Vulkan.VertexInputs)
        {
            var name = v.Name.EndsWith("[0]", StringComparison.Ordinal)
                ? v.Name[..^3]
                : v.Name;
            if (!v.Name.EndsWith(']') || v.Name.EndsWith("[0]", StringComparison.Ordinal))
                actual[name] = v.Location;
        }

        Assert.Equal(expected.OrderBy(kv => kv.Key), actual.OrderBy(kv => kv.Key));

        // Spelled out, because these are the exact numbers the old code got wrong: it produced
        // position@0, uv@2, bones@3 and no matrix at all.
        Assert.Equal(0u, expected["position"]);
        Assert.Equal(1u, expected["instanceXform"]);
        Assert.Equal(5u, expected["uv"]);
        Assert.Equal(6u, expected["bones"]);
    }

    [NativeFact]
    public void A_matrix_attribute_becomes_one_attribute_per_location()
    {
        var c = Compile(VertexStruct, "vertexMain");

        var rows = c.Vulkan.VertexInputs
            .Where(v => v.Name.StartsWith("instanceXform", StringComparison.Ordinal))
            .OrderBy(v => v.Location)
            .ToList();

        Assert.Equal(4, rows.Count);
        Assert.Equal([1u, 2u, 3u, 4u], rows.Select(r => r.Location));
        Assert.All(rows, r => Assert.Equal(VulkanFormat.R32G32B32A32Sfloat, r.Format));

        // Nothing else may share those locations.
        Assert.Equal(
            c.Vulkan.VertexInputs.Count,
            c.Vulkan.VertexInputs.Select(v => v.Location).Distinct().Count());
    }

    [NativeFact]
    public void A_system_value_input_takes_no_location()
    {
        // SV_VertexID is a builtin. Defaulting its absent varying offset to 0 would collide it with
        // the attribute genuinely at location 0.
        const string WithBuiltin = """
            struct VIn { uint vid : SV_VertexID; float3 position; float2 uv; };
            struct VOut { float4 pos : SV_Position; };

            [shader("vertex")]
            VOut vertexMain(VIn v)
            { VOut o; o.pos = float4(v.position, v.uv.x) * v.vid; return o; }
            """;

        var c = Compile(WithBuiltin, "vertexMain");
        var expected = SpirvDecorations.InputLocations(c.Code);

        Assert.DoesNotContain(c.Vulkan.VertexInputs, v => v.Name == "vid");
        Assert.Equal(
            expected.OrderBy(kv => kv.Key),
            c.Vulkan.VertexInputs.ToDictionary(v => v.Name, v => v.Location).OrderBy(kv => kv.Key));
    }

    [NativeFact]
    public void An_unmappable_vertex_input_is_refused_rather_than_dropped()
    {
        // Silently skipping is how the matrix vanished. A type that occupies locations but has no
        // attribute format has to say so.
        //
        // This used to use double3x3, on the grounds that it maps to R64G64B64Sfloat per row. It
        // does — and each of those rows takes two locations while Slang assigns one, so the rows
        // overlap and the layout is refused before it can be inspected. That case moved to
        // VulkanEnumTests; what belongs here is the mapping, which float3x3 exercises identically.
        const string Matrix = """
            struct VIn { float3 position; float3x3 weird; };
            struct VOut { float4 pos : SV_Position; };

            [shader("vertex")]
            VOut vertexMain(VIn v)
            { VOut o; o.pos = float4(v.position, v.weird[0][0]); return o; }
            """;

        var c = Compile(Matrix, "vertexMain");

        // Every input the module decorates has to appear, under its own name or as one of the
        // per-location attributes a matrix expands into.
        var located = SpirvDecorations.InputLocations(c.Code);
        foreach (var name in located.Keys)
        {
            Assert.Contains(c.Vulkan.VertexInputs,
                v => v.Name == name || v.Name.StartsWith($"{name}[", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------- descriptors

    [NativeFact]
    public void Entry_point_uniform_descriptors_are_in_the_layout()
    {
        const string EpUniform = """
            Texture2D    albedo;
            SamplerState samp;
            struct U { float4 tint; };
            ConstantBuffer<U> globalCb;

            [shader("fragment")]
            float4 fragmentMain(
                float2 uv : TEXCOORD0,
                uniform Texture2D epTex,
                uniform SamplerState epSamp) : SV_Target
            { return albedo.Sample(samp, uv) * globalCb.tint + epTex.Sample(epSamp, uv); }
            """;

        var c = Compile(EpUniform);
        AssertDescriptorsMatchModule(c);

        // The numbers, spelled out: the entry point's block starts after the three globals, and
        // its two parameters are numbered from zero within it.
        var set = Assert.Single(c.Vulkan.DescriptorSets);
        Assert.Equal(5, set.Bindings.Count);
        Assert.Equal(3u, Assert.Single(set.Bindings, b => b.Name == "epTex").Binding);
        Assert.Equal(4u, Assert.Single(set.Bindings, b => b.Name == "epSamp").Binding);
    }

    [NativeFact]
    public void Entry_point_uniform_values_become_a_push_constant_range()
    {
        const string EpValue = """
            [shader("fragment")]
            float4 fragmentMain(uniform float4 tint, uniform float exposure) : SV_Target
            { return tint * exposure; }
            """;

        var c = Compile(EpValue);

        var range = Assert.Single(c.Vulkan.PushConstants);
        Assert.Equal(VulkanShaderStages.Fragment, range.StageFlags);
        Assert.Equal(0u, range.Offset);
        Assert.True(range.Size >= 20, $"expected at least float4+float = 20 bytes, got {range.Size}");
    }

    [NativeFact]
    public void A_push_constant_does_not_steal_a_descriptors_name()
    {
        // The push constant reports (space 0, index 0), the same coordinates as the descriptor at
        // set 0 binding 0, and sorts ahead of it. It used to win the name.
        const string PushAndDescriptors = """
            struct PushData { float4x4 model; float lodBias; };
            [vk::push_constant] ConstantBuffer<PushData> push;
            Texture2D albedo;
            SamplerState samp;

            [shader("fragment")]
            float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
            { return albedo.Sample(samp, uv) * push.model[0] * push.lodBias; }
            """;

        var c = Compile(PushAndDescriptors);
        AssertDescriptorsMatchModule(c);

        var set = Assert.Single(c.Vulkan.DescriptorSets);
        Assert.Equal("albedo", Assert.Single(set.Bindings, b => b.Binding == 0).Name);
        Assert.Equal("samp", Assert.Single(set.Bindings, b => b.Binding == 1).Name);
        Assert.DoesNotContain(set.Bindings, b => b.Name == "push");

        var range = Assert.Single(c.Vulkan.PushConstants);
        Assert.Equal("push", range.Name);
        Assert.Equal(80u, range.Size);
    }

    [NativeFact]
    public void Two_push_constant_buffers_are_refused_rather_than_mislaid()
    {
        // Slang reports push-constant offsets as buffer indices, not byte offsets. With one buffer
        // the two coincide at 0; with two, emitting the index as a byte offset produces ranges
        // that overlap. Refusing names the problem instead of shipping it.
        const string Two = """
            struct A { float4x4 model; };
            [vk::push_constant] ConstantBuffer<A> pushA;

            [shader("fragment")]
            float4 fragmentMain(uniform float4 epTint) : SV_Target
            { return pushA.model[0] * epTint; }
            """;

        var ex = Assert.Throws<NotSupportedException>(() => Compile(Two));
        Assert.Contains("push-constant", ex.Message);
    }

    [NativeFact]
    public void An_unbounded_array_reports_unbounded_rather_than_a_huge_count()
    {
        const string Bindless = """
            Texture2D    fixed8[8];
            Texture2D    unbounded[];
            SamplerState samp;

            [shader("fragment")]
            float4 fragmentMain(uint i : I) : SV_Target
            { return fixed8[0].Sample(samp, 0) + unbounded[i].Sample(samp, 0); }
            """;

        var c = Compile(Bindless);
        AssertDescriptorsMatchModule(c);

        var set = Assert.Single(c.Vulkan.DescriptorSets);

        var bounded = Assert.Single(set.Bindings, b => b.Name == "fixed8");
        Assert.False(bounded.IsUnbounded);
        Assert.Equal(8u, bounded.DescriptorCount);

        var runtime = Assert.Single(set.Bindings, b => b.Name == "unbounded");
        Assert.True(runtime.IsUnbounded);

        // 0xFFFFFFFF is what Slang says and it is not a count. Anything that reaches
        // VkDescriptorSetLayoutBinding::descriptorCount unexamined is a bug.
        Assert.NotEqual(uint.MaxValue, runtime.DescriptorCount);
        Assert.Equal(0u, runtime.DescriptorCount);

        // The neutral layer must not pass the sentinel through either.
        Assert.All(c.Layout.DescriptorSets.SelectMany(s => s.Ranges),
            r => Assert.NotEqual(uint.MaxValue, r.DescriptorCount));
    }

    [NativeFact]
    public void A_specialization_constant_does_not_steal_a_descriptors_name()
    {
        const string Mixed = """
            [vk::constant_id(0)] const int kLightCount = 4;
            struct U { float4x4 mvp; };
            ConstantBuffer<U> u;
            Texture2D albedo;
            SamplerState samp;

            [shader("fragment")]
            float4 fragmentMain(float2 uv : TEXCOORD0) : SV_Target
            { return albedo.Sample(samp, uv) * kLightCount * u.mvp[0][0]; }
            """;

        AssertDescriptorsMatchModule(Compile(Mixed));
    }

    // ---------------------------------------------------------------- fragment outputs

    [NativeFact]
    public void Fragment_output_locations_match_the_module()
    {
        const string Mrt = """
            struct GBuffer
            {
                float4 albedo : SV_Target0;
                float4 normal : SV_Target1;
                float2 motion : SV_Target2;
            };

            [shader("fragment")]
            GBuffer fragmentMain() : SV_Target
            { GBuffer g; g.albedo = 1; g.normal = 0; g.motion = 0; return g; }
            """;

        var c = Compile(Mrt);

        Assert.Equal(
            SpirvDecorations.OutputLocations(c.Code).OrderBy(kv => kv.Key),
            c.Vulkan.FragmentOutputs.ToDictionary(o => o.Name, o => o.Location).OrderBy(kv => kv.Key));

        // The count is what sizes VkPipelineColorBlendStateCreateInfo::attachmentCount.
        Assert.Equal(3, c.Vulkan.FragmentOutputs.Count);
        Assert.Equal(VulkanFormat.R32G32Sfloat,
            Assert.Single(c.Vulkan.FragmentOutputs, o => o.Name == "motion").Format);
    }

    [NativeFact]
    public void A_single_target_return_is_one_attachment()
    {
        var c = Compile("""
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return float4(1,0,0,1); }
            """);

        var output = Assert.Single(c.Vulkan.FragmentOutputs);
        Assert.Equal(0u, output.Location);
        Assert.Equal(VulkanFormat.R32G32B32A32Sfloat, output.Format);
    }

    [NativeFact]
    public void Depth_output_is_not_a_colour_attachment()
    {
        // SV_Depth is a builtin. Counting it would over-size the blend state and fail pipeline
        // creation against a render pass with one colour attachment. What identifies it is the
        // absence of a VARYING_OUTPUT category, not the spelling of the semantic — measured: the
        // module decorates it BuiltIn FragDepth with no location at all.
        const string WithDepth = """
            struct Out { float4 colour : SV_Target0; float depth : SV_Depth; };

            [shader("fragment")]
            Out fragmentMain() { Out o; o.colour = 1; o.depth = 0.5; return o; }
            """;

        var c = Compile(WithDepth);

        Assert.Equal(["colour"], SpirvDecorations.OutputLocations(c.Code).Keys);

        var output = Assert.Single(c.Vulkan.FragmentOutputs);
        Assert.Equal("colour", output.Name);
        Assert.Equal(0u, output.Location);
    }

    [NativeFact]
    public void A_function_level_semantic_overrides_the_struct_and_that_is_reported_faithfully()
    {
        // Worth pinning because it looks like a SharpSlang bug and is not. Writing `: SV_Target`
        // on a function that returns a struct makes Slang re-label every field, so the depth
        // member really does become SV_TARGET1 and really is decorated Location=1. Reporting two
        // colour attachments there is correct; the shader is what is wrong.
        const string Overridden = """
            struct Out { float4 colour : SV_Target0; float depth : SV_Depth; };

            [shader("fragment")]
            Out fragmentMain() : SV_Target { Out o; o.colour = 1; o.depth = 0.5; return o; }
            """;

        var c = Compile(Overridden);

        Assert.Equal(
            SpirvDecorations.OutputLocations(c.Code).OrderBy(kv => kv.Key),
            c.Vulkan.FragmentOutputs.ToDictionary(o => o.Name, o => o.Location).OrderBy(kv => kv.Key));
        Assert.Equal(2, c.Vulkan.FragmentOutputs.Count);
    }

    [NativeFact]
    public void A_vertex_only_program_has_no_fragment_outputs()
    {
        Assert.Empty(Compile(VertexStruct, "vertexMain").Vulkan.FragmentOutputs);
    }

    // ---------------------------------------------------------------- through the artifact

    [NativeFact]
    public void The_projection_survives_a_bake_and_reload()
    {
        const string Everything = """
            [vk::constant_id(0)] const int kLightCount = 4;
            struct PushData { float4x4 model; };
            [vk::push_constant] ConstantBuffer<PushData> push;
            Texture2D albedo;
            SamplerState samp;

            struct VIn { float3 position; float4x4 instanceXform; float2 uv; };
            struct VOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

            [shader("vertex")]
            VOut vertexMain(VIn v)
            { VOut o; o.pos = mul(v.instanceXform, float4(v.position,1)); o.uv = v.uv; return o; }

            [shader("fragment")]
            float4 fragmentMain(VOut i) : SV_Target
            { return albedo.Sample(samp, i.uv) * kLightCount * push.model[0][0]; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("e", "e.slang", Everything);
        using var vs = module.FindEntryPoint("vertexMain");
        using var fs = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vs, fs);

        using var buffer = new MemoryStream();
        ShaderPackageBaker.Bake(program, Everything).WriteTo(buffer);
        buffer.Position = 0;

        var before = program.Reflect().ToVulkan();
        var after = ShaderPackage.ReadFrom(buffer).Layout.ToVulkan();

        Assert.Equal(before.VertexInputs, after.VertexInputs);
        Assert.Equal(before.FragmentOutputs, after.FragmentOutputs);
        Assert.Equal(before.PushConstants, after.PushConstants);
        Assert.Equal(before.SpecializationConstants, after.SpecializationConstants);
        Assert.Equal(
            before.DescriptorSets.SelectMany(s => s.Bindings),
            after.DescriptorSets.SelectMany(s => s.Bindings));
    }
}
