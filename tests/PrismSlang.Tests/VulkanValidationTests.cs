// Building the pipeline the projection describes, and letting Khronos judge it.
//
// Everything else in this suite compares PrismSlang against something PrismSlang can see. This
// compares it against the validation layers: descriptor set layouts, a pipeline layout and a
// VkPipeline, built out of the projection and the SPIR-V it accompanies, on a real device with
// VK_LAYER_KHRONOS_validation loaded. The layers check the pipeline layout against the shader
// interface — VUID-VkGraphicsPipelineCreateInfo-layout-00756 and its neighbours — which is exactly
// the claim the projection makes and cannot check about itself.
//
// Skipped, with a reason, wherever there is no loader, no device or no layer. A CI runner without a
// GPU can supply all three through lavapipe.

using PrismSlang.Reflection;
using PrismSlang.Vulkan;
using Vortice.Vulkan;
using Native = PrismSlang.Native;

namespace PrismSlang.Tests;

public sealed unsafe class VulkanValidationTests : IDisposable
{
    private readonly VulkanTestDevice? _vulkan =
        VulkanTestDevice.Unavailable is null && SlangNative.Available ? new VulkanTestDevice() : null;

    public void Dispose() => _vulkan?.Dispose();

    private VulkanTestDevice Vulkan => _vulkan ?? throw new InvalidOperationException("no device");

    // ---------------------------------------------------------------- building from the projection

    /// <summary>Everything created for one pipeline, so it can all be torn down together.</summary>
    private sealed class Built(VkDeviceApi api) : IDisposable
    {
        public List<VkDescriptorSetLayout> SetLayouts { get; } = [];
        public List<VkShaderModule> Modules { get; } = [];
        public VkPipelineLayout Layout;
        public VkPipeline Pipeline;

        public void Dispose()
        {
            if (Pipeline.IsNotNull) api.vkDestroyPipeline(Pipeline);
            if (Layout.IsNotNull) api.vkDestroyPipelineLayout(Layout);
            foreach (var m in Modules) api.vkDestroyShaderModule(m);
            foreach (var s in SetLayouts) api.vkDestroyDescriptorSetLayout(s);
        }
    }

    /// <summary>
    /// Descriptor set layouts and a pipeline layout, taken straight from the projection.
    /// </summary>
    /// <remarks>
    /// Nothing is adjusted on the way through — the point is that what PrismSlang reports is what
    /// gets built. The one place a decision is made is an unbounded binding, where the shader says
    /// nothing about the count and the host has to choose; the projection says so with
    /// <c>IsUnbounded</c> and this picks a cap, exactly as documented on <c>VulkanBinding</c>.
    /// </remarks>
    private Built BuildLayout(VulkanPipelineLayout layout)
    {
        var built = new Built(Vulkan.Api);

        foreach (var set in layout.DescriptorSets)
        {
            var bindings = new VkDescriptorSetLayoutBinding[set.Bindings.Count];
            var flags = new VkDescriptorBindingFlags[set.Bindings.Count];

            for (var i = 0; i < set.Bindings.Count; i++)
            {
                var b = set.Bindings[i];
                bindings[i] = new VkDescriptorSetLayoutBinding
                {
                    binding = b.Binding,
                    descriptorType = (VkDescriptorType)b.DescriptorType,
                    descriptorCount = b.IsUnbounded ? 64u : b.DescriptorCount,
                    stageFlags = (VkShaderStageFlags)b.StageFlags,
                };
                flags[i] = b.IsUnbounded
                    ? VkDescriptorBindingFlags.VariableDescriptorCount
                      | VkDescriptorBindingFlags.PartiallyBound
                    : VkDescriptorBindingFlags.None;
            }

            fixed (VkDescriptorSetLayoutBinding* pBindings = bindings)
            fixed (VkDescriptorBindingFlags* pFlags = flags)
            {
                var flagsInfo = new VkDescriptorSetLayoutBindingFlagsCreateInfo
                {
                    bindingCount = (uint)flags.Length,
                    pBindingFlags = pFlags,
                };

                var info = new VkDescriptorSetLayoutCreateInfo
                {
                    pNext = &flagsInfo,
                    bindingCount = (uint)bindings.Length,
                    pBindings = pBindings,
                };

                Check(Vulkan.Api.vkCreateDescriptorSetLayout(&info, out VkDescriptorSetLayout handle));
                built.SetLayouts.Add(handle);
            }
        }

        var ranges = layout.PushConstants
            .Select(r => new VkPushConstantRange
            {
                stageFlags = (VkShaderStageFlags)r.StageFlags,
                offset = r.Offset,
                size = r.Size,
            })
            .ToArray();

        var setHandles = built.SetLayouts.ToArray();
        fixed (VkDescriptorSetLayout* pSets = setHandles)
        fixed (VkPushConstantRange* pRanges = ranges)
        {
            var info = new VkPipelineLayoutCreateInfo
            {
                setLayoutCount = (uint)setHandles.Length,
                pSetLayouts = pSets,
                pushConstantRangeCount = (uint)ranges.Length,
                pPushConstantRanges = pRanges,
            };

            Check(Vulkan.Api.vkCreatePipelineLayout(&info, out built.Layout));
        }

        return built;
    }

    private VkShaderModule CreateModule(byte[] spirv)
    {
        Check(Vulkan.Api.vkCreateShaderModule(spirv.AsSpan(), null, out VkShaderModule module));
        return module;
    }

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
        // and a matrix vertex attribute, all described by PrismSlang and all handed to the layers
        // unmodified. Anything the projection got wrong about the shader interface is a VUID here.
        var (vertexCode, fragmentCode, layout) = CompileGraphics(Material);

        using var capture = VulkanTestDevice.Collect();
        using var built = BuildLayout(layout);

