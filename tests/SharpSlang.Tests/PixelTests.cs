// The whole stack, judged by pixels: Slang source to a rendered image, on every backend with a
// device, compared pixel by pixel with an image computed here from the same math.
//
// Everything else that runs a shader in this suite runs a compute shader. This one is the graphics
// path a renderer takes: a vertex and a fragment stage compiled by SharpSlang (SPIR-V for Vulkan,
// DXIL for D3D12), reflected, projected by SharpSlang.Pipeline.NRI, and handed to SharpNRI as a
// pipeline layout, a vertex input and the descriptors it names; then a render pass, a draw, a copy
// into host memory. A constant buffer read by both stages and filled at the offsets reflection
// reports, a vertex input of two attributes laid out by the projection's formats, and a texture and
// a sampler (separate: the projection refuses combined ones, AGENTS.md#nri-projection), each bound
// at the set and range the projection names. NRI's validation and the backend's own layer (the
// Khronos layer on Vulkan, the D3D12 debug layer) record every message, and any error fails.
//
// Exact match, no tolerance, and the scene is built so that is a fair demand rather than a lucky
// one:
//
//   * Coverage. Both quads have their edges on pixel boundaries (positions are multiples of 1/16 in
//     NDC, exact in binary), so no pixel centre lies on an outside edge and coverage does not depend
//     on a rasterizer's tie rule. The diagonal inside a quad can pass through a centre; both of a
//     quad's triangles carry the same flat colour, so whichever the top-left rule gives it to, the
//     pixel is the same.
//   * Colour. Every channel is a texel byte (a multiple of 4) times powers of two (1, 1/2, 1/4), so
//     the exact result is an integer over 255 and the float product is that value times an exact
//     power of two. The nearest UNORM byte is therefore the integer with an error of about 1e-5,
//     far inside the 0.6 ULP both APIs allow for the float-to-UNORM conversion. The clear colour is
//     0 or 1 per channel.
//   * Sampling. The texture is as large as the target and sampled with point filtering at
//     SV_Position / size, which is a texel centre: no filter weight, no LOD choice.
//   * The varying is nointerpolation, so nothing depends on barycentric precision.
//
// NRI's viewport origin is top-left on both backends by default (Viewport.originBottomLeft is 0; the
// header calls top-left "D3D native"), so one CPU reference serves Vulkan and D3D12: NDC +1 in y is
// the top row. Measured: both backends match it.

