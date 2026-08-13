// Actually running a shader.
//
// Every Vulkan oracle in this suite so far stops at pipeline creation. That is a real check — the
// validation layers compare the descriptor set layouts against the SPIR-V and refuse a mismatch —
// but it is a check on *shape*. It cannot see an offset. If reflection says a struct member is at
// byte 16 and it is really at 32, the pipeline is created without complaint, the layers say
// nothing, and the shader reads the wrong bytes.
//
// The only thing that catches that is running the shader and looking at what came out. So: fill
// buffers at the offsets PrismSlang's reflection reports, dispatch, read back, and compare with
// numbers the Slang team wrote down. A wrong offset is wrong output.
//
// Host-visible and host-coherent memory throughout, no staging, no barriers beyond the one the
// submit implies. A renderer would not upload this way; nothing here is measuring upload.

using Vortice.Vulkan;

namespace PrismSlang.Tests;

/// <summary>Buffers, descriptors and a dispatch, on the validating test device.</summary>
public sealed unsafe class VulkanComputeRunner(VulkanTestDevice device) : IDisposable
{
    /// <summary>A buffer and the memory behind it, mapped on demand.</summary>
    public readonly record struct Buffer(VkBuffer Handle, VkDeviceMemory Memory, ulong Size);

    private readonly List<Buffer> _buffers = [];
    private VkDescriptorPool _pool;
    private VkCommandPool _commands;

    private VkDeviceApi Api => device.Api;

    // ---------------------------------------------------------------- buffers

    /// <summary>A storage buffer of exactly this many bytes, zeroed.</summary>
    public Buffer CreateStorageBuffer(int bytes)
    {
        // Zero-length buffers are illegal; a shader with an empty output still needs somewhere to
        // point. One byte is enough and the comparison never looks at it.
        var size = (ulong)Math.Max(bytes, 4);

        var info = new VkBufferCreateInfo
        {
            size = size,
            usage = VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.UniformBuffer
                  | VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
        };

        Check(Api.vkCreateBuffer(&info, null, out VkBuffer handle), "vkCreateBuffer");

        Api.vkGetBufferMemoryRequirements(handle, out VkMemoryRequirements requirements);

        var allocation = new VkMemoryAllocateInfo
        {
            allocationSize = requirements.size,
            memoryTypeIndex = device.HostVisibleMemoryType(requirements.memoryTypeBits),
        };

        Check(Api.vkAllocateMemory(&allocation, null, out VkDeviceMemory memory), "vkAllocateMemory");
        Check(Api.vkBindBufferMemory(handle, memory, 0), "vkBindBufferMemory");

        var buffer = new Buffer(handle, memory, size);
        _buffers.Add(buffer);

        Write(buffer, new byte[size]);
        return buffer;
    }

    public void Write(Buffer buffer, ReadOnlySpan<byte> data)
    {
        void* mapped = null;
        Check(Api.vkMapMemory(buffer.Memory, 0, buffer.Size, 0, &mapped), "vkMapMemory");

        try
        {
            var destination = new Span<byte>(mapped, (int)buffer.Size);
            destination.Clear();
            data[..Math.Min(data.Length, destination.Length)].CopyTo(destination);
        }
        finally
        {
            Api.vkUnmapMemory(buffer.Memory);
        }
    }

    public byte[] Read(Buffer buffer)
    {
        void* mapped = null;
        Check(Api.vkMapMemory(buffer.Memory, 0, buffer.Size, 0, &mapped), "vkMapMemory");

        try
        {
            return new Span<byte>(mapped, (int)buffer.Size).ToArray();
        }
        finally
        {
            Api.vkUnmapMemory(buffer.Memory);
        }
    }

    // ---------------------------------------------------------------- storage images

    /// <summary>An image, its memory and its view.</summary>
    public readonly record struct Image(VkImage Handle, VkDeviceMemory Memory, VkImageView View);

    private readonly List<Image> _images = [];

    /// <summary>
    /// A storage image of the given rank and format, every texel set to 1.0, in GENERAL layout.
    /// </summary>
    /// <remarks>
    /// GENERAL because that is the only layout a storage image can be read and written in, and
    /// filled with ones because that is the only content Slang's compute corpus asks for. The fill
    /// is a clear rather than a mapped write: a device-local optimal-tiled image has no addressable
    /// layout for the host to write, and the whole point of the exercise is a normal storage image.
    /// </remarks>
    public Image CreateStorageImage(int dimensions, VkFormat format, uint size) =>
        CreateImage(dimensions, format, size, storage: true);

