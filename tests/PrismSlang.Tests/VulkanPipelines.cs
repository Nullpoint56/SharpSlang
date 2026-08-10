// Turning a projection into real Vulkan objects, so the layers can judge it.
//
// Extracted from VulkanValidationTests when the corpus driver arrived and needed the same thing.
// Duplicating it would have been the smaller change and the worse one: two builders drift, and the
// older one keeps passing while meaning less every month.
//
// Nothing here adjusts what the projection says. The only decisions this makes are the ones a host
// must make and a shader cannot express — how many descriptors an unbounded array gets, which
// buffer a vertex attribute is read from, what format the colour attachments are — and each of
// those is marked where it happens.

using PrismSlang.Vulkan;
using Vortice.Vulkan;

namespace PrismSlang.Tests;

/// <summary>One shader stage: the module, and which stage it is.</summary>
public readonly record struct StageModule(VkShaderStageFlags Stage, VkShaderModule Module, string EntryPoint);

/// <summary>Builds Vulkan objects out of a <see cref="VulkanPipelineLayout"/>.</summary>
public sealed unsafe class VulkanPipelines(VulkanTestDevice device)
{
    /// <summary>Everything created for one pipeline, so it can all be torn down together.</summary>
    public sealed class Built(VkDeviceApi api) : IDisposable
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

    private VkDeviceApi Api => device.Api;

    /// <summary>
    /// Descriptor set layouts and a pipeline layout, taken straight from the projection.
    /// </summary>
    /// <remarks>
    /// Nothing is adjusted on the way through — the point is that what PrismSlang reports is what
    /// gets built. The one place a decision is made is an unbounded binding, where the shader says
    /// nothing about the count and the host has to choose; the projection says so with
    /// <c>IsUnbounded</c> and this picks a cap, exactly as documented on <c>VulkanBinding</c>.
    /// </remarks>
    public Built BuildLayout(VulkanPipelineLayout layout)
    {
        var built = new Built(Api);

        // pSetLayouts is indexed by set number, so a shader using sets 0 and 2 needs three entries
        // with an empty one in the middle. Packing them would silently move set 2's bindings to
        // set 1 and the pipeline would still be created — this harness lying about the projection
        // rather than the projection being wrong. explicit-spaces and parameter-blocks in the
        // corpus both produce gaps.
        var bySet = layout.DescriptorSets.ToDictionary(s => s.Set);
        var highest = bySet.Count == 0 ? -1 : (int)bySet.Keys.Max();

        for (var index = 0u; index <= highest; index++)
        {
            if (!bySet.TryGetValue(index, out var set))
            {
                var empty = new VkDescriptorSetLayoutCreateInfo();
                Check(Api.vkCreateDescriptorSetLayout(&empty, out VkDescriptorSetLayout hole));
                built.SetLayouts.Add(hole);
                continue;
            }

            var bindings = new VkDescriptorSetLayoutBinding[set.Bindings.Count];
            var flags = new VkDescriptorBindingFlags[set.Bindings.Count];

            // VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT is only legal on the
            // highest-numbered binding in a set (VUID-…-pBindingFlags-03004). Slang does not
            // arrange for the unbounded array to be last: descriptor-arrays puts `daUnbounded` at
            // binding 1 with a sampler at binding 2, so a host that reads IsUnbounded and reaches
            // for variable count is refused by vkCreateDescriptorSetLayout. See §9.18 — the
            // projection is right about the shader and the host still has to make this choice.
            var lastBinding = set.Bindings.Max(b => b.Binding);

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

                // PartiallyBound alone is legal at any index and is what an unbounded binding that
                // is not last has to settle for: a fixed cap, with holes allowed.
                flags[i] = b.IsUnbounded
                    ? b.Binding == lastBinding
                        ? VkDescriptorBindingFlags.VariableDescriptorCount
                          | VkDescriptorBindingFlags.PartiallyBound
                        : VkDescriptorBindingFlags.PartiallyBound
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

                Check(Api.vkCreateDescriptorSetLayout(&info, out VkDescriptorSetLayout handle));
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

            Check(Api.vkCreatePipelineLayout(&info, out built.Layout));
        }

        return built;
    }

    public VkShaderModule CreateModule(byte[] spirv)
    {
        Check(Api.vkCreateShaderModule(spirv.AsSpan(), null, out VkShaderModule module));
        return module;
    }

