// Building the pipeline the projection describes, and letting Khronos judge it.
//
// Everything else in this suite compares SharpSlang against something SharpSlang can see. This
// compares it against the validation layers: descriptor set layouts, a pipeline layout and a
// VkPipeline, built out of the projection and the SPIR-V it accompanies, on a real device with
// VK_LAYER_KHRONOS_validation loaded. The layers check the pipeline layout against the shader
// interface — VUID-VkGraphicsPipelineCreateInfo-layout-00756 and its neighbours — which is exactly
// the claim the projection makes and cannot check about itself.
//
// Skipped, with a reason, wherever there is no loader, no device or no layer. A CI runner without a
// GPU can supply all three through lavapipe.

using SharpSlang.Reflection;
using SharpSlang.Vulkan;
using Vortice.Vulkan;
using Native = SharpSlang.Native;

namespace SharpSlang.Tests;

public sealed unsafe class VulkanValidationTests : IDisposable
{
    private readonly VulkanTestDevice? _vulkan =
        VulkanTestDevice.Unavailable is null && SlangNative.Available ? new VulkanTestDevice() : null;

    private VulkanPipelines? _pipelines;

    public void Dispose() => _vulkan?.Dispose();

    private VulkanTestDevice Vulkan => _vulkan ?? throw new InvalidOperationException("no device");

    // The Vulkan object-building lives in VulkanPipelines, shared with the corpus driver. These
    // three cases stay hand-written because each asks one specific question of one shader; the
    // driver asks the same generic question of all 28.
    private VulkanPipelines Pipelines => _pipelines ??= new VulkanPipelines(Vulkan);

    private VulkanPipelines.Built BuildLayout(VulkanPipelineLayout layout) => Pipelines.BuildLayout(layout);

    private VkShaderModule CreateModule(byte[] spirv) => Pipelines.CreateModule(spirv);

    // ---------------------------------------------------------------- the tests