    /// <summary>A sampled image of the given rank and format, every texel 1.0, ready to read.</summary>
    public Image CreateSampledImage(int dimensions, VkFormat format, uint size) =>
        CreateImage(dimensions, format, size, storage: false);

    private Image CreateImage(int dimensions, VkFormat format, uint size, bool storage)
    {
        var info = new VkImageCreateInfo
        {
            imageType = dimensions switch
            {
                1 => VkImageType.Image1D,
                3 => VkImageType.Image3D,
                _ => VkImageType.Image2D,
            },
            format = format,
            extent = new VkExtent3D(size, dimensions >= 2 ? size : 1, dimensions >= 3 ? size : 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = (storage ? VkImageUsageFlags.Storage : VkImageUsageFlags.Sampled)
                  | VkImageUsageFlags.TransferDst,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined,
        };

        Check(Api.vkCreateImage(&info, null, out VkImage handle), "vkCreateImage");
        Api.vkGetImageMemoryRequirements(handle, out VkMemoryRequirements requirements);

        // Any memory type the image accepts; a storage image is never mapped, so host visibility
        // is not required and asking for it would fail on a discrete GPU.
        var allocation = new VkMemoryAllocateInfo
        {
            allocationSize = requirements.size,
            memoryTypeIndex = device.AnyMemoryType(requirements.memoryTypeBits),
        };

        Check(Api.vkAllocateMemory(&allocation, null, out VkDeviceMemory memory), "vkAllocateMemory");
        Check(Api.vkBindImageMemory(handle, memory, 0), "vkBindImageMemory");

        var viewInfo = new VkImageViewCreateInfo
        {
            image = handle,
            viewType = dimensions switch
            {
                1 => VkImageViewType.Image1D,
                3 => VkImageViewType.Image3D,
                _ => VkImageViewType.Image2D,
            },
            format = format,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
        };

        Check(Api.vkCreateImageView(&viewInfo, null, out VkImageView view), "vkCreateImageView");

        var image = new Image(handle, memory, view);
        _images.Add(image);

        // GENERAL for a storage image, which is the only layout it can be read and written in;
        // SHADER_READ_ONLY_OPTIMAL for a sampled one, which is what a sampler expects.
        FillWithOnes(handle, storage ? VkImageLayout.General : VkImageLayout.ShaderReadOnlyOptimal);
        return image;
    }

    /// <summary>A default sampler: linear, clamped, no anisotropy.</summary>
    public VkSampler CreateSampler()
    {
        var info = new VkSamplerCreateInfo
        {
            magFilter = VkFilter.Linear,
            minFilter = VkFilter.Linear,
            mipmapMode = VkSamplerMipmapMode.Linear,
            addressModeU = VkSamplerAddressMode.ClampToEdge,
            addressModeV = VkSamplerAddressMode.ClampToEdge,
            addressModeW = VkSamplerAddressMode.ClampToEdge,
            maxLod = 1000f /* VK_LOD_CLAMP_NONE */,
        };

        Check(Api.vkCreateSampler(&info, null, out VkSampler sampler), "vkCreateSampler");
        _samplers.Add(sampler);
        return sampler;
    }

    private readonly List<VkSampler> _samplers = [];

    private void FillWithOnes(VkImage image, VkImageLayout final)
    {
        Record(command =>
        {
            var range = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1);

            // Undefined -> TransferDst to clear, then -> General for the shader. Discarding the
            // old contents is correct: there are none.
            Barrier(command, image, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal, range);

            var white = new VkClearColorValue(1f, 1f, 1f, 1f);
            Api.vkCmdClearColorImage(
                command, image, VkImageLayout.TransferDstOptimal, &white, 1, &range);

            Barrier(command, image, VkImageLayout.TransferDstOptimal, final, range);
        });
    }

