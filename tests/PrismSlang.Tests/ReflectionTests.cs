// Reflection tests — the ones that decide whether the binding is usable by a renderer.
//
// Asserted against a shader whose layout is known by hand, so a wrong offset is a test failure
// rather than something discovered later as a garbled uniform block.

using PrismSlang.Native;
using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class ReflectionTests
{
    // std140-ish expectations, written out deliberately:
    //   mvp       float4x4  offset  0  size 64
    //   tint      float3    offset 64  size 12   (16-byte aligned)
    //   exposure  float     offset 76  size  4   (packs into tint's tail padding)
    private const string Shader = """
        struct Uniforms
        {
            float4x4 mvp;
            float3   tint;
            float    exposure;
        };
        ConstantBuffer<Uniforms> u;

        Texture2D    albedo;
        SamplerState albedoSampler;

        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position, float2 uv)
        {
            VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        {
            return float4(albedo.Sample(albedoSampler, i.uv).rgb * u.tint * u.exposure, 1.0);
        }
        """;

    /// <summary>Compiles the shader and returns only the snapshot — everything native is disposed.</summary>
    private static ProgramLayout Reflect(string source = Shader, params string[] entryPoints)
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
            return program.Reflect();
        }
        finally
        {
            foreach (var e in eps) e.Dispose();
        }
    }

    // ---------------------------------------------------------------- uniform layout

    [NativeFact]
    public void Constant_buffer_fields_have_the_expected_offsets_and_sizes()
    {
        var layout = Reflect();

        var u = layout.Parameter("u");
        Assert.NotNull(u);

        var block = Assert.IsType<ContainerLayout>(u.Type);
        Assert.Equal(LayoutKind.ConstantBuffer, block.Kind);

        var uniforms = Assert.IsType<StructLayout>(block.ElementType);
        Assert.Equal(["mvp", "tint", "exposure"], uniforms.Fields.Select(f => f.Name).ToArray());

        var mvp = uniforms.Field("mvp")!;
        Assert.Equal(0ul, mvp.UniformOffset);
        Assert.Equal(64ul, mvp.Type.UniformSize);
        var matrix = Assert.IsType<MatrixLayout>(mvp.Type);
        Assert.Equal(4, matrix.RowCount);
        Assert.Equal(4, matrix.ColumnCount);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32, matrix.ElementType);

        var tint = uniforms.Field("tint")!;
        Assert.Equal(64ul, tint.UniformOffset);
        Assert.Equal(12ul, tint.Type.UniformSize);
        var vector = Assert.IsType<VectorLayout>(tint.Type);
        Assert.Equal(3, vector.ElementCount);

        // exposure packs into the padding after tint rather than starting a new 16-byte slot.
        var exposure = uniforms.Field("exposure")!;
        Assert.Equal(76ul, exposure.UniformOffset);
        Assert.Equal(4ul, exposure.Type.UniformSize);
        Assert.IsType<ScalarLayout>(exposure.Type);
    }

    [NativeFact]
    public void Matrix_layout_mode_is_reported_and_follows_the_session()
    {
        static MatrixLayout Mvp(SlangMatrixLayoutMode mode)
        {
            using var global = SlangGlobalSession.Create();
            using var session = global.CreateSession(new SessionOptions
            {
                Targets = [new TargetOptions { Profile = "spirv_1_5" }],
                MatrixLayout = mode,
            });
            using var module = session.LoadModuleFromSource("t", "t.slang", Shader);
            using var vs = module.FindEntryPoint("vertexMain");
            using var program = session.Link(module, vs);

            var block = (ContainerLayout)program.Reflect().Parameter("u")!.Type;
            return (MatrixLayout)((StructLayout)block.ElementType).Field("mvp")!.Type;
        }

        Assert.Equal(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR,
            Mvp(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR).Mode);
        Assert.Equal(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
            Mvp(SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR).Mode);
    }

    // ---------------------------------------------------------------- bindings

    [NativeFact]
    public void Resources_land_on_distinct_sequential_bindings_in_set_zero()
    {
        var layout = Reflect();

        Assert.Equal(["u", "albedo", "albedoSampler"], layout.Parameters.Select(p => p.Name).ToArray());

        foreach (var (name, expectedBinding) in new[] { ("u", 0u), ("albedo", 1u), ("albedoSampler", 2u) })
        {
            var b = layout.Parameter(name)!.PrimaryBinding;
            Assert.NotNull(b);
            Assert.Equal(expectedBinding, b.Value.Index);
            Assert.Equal(0u, b.Value.Space);
        }
    }

    [NativeFact]
    public void Descriptor_sets_describe_the_three_bindings_with_their_kinds()
    {
        var layout = Reflect();

        var set = Assert.Single(layout.DescriptorSets);
        Assert.Equal(0u, set.SpaceOffset);
        Assert.Equal(3, set.Ranges.Count);

        Assert.Equal(SlangBindingType.SLANG_BINDING_TYPE_CONSTANT_BUFFER, set.Ranges[0].BindingType);
        Assert.Equal(SlangBindingType.SLANG_BINDING_TYPE_TEXTURE, set.Ranges[1].BindingType);
        Assert.Equal(SlangBindingType.SLANG_BINDING_TYPE_SAMPLER, set.Ranges[2].BindingType);

        Assert.Equal([0u, 1u, 2u], set.Ranges.Select(r => r.IndexOffset).ToArray());
        Assert.All(set.Ranges, r => Assert.Equal(1u, r.DescriptorCount));
    }

    [NativeFact]
    public void Texture_reports_its_shape_and_access()
    {
        var layout = Reflect();

        var albedo = Assert.IsType<ResourceLayout>(layout.Parameter("albedo")!.Type);
        Assert.Equal(SlangResourceShape.SLANG_TEXTURE_2D,
            albedo.Shape & SlangResourceShape.SLANG_RESOURCE_BASE_SHAPE_MASK);
        Assert.Equal(SlangResourceAccess.SLANG_RESOURCE_ACCESS_READ, albedo.Access);

        Assert.Equal(LayoutKind.SamplerState, layout.Parameter("albedoSampler")!.Type.Kind);
    }

    [NativeFact]
    public void ParameterBlock_space_assignment_follows_the_target_flag()
    {
        // Discovered rather than assumed: ParameterBlock<T> does NOT get its own register space by
        // default. Slang packs the blocks into space 0 unless
        // SLANG_TARGET_FLAG_PARAMETER_BLOCKS_USE_REGISTER_SPACES is set, which is what
        // TargetOptions.ParameterBlocksUseRegisterSpaces controls.
        //
        // This is the "policy input the engine controls" case: the descriptor-set structure the
        // renderer reflects is a consequence of session configuration, not a fixed property of the
        // shader. A Vulkan renderer modelling per-frame / per-material sets wants the flag on.
        const string TwoBlocks = """
            struct A { float4 x; };
            struct B { float4 y; };
            ParameterBlock<A> a;
            ParameterBlock<B> b;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return a.x + b.y; }
            """;

        static uint[] Spaces(bool useRegisterSpaces)
        {
            using var global = SlangGlobalSession.Create();
            using var session = global.CreateSession(new SessionOptions
            {
                Targets =
                [
                    new TargetOptions
                    {
                        Profile = "spirv_1_5",
                        ParameterBlocksUseRegisterSpaces = useRegisterSpaces,
                    },
                ],
            });
            using var module = session.LoadModuleFromSource("t", "t.slang", TwoBlocks);
            using var fs = module.FindEntryPoint("fragmentMain");
            using var program = session.Link(module, fs);

            var layout = program.Reflect();
            Assert.Equal(LayoutKind.ParameterBlock, layout.Parameter("a")!.Type.Kind);
            Assert.Equal(LayoutKind.ParameterBlock, layout.Parameter("b")!.Type.Kind);
            return layout.Parameters.Select(p => p.DescriptorSet).ToArray();
        }

        // Each block occupies a space of its own, on SPIR-V, with or without the target flag —
        // ParameterBlock introduces a sub-element register space regardless.
        //
        // Which numbers it gets depends on what else is in the global scope: with no loose
        // resources the first block takes space 0, whereas in
        // Descriptor_sets_include_bindings_nested_inside_parameter_blocks a loose texture holds
        // space 0 and the block moves to 1. So assert distinctness, not specific indices.
        foreach (var flag in new[] { false, true })
        {
            var spaces = Spaces(flag);
            Assert.Equal(2, spaces.Length);
            Assert.Equal(spaces.Length, spaces.Distinct().Count());
        }
    }

    [NativeFact]
    public void Descriptor_sets_include_bindings_nested_inside_parameter_blocks()
    {
        // Regression for a real modelling bug: reading descriptor sets only off the global scope
        // silently drops everything inside a ParameterBlock. The global scope reports such blocks
        // as binding ranges with descriptorSetIndex == -1, and their contents live in the block's
        // element layout under a space offset. The walk has to recurse.
        const string Nested = """
            struct A { float4 x; Texture2D t; };
            ParameterBlock<A> a;
            Texture2D loose;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target
            {
                return a.x + a.t.Load(int3(0,0,0)) + loose.Load(int3(0,0,0));
            }
            """;

        var layout = Reflect(Nested, "fragmentMain");

        // Space 0 holds the loose texture; the block gets a space of its own.
        Assert.True(layout.DescriptorSets.Count >= 2,
            $"expected the block's space as well as the global one, got " +
            $"{layout.DescriptorSets.Count}: [{string.Join(", ", layout.DescriptorSets.Select(s => s.SpaceOffset))}]");

        var blockSpace = layout.Parameter("a")!.DescriptorSet;
        Assert.NotEqual(0u, blockSpace);
        Assert.Equal(0u, layout.Parameter("loose")!.DescriptorSet);

        // The texture declared inside the block must appear in the block's space.
        var set = layout.DescriptorSets.Single(s => s.SpaceOffset == blockSpace);
        Assert.Contains(set.Ranges, r => r.BindingType == SlangBindingType.SLANG_BINDING_TYPE_TEXTURE);
    }

    // ---------------------------------------------------------------- entry points

    [NativeFact]
    public void Entry_points_report_their_stages()
    {
        var layout = Reflect();

        Assert.Equal(SlangStage.SLANG_STAGE_VERTEX, layout.EntryPoint("vertexMain")!.Stage);
        Assert.Equal(SlangStage.SLANG_STAGE_PIXEL, layout.EntryPoint("fragmentMain")!.Stage);
        Assert.Null(layout.EntryPoint("vertexMain")!.ThreadGroupSize);
    }

    [NativeFact]
    public void Vertex_entry_point_exposes_its_inputs()
    {
        var vs = Reflect().EntryPoint("vertexMain")!;

        Assert.Equal(["position", "uv"], vs.Parameters.Select(p => p.Name).ToArray());

        var position = Assert.IsType<VectorLayout>(vs.Parameters[0].Type);
        Assert.Equal(3, position.ElementCount);
        Assert.Equal(SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32, position.ElementType);

        var uv = Assert.IsType<VectorLayout>(vs.Parameters[1].Type);
        Assert.Equal(2, uv.ElementCount);

        // Varying inputs must carry a VARYING_INPUT binding — that is where the SPIR-V location
        // comes from, and the Vulkan projection depends on it.
        Assert.All(vs.Parameters, p => Assert.Contains(p.Bindings,
            b => b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_VARYING_INPUT));
    }

    [NativeFact]
    public void Compute_entry_point_reports_its_thread_group_size()
    {
        const string Compute = """
            RWStructuredBuffer<float> output;

            [shader("compute")]
            [numthreads(8, 4, 2)]
            void computeMain(uint3 tid : SV_DispatchThreadID)
            {
                output[tid.x] = 1.0;
            }
            """;

        var cs = Reflect(Compute, "computeMain").EntryPoint("computeMain")!;

        Assert.Equal(SlangStage.SLANG_STAGE_COMPUTE, cs.Stage);
        Assert.Equal(new ThreadGroupSize(8, 4, 2), cs.ThreadGroupSize);
    }

    // ---------------------------------------------------------------- arrays and attributes

    [NativeFact]
    public void Arrays_report_element_type_count_and_stride()
    {
        const string WithArray = """
            struct Light { float4 position; float4 colour; };
            struct Scene { Light lights[4]; };
            ConstantBuffer<Scene> scene;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return scene.lights[0].colour; }
            """;

        var layout = Reflect(WithArray, "fragmentMain");
        var scene = (StructLayout)((ContainerLayout)layout.Parameter("scene")!.Type).ElementType;

        var lights = Assert.IsType<ArrayLayout>(scene.Field("lights")!.Type);
        Assert.Equal(4, lights.ElementCount);
        Assert.Equal(32ul, lights.ElementStride);          // two float4s
        Assert.Equal(128ul, lights.Type_UniformSizeOrZero()); // 4 * 32

        var element = Assert.IsType<StructLayout>(lights.ElementType);
        Assert.Equal(["position", "colour"], element.Fields.Select(f => f.Name).ToArray());
        Assert.Equal(16ul, element.Field("colour")!.UniformOffset);
    }

    [NativeFact]
    public void User_attributes_are_captured_for_data_driven_materials()
    {
        // This is the mechanism a data-driven material system uses to learn about a parameter
        // without hardcoding shader knowledge in C#.
        const string WithAttributes = """
            [__AttributeUsage(_AttributeTargets.Var)]
            struct MaterialSlotAttribute { int slot; }

            struct Params { float4 tint; };

            [MaterialSlot(3)]
            ConstantBuffer<Params> material;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return material.tint; }
            """;

        var layout = Reflect(WithAttributes, "fragmentMain");
        var material = layout.Parameter("material")!;

        Assert.NotNull(material.Attributes);
        var attr = Assert.Single(material.Attributes!, a => a.Name.Contains("MaterialSlot", StringComparison.Ordinal));
        Assert.Equal(3, Assert.Single(attr.Arguments));
    }

    // ---------------------------------------------------------------- snapshot properties

    [NativeFact]
    public void Snapshot_outlives_the_program_that_produced_it()
    {
        // The whole point of §4.4: no native reflection pointer escapes the walk, so the snapshot
        // stays readable after everything native has been released.
        var layout = Reflect();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(76ul,
            ((StructLayout)((ContainerLayout)layout.Parameter("u")!.Type).ElementType)
                .Field("exposure")!.UniformOffset);
    }

    [NativeFact]
    public void Snapshot_round_trips_through_json()
    {
        // Enables offline baking: layout ships beside the SPIR-V so the shipped game never loads
        // the 36 MB compiler.
        var original = Reflect();
        var restored = ProgramLayout.FromJson(original.ToJson());

        Assert.Equal(original.Parameters.Count, restored.Parameters.Count);
        Assert.Equal(original.EntryPoints.Select(e => (e.Name, e.Stage)),
                     restored.EntryPoints.Select(e => (e.Name, e.Stage)));

        var before = (StructLayout)((ContainerLayout)original.Parameter("u")!.Type).ElementType;
        var after = (StructLayout)((ContainerLayout)restored.Parameter("u")!.Type).ElementType;
        Assert.Equal(before.Fields.Select(f => (f.Name, f.UniformOffset)),
                     after.Fields.Select(f => (f.Name, f.UniformOffset)));
    }
}

internal static class ArrayLayoutExtensions
{
    /// <summary>Uniform footprint of the array itself, or 0 if it has none.</summary>
    internal static ulong Type_UniformSizeOrZero(this ArrayLayout a) => a.UniformSize;
}