using System.Security.Cryptography;
using SharpNRI;
using SharpNRI.Native;
using SharpNRI.Testing;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public sealed class PixelTests(ITestOutputHelper output)
{
    private const int Size = 32;

    private const string Source = """
        struct Globals
        {
            float2 offset;   // added to every position, in NDC
            float4 tint;     // multiplied into every fragment
        };

        ConstantBuffer<Globals> gGlobals;
        Texture2D               gTexture;
        SamplerState            gSampler;

        static const float kSize = 32.0;

        struct VIn
        {
            float2 position : POSITION;
            float4 colour   : COLOR0;
        };

        // SV_Position last: D3D12 links stages by register in declaration order
        // (AGENTS.md#dxil).
        struct VOut
        {
            nointerpolation float4 colour : COLOR0;
            float4 clip : SV_Position;
        };

        [shader("vertex")]
        VOut vertexMain(VIn v)
        {
            VOut o;
            o.colour = v.colour;
            o.clip = float4(v.position + gGlobals.offset, 0.5, 1.0);
            return o;
        }

        [shader("fragment")]
        float4 fragmentMain(VOut i) : SV_Target
        {
            float2 uv = i.clip.xy / kSize;
            return gTexture.Sample(gSampler, uv) * gGlobals.tint * i.colour;
        }
        """;

    /// <summary>A quad in pixels before the offset: left, top, right, bottom (exclusive), and its colour.</summary>
    private sealed record Quad(int Left, int Top, int Right, int Bottom, float[] Colour);

    private static readonly Quad[] Quads =
    [
        new(2, 4, 14, 28, [1.0f, 1.0f, 1.0f, 1.0f]),
        new(16, 2, 28, 12, [0.5f, 1.0f, 0.5f, 1.0f]),
    ];

    /// <summary>The constant buffer's values: the offset is two pixels right and two down.</summary>
    private static readonly float[] Offset = [2.0f / (Size / 2), -2.0f / (Size / 2)];
    private const int OffsetPixels = 2;
    private static readonly float[] Tint = [1.0f, 0.5f, 0.25f, 1.0f];

    /// <summary>The clear colour: 0 or 1 per channel, so its bytes are exact.</summary>
    private static readonly byte[] Clear = [255, 0, 255, 255];

    /// <summary>The texture, RGBA8: red 8x, green 8y, blue one of 0, 64, 128, 192, opaque.</summary>
    private static byte[] Texel(int x, int y) => [(byte)(x * 8), (byte)(y * 8), (byte)(((x ^ y) & 3) * 64), 255];

    [NriVulkanFact]
    public void A_scene_compiled_to_spirv_renders_on_vulkan_exactly_as_computed()
    {
        var compiled = NriDeviceTests.Compile("pixels", Source, ["vertexMain", "fragmentMain"], new TargetOptions());
        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        Assert.NotEqual(0, fixture.Device.Desc.features.shaderBytecodeSPIRV);

        Render(fixture, compiled, compiled.Layout.ToNri(),
            NriDeviceTests.SpirvShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0]),
            NriDeviceTests.SpirvShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1]));
    }

    [NriVulkanFact]
    public void A_scene_drawn_with_the_projection_stored_at_bake_time_renders_on_vulkan_exactly_as_computed()
    {
        // The path an application takes when it projects at bake time: the pipeline built from the
        // NRI section read back out of the package, never from ToNri.
        var compiled = NriDeviceTests.Compile("pixels", Source, ["vertexMain", "fragmentMain"], new TargetOptions());
        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);

        Render(fixture, compiled, Stored(compiled),
            NriDeviceTests.SpirvShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0]),
            NriDeviceTests.SpirvShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1]));
    }

    [NriD3D12Fact]
    public void A_scene_compiled_to_dxil_renders_on_d3d12_exactly_as_computed()
    {
        var compiled = NriDeviceTests.Compile("pixels", Source, ["vertexMain", "fragmentMain"], NriDeviceTests.Dxil);
        using var fixture = NriTestDevice.Create(GraphicsAPI.D3D12);
        Assert.NotEqual(0, fixture.Device.Desc.features.shaderBytecodeDXIL);

        // A DXIL container holds one entry point and D3D12 never asks its name (AGENTS.md#dxil).
        Render(fixture, compiled, compiled.Layout.ToNri(),
            new NriShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0], "main"),
            new NriShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1], "main"));
    }

    [NriD3D12Fact]
    public void A_scene_drawn_with_the_projection_stored_at_bake_time_renders_on_d3d12_exactly_as_computed()
    {
        var compiled = NriDeviceTests.Compile("pixels", Source, ["vertexMain", "fragmentMain"], NriDeviceTests.Dxil);
        using var fixture = NriTestDevice.Create(GraphicsAPI.D3D12);

        Render(fixture, compiled, Stored(compiled),
            new NriShaderDesc(StageBits.VERTEX_SHADER, compiled.Code[0], "main"),
            new NriShaderDesc(StageBits.FRAGMENT_SHADER, compiled.Code[1], "main"));
    }

    /// <summary>The projection stored in a package at bake time, read back as a game reads it, equal to a fresh one.</summary>
    private static NriPipelineProjection Stored(NriDeviceTests.Compiled compiled)
    {
        var package = NriPackageTests.ThroughBytes(NriPackageTests.Package(compiled.Layout).WithNriProjection());
        var stored = package.ReadNriProjection() ?? throw new InvalidOperationException("the package carries no stored projection");
        NriPackageTests.AssertSameProjection(compiled.Layout.ToNri(), stored);
        return stored;
    }

    /// <summary>The image the scene must produce, from the same math the shaders do.</summary>
    private static byte[] Expected()
    {
        var image = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var pixel = image.AsSpan((y * Size + x) * 4, 4);
                Clear.CopyTo(pixel);

                foreach (var q in Quads)
                {
                    var covered = x >= q.Left + OffsetPixels && x < q.Right + OffsetPixels
                               && y >= q.Top + OffsetPixels && y < q.Bottom + OffsetPixels;
                    if (!covered) continue;

                    var texel = Texel(x, y);
                    for (var c = 0; c < 4; c++)
                    {
                        // The exact value is an integer: a multiple of 4 times 1, 1/2 or 1/4 twice.
                        var exact = texel[c] * (double)Tint[c] * q.Colour[c];
                        Assert.Equal(Math.Round(exact), exact);
                        pixel[c] = (byte)exact;
                    }
                }
            }
        }

        return image;
    }

    private void Render(
        NriTestDevice fixture, NriDeviceTests.Compiled compiled, NriPipelineProjection projection, NriShaderDesc vertex, NriShaderDesc fragment)
    {
        var device = fixture.Device;

        // ---------------------------------------------------------------- what the projection says

        var (cbSet, cbRange) = projection.Range("gGlobals") ?? throw new InvalidOperationException("no range named gGlobals");
        var (texSet, texRange) = projection.Range("gTexture") ?? throw new InvalidOperationException("no range named gTexture");
        var (smpSet, smpRange) = projection.Range("gSampler") ?? throw new InvalidOperationException("no range named gSampler");
        Assert.Equal(DescriptorType.CONSTANT_BUFFER, cbRange.Desc.descriptorType);
        Assert.Equal(DescriptorType.TEXTURE, texRange.Desc.descriptorType);
        Assert.Equal(DescriptorType.SAMPLER, smpRange.Desc.descriptorType);
        Assert.Equal(StageBits.VERTEX_SHADER | StageBits.FRAGMENT_SHADER, cbRange.Desc.shaderStages & (StageBits.VERTEX_SHADER | StageBits.FRAGMENT_SHADER));

        var position = projection.VertexAttributes.Single(a => a.Name == "position");
        var colour = projection.VertexAttributes.Single(a => a.Name == "colour");
        Assert.Equal(Format.RG32_SFLOAT, position.Desc.Format);
        Assert.Equal(Format.RGBA32_SFLOAT, colour.Desc.Format);
        Assert.Equal(Format.RGBA32_SFLOAT, Assert.Single(projection.FragmentOutputs).Format);

        // The constant buffer, filled where reflection says each member is.
        var globals = (StructLayout)((ContainerLayout)compiled.Layout.Parameter("gGlobals")!.Type).ElementType;
        var offsetAt = (int)globals.Field("offset")!.UniformOffset;
        var tintAt = (int)globals.Field("tint")!.UniformOffset;
        output.WriteLine($"gGlobals: offset at byte {offsetAt}, tint at byte {tintAt}, {globals.UniformSize} bytes; "
                       + $"set {cbSet.SetIndex} range {cbRange.RangeIndex}, texture set {texSet.SetIndex} range {texRange.RangeIndex}, "
                       + $"sampler set {smpSet.SetIndex} range {smpRange.RangeIndex}");

        var constants = new byte[256];
        Floats(Offset).CopyTo(constants.AsSpan(offsetAt));
        Floats(Tint).CopyTo(constants.AsSpan(tintAt));

        // Interleaved: one stream, position then colour, at offsets of this harness's choosing
        // (the projection leaves Offset and StreamIndex to the renderer).
        const int Stride = 6 * sizeof(float);
        var vertices = new List<float>();
        foreach (var q in Quads)
        {
            float X(int px) => px / (Size / 2.0f) - 1.0f;
            float Y(int py) => 1.0f - py / (Size / 2.0f);
            (float, float)[] corners =
            [
                (X(q.Left), Y(q.Top)), (X(q.Right), Y(q.Top)), (X(q.Left), Y(q.Bottom)),
                (X(q.Right), Y(q.Top)), (X(q.Right), Y(q.Bottom)), (X(q.Left), Y(q.Bottom)),
            ];
            foreach (var (cx, cy) in corners) vertices.AddRange([cx, cy, .. q.Colour]);
        }

        var vertexInput = new NriVertexInputDesc
        {
            Attributes =
            [
                position.Desc with { Offset = 0, StreamIndex = 0 },
                colour.Desc with { Offset = 2 * sizeof(float), StreamIndex = 0 },
            ],
            Streams = [new VertexStreamDesc { bindingSlot = 0, stepRate = VertexStreamStepRate.PER_VERTEX, stride = Stride }],
        };

        // ---------------------------------------------------------------- resources

        using var layout = device.CreatePipelineLayout(projection.Layout);
        using var pipeline = device.CreateGraphicsPipeline(new NriGraphicsPipelineDesc
        {
            PipelineLayout = layout,
            VertexInput = vertexInput,
            InputAssembly = new InputAssemblyDesc { topology = Topology.TRIANGLE_LIST },
            Rasterization = new RasterizationDesc { fillMode = FillMode.SOLID, cullMode = CullMode.NONE },
            OutputMerger = new NriOutputMergerDesc
            {
                Colors = [new ColorAttachmentDesc { format = Format.RGBA8_UNORM, colorWriteMask = ColorWriteBits.RGBA }],
            },
            Shaders = [vertex, fragment],
        });

        using var vertexBuffer = HostBuffer(device, Floats([.. vertices]), BufferUsageBits.VERTEX);
        using var constantBuffer = HostBuffer(device, constants, BufferUsageBits.CONSTANT);
        using var constantView = constantBuffer.CreateView(BufferView.CONSTANT_BUFFER);

        using var texture = device.CreateCommittedTexture(MemoryLocation.DEVICE, Texture(TextureUsageBits.SHADER_RESOURCE));
        var texels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                Texel(x, y).CopyTo(texels, (y * Size + x) * 4);
        device.GetQueue(QueueType.GRAPHICS).UploadData(
            [new NriTextureUpload(texture, [new NriTextureSubresourceUpload(texels, 1, Size * 4, Size * Size * 4)],
                new AccessLayoutStage { access = AccessBits.SHADER_RESOURCE, layout = Layout.SHADER_RESOURCE, stages = StageBits.FRAGMENT_SHADER })],
            []);
        using var textureView = texture.CreateView(TextureView.TEXTURE, Format.RGBA8_UNORM);

        using var sampler = device.CreateSampler(new SamplerDesc
        {
            filters = new Filters { min = Filter.NEAREST, mag = Filter.NEAREST, mip = Filter.NEAREST },
            addressModes = new AddressModes { u = AddressMode.CLAMP_TO_EDGE, v = AddressMode.CLAMP_TO_EDGE, w = AddressMode.CLAMP_TO_EDGE },
        });

        using var target = device.CreateCommittedTexture(MemoryLocation.DEVICE, Texture(TextureUsageBits.COLOR_ATTACHMENT));
        using var targetView = target.CreateView(TextureView.COLOR_ATTACHMENT, Format.RGBA8_UNORM);

        ref readonly var desc = ref device.Desc;
        var rowPitch = Align(Size * 4u, desc.memoryAlignment.uploadBufferTextureRow);
        var slicePitch = Align(rowPitch * Size, desc.memoryAlignment.uploadBufferTextureSlice);
        using var readback = device.CreateCommittedBuffer(MemoryLocation.HOST_READBACK, new BufferDesc { size = slicePitch });

        using var pool = device.CreateDescriptorPool(new DescriptorPoolDesc
        {
            descriptorSetMaxNum = (uint)projection.DescriptorSets.Count,
            constantBufferMaxNum = 1,
            textureMaxNum = 1,
            samplerMaxNum = 1,
        });
        var sets = projection.DescriptorSets.ToDictionary(s => s.SetIndex, s => pool.AllocateDescriptorSet(layout, s.SetIndex));
        sets[cbSet.SetIndex].Update(cbRange.RangeIndex, 0, constantView);
        sets[texSet.SetIndex].Update(texRange.RangeIndex, 0, textureView);
        sets[smpSet.SetIndex].Update(smpRange.RangeIndex, 0, sampler);

        // ---------------------------------------------------------------- the frame

        var queue = device.GetQueue(QueueType.GRAPHICS);
        using var allocator = queue.CreateCommandAllocator();
        using var cmd = allocator.CreateCommandBuffer();
        using var fence = device.CreateFence();

        var attachment = new AccessLayoutStage { access = AccessBits.COLOR_ATTACHMENT, layout = Layout.COLOR_ATTACHMENT, stages = StageBits.COLOR_ATTACHMENT };
        var copySource = new AccessLayoutStage { access = AccessBits.COPY_SOURCE, layout = Layout.COPY_SOURCE, stages = StageBits.COPY };

        cmd.Begin(pool);
        cmd.Barrier(new NriTextureBarrier(target, default, attachment));

        var clear = new ClearValue();
        clear.color.f = new Color32f { x = Clear[0] / 255f, y = Clear[1] / 255f, z = Clear[2] / 255f, w = Clear[3] / 255f };
        cmd.BeginRendering([new NriAttachment(targetView, clear, LoadOp.CLEAR, StoreOp.STORE)]);
        cmd.SetViewport(new Viewport { x = 0, y = 0, width = Size, height = Size, depthMin = 0, depthMax = 1 });
        cmd.SetScissor(new Rect { x = 0, y = 0, width = Size, height = Size });
        cmd.SetPipelineLayout(BindPoint.GRAPHICS, layout);
        cmd.SetPipeline(pipeline);
        foreach (var (index, set) in sets) cmd.SetDescriptorSet(index, set, BindPoint.GRAPHICS);
        cmd.SetVertexBuffer(0, vertexBuffer, 0, Stride);
        cmd.Draw((uint)(vertices.Count / 6));
        cmd.EndRendering();

        cmd.Barrier(new NriTextureBarrier(target, attachment, copySource));
        cmd.ReadbackTextureToBuffer(
            readback,
            new TextureDataLayoutDesc { offset = 0, rowPitch = rowPitch, slicePitch = slicePitch },
            target,
            new TextureRegionDesc { x = 0, y = 0, z = 0, width = Size, height = Size, depth = 1, mipOffset = 0, layerOffset = 0, planes = PlaneBits.COLOR });
        cmd.End();

        queue.Submit(cmd, fence, 1);
        GpuWait.For(fence, 1, "the frame");

        var pixels = new byte[Size * Size * 4];
        var mapped = readback.Map();
        for (var y = 0; y < Size; y++)
            mapped.Slice((int)(y * rowPitch), Size * 4).CopyTo(pixels.AsSpan(y * Size * 4, Size * 4));
        readback.Unmap();

        // ---------------------------------------------------------------- the verdict

        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());
        Assert.True(!fixture.Errors.Any(),
            "NRI or the backend's validation reported errors:\n" + string.Join("\n", fixture.Errors.Select(m => "  " + m)));

        var expected = Expected();
        output.WriteLine($"{fixture.Api}: {Size}x{Size} RGBA8, sha256 {Convert.ToHexString(SHA256.HashData(pixels))} "
                       + $"(expected {Convert.ToHexString(SHA256.HashData(expected))})");

        var differing = new List<string>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels.AsSpan(i, 4).SequenceEqual(expected.AsSpan(i, 4))) continue;
            var (x, y) = (i / 4 % Size, i / 4 / Size);
            differing.Add($"({x},{y}) is {Rgba(pixels, i)}, expected {Rgba(expected, i)}");
        }

        Assert.True(differing.Count == 0,
            $"{differing.Count} of {Size * Size} pixels differ from the CPU reference on {fixture.Api}; the first:\n  "
            + string.Join("\n  ", differing.Take(12)));
    }

    private static TextureDesc Texture(TextureUsageBits usage) => new()
    {
        type = TextureType.TEXTURE_2D,
        usage = usage,
        format = Format.RGBA8_UNORM,
        width = Size,
        height = Size,
        depth = 1,
        mipNum = 1,
        layerNum = 1,
        sampleNum = 1,
    };

    /// <summary>A host-visible buffer holding <paramref name="bytes"/>; read by the GPU after the submit that follows.</summary>
    private static NriBuffer HostBuffer(NriDevice device, ReadOnlySpan<byte> bytes, BufferUsageBits usage)
    {
        var buffer = device.CreateCommittedBuffer(MemoryLocation.HOST_UPLOAD, new BufferDesc { size = (ulong)bytes.Length, usage = usage });
        bytes.CopyTo(buffer.Map());
        buffer.Unmap();
        return buffer;
    }

    private static byte[] Floats(float[] values) => [.. System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(values)];

    private static string Rgba(byte[] image, int i) => $"({image[i]},{image[i + 1]},{image[i + 2]},{image[i + 3]})";

    private static uint Align(uint value, uint alignment) =>
        alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
}