    private void Barrier(
        VkCommandBuffer command, VkImage image, VkImageLayout from, VkImageLayout to,
        VkImageSubresourceRange range)
    {
        var barrier = new VkImageMemoryBarrier
        {
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = uint.MaxValue /* VK_QUEUE_FAMILY_IGNORED */,
            dstQueueFamilyIndex = uint.MaxValue /* VK_QUEUE_FAMILY_IGNORED */,
            image = image,
            subresourceRange = range,
            srcAccessMask = VkAccessFlags.TransferWrite,
            dstAccessMask = VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite,
        };

        Api.vkCmdPipelineBarrier(
            command, VkPipelineStageFlags.AllCommands, VkPipelineStageFlags.AllCommands,
            0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Points one binding at an image, as a storage image or a sampled one.</summary>
    public void BindImage(VkDescriptorSet set, uint binding, Image image, bool storage, uint arrayElement = 0)
    {
        var info = new VkDescriptorImageInfo
        {
            imageView = image.View,
            imageLayout = storage ? VkImageLayout.General : VkImageLayout.ShaderReadOnlyOptimal,
        };

        Write(set, binding, arrayElement,
            storage ? VkDescriptorType.StorageImage : VkDescriptorType.SampledImage, &info);
    }

    /// <summary>Points one binding at a sampler.</summary>
    public void BindSampler(VkDescriptorSet set, uint binding, VkSampler sampler, uint arrayElement = 0)
    {
        var info = new VkDescriptorImageInfo { sampler = sampler };
        Write(set, binding, arrayElement, VkDescriptorType.Sampler, &info);
    }

    private void Write(
        VkDescriptorSet set, uint binding, uint arrayElement, VkDescriptorType type,
        VkDescriptorImageInfo* info)
    {
        var write = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = binding,
            dstArrayElement = arrayElement,
            descriptorCount = 1,
            descriptorType = type,
            pImageInfo = info,
        };

        Api.vkUpdateDescriptorSets(1, &write, 0, null);
    }

    // ---------------------------------------------------------------- descriptors

    /// <summary>One descriptor set per layout, from a pool sized for this dispatch.</summary>
    public VkDescriptorSet[] AllocateSets(IReadOnlyList<VkDescriptorSetLayout> layouts, int descriptorsPerType = 64)
    {
        if (layouts.Count == 0) return [];

        if (_pool.IsNull)
        {
            // Generous and uniform rather than counted per type. Getting the count wrong produces
            // VK_ERROR_OUT_OF_POOL_MEMORY, which reads as a harness bug and is one.
            VkDescriptorType[] types =
            [
                VkDescriptorType.StorageBuffer, VkDescriptorType.UniformBuffer,
                VkDescriptorType.StorageImage, VkDescriptorType.SampledImage,
                VkDescriptorType.Sampler, VkDescriptorType.CombinedImageSampler,
                VkDescriptorType.UniformTexelBuffer, VkDescriptorType.StorageTexelBuffer,
            ];

            var sizes = types
                .Select(t => new VkDescriptorPoolSize { type = t, descriptorCount = (uint)descriptorsPerType })
                .ToArray();

            fixed (VkDescriptorPoolSize* pSizes = sizes)
            {
                var info = new VkDescriptorPoolCreateInfo
                {
                    maxSets = (uint)Math.Max(8, layouts.Count),
                    poolSizeCount = (uint)sizes.Length,
                    pPoolSizes = pSizes,
                };

                Check(Api.vkCreateDescriptorPool(&info, null, out _pool), "vkCreateDescriptorPool");
            }
        }

        var handles = layouts.ToArray();
        var sets = new VkDescriptorSet[handles.Length];

        fixed (VkDescriptorSetLayout* pLayouts = handles)
        fixed (VkDescriptorSet* pSets = sets)
        {
            var info = new VkDescriptorSetAllocateInfo
            {
                descriptorPool = _pool,
                descriptorSetCount = (uint)handles.Length,
                pSetLayouts = pLayouts,
            };

            Check(Api.vkAllocateDescriptorSets(&info, pSets), "vkAllocateDescriptorSets");
        }

        return sets;
    }

    /// <summary>Points one descriptor at a buffer.</summary>
    /// <param name="arrayElement">
    /// Which descriptor within the binding, for an array of buffers. Zero for the ordinary case.
    /// </param>
    public void BindBuffer(
        VkDescriptorSet set, uint binding, VkDescriptorType type, Buffer buffer, uint arrayElement = 0)
    {
        var info = new VkDescriptorBufferInfo
        {
            buffer = buffer.Handle,
            offset = 0,
            range = ulong.MaxValue, // VK_WHOLE_SIZE
        };

        var write = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = binding,
            dstArrayElement = arrayElement,
            descriptorCount = 1,
            descriptorType = type,
            pBufferInfo = &info,
        };

        Api.vkUpdateDescriptorSets(1, &write, 0, null);
    }

