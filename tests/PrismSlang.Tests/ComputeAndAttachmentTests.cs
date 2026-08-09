// The two pipeline shapes the projection could not describe.
//
// A compute pipeline needs a workgroup size to dispatch, and a subpass needs its input attachments
// in the descriptor set layout. Both were missing in ways that produced a plausible answer:
// reflection reported a thread group size of 0 for a specialized dimension, and dropped subpass
// inputs from the descriptor sets while Slang went on consuming their binding indices.

using PrismSlang.Baking;
using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public class ComputeAndAttachmentTests
{
    private static (byte[] Code, ProgramLayout Layout) Compile(string source, string entryPoint)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", source);
        using var ep = module.FindEntryPoint(entryPoint);
        using var program = session.Link(module, ep);

        return (program.GetEntryPointCodeBytes(0), program.Reflect());
    }

    // ---------------------------------------------------------------- workgroup size

    private const string FixedCompute = """
        RWStructuredBuffer<float> gOut;

        [shader("compute")]
        [numthreads(8, 4, 2)]
        void computeMain(uint3 tid : SV_DispatchThreadID) { gOut[tid.x] = 1; }
        """;

    private const string SpecializedCompute = """
        [vk::constant_id(0)] const uint kGroupX = 64;
        RWStructuredBuffer<float> gOut;

        [shader("compute")]
        [numthreads(kGroupX, 1, 1)]
        void computeMain(uint3 tid : SV_DispatchThreadID) { gOut[tid.x] = 1; }
        """;

    [NativeFact]
    public void A_fixed_workgroup_size_matches_the_modules_execution_mode()
    {
        var (code, layout) = Compile(FixedCompute, "computeMain");

        Assert.Equal((8u, 4u, 2u, false), SpirvDecorations.LocalSize(code));

        var reflected = layout.EntryPoints[0].ThreadGroupSize;
        Assert.Equal(new ThreadGroupSize(8, 4, 2), reflected);
        Assert.False(reflected!.Value.IsSpecialized);
        Assert.Equal((8ul, 4ul, 2ul), reflected.Value.Resolved);

        var module = SpirvModule.Read(code).EntryPoints[0].WorkgroupSize;
        Assert.Equal((8u, 4u, 2u), module!.Resolve());
        Assert.False(module.IsSpecialized);
    }

    [NativeFact]
    public void A_specialized_workgroup_size_says_so_instead_of_reporting_zero()
    {
        // The bug: Slang reports [0, 1, 1], and a host computing (width + size.X - 1) / size.X
        // divides by zero. The value is genuinely unknown until pipeline creation — what was
        // missing was any way to tell that apart from a real number.
        var (code, layout) = Compile(SpecializedCompute, "computeMain");

        var reflected = layout.EntryPoints[0].ThreadGroupSize!.Value;
        Assert.True(reflected.IsSpecialized);
        Assert.Null(reflected.Resolved);
        Assert.Equal(0ul, reflected.X);
        Assert.Contains("specialized", reflected.ToString());

        // The module knows what reflection does not: LocalSizeId, not LocalSize.
        Assert.True(SpirvDecorations.LocalSize(code)!.Value.ById);
    }

    [NativeFact]
    public void The_module_carries_the_constant_id_and_the_declared_default()
    {
        var (code, layout) = Compile(SpecializedCompute, "computeMain");
        var size = SpirvModule.Read(code).EntryPoints[0].WorkgroupSize!;

        Assert.True(size.IsSpecialized);
        Assert.Equal(layout.SpecializationConstant("kGroupX")!.ConstantId, size.X.SpecializationConstantId);
        Assert.Equal(64u, size.X.Value);          // the default the shader declared
        Assert.False(size.Y.IsSpecialized);
        Assert.Equal(1u, size.Y.Value);

        // Unsupplied constants keep their defaults, which is what Vulkan does.
        Assert.Equal((64u, 1u, 1u), size.Resolve());
        Assert.Equal((128u, 1u, 1u), size.Resolve(new Dictionary<uint, uint> { [0] = 128 }));
    }

    [NativeFact]
    public void A_raster_stage_declares_no_workgroup_size()
    {
        const string Source = """
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """;

        var (code, layout) = Compile(Source, "fragmentMain");

        Assert.Null(layout.EntryPoints[0].ThreadGroupSize);
        Assert.Null(SpirvModule.Read(code).EntryPoints[0].WorkgroupSize);
        Assert.Null(layout.ToVulkan().Stage("fragmentMain")!.WorkgroupSize);
    }

    // ---------------------------------------------------------------- dispatch

    [NativeFact]
    public void The_projection_can_size_a_dispatch()
    {
        var (_, layout) = Compile(FixedCompute, "computeMain");
        var stage = layout.ToVulkan().Stage("computeMain")!;

        Assert.Equal(VulkanShaderStages.Compute, stage.Stage);
        Assert.Equal(new VulkanWorkgroupSize(8, 4, 2), stage.WorkgroupSize);

        // Rounded up, not truncated: 1920/8 exactly, 1080/4 = 270 exactly, 5/2 -> 3.
        Assert.Equal((240u, 270u, 3u), stage.WorkgroupSize!.GroupCountFor(1920, 1080, 5));
        Assert.Equal((1u, 1u, 1u), stage.WorkgroupSize.GroupCountFor(1));
    }

    [NativeFact]
    public void A_specialized_dispatch_is_refused_rather_than_divided_by_zero()
    {
        var (code, layout) = Compile(SpecializedCompute, "computeMain");
        var stage = layout.ToVulkan().Stage("computeMain")!;

        Assert.True(stage.WorkgroupSize!.IsSpecialized);
        var e = Assert.Throws<InvalidOperationException>(() => stage.WorkgroupSize.GroupCountFor(1920));
        Assert.Contains("specialization constant", e.Message);

        // Resolved against the values the pipeline will actually use, it dispatches.
        var fromModule = SpirvModule.Read(code).EntryPoints[0].WorkgroupSize!;
        var resolved = VulkanWorkgroupSize.From(fromModule, new Dictionary<uint, uint> { [0] = 128 });

        Assert.False(resolved.IsSpecialized);
        Assert.Equal(15u, resolved.GroupCountFor(1920).X);

        // An empty dictionary is a real answer — "I supply nothing", so Vulkan uses the shader's
        // defaults — and that determines the size just as well.
        var defaulted = VulkanWorkgroupSize.From(fromModule, new Dictionary<uint, uint>());
        Assert.False(defaulted.IsSpecialized);
        Assert.Equal(30u, defaulted.GroupCountFor(1920).X);

        // Saying nothing at all is not the same as saying nothing is supplied, and still refuses.
        Assert.True(VulkanWorkgroupSize.From(fromModule).IsSpecialized);
    }

    [NativeFact]
    public void A_baked_package_carries_the_workgroup_size_through_the_round_trip()
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", SpecializedCompute);
        using var ep = module.FindEntryPoint("computeMain");
        using var program = session.Link(module, ep);

        using var buffer = new MemoryStream();
        ShaderPackage.Bake(program, SpecializedCompute).WriteTo(buffer);
        buffer.Position = 0;

        var size = ShaderPackage.ReadFrom(buffer).EntryPoint("computeMain")!.WorkgroupSize!;

        // Constant id 0 has to survive as an id rather than collapsing into "not specialized".
        Assert.Equal(0u, size.X.SpecializationConstantId);
        Assert.Equal(64u, size.X.Value);
        Assert.Null(size.Y.SpecializationConstantId);
    }

    [NativeFact]
    public void A_raster_package_records_no_workgroup_size()
    {
        const string Source = """
            [shader("vertex")]
            float4 vertexMain(float3 p : POSITION) : SV_Position { return float4(p, 1); }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("m", "m.slang", Source);
        using var ep = module.FindEntryPoint("vertexMain");
        using var program = session.Link(module, ep);

        using var buffer = new MemoryStream();
        ShaderPackage.Bake(program, Source).WriteTo(buffer);
        buffer.Position = 0;

        Assert.Null(ShaderPackage.ReadFrom(buffer).EntryPoint("vertexMain")!.WorkgroupSize);
    }

    // ---------------------------------------------------------------- subpass inputs

    private const string SubpassShader = """
        [[vk::input_attachment_index(0)]] SubpassInput gGbufferA;
        [[vk::input_attachment_index(1)]] SubpassInput gGbufferB;
        Texture2D gOrdinary;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        {
            return gGbufferA.SubpassLoad() + gGbufferB.SubpassLoad() + gOrdinary.Load(0);
        }
        """;

    [NativeFact]
    public void Subpass_inputs_reach_the_descriptor_set_layout()
    {
        // The bug: Slang omits subpass inputs from both its binding-range and descriptor-set views
        // while still consuming their indices, so the layout held one binding, at index 2, for a
        // shader that reads three. The descriptor set the renderer built did not describe the
        // shader it was built for.
        var (code, layout) = Compile(SubpassShader, "fragmentMain");

        var expected = SpirvDecorations.Descriptors(code);
        var actual = layout.ToVulkan().DescriptorSets
            .SelectMany(s => s.Bindings.Select(b => (b.Name, Set: s.Set, b.Binding)))
            .ToDictionary(b => b.Name!, b => (b.Set, b.Binding));

        Assert.Equal(expected, actual);
    }

    [NativeFact]
    public void They_are_input_attachments_and_carry_their_attachment_index()
    {
        var (code, layout) = Compile(SubpassShader, "fragmentMain");
        var bindings = layout.ToVulkan().Set(0)!.Bindings.ToDictionary(b => b.Name!);

        Assert.Equal(VulkanDescriptorType.InputAttachment, bindings["gGbufferA"].DescriptorType);
        Assert.Equal(VulkanDescriptorType.InputAttachment, bindings["gGbufferB"].DescriptorType);
        Assert.Equal(VulkanDescriptorType.SampledImage, bindings["gOrdinary"].DescriptorType);

        // The attachment index is a separate number from the binding, and here they differ from
        // each other's ordering only because the shader keeps them aligned — the module is the
        // check that matters.
        var indices = SpirvDecorations.InputAttachmentIndices(code);
        Assert.Equal(indices["gGbufferA"], bindings["gGbufferA"].InputAttachmentIndex);
        Assert.Equal(indices["gGbufferB"], bindings["gGbufferB"].InputAttachmentIndex);
        Assert.Null(bindings["gOrdinary"].InputAttachmentIndex);
    }

    [NativeFact]
    public void A_bare_subpass_input_works_too()
    {
        // Without the vk:: attribute Slang assigns the index itself; it must still be reported.
        const string Source = """
            SubpassInput gA;
            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return gA.SubpassLoad(); }
            """;

        var (code, layout) = Compile(Source, "fragmentMain");
        var binding = Assert.Single(layout.ToVulkan().Set(0)!.Bindings);

        Assert.Equal(VulkanDescriptorType.InputAttachment, binding.DescriptorType);
        Assert.Equal(SpirvDecorations.InputAttachmentIndices(code)["gA"], binding.InputAttachmentIndex);
    }

    // ---------------------------------------------------------------- descriptor names

    [NativeFact]
    public void Bindings_inside_a_parameter_block_are_named()
    {
        // The bug: names were recovered by scanning the top-level parameter list, which cannot see
        // inside a ParameterBlock — so every binding of Slang's own descriptor-set feature came
        // back with an empty name and a material system had nothing to bind against.
        const string Source = """
            struct Frame { float4x4 view; Texture2D shadow; SamplerState shadowSampler; };
            ParameterBlock<Frame> gFrame;
            Texture2D gAlbedo;

            [shader("fragment")]
            float4 fragmentMain() : SV_Target
            {
                return mul(gFrame.view, gAlbedo.Load(0)) + gFrame.shadow.Sample(gFrame.shadowSampler, 0);
            }
            """;

        var (code, layout) = Compile(Source, "fragmentMain");
        var vulkan = layout.ToVulkan();

        // The module names them gFrame, gFrame.shadow, gFrame.shadowSampler; reflection reports
        // each by its declared leaf name, which is what SpirvVariable.LeafName strips to.
        var expected = SpirvDecorations.Descriptors(code);
        var actual = vulkan.DescriptorSets
            .SelectMany(s => s.Bindings.Select(b => (b.Name, Set: s.Set, b.Binding)))
            .ToDictionary(b => b.Name!, b => (b.Set, b.Binding));

        Assert.Equal(expected, actual);
        Assert.Equal(
            VulkanDescriptorType.UniformBuffer,
            vulkan.Set(1)!.Bindings.Single(b => b.Name == "gFrame").DescriptorType);
    }

    [NativeTheory]
    [InlineData("""
        struct Inner { Texture2D t; };
        struct Outer { float4 c; ParameterBlock<Inner> inner; Texture2D o; };
        ParameterBlock<Outer> gOuter;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        { return gOuter.c + gOuter.o.Load(0) + gOuter.inner.t.Load(0); }
        """)]
    [InlineData("""
        struct Leaf { Texture2D t; };
        struct Mid  { float2 pad; ParameterBlock<Leaf> leaf; };
        struct Outer { float4 c; Mid mid; Texture2D o; };
        ParameterBlock<Outer> gOuter;

        [shader("fragment")]
        float4 fragmentMain() : SV_Target
        { return gOuter.c + gOuter.o.Load(0) + gOuter.mid.leaf.t.Load(0) + gOuter.mid.pad.x; }
        """)]
    public void A_block_nested_inside_a_block_keeps_its_own_descriptor_set(string source)
    {
        // The bug: the walk found a top-level block's space and stopped, so a block declared inside
        // one — directly or through a plain struct — contributed no descriptor set at all. The
        // second case is the one that needs the space offset to accumulate through a struct that
        // owns no set itself.
        var (code, layout) = Compile(source, "fragmentMain");

        var expected = SpirvDecorations.Descriptors(code);
        var actual = layout.ToVulkan().DescriptorSets
            .SelectMany(s => s.Bindings.Select(b => (b.Name, Set: s.Set, b.Binding)))
            .ToDictionary(b => b.Name!, b => (b.Set, b.Binding));

        Assert.Equal(expected, actual);
        Assert.Equal(2, layout.ToVulkan().DescriptorSets.Count);
    }

    [NativeFact]
    public void An_entry_point_uniform_descriptor_is_still_named()
    {
        // Not a new feature — a guard. Names now come from the binding-range view, which does not
        // attribute entry-point uniforms; deleting the parameter-list scan as redundant would have
        // dropped this one silently, and the whole suite still passed while it was disabled.
        const string Source = """
            Texture2D albedo;
            SamplerState samp;

            [shader("fragment")]
            float4 fragmentMain(uniform Texture2D extra) : SV_Target
            { return albedo.Sample(samp, 0) + extra.Sample(samp, 0); }
            """;

        var (code, layout) = Compile(Source, "fragmentMain");

        var expected = SpirvDecorations.Descriptors(code);
        var actual = layout.ToVulkan().DescriptorSets
            .SelectMany(s => s.Bindings.Select(b => (b.Name, Set: s.Set, b.Binding)))
            .ToDictionary(b => b.Name!, b => (b.Set, b.Binding));

        Assert.Equal(expected, actual);
        Assert.Contains("extra", actual.Keys);
    }

    // ---------------------------------------------------------------- image formats

    [NativeFact]
    public void A_storage_images_declared_format_is_reported()
    {
        // Needed to know whether the device must have shaderStorageImageWriteWithoutFormat, and to
        // check the image view about to be bound agrees with the shader. [format(...)] is a
        // built-in attribute, so it never appeared in the user-attribute list either.
        const string Source = """
            struct Frame { float4x4 view; };
            ParameterBlock<Frame> gFrame;
            [format("rgba8")] RWTexture2D<float4> gFormatted;
            RWTexture2D<float4> gPlain;
            Texture2D gSampled;

            [shader("compute")]
            [numthreads(8, 8, 1)]
            void computeMain(uint2 id : SV_DispatchThreadID)
            {
                gFormatted[id] = gPlain[id] + gSampled.Load(0) + gFrame.view[0];
            }
            """;

        // The parameter block is here on purpose: getBindingRangeImageFormat access-violates on a
        // PARAMETER_BLOCK binding range, so this shader is what exercises the guard around it.
        var (code, layout) = Compile(Source, "computeMain");
        var bindings = layout.ToVulkan().Set(0)!.Bindings.ToDictionary(b => b.Name!);

        Assert.Equal(SlangImageFormat.SLANG_IMAGE_FORMAT_rgba8, bindings["gFormatted"].ImageFormat);
        Assert.Equal(SlangImageFormat.SLANG_IMAGE_FORMAT_unknown, bindings["gPlain"].ImageFormat);
        Assert.Equal(SlangImageFormat.SLANG_IMAGE_FORMAT_unknown, bindings["gSampled"].ImageFormat);

        // 4 is SPIR-V's Rgba8; the unformatted images are Unknown. Both are in the module.
        var formats = SpirvDecorations.ImageFormats(code);
        Assert.Contains(4u, formats);
        Assert.Contains(0u, formats);
    }
}
