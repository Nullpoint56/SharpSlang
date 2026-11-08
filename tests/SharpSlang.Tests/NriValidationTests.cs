// Three shaders, each asked one question, and the Khronos validation layer answers.
//
// NriDeviceTests asks the generic question of the whole corpus: does a pipeline built from the
// projection pass validation. These stay hand-written because each asks one specific thing of one
// shader: a graphics pipeline with everything a material binds (descriptor sets, a push-constant
// block, a bindless array and a matrix vertex attribute), a compute pipeline with buffers, a
// constant buffer and a formatted storage image, and the one place the projection refuses a shader
// on the strength of the specification rather than a measurement. Each is built on SharpNRI's
// validated Vulkan device, where NRI's own validation and the Khronos layer record every message,
// and the layer checks the pipeline layout against the shader interface
// (VUID-VkGraphicsPipelineCreateInfo-layout-07988 and its neighbours) — exactly the claim the
// projection makes and cannot check about itself.

using SharpNRI;
using SharpNRI.Native;
using SharpNRI.Testing;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public sealed class NriValidationTests(Xunit.Abstractions.ITestOutputHelper output)
{
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

    /// <summary>The bindless array's cap, which the shader cannot state and NRI must be told.</summary>
    private static readonly NriProjectionOptions Capped = new() { UnboundedArrayCap = 64 };

    [NriVulkanFact]
    public void A_graphics_pipeline_built_from_the_projection_passes_validation()
    {
        // The whole exercise in one test: descriptor sets, a push-constant block, a bindless array
        // and a matrix vertex attribute, all described by SharpSlang and all handed to NRI
        // unmodified. Anything the projection got wrong about the shader interface is a VUID here.
        var compiled = NriDeviceTests.Compile("v", Material, ["vertexMain", "fragmentMain"], new TargetOptions());
        var projection = compiled.Layout.ToNri(Capped);

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        using var layout = fixture.Device.CreatePipelineLayout(projection.Layout);
        using var pipeline = fixture.Device.CreateGraphicsPipeline(new NriGraphicsPipelineDesc
        {
            PipelineLayout = layout,
            VertexInput = NriDeviceTests.VertexInput(projection),
            InputAssembly = new InputAssemblyDesc { topology = Topology.TRIANGLE_LIST },
            Rasterization = new RasterizationDesc { fillMode = FillMode.SOLID, cullMode = CullMode.NONE },
            OutputMerger = new NriOutputMergerDesc { Colors = NriDeviceTests.ColourAttachments(projection) },
            Shaders =
            [
                NriDeviceTests.SpirvShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0]),
                NriDeviceTests.SpirvShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1]),
            ],
        });

        RequireNoErrors(fixture, "a graphics pipeline built from SharpSlang's own projection");
    }

    [NriVulkanFact]
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

        var compiled = NriDeviceTests.Compile("c", Source, ["computeMain"], new TargetOptions());
        var projection = compiled.Layout.ToNri();

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        using var layout = fixture.Device.CreatePipelineLayout(projection.Layout);
        using var pipeline = fixture.Device.CreateComputePipeline(new NriComputePipelineDesc
        {
            PipelineLayout = layout,
            Shader = NriDeviceTests.SpirvShaderDesc(StageBits.COMPUTE_SHADER, compiled.Code[0]),
        });

        RequireNoErrors(fixture, "a compute pipeline built from the projection");

        // And the dispatch geometry the projection reports is what the shader declared.
        Assert.Equal(new ThreadGroupSize(8, 8, 1), projection.Stage("computeMain")!.ThreadGroupSize);
    }

    // ---------------------------------------------------------------- the open question

    [NriVulkanFact]
    public void A_wide_64_bit_vertex_attribute_really_does_consume_two_locations()
    {
        // Vulkan gives a three- or four-component 64-bit vector two consecutive locations; Slang
        // assigns it one and decorates the module to match, so reflection and the module agree
        // with each other and disagree with the specification as read here. PipelineInterface
        // refuses the overlap on the strength of that reading (RequireDistinctLocations' rule, and
        // NRI's Format has no 64-bit type to describe the attribute with in any case), which was
        // the one assertion taken from a document rather than from a measurement.
        //
        // So: hand the layer the module Slang's numbering produced and see whether it objects.
        // The projection refuses to describe this vertex input, which is the thing on trial, so
        // the pipeline layout is empty and no attribute is described; the question is about the
        // module, which the layer checks when the pipeline's shader stages are created.
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

        var compiled = NriDeviceTests.Compile("w", Source, ["vertexMain", "fragmentMain"], new TargetOptions());

        // Slang's own numbering, read out of the emitted module rather than from the projection —
        // the projection refuses to produce it.
        var locations = SpirvDecorations.InputLocations(compiled.Code[0]);
        Assert.Equal(1u, locations["wide"]);
        Assert.Equal(2u, locations["after"]);
        Assert.Throws<NotSupportedException>(() => compiled.Layout.ToNri());

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        using var layout = fixture.Device.CreatePipelineLayout(new NriPipelineLayoutDesc());
        using var pipeline = fixture.Device.CreateGraphicsPipeline(new NriGraphicsPipelineDesc
        {
            PipelineLayout = layout,
            InputAssembly = new InputAssemblyDesc { topology = Topology.TRIANGLE_LIST },
            Rasterization = new RasterizationDesc { fillMode = FillMode.SOLID, cullMode = CullMode.NONE },
            OutputMerger = new NriOutputMergerDesc { Colors = [new ColorAttachmentDesc { format = Format.RGBA8_UNORM, colorWriteMask = ColorWriteBits.RGBA }] },
            Shaders =
            [
                NriDeviceTests.SpirvShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0]),
                NriDeviceTests.SpirvShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1]),
            ],
        });

        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());

        // The answer: the layer objects, spirv-val names the same location
        // RequireDistinctLocations names, and the reading of the specification was right.
        // Matched on the VUID and the location, so this cannot pass on some unrelated message that
        // happens to contain the word.
        Assert.Contains(fixture.Errors, m =>
            m.Text.Contains("OpEntryPoint-08721", StringComparison.Ordinal)
            && m.Text.Contains("conflicting input location assignment at location 2", StringComparison.Ordinal));
    }

    private void RequireNoErrors(NriTestDevice fixture, string what)
    {
        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());

        var errors = fixture.Errors.ToList();
        Assert.True(errors.Count == 0,
            $"NRI or the Vulkan validation layers rejected {what}:\n"
            + string.Join("\n", fixture.Messages.Select(m => "  " + m)));
    }
}