    /// <summary>
    /// A graphics pipeline with everything dynamic that can be, so nothing is under test but the
    /// pipeline layout and the shader interface.
    /// </summary>
    /// <param name="built">Holds the pipeline layout, and receives the pipeline.</param>
    /// <param name="stages">One per shader stage, in any order.</param>
    /// <param name="attributes">Vertex attributes; may be empty.</param>
    /// <param name="colourFormats">
    /// One per colour attachment. A gap in the fragment shader's output locations is
    /// <c>VK_FORMAT_UNDEFINED</c> here, which is how dynamic rendering spells "nothing bound".
    /// </param>
    /// <param name="patchControlPoints">
    /// Non-zero adds tessellation state and switches the topology to a patch list, which a pipeline
    /// with tessellation stages requires and one without must not have.
    /// </param>
    public void CreateGraphicsPipeline(
        Built built,
        IReadOnlyList<StageModule> stages,
        VkVertexInputAttributeDescription[] attributes,
        VkFormat[] colourFormats,
        uint patchControlPoints = 0)
    {
        // One binding description per distinct binding the attributes reference — derived from the
        // attributes rather than invented, so a caller that puts everything in binding 0 with real
        // offsets and a caller that gives each attribute its own both work. Interleaving is a
        // renderer decision the projection deliberately does not make (see VulkanVertexInput), so
        // the stride is simply generous.
        var bindings = attributes
            .Select(a => a.binding)
            .Distinct()
            .Order()
            .Select(b => new VkVertexInputBindingDescription
            {
                binding = b, stride = 128, inputRate = VkVertexInputRate.Vertex,
            })
            .ToArray();

        // Names have to outlive the fixed block below, and each stage's pName must point at its own.
        var names = stages
            .Select(s => System.Text.Encoding.UTF8.GetBytes(s.EntryPoint + "\0"))
            .ToArray();
        var handles = names.Select(n => System.Runtime.InteropServices.GCHandle.Alloc(
            n, System.Runtime.InteropServices.GCHandleType.Pinned)).ToArray();

        try
        {
            fixed (VkVertexInputAttributeDescription* pAttributes = attributes)
            fixed (VkVertexInputBindingDescription* pBindings = bindings)
            fixed (VkFormat* pColourFormats = colourFormats)
            {
                var stageInfos = stackalloc VkPipelineShaderStageCreateInfo[stages.Count];
                for (var i = 0; i < stages.Count; i++)
                {
                    stageInfos[i] = new VkPipelineShaderStageCreateInfo
                    {
                        stage = stages[i].Stage,
                        module = stages[i].Module,
                        pName = (byte*)handles[i].AddrOfPinnedObject(),
                    };
                }

                var vertexInput = new VkPipelineVertexInputStateCreateInfo
                {
                    vertexBindingDescriptionCount = (uint)bindings.Length,
                    pVertexBindingDescriptions = pBindings,
                    vertexAttributeDescriptionCount = (uint)attributes.Length,
                    pVertexAttributeDescriptions = pAttributes,
                };

                var assembly = new VkPipelineInputAssemblyStateCreateInfo
                {
                    topology = patchControlPoints > 0
                        ? VkPrimitiveTopology.PatchList
                        : VkPrimitiveTopology.TriangleList,
                };
                var tessellation = new VkPipelineTessellationStateCreateInfo
                {
                    patchControlPoints = patchControlPoints,
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

                var blendAttachments = colourFormats
                    .Select(_ => new VkPipelineColorBlendAttachmentState
                    {
                        colorWriteMask = VkColorComponentFlags.All,
                    })
                    .ToArray();

                fixed (VkPipelineColorBlendAttachmentState* pBlend = blendAttachments)
                {
                    var blend = new VkPipelineColorBlendStateCreateInfo
                    {
                        attachmentCount = (uint)blendAttachments.Length,
                        pAttachments = pBlend,
                    };

                    var dynamicStates = stackalloc VkDynamicState[2]
                    {
                        VkDynamicState.Viewport, VkDynamicState.Scissor,
                    };
                    var dynamic = new VkPipelineDynamicStateCreateInfo
                    {
                        dynamicStateCount = 2, pDynamicStates = dynamicStates,
                    };

                    // Dynamic rendering rather than a render pass: fewer objects, and nothing about
                    // a render pass is what this is testing.
                    var rendering = new VkPipelineRenderingCreateInfo
                    {
                        colorAttachmentCount = (uint)colourFormats.Length,
                        pColorAttachmentFormats = pColourFormats,
                    };

                    var info = new VkGraphicsPipelineCreateInfo
                    {
                        pNext = &rendering,
                        stageCount = (uint)stages.Count,
                        pStages = stageInfos,
                        pVertexInputState = &vertexInput,
                        pInputAssemblyState = &assembly,
                        pTessellationState = patchControlPoints > 0 ? &tessellation : null,
                        pViewportState = &viewport,
                        pRasterizationState = &raster,
                        pMultisampleState = &multisample,
                        pColorBlendState = &blend,
                        pDynamicState = &dynamic,
                        layout = built.Layout,
                    };

                    fixed (VkPipeline* pPipeline = &built.Pipeline)
                    {
                        Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &info, pPipeline);
                    }
                }
            }
        }
        finally
        {
            foreach (var h in handles) h.Free();
        }
    }

    public void CreateComputePipeline(Built built, StageModule stage)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(stage.EntryPoint + "\0");

        fixed (byte* pName = name)
        {
            var info = new VkComputePipelineCreateInfo
            {
                stage = new VkPipelineShaderStageCreateInfo
                {
                    stage = VkShaderStageFlags.Compute,
                    module = stage.Module,
                    pName = pName,
                },
                layout = built.Layout,
            };

            fixed (VkPipeline* pPipeline = &built.Pipeline)
            {
                Api.vkCreateComputePipelines(VkPipelineCache.Null, 1, &info, pPipeline);
            }
        }
    }

    private static void Check(VkResult result)
    {
        if (result != VkResult.Success) throw new InvalidOperationException(result.ToString());
    }
}