    // ---------------------------------------------------------------- the dispatch

    /// <summary>Records and submits one dispatch, and waits for it.</summary>
    public void Dispatch(
        VkPipeline pipeline, VkPipelineLayout layout, VkDescriptorSet[] sets, uint x, uint y, uint z) =>
        Record(command =>
        {
            Api.vkCmdBindPipeline(command, VkPipelineBindPoint.Compute, pipeline);

            if (sets.Length > 0)
            {
                fixed (VkDescriptorSet* pSets = sets)
                {
                    Api.vkCmdBindDescriptorSets(
                        command, VkPipelineBindPoint.Compute, layout, 0, (uint)sets.Length, pSets, 0, null);
                }
            }

            Api.vkCmdDispatch(command, x, y, z);
        });

    /// <summary>Records one command buffer, submits it, and waits for it to finish.</summary>
    private void Record(Action<VkCommandBuffer> body)
    {
        if (_commands.IsNull)
        {
            var poolInfo = new VkCommandPoolCreateInfo
            {
                queueFamilyIndex = 0,
                flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
            };
            Check(Api.vkCreateCommandPool(&poolInfo, null, out _commands), "vkCreateCommandPool");
        }

        var allocate = new VkCommandBufferAllocateInfo
        {
            commandPool = _commands,
            level = VkCommandBufferLevel.Primary,
            commandBufferCount = 1,
        };

        VkCommandBuffer command;
        Check(Api.vkAllocateCommandBuffers(&allocate, &command), "vkAllocateCommandBuffers");

        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(Api.vkBeginCommandBuffer(command, &begin), "vkBeginCommandBuffer");

        body(command);

        Check(Api.vkEndCommandBuffer(command), "vkEndCommandBuffer");

        var fenceInfo = new VkFenceCreateInfo();
        Check(Api.vkCreateFence(&fenceInfo, null, out VkFence fence), "vkCreateFence");

        try
        {
            var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &command };
            Check(Api.vkQueueSubmit(device.Queue, 1, &submit, fence), "vkQueueSubmit");

            // Waiting on a fence rather than vkQueueWaitIdle, so a shader that hangs fails this
            // call instead of the test host. Ten seconds is far beyond anything the corpus does.
            var result = Api.vkWaitForFences(1, &fence, true, 10_000_000_000UL);
            if (result == VkResult.Timeout)
                throw new TimeoutException("the submitted work did not finish within 10 seconds");

            Check(result, "vkWaitForFences");
        }
        finally
        {
            Api.vkDestroyFence(fence, null);
            Api.vkFreeCommandBuffers(_commands, 1, &command);
        }
    }

    // ---------------------------------------------------------------- teardown

    public void Dispose()
    {
        // The queue has already been waited on by every Dispatch, so nothing is in flight.
        foreach (var buffer in _buffers)
        {
            Api.vkDestroyBuffer(buffer.Handle, null);
            Api.vkFreeMemory(buffer.Memory, null);
        }
        _buffers.Clear();

        foreach (var image in _images)
        {
            Api.vkDestroyImageView(image.View, null);
            Api.vkDestroyImage(image.Handle, null);
            Api.vkFreeMemory(image.Memory, null);
        }
        _images.Clear();

        foreach (var sampler in _samplers) Api.vkDestroySampler(sampler, null);
        _samplers.Clear();

        if (_commands.IsNotNull) { Api.vkDestroyCommandPool(_commands, null); _commands = VkCommandPool.Null; }
        if (_pool.IsNotNull) { Api.vkDestroyDescriptorPool(_pool, null); _pool = VkDescriptorPool.Null; }
    }

    private static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException($"{what} failed: {result}");
    }
}
