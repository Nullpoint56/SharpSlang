// Actually running a shader, through NRI.
//
// Every device oracle in this suite but this one stops at pipeline creation. That is a real check —
// the validation layers compare the pipeline layout against the SPIR-V and refuse a mismatch — but
// it is a check on *shape*. It cannot see an offset. If reflection says a struct member is at byte
// 16 and it is really at 32, the pipeline is created without complaint, the layers say nothing,
// and the shader reads the wrong bytes.
//
// The only thing that catches that is running the shader and looking at what came out. So: fill
// buffers at the offsets SharpSlang's reflection reports, dispatch, read back, and compare with
// numbers the Slang team wrote down. A wrong offset is wrong output.
//
// On SharpNRI's validated device (SharpNRI.Testing), so NRI's own validation and the backend's
// layer judge every call made here as well. Device-local resources filled through NRI's own upload
// helper, and one copy into a readback buffer after the dispatch; a renderer would upload the same
// way, and nothing here measures upload.

using SharpNRI;
using SharpNRI.Native;

namespace SharpSlang.Tests;

/// <summary>Buffers, textures, descriptors and a dispatch, on one validated NRI device.</summary>
internal sealed class NriComputeRunner(NriDevice device) : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly NriQueue _queue = device.GetQueue(QueueType.GRAPHICS);

    private T Own<T>(T resource) where T : IDisposable
    {
        _owned.Add(resource);
        return resource;
    }

    // ---------------------------------------------------------------- buffers

    /// <summary>A device-local buffer of at least <paramref name="bytes"/> bytes, holding <paramref name="data"/> and zeros after it.</summary>
    /// <param name="bytes">The size; a buffer cannot be empty, so fewer than 4 becomes 4.</param>
    /// <param name="data">The initial contents.</param>
    /// <param name="after">The access the shader makes of it, which the upload leaves it in.</param>
    public NriBuffer CreateBuffer(int bytes, ReadOnlySpan<byte> data, AccessBits after)
    {
        var size = (ulong)Math.Max(bytes, 4);
        var buffer = Own(device.CreateCommittedBuffer(MemoryLocation.DEVICE, new BufferDesc
        {
            size = size,
            // Every element of the corpus' buffers is read as 32-bit words, and a structured view
            // needs a stride that divides the size; Vulkan ignores it, NRI's validation does not.
            structureStride = 4,
            usage = BufferUsageBits.SHADER_RESOURCE | BufferUsageBits.SHADER_RESOURCE_STORAGE | BufferUsageBits.CONSTANT,
        }));

        var initial = new byte[size];
        data[..Math.Min(data.Length, initial.Length)].CopyTo(initial);
        _queue.UploadData([], [new NriBufferUpload(buffer, initial, new AccessStage { access = after, stages = StageBits.COMPUTE_SHADER })]);
        return buffer;
    }

    /// <summary>The view a range of this type binds a buffer through.</summary>
    public NriDescriptor CreateView(NriBuffer buffer, DescriptorType type) => Own(type switch
    {
        DescriptorType.CONSTANT_BUFFER => buffer.CreateView(BufferView.CONSTANT_BUFFER),
        DescriptorType.STRUCTURED_BUFFER => buffer.CreateView(BufferView.STRUCTURED_BUFFER, structureStride: 4),
        DescriptorType.STORAGE_STRUCTURED_BUFFER => buffer.CreateView(BufferView.STORAGE_STRUCTURED_BUFFER, structureStride: 4),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "this harness binds buffers as constant or structured buffers only"),
    });

    /// <summary>The access a shader makes of a buffer bound through a range of this type.</summary>
    public static AccessBits AccessFor(DescriptorType type) => type switch
    {
        DescriptorType.CONSTANT_BUFFER => AccessBits.CONSTANT_BUFFER,
        DescriptorType.STRUCTURED_BUFFER => AccessBits.SHADER_RESOURCE,
        _ => AccessBits.SHADER_RESOURCE_STORAGE,
    };

    // ---------------------------------------------------------------- textures

    /// <summary>
    /// A texture of the given rank and format, every texel 1.0, viewed as a storage texture or a
    /// sampled one and left in the matching layout.
    /// </summary>
    /// <remarks>
    /// Filled with ones because that is the only content Slang's compute corpus asks for
    /// (<c>content = one</c>), and through NRI's upload helper because a device-local optimally
    /// tiled image has no addressable layout for the host to write.
    /// </remarks>
    public NriDescriptor CreateTexture(int dimensions, Format format, uint size, bool storage)
    {
        var width = (ushort)size;
        var height = (ushort)(dimensions >= 2 ? size : 1);
        var depth = (ushort)(dimensions >= 3 ? size : 1);

        var texture = Own(device.CreateCommittedTexture(MemoryLocation.DEVICE, new TextureDesc
        {
            type = dimensions switch { 1 => TextureType.TEXTURE_1D, 3 => TextureType.TEXTURE_3D, _ => TextureType.TEXTURE_2D },
            usage = storage ? TextureUsageBits.SHADER_RESOURCE_STORAGE : TextureUsageBits.SHADER_RESOURCE,
            format = format,
            width = width,
            height = height,
            depth = depth,
            mipNum = 1,
            layerNum = 1,
            sampleNum = 1,
        }));

        var texel = One(format);
        var rowPitch = (uint)(texel.Length * width);
        var slicePitch = rowPitch * height;
        var ones = new byte[slicePitch * depth];
        for (var i = 0; i < ones.Length; i += texel.Length) texel.CopyTo(ones, i);

        var after = storage
            ? new AccessLayoutStage { access = AccessBits.SHADER_RESOURCE_STORAGE, layout = Layout.SHADER_RESOURCE_STORAGE, stages = StageBits.COMPUTE_SHADER }
            : new AccessLayoutStage { access = AccessBits.SHADER_RESOURCE, layout = Layout.SHADER_RESOURCE, stages = StageBits.COMPUTE_SHADER };

        _queue.UploadData(
            [new NriTextureUpload(texture, [new NriTextureSubresourceUpload(ones, depth, rowPitch, slicePitch)], after)],
            []);

        return Own(texture.CreateView(storage ? TextureView.STORAGE_TEXTURE : TextureView.TEXTURE, format));
    }

    /// <summary>One texel of 1.0 in every channel, for the float formats the corpus declares.</summary>
    private static byte[] One(Format format)
    {
        var name = format.ToString();
        var channels = name.TakeWhile(c => c is 'R' or 'G' or 'B' or 'A').Count();

        byte[] channel = name.Contains("16_SFLOAT", StringComparison.Ordinal)
            ? BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)1.0f))
            : name.Contains("32_SFLOAT", StringComparison.Ordinal)
                ? BitConverter.GetBytes(1.0f)
                : throw new ArgumentOutOfRangeException(nameof(format), format, "this harness fills float formats only");

        return [.. Enumerable.Repeat(channel, channels).SelectMany(b => b)];
    }

    /// <summary>A default sampler: linear, clamped, no anisotropy.</summary>
    public NriDescriptor CreateSampler() => Own(device.CreateSampler(new SamplerDesc
    {
        filters = new Filters { min = Filter.LINEAR, mag = Filter.LINEAR, mip = Filter.LINEAR },
        addressModes = new AddressModes { u = AddressMode.CLAMP_TO_EDGE, v = AddressMode.CLAMP_TO_EDGE, w = AddressMode.CLAMP_TO_EDGE },
        mipMax = 1000f,
    }));

    // ---------------------------------------------------------------- the dispatch

    /// <summary>
    /// Records one dispatch with these sets bound, copies <paramref name="output"/> into host
    /// memory after it, submits, waits, and returns the copy.
    /// </summary>
    public byte[] Dispatch(
        NriPipelineLayout layout,
        NriPipeline pipeline,
        NriDescriptorPool pool,
        IReadOnlyList<(uint SetIndex, NriDescriptorSet Set)> sets,
        uint x, uint y, uint z,
        NriBuffer output,
        AccessBits outputAccess)
    {
        var size = output.Desc.size;
        using var readback = device.CreateCommittedBuffer(MemoryLocation.HOST_READBACK, new BufferDesc { size = size });
        using var allocator = _queue.CreateCommandAllocator();
        using var cmd = allocator.CreateCommandBuffer();
        using var fence = device.CreateFence();

        var written = new AccessStage { access = outputAccess, stages = StageBits.COMPUTE_SHADER };
        var copySource = new AccessStage { access = AccessBits.COPY_SOURCE, stages = StageBits.COPY };

        cmd.Begin(pool);
        cmd.SetPipelineLayout(BindPoint.COMPUTE, layout);
        cmd.SetPipeline(pipeline);
        foreach (var (index, set) in sets) cmd.SetDescriptorSet(index, set, BindPoint.COMPUTE);
        cmd.Dispatch(x, y, z);
        cmd.Barrier(new NriBufferBarrier(output, written, copySource));
        cmd.CopyBuffer(readback, 0, output, 0, size);
        cmd.End();

        _queue.Submit(cmd, fence, 1);
        WaitFor(fence, 1);

        var bytes = readback.Map().ToArray();
        readback.Unmap();
        return bytes;
    }

    /// <summary>Returns once the fence has reached the value; fails after two minutes.</summary>
    /// <remarks>
    /// Not <c>NriFence.Wait</c>, whose own five-second timeout lavapipe passes while it
    /// JIT-compiles shaders on a cold cache (NriDeviceTests.WaitFor says what was measured).
    /// </remarks>
    private static void WaitFor(NriFence fence, ulong value)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (fence.Value < value)
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException($"the GPU work did not reach fence value {value} in 2 minutes (the fence is at {fence.Value})");
            Thread.Sleep(1);
        }
    }

    // ---------------------------------------------------------------- teardown

    /// <summary>Every view, buffer, texture and sampler made here, newest first.</summary>
    public void Dispose()
    {
        // Every dispatch waited for its fence, so nothing is in flight.
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}
