// What allocates after load, measured, and the part that must not.
//
// SharpSlang is an asset-pipeline library with a reader half. Compiling, reflecting, baking and
// projecting allocate by nature — a reflection snapshot is a tree of records, a projection is a
// list of descs — and all of it runs once per shader, in the cooker or at load. What an engine
// touches after that is the projected data: properties, indexers, and a handful of lookups by
// name or index. Those are the paths that have to be allocation-free, because a renderer that
// calls one per draw cannot afford the garbage, and the reader deserves to know which is which.
//
// Measured on the first version of this file: every property and indexer was already zero, and
// every by-name lookup was 80 to 120 bytes, a LINQ closure and an enumerator each. The lookups
// are loops now. This pins both halves — zero where it must be, and the order of magnitude of
// the load-time work so a regression there is at least visible.
//
// The lookups live in a frame, and the last test measures the frame. SharpNRI's per-frame path is
// allocation-free — from PrismNRI 0.3.0, as it was called then, NriDescriptorSet is a struct and
// sets are allocated into storage the caller owns — and it measures that on NRI's NONE backend.
// Two allocation-free halves do not make an allocation-free frame by themselves: the code between
// them, walking the projection's lists and reading its records, can allocate where neither half
// does. So the frame an engine would write, every index it hands NRI taken from a projection, is
// measured whole.
//
// GC.GetAllocatedBytesForCurrentThread is the instrument: exact, per thread, and unaffected by
// collections. The suite runs serially (AssemblyInfo), so nothing else allocates on this thread
// mid-measurement, and a warm-up precedes every measurement so tiered compilation and the first
// call's one-time work are not counted.

using System.Runtime.CompilerServices;
using SharpNRI;
using SharpNRI.Native;
using SharpNRI.Testing;
using SharpSlang.Pipeline.NRI;

namespace SharpSlang.Tests;

public class AllocationTests
{
    private const string Shader = """
        struct Uniforms { float4x4 mvp; float3 tint; float exposure; };
        ConstantBuffer<Uniforms> u;
        Texture2D    albedo;
        SamplerState albedoSampler;
        RWStructuredBuffer<float4> outBuffer;
        [[vk::binding(0, 2)]] Texture2D far;
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VSOut vertexMain(float3 position : POSITION, float2 uv : TEXCOORD0)
        { VSOut o; o.pos = mul(u.mvp, float4(position, 1.0)); o.uv = uv; return o; }

        [shader("fragment")]
        float4 fragmentMain(VSOut i) : SV_Target
        { outBuffer[0] = 1; return albedo.Sample(albedoSampler, i.uv) * u.exposure + far.Load(0); }
        """;