    private (byte[] Vertex, byte[] Fragment, VulkanPipelineLayout Layout) CompileGraphics(string source)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("v", "v.slang", source);
        using var vertex = module.FindEntryPoint("vertexMain");
        using var fragment = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vertex, fragment);

        var layout = program.Reflect();
        return (program.GetEntryPointCodeBytes(0), program.GetEntryPointCodeBytes(1), layout.ToVulkan());
    }

    private const string Material = """
        struct Material { float3 tint; float gloss; float4x4 transform; float3x3 normalMatrix; };
        // `bindlessIndex` is a push constant rather than a fragment input on purpose: an input the
        // vertex stage does not write is VUID-RuntimeSpirv-OpEntryPoint-08743, which the layers
        // caught in the first draft of this shader.
        struct Push     { float4 overrideTint; uint bindlessIndex; };

        ConstantBuffer<Material> gMaterial;
        Texture2D                gAlbedo;
        SamplerState             gSampler;
        Texture2D                gBindless[];
        [[vk::push_constant]] ConstantBuffer<Push> gPush;

        struct VIn  { float3 position : POSITION; float2 uv : TEXCOORD0; float4x4 instance : INSTANCE; };
        struct VOut { float4 clip : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VOut vertexMain(VIn v)
        {
            VOut o;
            o.clip = mul(gMaterial.transform, mul(v.instance, float4(v.position, 1)));
            o.uv = v.uv;
            return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VOut i) : SV_Target
        {
            float3 n = mul(gMaterial.normalMatrix, float3(0, 0, 1));
            return gAlbedo.Sample(gSampler, i.uv) * float4(gMaterial.tint * n, gMaterial.gloss)
                 + gBindless[gPush.bindlessIndex].Sample(gSampler, i.uv) + gPush.overrideTint;
        }
        """;

    [VulkanValidationFact]
    public void A_graphics_pipeline_built_from_the_projection_passes_validation()
    {
        // The whole exercise in one test: descriptor sets, a push constant range, a bindless array
        // and a matrix vertex attribute, all described by SharpSlang and all handed to the layers
        // unmodified. Anything the projection got wrong about the shader interface is a VUID here.
        var (vertexCode, fragmentCode, layout) = CompileGraphics(Material);

        using var capture = VulkanTestDevice.Collect();
        using var built = BuildLayout(layout);

        built.Modules.Add(CreateModule(vertexCode));
        built.Modules.Add(CreateModule(fragmentCode));

        CreateGraphicsPipeline(built, layout);

        Assert.True(capture.Errors.Count == 0,
            $"the validation layers rejected a pipeline built from SharpSlang's own projection:\n"
            + capture.Report());
    }

    [VulkanValidationFact]
    public void A_compute_pipeline_built_from_the_projection_passes_validation()
    {
        const string Source = """
            struct Params { uint4 bounds; float scale; };
            ConstantBuffer<Params>      gParams;
            RWStructuredBuffer<float4>  gOutput;
            StructuredBuffer<float4>    gInput;
            [format("rgba8")] RWTexture2D<float4> gImage;

            [shader("compute")]
            [numthreads(8, 8, 1)]
            void computeMain(uint3 id : SV_DispatchThreadID)
            {
                gOutput[id.x] = gInput[id.x] * gParams.scale;
                gImage[id.xy] = gInput[id.x];
            }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("c", "c.slang", Source);
        using var entry = module.FindEntryPoint("computeMain");
        using var program = session.Link(module, entry);

        var reflection = program.Reflect();
        var layout = reflection.ToVulkan();
        var code = program.GetEntryPointCodeBytes(0);

        using var capture = VulkanTestDevice.Collect();
        using var built = BuildLayout(layout);
        built.Modules.Add(CreateModule(code));

        Pipelines.CreateComputePipeline(
            built, new StageModule(VkShaderStageFlags.Compute, built.Modules[0], "main"));

        Assert.True(capture.Errors.Count == 0,
            $"the validation layers rejected a compute pipeline built from the projection:\n"
            + capture.Report());

        // And the dispatch geometry the projection reports is what the shader declared.
        Assert.Equal(
            new VulkanWorkgroupSize(8, 8, 1),
            layout.Stage("computeMain")!.WorkgroupSize);
    }

    // ---------------------------------------------------------------- the open question

    [Wide64VertexFact]
    public void A_wide_64_bit_vertex_attribute_really_does_consume_two_locations()
    {
        //  left this open, and it is the reason for building any of this. Vulkan gives a
        // three- or four-component 64-bit vector two consecutive locations; Slang assigns it one and
        // decorates the module to match, so reflection and the module agree with each other and
        // disagree with the specification as read here. RequireDistinctLocations refuses the
        // overlap on the strength of that reading, which was the one assertion in the projection
        // taken from a document rather than from a measurement.
        //
        // So: build the pipeline Slang's numbering describes — the overlapping one, bypassing the
        // refusal — and see whether the layers object.
        const string Source = """
            struct VIn
            {
                float3  position : POSITION;
                double4 wide     : TEXCOORD0;
                float2  after    : TEXCOORD1;
            };

            [shader("vertex")]
            float4 vertexMain(VIn v) : SV_Position
            { return float4(v.position, 1) + (float)v.wide.x + float4(v.after, 0, 0); }

            [shader("fragment")]
            float4 fragmentMain() : SV_Target { return 1; }
            """;

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("w", "w.slang", Source);
        using var vertex = module.FindEntryPoint("vertexMain");
        using var fragment = module.FindEntryPoint("fragmentMain");
        using var program = session.Link(module, vertex, fragment);

        var vertexCode = program.GetEntryPointCodeBytes(0);
        var fragmentCode = program.GetEntryPointCodeBytes(1);

        // Slang's own numbering, read out of the emitted module rather than from the projection —
        // the projection refuses to produce it, which is the thing under test.
        var locations = SpirvDecorations.InputLocations(vertexCode);
        Assert.Equal(1u, locations["wide"]);
        Assert.Equal(2u, locations["after"]);

        var attributes = new[]
        {
            new VkVertexInputAttributeDescription
            {
                location = locations["position"], binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0,
            },
            new VkVertexInputAttributeDescription
            {
                location = locations["wide"], binding = 0, format = VkFormat.R64G64B64A64Sfloat, offset = 16,
            },
            new VkVertexInputAttributeDescription
            {
                location = locations["after"], binding = 0, format = VkFormat.R32G32Sfloat, offset = 48,
            },
        };

        // An empty pipeline layout, built directly rather than projected: the shader binds nothing,
        // and ToVulkan is what refuses this vertex layout, which is the thing being put on trial.
        using var capture = VulkanTestDevice.Collect();
        using var built = BuildLayout(new VulkanPipelineLayout
        {
            DescriptorSets = [],
            PushConstants = [],
            VertexInputs = [],
        });

        built.Modules.Add(CreateModule(vertexCode));
        built.Modules.Add(CreateModule(fragmentCode));

        CreateGraphicsPipeline(built, attributes);

        // The answer, and it settles : the layers object, spirv-val names the same location
        // RequireDistinctLocations names, and the reading of the specification was right.
        var complaints = capture.Messages
            .Where(m => m.Text.Contains("ocation", StringComparison.Ordinal))
            .ToList();

        Assert.True(complaints.Count > 0,
            "the validation layers accepted a double4 vertex attribute immediately followed by "
            + "another at the next location. If they are right, the two-location rule does not "
            + "apply to vertex inputs and VulkanProjection.RequireDistinctLocations refuses a legal "
            + "shader — remove it and update . Everything the layers did say:\n"
            + capture.Report());

        // Matched on the VUID and the location, so this cannot pass on some unrelated message that
        // happens to contain the word.
        //
        // Searched in the id *or* the text, because the two implementations this ran on disagree
        // about which one carries it. The SDK layers on a discrete GPU report the message under
        // VUID-StandaloneSpirv-OpEntryPoint-08721; the layers on lavapipe report it under
        // VUID-VkShaderModuleCreateInfo-pCode-08737 and quote the spirv-val VUID inside the text.
        // Same finding, same wording for the part that matters.
        Assert.Contains(complaints,
            m => (m.Id.Contains("OpEntryPoint-08721", StringComparison.Ordinal)
                  || m.Text.Contains("OpEntryPoint-08721", StringComparison.Ordinal))
              && m.Text.Contains("conflicting input location assignment at location 2",
                     StringComparison.Ordinal));

        // And it is the *module* that is invalid, not the pipeline built around it: the complaint
        // comes from vkCreateShaderModule, before any layout is involved. Slang emitted SPIR-V that
        // does not satisfy the specification, and refusing it at cook time is the earliest anything
        // could.
        Assert.Contains(complaints,
            m => m.Text.Contains("vkCreateShaderModule", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- pipeline plumbing

    private void CreateGraphicsPipeline(VulkanPipelines.Built built, VulkanPipelineLayout layout) =>
        CreateGraphicsPipeline(built, layout.VertexInputs
            .Select(v => new VkVertexInputAttributeDescription
            {
                location = v.Location,
                binding = v.Location,
                format = (VkFormat)v.Format,
                offset = 0,
            })
            .ToArray());

    /// <summary>Vertex and fragment, in that order, writing one colour attachment.</summary>
    private void CreateGraphicsPipeline(
        VulkanPipelines.Built built, VkVertexInputAttributeDescription[] attributes) =>
        Pipelines.CreateGraphicsPipeline(
            built,
            [
                new StageModule(VkShaderStageFlags.Vertex, built.Modules[0], "main"),
                new StageModule(VkShaderStageFlags.Fragment, built.Modules[1], "main"),
            ],
            attributes,
            [VkFormat.R8G8B8A8Unorm]);
}