        built.Modules.Add(CreateModule(vertexCode));
        built.Modules.Add(CreateModule(fragmentCode));

        CreateGraphicsPipeline(built, layout);

        Assert.True(capture.Errors.Count == 0,
            $"the validation layers rejected a pipeline built from PrismSlang's own projection:\n"
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

        var entryName = "main"u8;
        fixed (byte* pName = entryName)
        {
            var stage = new VkPipelineShaderStageCreateInfo
            {
                stage = VkShaderStageFlags.Compute,
                module = built.Modules[0],
                pName = pName,
            };

            var info = new VkComputePipelineCreateInfo
            {
                stage = stage,
                layout = built.Layout,
            };

            fixed (VkPipeline* pPipeline = &built.Pipeline)
            {
                Vulkan.Api.vkCreateComputePipelines(VkPipelineCache.Null, 1, &info, pPipeline);
            }
        }

        Assert.True(capture.Errors.Count == 0,
            $"the validation layers rejected a compute pipeline built from the projection:\n"
            + capture.Report());

        // And the dispatch geometry the projection reports is what the shader declared.
        Assert.Equal(
            new VulkanWorkgroupSize(8, 8, 1),
            layout.Stage("computeMain")!.WorkgroupSize);
    }

    // ---------------------------------------------------------------- the open question

    [VulkanValidationFact]
    public void A_wide_64_bit_vertex_attribute_really_does_consume_two_locations()
    {
        // §9.14 left this open, and it is the reason for building any of this. Vulkan gives a
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

        // The answer, and it settles §9.14: the layers object, spirv-val names the same location
        // RequireDistinctLocations names, and the reading of the specification was right.
        var complaints = capture.Messages
            .Where(m => m.Text.Contains("ocation", StringComparison.Ordinal))
            .ToList();

        Assert.True(complaints.Count > 0,
            "the validation layers accepted a double4 vertex attribute immediately followed by "
            + "another at the next location. If they are right, the two-location rule does not "
            + "apply to vertex inputs and VulkanProjection.RequireDistinctLocations refuses a legal "
            + "shader — remove it and update §9.14. Everything the layers did say:\n"
            + capture.Report());

        // Matched exactly, so this cannot pass on some unrelated message that happens to contain
        // the word.
        Assert.Contains(complaints,
            m => m.Id == "VUID-StandaloneSpirv-OpEntryPoint-08721"
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

    private void CreateGraphicsPipeline(Built built, VulkanPipelineLayout layout)
    {
        var attributes = layout.VertexInputs
            .Select((v, i) => new VkVertexInputAttributeDescription
            {
                location = v.Location,
                binding = 0,
                format = (VkFormat)v.Format,
                offset = (uint)(i * 16),
            })
            .ToArray();

        CreateGraphicsPipeline(built, attributes);
    }

    /// <summary>
    /// A pipeline with everything dynamic that can be, so nothing here is under test but the
    /// layout and the shader interface.
    /// </summary>
    private void CreateGraphicsPipeline(Built built, VkVertexInputAttributeDescription[] attributes)
    {
        var entryName = "main"u8;

        fixed (byte* pName = entryName)
        fixed (VkVertexInputAttributeDescription* pAttributes = attributes)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo
            {
                stage = VkShaderStageFlags.Vertex, module = built.Modules[0], pName = pName,
            };
            stages[1] = new VkPipelineShaderStageCreateInfo
            {
                stage = VkShaderStageFlags.Fragment, module = built.Modules[1], pName = pName,
            };

            var binding = new VkVertexInputBindingDescription
            {
                binding = 0, stride = 128, inputRate = VkVertexInputRate.Vertex,
            };

            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = attributes.Length == 0 ? 0u : 1u,
                pVertexBindingDescriptions = attributes.Length == 0 ? null : &binding,
                vertexAttributeDescriptionCount = (uint)attributes.Length,
                pVertexAttributeDescriptions = pAttributes,
            };

            var assembly = new VkPipelineInputAssemblyStateCreateInfo
            {
                topology = VkPrimitiveTopology.TriangleList,
            };
            var viewport = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            var raster = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1.0f,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo
            {
                rasterizationSamples = VkSampleCountFlags.Count1,
            };
            var blendAttachment = new VkPipelineColorBlendAttachmentState
            {
                colorWriteMask = VkColorComponentFlags.All,
            };
            var blend = new VkPipelineColorBlendStateCreateInfo
            {
                attachmentCount = 1, pAttachments = &blendAttachment,
            };

            var dynamicStates = stackalloc VkDynamicState[2]
            {
                VkDynamicState.Viewport, VkDynamicState.Scissor,
            };
            var dynamic = new VkPipelineDynamicStateCreateInfo
            {
                dynamicStateCount = 2, pDynamicStates = dynamicStates,
            };

            // Dynamic rendering rather than a render pass: fewer objects, and nothing about a
            // render pass is what this is testing.
            var colourFormat = VkFormat.R8G8B8A8Unorm;
            var rendering = new VkPipelineRenderingCreateInfo
            {
                colorAttachmentCount = 1, pColorAttachmentFormats = &colourFormat,
            };

            var info = new VkGraphicsPipelineCreateInfo
            {
                pNext = &rendering,
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &assembly,
                pViewportState = &viewport,
                pRasterizationState = &raster,
                pMultisampleState = &multisample,
                pColorBlendState = &blend,
                pDynamicState = &dynamic,
                layout = built.Layout,
            };

            fixed (VkPipeline* pPipeline = &built.Pipeline)
            {
                Vulkan.Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &info, pPipeline);
            }
        }
    }

    private static void Check(VkResult result)
    {
        if (result != VkResult.Success) throw new InvalidOperationException(result.ToString());
    }
}