    /// <summary>Bytes allocated per call of <paramref name="action"/>, after a warm-up.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double BytesPerCall(Action action, int iterations = 1000)
    {
        for (var i = 0; i < 50; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) action();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)iterations;
    }

    private static void AssertZero(string what, Action action)
    {
        var bytes = BytesPerCall(action);
        Assert.True(bytes == 0, $"{what} allocates {bytes:N0} bytes per call; an engine may call it per draw, and it must allocate nothing.");
    }

    [NativeFact]
    public void Lookups_on_a_projection_allocate_nothing()
    {
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var nri = layout.ToNri();

        AssertZero("NriPipelineProjection.Range(name)", () => nri.Range("albedo"));
        AssertZero("NriPipelineProjection.Range(name) for a miss", () => nri.Range("nothing"));
        AssertZero("NriPipelineProjection.Set(space)", () => nri.Set(2));
        AssertZero("NriPipelineProjection.Stage(name)", () => nri.Stage("fragmentMain"));
        AssertZero("ProjectedDescriptorSet.Range(name)", () => nri.DescriptorSets[0].Range("outBuffer"));
        // Read into a typed sink rather than GC.KeepAlive(object): the first version of this
        // test boxed the DescriptorRangeDesc struct into that parameter and reported 40 bytes the
        // library never allocated.
        AssertZero("indexing the projected ranges", () => Sink = nri.DescriptorSets[0].Ranges[1].Desc.descriptorNum);
        AssertZero("reading the layout's stage mask", () => Sink = (uint)nri.Layout.ShaderStages);
    }

    // Internal rather than private: a value read only to be stored is the point of a sink, and
    // IDE0052 reports a private field nothing reads.
    internal static uint Sink;

    [NativeFact]
    public void Load_time_work_allocates_and_says_how_much()
    {
        // Not a ceiling anyone tuned; an order of magnitude, so a projection that starts
        // allocating a megabyte per shader is a red test rather than a slow load. Measured on the
        // first run: ToNri 5,184 bytes for the shader above.
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var options = new NriProjectionOptions();

        var toNri = BytesPerCall(() => layout.ToNri(options), iterations: 200);

        Assert.True(toNri > 0 && toNri < 64 * 1024, $"ToNri allocated {toNri:N0} bytes per call");
    }

    // ---------------------------------------------------------------- a whole frame

    /// <summary>
    /// A compute shader with what a frame binds: a set holding storage buffers, a second set in a
    /// register space of its own, and a push-constant block.
    /// </summary>
    /// <remarks>
    /// Particles stepped from last frame's state into this frame's, so two ranges change every
    /// frame, with per-frame constants beside them. The constants are in space 2 rather than 1 so
    /// that the projected set index (1) and the register space (2) differ, which is when binding by
    /// <c>RegisterSpace</c> instead of <c>SetIndex</c> goes wrong — silently, on NONE.
    /// </remarks>
    private const string FrameShader = """
        struct Particle { float3 position; float age; float3 velocity; float mass; };
        struct FrameData { float4 gravity; float4 boundsMin; float4 boundsMax; };
        struct Step { float dt; uint count; };

        [vk::push_constant] ConstantBuffer<Step> step;

        StructuredBuffer<Particle>   previous;
        RWStructuredBuffer<Particle> next;
        StructuredBuffer<float4>     fields[2];
        [[vk::binding(0, 2)]] ConstantBuffer<FrameData> frameData;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void simulate(uint3 id : SV_DispatchThreadID)
        {
            if (id.x >= step.count) return;
            Particle p = previous[id.x];
            float3 force = frameData.gravity.xyz + fields[0][id.x].xyz + fields[1][id.x].xyz;
            p.velocity += force / p.mass * step.dt;
            p.position = clamp(p.position + p.velocity * step.dt, frameData.boundsMin.xyz, frameData.boundsMax.xyz);
            p.age += step.dt;
            next[id.x] = p;
        }
        """;

    /// <summary>The push-constant block as the frame hands it to NRI: a struct, by reference.</summary>
    private readonly record struct Step(float Dt, uint Count);

    /// <summary>
    /// Frames measured after the warm-up frame; SharpNRI measures its own frame over as many.
    /// </summary>
    private const int Frames = 300;

    [NriNoneFact]
    public void A_whole_frame_bound_through_the_nri_projection_allocates_nothing()
    {
        // SharpNRI measures its own frame at zero on NONE, and the test above measures the
        // projection's lookups at zero one at a time. Neither is the frame a renderer writes, in
        // which every index NRI is handed comes off the projection: sets allocated and bound by
        // SetIndex, ranges written by RangeIndex, root constants by Index, the group count from
        // ThreadGroupSize, and ranges looked up by name along the way. This is that frame.
        //
        // NONE does nothing per call, so whatever allocates is managed code: SharpNRI's,
        // SharpSlang's, or this frame's use of them. It validates nothing either (NRI never wraps
        // NONE in its validation layer), so this says nothing about whether the stream is legal —
        // NriDeviceTests asks Vulkan and D3D12 that — only what it costs the heap.
        var package = NriDeviceTests.Bake(FrameShader, "simulate");
        var projection = package.Layout.ToNri();

        // The shape the frame is written against, pinned, so a change in how Slang places these
        // fails here rather than leaving a frame that quietly binds less.
        Assert.Equal(2, projection.DescriptorSets.Count);
        Assert.Equal((0u, 0u), (projection.DescriptorSets[0].SetIndex, projection.DescriptorSets[0].RegisterSpace));
        Assert.Equal((1u, 2u), (projection.DescriptorSets[1].SetIndex, projection.DescriptorSets[1].RegisterSpace));
        Assert.Equal((uint)Unsafe.SizeOf<Step>(), Assert.Single(projection.RootConstants).Desc.size);

        using var fixture = NriTestDevice.Create(GraphicsAPI.NONE);
        using var frame = new ProjectedFrame(fixture.Device, projection, package.EntryPoints[0].ToNriShaderDesc());

        // One warm-up frame, as SharpNRI's own measurement has: the JIT, static constructors and
        // first calls allocate once, and that is not what is being measured.
        frame.Run();
        frame.ClearMeter();
        var messages = fixture.Messages.Count;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Frames; i++) frame.Run();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // 1,000 particles at the 64 threads per group the frame read off the projection.
        Assert.Equal(16u, frame.GroupCount);
        Assert.True(fixture.Messages.Count == messages,
            "NRI reported messages during the measured frames. NONE should report nothing, and each "
            + "message allocates in the fixture's handler, where it would be counted as the frame's.");

        // Measured: 0 bytes over the 300 frames after the one warm-up frame. They take about 0.2 ms
        // here and the JIT compiles nothing meanwhile, but a slower machine may tier the frame up in
        // the middle of the loop, so that was measured too: with call counting started at once
        // (DOTNET_TC_CallCountingDelayMs=0) the JIT compiled 12 methods during the 300 frames, and
        // over a million frames it compiled 1,647. Both measured 0 bytes, as did runs with tiered
        // compilation, dynamic PGO or quick JIT for loops switched off. The meter's own calls are
        // inside every one of those zeros.
        //
        // Watched failing before it was trusted to pass. Three faults were planted one at a time and
        // reverted, and each failure message named the part of the frame the fault was in. The
        // array-returning AllocateDescriptorSets(layout, setIndex), which SharpNRI documents as
        // allocating, in place of the span overload: 24,000 bytes, 80 per frame, a one-element
        // NriDescriptorSet[] for each of the two sets. The one-command-buffer Submit(cmd, fence,
        // value) in place of Submit over spans: 14,400 bytes, 48 per frame, the one-element array
        // its signal span was built from — against the PrismNRI 0.3.0-alpha release. This test
        // found that, PrismNRI changed the overload to a span over a local (2b4ccf4), and against
        // SharpNRI 191.0.0-beta.4 the same planted fault measures 0, so it no longer fails. A
        // foreach over DescriptorSets and each set's Ranges in
        // place of the indexed loops: 36,000 bytes, 120 per frame, three boxed List<T> enumerators.
        Assert.True(bytes == 0,
            $"{Frames} frames bound through SharpSlang.Pipeline.NRI's projection allocated {bytes:N0} managed "
            + $"bytes, {bytes / (double)Frames:N1} per frame. An engine runs this every frame and it "
            + $"must allocate nothing. By part of the frame, with what to suspect in each:\n"
            + frame.Breakdown());
    }

    /// <summary>
    /// What a renderer keeps to dispatch one projected compute shader every frame, created once,
    /// and the frame itself, <see cref="Run"/>, which takes every index it hands NRI from the
    /// projection.
    /// </summary>
    private sealed class ProjectedFrame : IDisposable
    {
        private const uint ParticleCount = 1000;
        private const uint ParticleStride = 32;     // Particle: a float3 and a float, twice

        /// <summary>
        /// What the dispatch does to the particle state: reads one half, writes the other.
        /// </summary>
        private static readonly AccessStage ReadAndWritten = new()
        {
            access = AccessBits.SHADER_RESOURCE | AccessBits.SHADER_RESOURCE_STORAGE,
            stages = StageBits.COMPUTE_SHADER,
        };

        /// <summary>
        /// Last frame wrote the state this frame reads and read the state this frame writes, so one
        /// global barrier orders both before the dispatch. After <see cref="ReadAndWritten"/>,
        /// because static fields are initialized in the order they are declared.
        /// </summary>
        private static readonly GlobalBarrierDesc FrameToFrame =
            new() { before = ReadAndWritten, after = ReadAndWritten };

        private readonly NriPipelineProjection _projection;
        private readonly ProjectedRootConstants _rootConstants;
        private readonly ProjectedStage _stage;
        private readonly List<IDisposable> _owned = [];
        private readonly NriPipelineLayout _layout;
        private readonly NriPipeline _pipeline;
        private readonly NriDescriptorPool _pool;
        private readonly NriQueue _queue;
        private readonly NriCommandAllocator _allocator;
        private readonly NriCommandBuffer _cmd;
        private readonly NriFence _fence;

        // Everything Run writes into is allocated here, once: the sets, by SetIndex; the binding
        // table, by SetIndex and RangeIndex; the views that change per frame; the spans Submit takes.
        private readonly NriDescriptorSet[] _sets;
        private readonly NriDescriptor[][][] _bindings;
        private readonly NriDescriptor[] _stateReads;
        private readonly NriDescriptor[] _stateWrites;
        private readonly NriDescriptor[] _frameConstants;
        private readonly NriCommandBuffer[] _commandBuffers;
        private readonly NriFenceSubmit[] _signals = new NriFenceSubmit[1];
        private ulong _frame;

        public ProjectedFrame(NriDevice device, NriPipelineProjection projection, NriShaderDesc shader)
        {
            _projection = projection;
            _rootConstants = projection.RootConstants[0];
            _stage = projection.Stage("simulate")
                ?? throw new InvalidOperationException("the projection has no stage named simulate");

            try
            {
                _layout = Own(device.CreatePipelineLayout(projection.Layout));
                _pipeline = Own(device.CreateComputePipeline(new NriComputePipelineDesc
                {
                    PipelineLayout = _layout,
                    Shader = shader,
                }));
                _pool = Own(device.CreateDescriptorPool(PoolFor(projection)));
                _queue = device.GetQueue(QueueType.GRAPHICS);
                _allocator = Own(_queue.CreateCommandAllocator());
                _cmd = Own(_allocator.CreateCommandBuffer());
                _fence = Own(device.CreateFence());
                _commandBuffers = [_cmd];
                _sets = new NriDescriptorSet[projection.DescriptorSets.Count];

                // The binding table: for every projected range, the descriptors it is written with.
                _bindings = new NriDescriptor[projection.DescriptorSets.Count][][];
                foreach (var set in projection.DescriptorSets)
                {
                    _bindings[set.SetIndex] = new NriDescriptor[set.Ranges.Count][];
                    foreach (var range in set.Ranges)
                        _bindings[set.SetIndex][range.RangeIndex] = new NriDescriptor[range.Desc.descriptorNum];
                }

                // The resources, by the shader's names for them, each viewed as the projection says
                // its range is: the particle state twice, for the frames to alternate between, read
                // through `previous` and written through `next`; a constant buffer per frame in
                // flight; and two force fields, which never change and go into the table now. The
                // other three go in by name every frame, in Run.
                var state = Buffers(device, 2, ParticleCount * ParticleStride, ParticleStride,
                    BufferUsageBits.SHADER_RESOURCE | BufferUsageBits.SHADER_RESOURCE_STORAGE);
                _stateReads = Views("previous", state, ParticleStride);
                _stateWrites = Views("next", state, ParticleStride);
                // 256 bytes for FrameData's 48: the size a D3D12 constant-buffer view is counted in.
                _frameConstants = Views("frameData", Buffers(device, 2, 256, 0, BufferUsageBits.CONSTANT), 0);
                Views("fields", Buffers(device, 2, ParticleCount * 16, 16, BufferUsageBits.SHADER_RESOURCE), 16)
                    .CopyTo(Slot("fields"), 0);
            }
            catch
            {
                // What was created goes before the device does, or the device's Dispose throws about
                // it and the exception that matters is lost.
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// The group count the last frame derived: particles over threads per group, rounded up.
        /// </summary>
        public uint GroupCount { get; private set; }

        /// <summary>One frame, in the order an engine records it.</summary>
        public void Run()
        {
            _frame++;
            _mark = GC.GetAllocatedBytesForCurrentThread();

            // The last frame was waited for at its end, so its command memory and its sets are free.
            _allocator.Reset();
            _pool.Reset();
            _cmd.Begin(_pool);
            Charge(Part.Begin);

            // A set for every projected set, into storage this object owns, at its SetIndex.
            var sets = _projection.DescriptorSets;
            for (var s = 0; s < sets.Count; s++)
            {
                var setIndex = sets[s].SetIndex;
                _pool.AllocateDescriptorSets(_layout, setIndex, _sets.AsSpan((int)setIndex, 1));
            }
            Charge(Part.Allocate);

            // What changes this frame, put into the table by the names the shader gives it: the
            // state the last frame wrote is read, the other half is written, and the constants are
            // this frame's.
            var slot = (int)(_frame % 2);
            Slot("previous")[0] = _stateReads[1 - slot];
            Slot("next")[0] = _stateWrites[slot];
            Slot("frameData")[0] = _frameConstants[slot];
            Charge(Part.Lookups);

            // Every range of every set, by the RangeIndex the projection gave it.
            for (var s = 0; s < sets.Count; s++)
            {
                var set = sets[s];
                for (var r = 0; r < set.Ranges.Count; r++)
                {
                    var range = set.Ranges[r];
                    _sets[set.SetIndex].UpdateRange(range.RangeIndex, 0, _bindings[set.SetIndex][range.RangeIndex]);
                }
            }
            Charge(Part.Write);

            _cmd.Barrier(in FrameToFrame);
            _cmd.SetPipelineLayout(BindPoint.COMPUTE, _layout);
            _cmd.SetPipeline(_pipeline);
            for (var s = 0; s < sets.Count; s++)
                _cmd.SetDescriptorSet(sets[s].SetIndex, _sets[sets[s].SetIndex], BindPoint.COMPUTE);
            var step = new Step(1f / 60, ParticleCount);
            _cmd.SetRootConstants(_rootConstants.Index, in step, 0, BindPoint.COMPUTE);
            Charge(Part.Bind);

            var size = _stage.ThreadGroupSize?.Resolved
                ?? throw new InvalidOperationException("the compute stage's thread-group size is specialized");
            GroupCount = checked((uint)((ParticleCount + size.X - 1) / size.X));
            _cmd.Dispatch(GroupCount);
            _cmd.End();
            Charge(Part.Dispatch);

            _signals[0] = new NriFenceSubmit(_fence, _frame);
            _queue.Submit(_commandBuffers, default, _signals);
            _fence.Wait(_frame);
            Charge(Part.Submit);
        }

        // Run charges each of its parts with what it allocated, so a failure names the part rather
        // than the frame. GC.GetAllocatedBytesForCurrentThread allocates nothing itself: the zero
        // the test measures includes every call to it.

        private enum Part { Begin, Allocate, Lookups, Write, Bind, Dispatch, Submit }

        /// <summary>
        /// Each part, and what to suspect when it allocates, in <see cref="Part"/> order.
        /// </summary>
        private static readonly string[] Suspects =
        [
            "resetting and beginning: NriCommandAllocator.Reset, NriDescriptorPool.Reset and Begin "
                + "are SharpNRI's calls alone, and its own AllocationTests measures them at zero",
            "allocating the sets: per frame that is AllocateDescriptorSets into a span the caller "
                + "owns; the overload that returns an array allocates the array on every call",
            "the lookups by name: NriPipelineProjection.Range, which "
                + "Lookups_on_a_projection_allocate_nothing also pins at zero on its own",
            "writing the ranges: a foreach over DescriptorSets or Ranges takes its enumerator "
                + "through IReadOnlyList and boxes it; index them",
            "binding: Barrier, SetPipelineLayout, SetDescriptorSet, SetPipeline and both "
                + "SetRootConstants overloads are in SharpNRI's own measured frame (the last two from "
                + "PrismNRI 3eceaf1, after this test measured them first); the root constants go in "
                + "as a struct by reference",
            "the dispatch: Dispatch and End are in SharpNRI's measured frame, so first the group "
                + "count read from ProjectedStage.ThreadGroupSize",
            "submitting and waiting: the one-command-buffer Submit(cmd, fence, value) built its "
                + "signal from an array up to PrismNRI 0.3.0-alpha (48 bytes a call); Submit over "
                + "spans never did, and from PrismNRI 2b4ccf4 neither does the one-buffer form "
                + "(measured 0 against SharpNRI 191.0.0-beta.4)",
        ];

        private readonly long[] _bytes = new long[Suspects.Length];
        private long _mark;

        private void Charge(Part part)
        {
            var now = GC.GetAllocatedBytesForCurrentThread();
            _bytes[(int)part] += now - _mark;
            _mark = now;
        }

        /// <summary>Forgets what the warm-up frame allocated.</summary>
        public void ClearMeter() => Array.Clear(_bytes);

        /// <summary>
        /// The parts that allocated since <see cref="ClearMeter"/>, with what to suspect in each.
        /// </summary>
        public string Breakdown()
        {
            var lines = new List<string>();
            for (var i = 0; i < _bytes.Length; i++)
                if (_bytes[i] != 0) lines.Add($"  {_bytes[i]:N0} bytes in {Suspects[i]}");
            return lines.Count > 0
                ? string.Join("\n", lines)
                : "  none of Run's parts: the bytes were allocated between frames";
        }

        /// <summary>
        /// The table entry for the range named <paramref name="name"/>: found through the projection
        /// by the shader's name for it, at the SetIndex and RangeIndex the projection reports.
        /// </summary>
        private NriDescriptor[] Slot(string name)
        {
            var (set, range) = _projection.Range(name)
                ?? throw new InvalidOperationException($"the projection has no range named {name}");
            return _bindings[set.SetIndex][range.RangeIndex];
        }

        private NriBuffer[] Buffers(NriDevice device, int count, ulong size, uint stride, BufferUsageBits usage)
        {
            var buffers = new NriBuffer[count];
            for (var i = 0; i < count; i++)
            {
                buffers[i] = Own(device.CreateCommittedBuffer(MemoryLocation.DEVICE,
                    new BufferDesc { size = size, structureStride = stride, usage = usage }));
            }

            return buffers;
        }

        /// <summary>
        /// A view of each buffer, of the kind the range named <paramref name="name"/> takes: what the
        /// projection says the range is, rather than what the test assumes.
        /// </summary>
        private NriDescriptor[] Views(string name, NriBuffer[] buffers, uint stride)
        {
            var (_, range) = _projection.Range(name)
                ?? throw new InvalidOperationException($"the projection has no range named {name}");
            var kind = range.Desc.descriptorType switch
            {
                DescriptorType.CONSTANT_BUFFER => BufferView.CONSTANT_BUFFER,
                DescriptorType.STRUCTURED_BUFFER => BufferView.STRUCTURED_BUFFER,
                DescriptorType.STORAGE_STRUCTURED_BUFFER => BufferView.STORAGE_STRUCTURED_BUFFER,
                var other => throw new NotSupportedException(
                    $"'{name}' is a {other} range, and this frame makes buffer views only"),
            };
            return Array.ConvertAll(buffers, b => Own(b.CreateView(kind, structureStride: stride)));
        }

        /// <summary>
        /// A pool for one of each projected set, sized by adding up the ranges' descriptor counts
        /// per type: the sizing the projection leaves to the renderer.
        /// </summary>
        private static DescriptorPoolDesc PoolFor(NriPipelineProjection projection)
        {
            var count = new uint[(int)DescriptorType.MAX_NUM];
            foreach (var set in projection.DescriptorSets)
            {
                foreach (var range in set.Ranges)
                    count[(int)range.Desc.descriptorType] += range.Desc.descriptorNum;
            }

            return new DescriptorPoolDesc
            {
                descriptorSetMaxNum = (uint)projection.DescriptorSets.Count,
                samplerMaxNum = count[(int)DescriptorType.SAMPLER],
                mutableMaxNum = count[(int)DescriptorType.MUTABLE],
                textureMaxNum = count[(int)DescriptorType.TEXTURE],
                storageTextureMaxNum = count[(int)DescriptorType.STORAGE_TEXTURE],
                inputAttachmentMaxNum = count[(int)DescriptorType.INPUT_ATTACHMENT],
                bufferMaxNum = count[(int)DescriptorType.BUFFER],
                storageBufferMaxNum = count[(int)DescriptorType.STORAGE_BUFFER],
                constantBufferMaxNum = count[(int)DescriptorType.CONSTANT_BUFFER],
                structuredBufferMaxNum = count[(int)DescriptorType.STRUCTURED_BUFFER],
                storageStructuredBufferMaxNum = count[(int)DescriptorType.STORAGE_STRUCTURED_BUFFER],
                accelerationStructureMaxNum = count[(int)DescriptorType.ACCELERATION_STRUCTURE],
            };
        }

        private T Own<T>(T obj) where T : IDisposable
        {
            _owned.Add(obj);
            return obj;
        }

        public void Dispose()
        {
            // In reverse: views before their buffers, the command buffer before its allocator, and
            // all of it before the device, whose Dispose refuses to run with children alive.
            for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
        }
    }
}
