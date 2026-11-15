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
using SharpSlang.Native;
using SharpSlang.Pipeline.NRI;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

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

    /// <summary>
    /// Measures one loop. Its body is a local function named Frame...: AllocationCoverageTests reads
    /// the IL of exactly those, so every public member a game calls after load is in one of them or
    /// named there as not per-frame. The message names the members the loop calls.
    /// </summary>
    private static void AssertZero(string what, Action frame)
    {
        var bytes = BytesPerCall(frame);
        Assert.True(bytes == 0, $"{what} allocates {bytes:N0} bytes per call; an engine may call it per draw, and it must allocate nothing.");
    }

    /// <summary>
    /// Keeps a value a loop read, without boxing it: the first version of these tests passed a
    /// DescriptorRangeDesc to GC.KeepAlive(object) and reported 40 bytes the library never allocated.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Keep<T>(T value) => Kept<T>.Value = value;

    private static class Kept<T>
    {
        // Internal rather than private: a value written only to be kept is the point of a sink,
        // and IDE0052 reports a private field nothing reads.
        internal static T? Value;
    }

    [NativeFact]
    public void Lookups_on_a_projection_allocate_nothing()
    {
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        ProjectionLookups(layout, layout.ToNri());
    }

    [NativeFact]
    public void Lookups_on_a_projection_loaded_from_a_package_allocate_nothing()
    {
        // The projection a game reads back from the package's NRI section (stored at bake time)
        // rather than computes: the same lookups, the same zero (AGENTS.md#pipeline-baking-packages-allocation).
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var package = NriPackageTests.ThroughBytes(NriPackageTests.Package(layout).WithNriProjection());
        var nri = package.ReadNriProjection()!;
        ProjectionLookups(package.Layout, nri);

        void FramePackage()
        {
            Keep(package.Section(NriPackage.SectionName));
            Keep(package.Section("nothing"));
        }

        Assert.NotNull(package.Section(NriPackage.SectionName));
        AssertZero("ShaderPackage.Section, by name", FramePackage);
    }

    private static void ProjectionLookups(ProgramLayout layout, NriPipelineProjection nri)
    {
        var position = layout.VertexInputs()[0].Type;

        void FrameLookups()
        {
            Keep(nri.Range("albedo"));
            Keep(nri.Range("nothing"));
            Keep(nri.Set(2));
            Keep(nri.Stage("fragmentMain"));
            Keep(nri.DescriptorSets[0].Range("outBuffer"));
            Keep(nri.DescriptorSets[0].Ranges[1].Desc.descriptorNum);
            Keep(nri.Layout.ShaderStages);
        }

        // The projection's static conversions, which a renderer may apply to a reflected value as
        // it binds: pure functions of an enum or a layout.
        void FrameConversions()
        {
            Keep(NriProjection.ToDescriptorType(SlangBindingType.SLANG_BINDING_TYPE_TEXTURE));
            Keep(NriProjection.ToStage(SlangStage.SLANG_STAGE_FRAGMENT));
            Keep(NriProjection.ToVertexFormat(position));
        }

        Assert.NotNull(nri.Range("albedo"));
        AssertZero("the NRI projection's lookups (Range, Set, Stage, ProjectedDescriptorSet.Range) and reads", FrameLookups);
        AssertZero("NriProjection.ToDescriptorType, ToStage, ToVertexFormat", FrameConversions);
    }

    /// <summary>
    /// What a renderer reads off a loaded reflection snapshot while it fills a constant buffer and
    /// dispatches: the uniform offsets and sizes, a matrix component, the parameters, entry points
    /// and specialization constants by name, the thread-group size, and the SPIR-V header.
    /// </summary>
    private const string LayoutShader = """
        struct Light { float4x4 transform; float3 color; float intensity; };
        ConstantBuffer<Light> light;
        RWStructuredBuffer<float4> output;
        Texture2D<float4> shadow;
        [vk::constant_id(3)] const int kTaps = 4;
        [vk::constant_id(4)] const float kScale = 0.5;

        [shader("compute")]
        [numthreads(8, 4, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            float4 v = mul(light.transform, float4(light.color * light.intensity, 1.0));
            for (int i = 0; i < kTaps; i++) v += shadow.Load(int3(i, 0, 0)) * kScale;
            output[id.x] = v;
        }
        """;

    [NativeFact]
    public void Reads_of_a_layout_loaded_from_a_package_allocate_nothing()
    {
        // The snapshot as a game has it: baked, written, read back.
        var package = NriPackageTests.ThroughBytes(NriDeviceTests.Bake(LayoutShader, "main"));
        var layout = package.Layout;
        var entryPoint = package.EntryPoints[0];
        var parameter = layout.Parameter("light") ?? throw new InvalidOperationException("no parameter named light");
        var element = (parameter.Type as ContainerLayout)?.ElementType as StructLayout
            ?? throw new InvalidOperationException($"light is a {parameter.Type.GetType().Name}, not a constant buffer of a struct");
        var field = element.Field("transform") ?? throw new InvalidOperationException("no field named transform");
        var matrix = (MatrixLayout)field.Type;
        var range = layout.DescriptorSets[0].Ranges[0];
        var constant = layout.SpecializationConstant("kTaps") ?? throw new InvalidOperationException("no specialization constant kTaps");
        var main = layout.EntryPoint("main") ?? throw new InvalidOperationException("no entry point main");
        var size = main.ThreadGroupSize ?? throw new InvalidOperationException("no thread-group size");
        var function = main.Function;
        var workgroup = entryPoint.WorkgroupSize ?? throw new InvalidOperationException("the baked entry point has no workgroup size");
        var code = entryPoint.Code;
        var offset = field.Offsets[0];
        var footprint = element.Sizes[0];

        void FrameLookups()
        {
            Keep(layout.Parameter("light"));
            Keep(layout.Parameter("nothing"));
            Keep(layout.EntryPoint("main"));
            Keep(layout.SpecializationConstant("kTaps"));
            Keep(element.Field("color"));
            Keep(package.EntryPoint("main"));
        }

        void FrameUniforms()
        {
            Keep(field.UniformOffset);
            Keep(field.IsUniformOffsetKnown);
            Keep(element.UniformSize);
            Keep(element.IsUniformSizeKnown);
            Keep(matrix.ComponentOffset(2, 3));
            Keep(offset.IsKnown);
            Keep(footprint.IsKnown);
        }

        void FrameParameters()
        {
            Keep(parameter.DescriptorSet);
            Keep(parameter.PrimaryBinding);
            Keep(parameter.OccupiesDescriptorSlot);
            Keep(parameter.IsSpecializationConstant);
            Keep(parameter.InputAttachmentIndex);
            Keep(range.IsCounter);
            Keep(constant.ByteSize);
            Keep(constant.IsFloatingPoint);
            Keep(function?.IsOverloaded);
        }

        void FrameDispatch()
        {
            Keep(size.IsSpecialized);
            Keep(size.Resolved);
            Keep(main.BaseOffset(SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT));
            Keep(workgroup.IsSpecialized);
            Keep(workgroup.X.IsSpecialized);
            Keep(workgroup.Resolve());
            Keep(SpirvModule.IsSpirv(code));
            Keep(SpirvModule.ReadVersion(code));
        }

        Assert.Equal((8ul, 4ul, 1ul), size.Resolved);
        Assert.Equal((8u, 4u, 1u), workgroup.Resolve());
        Assert.True(field.IsUniformOffsetKnown);
        Assert.NotNull(package.EntryPoint("main"));
        AssertZero("ProgramLayout.Parameter, EntryPoint, SpecializationConstant, StructLayout.Field and ShaderPackage.EntryPoint, by name", FrameLookups);
        AssertZero("the uniform offsets and sizes (FieldLayout, TypeLayout, MatrixLayout.ComponentOffset, CategoryOffset, CategorySize)", FrameUniforms);
        AssertZero("ShaderParameter's computed bindings, DescriptorRange.IsCounter, SpecializationConstant's size and kind, FunctionSignature.IsOverloaded", FrameParameters);
        AssertZero("the thread-group and workgroup sizes, EntryPointLayout.BaseOffset and the SPIR-V header reads", FrameDispatch);
    }

    [NativeFact]
    public void Reads_of_a_declaration_tree_allocate_nothing()
    {
        // The declaration tree is tooling's (ProgramLayout.Declarations), but its one computed
        // property is a plain read, so it is measured rather than excused.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        using var module = session.LoadModuleFromSource("decl", "decl.slang", LayoutShader);
        var node = module.ReflectDeclarations();

        void FrameDeclarations() => Keep(node.IsSynthetic);

        AssertZero("DeclarationNode.IsSynthetic", FrameDeclarations);
    }

    [NativeFact]
    public void Reading_the_stored_projection_is_load_time_work_and_says_how_much()
    {
        // Once per shader, like ToNri; an order of magnitude, measured beside ToNri's.
        var layout = NriProjectionTests.Reflect(Shader, "vertexMain", "fragmentMain");
        var package = NriPackageTests.ThroughBytes(NriPackageTests.Package(layout).WithNriProjection());

        var read = BytesPerCall(() => package.ReadNriProjection(), iterations: 200);

        Assert.True(read > 0 && read < 64 * 1024, $"ReadNriProjection allocated {read:N0} bytes per call");
    }

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
        var package = NriDeviceTests.Bake(FrameShader, "simulate");
        WholeFrame(package, package.Layout.ToNri());
    }

    [NriNoneFact]
    public void A_whole_frame_bound_through_a_projection_loaded_from_the_package_allocates_nothing()
    {
        // The projection stored at bake time and read back, not computed: the path a game takes.
        var package = NriPackageTests.ThroughBytes(NriDeviceTests.Bake(FrameShader, "simulate").WithNriProjection());
        WholeFrame(package, package.ReadNriProjection()!);
    }

    private static void WholeFrame(SharpSlang.Baking.ShaderPackage package, NriPipelineProjection projection)
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
        // The shape the frame is written against, pinned, so a change in how Slang places these
        // fails here rather than leaving a frame that quietly binds less.
        Assert.Equal(2, projection.DescriptorSets.Count);
        Assert.Equal((0u, 0u), (projection.DescriptorSets[0].SetIndex, projection.DescriptorSets[0].RegisterSpace));
        Assert.Equal((1u, 2u), (projection.DescriptorSets[1].SetIndex, projection.DescriptorSets[1].RegisterSpace));
        Assert.Equal((uint)Unsafe.SizeOf<Step>(), Assert.Single(projection.RootConstants).Desc.size);

        // The device on NRI's own counting allocator, as SharpNRI measures its frames: every host
        // allocation NRI makes goes through it, so the frame is held to 0 host allocations as
        // well as 0 managed bytes. The reader and the projection make none of their own: they
        // declare no native call (AllocationCoverageTests.The_packages_a_game_ships_declare_no_native_call).
        var allocator = new NriCountingAllocator();
        var messages = 0;
        using var device = NriDevice.Create(new NriDeviceOptions
        {
            GraphicsApi = GraphicsAPI.NONE,
            Allocator = allocator,
            MessageHandler = (in NriMessage _) => Interlocked.Increment(ref messages),
        });

        long bytes, hostAllocations, hostReallocations;
        using (var frame = new ProjectedFrame(device, projection, package.EntryPoints[0].ToNriShaderDesc()))
        {
            // One warm-up frame, as SharpNRI's own measurement has: the JIT, static constructors and
            // first calls allocate once, and that is not what is being measured.
            frame.Run();
            frame.ClearMeter();
            var messagesBefore = Volatile.Read(ref messages);
            var (allocationsBefore, reallocationsBefore) = (allocator.Allocations, allocator.Reallocations);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Frames; i++) frame.Run();
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            hostAllocations = allocator.Allocations - allocationsBefore;
            hostReallocations = allocator.Reallocations - reallocationsBefore;

            // 1,000 particles at the 64 threads per group the frame read off the projection.
            Assert.Equal(16u, frame.GroupCount);
            Assert.True(Volatile.Read(ref messages) == messagesBefore,
                "NRI reported messages during the measured frames. NONE should report nothing, and each "
                + "message allocates in the handler, where it would be counted as the frame's.");
            Assert.True(bytes == 0,
                $"{Frames} frames bound through SharpSlang.Pipeline.NRI's projection allocated {bytes:N0} managed "
                + $"bytes, {bytes / (double)Frames:N1} per frame. An engine runs this every frame and it "
                + $"must allocate nothing. By part of the frame, with what to suspect in each:\n"
                + frame.Breakdown());
        }

        // Measured: 0 host allocations and 0 reallocations over the 300 frames (workstation, NONE,
        // SharpNRI 192.0.0-beta.1; NONE's objects are static dummies, and SharpNRI measures the
        // same of its own frame).
        Assert.True(hostAllocations == 0 && hostReallocations == 0,
            $"{Frames} frames made {hostAllocations} host allocations and {hostReallocations} reallocations through NRI's allocator; a frame must make none.");

        // The managed bytes, asserted above. Measured: 0 over the 300 frames after the one warm-up frame. They take about 0.2 ms
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
        // SharpNRI 192.0.0-beta.1 the same planted fault measures 0, so it no longer fails. A
        // foreach over DescriptorSets and each set's Ranges in
        // place of the indexed loops: 36,000 bytes, 120 per frame, three boxed List<T> enumerators.
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
            // NRI's own wait, not GpuWait: this frame runs on the NONE backend, whose Wait is an
            // empty function and whose fence value is always 0, so a poll would never return
            // (GpuWait says where that was read). What is measured here is the call's allocation.
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
                + "(measured 0 against SharpNRI 192.0.0-beta.1)",
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
