// The NRI projection, put in front of a device.
//
// NriProjectionTests pins what the projection says and checks that it says the same thing as the
// Vulkan projection. Both of those are arithmetic over one reflection snapshot, so neither can tell
// whether what the projection says is something NRI will *take*: a range flag NRI's Vulkan backend
// reads differently than the comment beside it claims, a register space with a hole in it, a root
// constant block whose size NRI rounds, a vertex format NRI's own conversion table maps to a
// different VkFormat than the Vulkan projection chose. All of those survive agreement. This file is
// where NRI, and behind it the Vulkan validation layers, decide.
//
// Four questions, cheapest first:
//
//   1. Does PrismNRI marshal every desc the projection emits? The NONE backend validates nothing
//      and allocates nothing, so what it answers is purely the managed side — array pins, counts,
//      the tree of native structs — and it answers it on every CI leg that runs Windows.
//   2. Does a validated Vulkan device accept the layout and the pipelines built from it, for the
//      whole corpus? The VulkanCorpusValidationTests twin, with NRI between the projection and the
//      driver instead of hand-written Vortice calls.
//   3. Does data reach the shader through what the projection named? A layout the layers accept
//      can still bind a buffer to the wrong range index or a root constant to the wrong block, and
//      only a dispatch that reads back what the shader wrote can tell. Two of those: a descriptor
//      and a root constant.
//   4. Does NRI's own NRI-to-VkFormat table agree with the Vulkan projection's choice for every
//      vertex attribute and fragment output? The one place a third party judges the format mapping.
//   5. Questions 2 and 3 again on D3D12, with the shader compiled to DXIL. A different oracle, not
//      a repeat: D3D12 checks the root signature against the registers the DXIL reads when it
//      creates a pipeline, D3D links stages by register and inputs by semantic, and a DXIL layout
//      numbers b, t, s and u registers separately. Its first run found four projection defects the
//      Vulkan leg cannot see, since fixed; D3D12ProjectionDefects records them and waits for the next.
//
// A case that cannot be built states why, in a dictionary below, and the dictionaries are checked:
// a stale entry is a failing test, so they cannot quietly grow to excuse a regression.

using PrismNRI;
using PrismNRI.Native;
using PrismNRI.Testing;
using PrismSlang.Baking;
using PrismSlang.NRI;
using PrismSlang.Reflection;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public sealed class NriDeviceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>
    /// The cap every test hands the projection, so <c>descriptor-arrays</c>' unbounded texture
    /// array becomes a real binding instead of a refusal. 64 is the number NriProjectionTests uses,
    /// so the two files project the same layout.
    /// </summary>
    private static readonly NriProjectionOptions Capped = new() { UnboundedArrayCap = 64 };

    /// <summary>Corpus cases the NRI projection refuses, and the construct each message must name.</summary>
    /// <remarks>
    /// Restated rather than shared with NriProjectionTests' own list, on purpose: there it is a
    /// fact about the projection, here it is a fact about what this driver does not build, and a
    /// change to one should be a decision about the other rather than something that happens to
    /// it. Both are exact, so a refusal that stops happening fails here too.
    /// </remarks>
    private static readonly Dictionary<string, string> Refused = new(StringComparer.Ordinal)
    {
        ["descriptor-kinds"] = "combined texture sampler",
        ["wide-vertex-inputs"] = "64-bit",
    };

    /// <summary>
    /// Corpus cases whose pipeline this cannot build, and why. The pipeline layout is still built
    /// for every one of them, unless the device lacks the tier or feature it names a stage of —
    /// see <see cref="LayoutUnbuildable"/>.
    /// </summary>
    /// <remarks>
    /// Each of these is a property of the pipeline shape or of Slang's output, not of the device.
    /// The device's limits are decided separately, from <c>device.Desc</c>, because they differ
    /// between a CI runner and a workstation and are not something to record as permanent.
    /// </remarks>
    private static readonly Dictionary<string, string> PipelineOutOfReach = new(StringComparer.Ordinal)
    {
        ["mesh"] =
            "a mesh pipeline is not a graphics pipeline with a vertex stage, and NRI creates one "
            + "through its mesh shader extension rather than CreateGraphicsPipeline. This harness "
            + "builds compute and vertex-fed graphics pipelines; the layout is still built.",
        ["amplification"] =
            "a task shader is half of a mesh pipeline, so it is out for the same reason as `mesh`.",
        ["ray-tracing"] =
            "a ray tracing pipeline needs NRI's ray tracing extension, a shader group per stage "
            + "and a shader binding table. None of that is a layout question; the layout, with its "
            + "acceleration structure range, is still built when the device has the tier.",
        ["tessellation"] =
            "hull and domain with no vertex stage. A graphics pipeline needs one, and a supplied "
            + "vertex stage would have to produce this hull shader's exact input patch — which is "
            + "writing a second shader to test the first. The layout is still built.",
        ["geometry"] =
            "a geometry stage with no vertex stage, for the same reason as `tessellation`: the "
            + "supplied vertex stage would have to match the geometry shader's input primitive.",
        ["subpass-inputs"] =
            "an input attachment is read through a render pass or Vulkan 1.4's "
            + "dynamicRenderingLocalRead, and NRI renders with plain dynamic rendering. A property "
            + "of how NRI draws, not of the projection, so the layout — the part under test — is "
            + "built and accepted, and the pipeline is not attempted.",
    };

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    [Fact]
    public void Every_declared_refusal_and_exclusion_names_a_shader_that_exists()
    {
        // The lists are only trustworthy while they are exact. A renamed corpus case would leave a
        // dead entry that excuses nothing, and a typo would silently exclude nothing while reading
        // as though it excluded something.
        var unknown = Refused.Keys.Concat(PipelineOutOfReach.Keys).Concat(D3D12PipelineOutOfReach.Keys)
            .Concat(D3D12ProjectionDefects.Keys)
            .Where(name => ShaderCorpus.All.All(c => c.Name != name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "Refused, PipelineOutOfReach, D3D12PipelineOutOfReach or D3D12ProjectionDefects names "
            + "shaders the corpus does not have: "
            + string.Join(", ", unknown));

        // And a refused case is not also out of reach: the refusal is asserted, the exclusion only
        // explained, and a case in both would have its assertion skipped by whichever ran first.
        Assert.Empty(Refused.Keys.Intersect(PipelineOutOfReach.Keys));
        Assert.Empty(Refused.Keys.Intersect(D3D12PipelineOutOfReach.Keys));

        // Nor is a case DXIL cannot compile also out of reach on D3D12: the D3D12 theory returns at
        // the compile, so an exclusion behind it would explain something that never happens.
        // wide-vertex-inputs is the one case in both Refused and CannotCompile, and that overlap
        // is right: the SPIR-V legs reach its refusal, the DXIL leg never gets that far.
        Assert.Empty(DxilTests.CannotCompile.Keys.Intersect(D3D12PipelineOutOfReach.Keys));

        // A pinned defect is asserted to be refused by D3D12, so it must be a case the theory
        // actually hands D3D12 a pipeline for: compiled, projected, and not excluded.
        Assert.Empty(D3D12ProjectionDefects.Keys.Intersect(
            DxilTests.CannotCompile.Keys.Concat(Refused.Keys).Concat(D3D12PipelineOutOfReach.Keys)));
    }

    // ---------------------------------------------------------------- 1. the NONE backend

    [NriNoneTheory]
    [MemberData(nameof(Cases))]
    public void A_pipeline_layout_built_from_the_projection_is_accepted_by_the_none_backend(string caseName)
    {
        // NONE accepts anything, so this is not a question about the layout's legality — test 2 is.
        // It is the question that comes before it: PrismNRI turns the managed desc into a tree of
        // native arrays (one pinned ranges array per set, fixed root constants, counts read off the
        // arrays), and this asks that every shape the projection emits survives that — empty sets,
        // a set per parameter block, a gap in register space, a root constant block with no sets.
        // It is also the half that runs on hosted CI, where there is no Vulkan driver.
        var c = ShaderCorpus.Case(caseName);
        var layout = NriProjectionTests.Reflect(c.Source, [.. c.EntryPoints]);

        if (Refused.TryGetValue(caseName, out var construct))
        {
            var e = Assert.Throws<NotSupportedException>(() => layout.ToNri(Capped));
            Assert.Contains(construct, e.Message);
            return;
        }

        var projection = layout.ToNri(Capped);

        using var fixture = NriTestDevice.Create(GraphicsAPI.NONE);
        using var created = fixture.Device.CreatePipelineLayout(projection.Layout);

        // The desc a layout reports is the object it was created from, not a copy rebuilt from the
        // native side — so a host that keeps the layout can keep reading the projection through it.
        Assert.Same(projection.Layout, created.Desc);
        Assert.Empty(fixture.Errors);
    }

    // ---------------------------------------------------------------- 2. the corpus, on Vulkan

    [NriVulkanTheory]
    [MemberData(nameof(Cases))]
    public void A_pipeline_built_from_the_projection_passes_nri_and_vulkan_validation(string caseName)
    {
        var shaderCase = ShaderCorpus.Case(caseName);
        var (source, entryPointNames) = VulkanCorpusValidationTests.PairedWithAVertexStage(shaderCase);

        // So each stage's module names its entry point as the shader does. NRI hands the name to
        // pName, and without this every stage is `main` — legal, but a two-stage pipeline would
        // then be two modules that cannot be told apart in a validation message.
        var compiled = Compile(caseName, source, entryPointNames, new TargetOptions { UseEntryPointNameInSpirv = true });

        if (Refused.TryGetValue(caseName, out var construct))
        {
            var e = Assert.Throws<NotSupportedException>(() => compiled.Layout.ToNri(Capped));
            Assert.Contains(construct, e.Message);
            return;
        }

        var projection = compiled.Layout.ToNri(Capped);
        Assert.Equal(entryPointNames.Count, projection.Stages.Count);

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        var device = fixture.Device;

        if (LayoutUnbuildable(projection, device) is { } reason)
        {
            // A device limit rather than a permanent exclusion: the same shader is buildable on a
            // workstation and not on lavapipe. Written to the output because a test that covers
            // nothing and a test that covers everything are both green, and the log is the only
            // thing that tells them apart.
            output.WriteLine($"layout not built on this device — {reason}");
            return;
        }

        using (var layout = device.CreatePipelineLayout(projection.Layout))
        {
            Assert.Same(projection.Layout, layout.Desc);

            if (PipelineOutOfReach.TryGetValue(caseName, out var why))
                output.WriteLine($"pipeline layout built; no pipeline — {why}");
            else
                BuildPipelines(device, layout, projection, compiled.Code, SpirvShaderDesc);
        }

        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());

        // No message is filtered. The Vulkan driver excuses two VUIDs (pCode-08740 and -08742, a
        // module capability or extension the device lacks); on the RTX 4070 SUPER, with the 55
        // device extensions NRI enables there, neither appeared for any case, so there is nothing
        // here to excuse and no filter to drift into swallowing something.
        //
        // And the oracle was watched failing before it was trusted to pass, because a first run
        // where all 32 cases are green is exactly what a switched-off layer looks like. Two faults
        // were planted by hand and reverted: every range's stage visibility set to MESH_SHADER
        // failed 21 of the 32 — every case with a descriptor range, layout-only ray-tracing and
        // subpass-inputs included — and pointing each vertex attribute at a stream that does not
        // exist failed the two cases that have attributes (system-values, vertex-interface) with
        // VUID-VkPipelineVertexInputStateCreateInfo-binding errors, each printed in the failure.
        var errors = fixture.Errors.ToList();
        Assert.True(errors.Count == 0,
            $"NRI or the Vulkan validation layers rejected what was built from PrismSlang's NRI "
            + $"projection of the '{caseName}' shader. Nothing was adjusted on the way through, so "
            + $"each of these is something the projection said:\n"
            + string.Join("\n", fixture.Messages.Select(m => "  " + m)));
    }

    /// <summary>Why this device cannot take the layout at all, or null.</summary>
    /// <remarks>
    /// Decided from <c>device.Desc</c> before anything is created, rather than by filtering the
    /// messages afterwards. VulkanCorpusValidationTests learned that the hard way: its first filter
    /// matched message prose, two implementations worded the same rejection differently, and a
    /// filter that matches prose is one rewording away from swallowing a real defect. A tier or a
    /// feature bit does not get reworded.
    ///
    /// Measured on the RTX 4070 SUPER: every condition below is satisfied, so no case is skipped
    /// here on a workstation — all 30 projectable cases build their layout, 23 build pipelines on
    /// top, and the other 7 are the PipelineOutOfReach entries. The conditions exist for lavapipe,
    /// which has no ray tracing and no mesh shaders.
    ///
    /// The D3D12 theory asks the same of its device. Measured on the RTX's D3D12 device and on
    /// WARP (PRISMNRI_TEST_SOFTWARE=1, what hosted Windows runs): neither trips any condition —
    /// WARP reports mesh shaders and a ray tracing tier — so on both, the mesh, amplification and
    /// ray-tracing layouts are built, and the two devices' outcomes match case for case.
    /// </remarks>
    private static string? LayoutUnbuildable(NriPipelineProjection projection, NriDevice device)
    {
        const StageBits RayTracingStages =
            StageBits.RAYGEN_SHADER | StageBits.ANY_HIT_SHADER | StageBits.CLOSEST_HIT_SHADER
            | StageBits.MISS_SHADER | StageBits.INTERSECTION_SHADER | StageBits.CALLABLE_SHADER;

        var desc = device.Desc;
        var stages = projection.Layout.ShaderStages;
        var ranges = projection.DescriptorSets.SelectMany(s => s.Ranges).ToList();

        if (desc.tiers.rayTracing == 0
            && ((stages & RayTracingStages) != 0
                || ranges.Any(r => r.Desc.descriptorType == DescriptorType.ACCELERATION_STRUCTURE)))
        {
            return "the layout names ray tracing stages or an acceleration structure range, and "
                 + "the device reports tiers.rayTracing = 0, so NRI has not enabled the extensions "
                 + "that make either a token Vulkan recognises";
        }

        if (desc.features.meshShader == 0 && (stages & (StageBits.MESH_SHADER | StageBits.TASK_SHADER)) != 0)
            return "the layout names a mesh or task stage and the device has no mesh shaders";

        if (desc.features.geometryShader == 0 && (stages & StageBits.GEOMETRY_SHADER) != 0)
            return "the layout names a geometry stage and the device has no geometry shaders";

        if (desc.features.tessellationShader == 0
            && (stages & (StageBits.TESS_CONTROL_SHADER | StageBits.TESS_EVALUATION_SHADER)) != 0)
            return "the layout names a tessellation stage and the device has no tessellation shaders";

        return null;
    }

    /// <summary>Every pipeline the program describes that this harness knows how to build.</summary>
    /// <remarks>
    /// <paramref name="shaderDesc"/> is the one thing that differs between the backends: a SPIR-V
    /// module is named by the entry point inside it, a DXIL container is not named at all.
    /// </remarks>
    private void BuildPipelines(
        NriDevice device, NriPipelineLayout layout, NriPipelineProjection projection, byte[][] code,
        Func<StageBits, byte[], NriShaderDesc> shaderDesc)
    {
        var built = 0;

        // Each compute entry point is its own pipeline sharing the program-wide layout. A layout
        // may declare ranges for stages a pipeline does not have; it may not omit one the pipeline
        // does have, which is the direction being tested.
        for (var i = 0; i < projection.Stages.Count; i++)
        {
            if (projection.Stages[i].Stage != StageBits.COMPUTE_SHADER) continue;

            using var pipeline = device.CreateComputePipeline(new NriComputePipelineDesc
            {
                PipelineLayout = layout,
                Shader = shaderDesc(projection.Stages[i].Stage, code[i]),
            });
            built++;
        }

        // A graphics pipeline whenever there is a vertex stage, with the fragment stage if there is
        // one. Vertex-only is a legal pipeline with no colour attachments, and the corpus's
        // vertex-interface — the richest vertex input it has, matrix attributes and all — is
        // vertex-only; insisting on both stages would leave its attributes untested by a device.
        var vertex = IndexOf(projection, StageBits.VERTEX_SHADER);
        if (vertex >= 0)
        {
            var fragment = IndexOf(projection, StageBits.FRAGMENT_SHADER);

            NriShaderDesc[] shaders = fragment >= 0
                ? [shaderDesc(StageBits.VERTEX_SHADER, code[vertex]), shaderDesc(StageBits.FRAGMENT_SHADER, code[fragment])]
                : [shaderDesc(StageBits.VERTEX_SHADER, code[vertex])];

            using var pipeline = device.CreateGraphicsPipeline(new NriGraphicsPipelineDesc
            {
                PipelineLayout = layout,
                VertexInput = VertexInput(projection),
                InputAssembly = new InputAssemblyDesc { topology = Topology.TRIANGLE_LIST },
                Rasterization = new RasterizationDesc { fillMode = FillMode.SOLID, cullMode = CullMode.NONE },
                OutputMerger = new NriOutputMergerDesc { Colors = ColourAttachments(projection) },
                Shaders = shaders,
            });
            built++;
        }

        // A case that reaches here and builds nothing is a hole in the harness, not a pass: every
        // case without a compute or vertex stage is meant to be in PipelineOutOfReach with a reason.
        Assert.True(built > 0,
            "the program has neither a compute nor a vertex stage, so nothing was built; add the "
            + "case to PipelineOutOfReach with the reason, or teach this harness its pipeline type");
        output.WriteLine($"pipeline layout and {built} pipeline(s) built");
    }

    private static int IndexOf(NriPipelineProjection projection, StageBits stage)
    {
        for (var i = 0; i < projection.Stages.Count; i++)
            if (projection.Stages[i].Stage == stage) return i;
        return -1;
    }

    /// <summary>A shader desc named as the emitted module names its entry point.</summary>
    /// <remarks>
    /// The name comes out of the bytes, not the projection. The projection carries the Slang
    /// function name and deliberately not the module's — that is a property of the emitted bytes
    /// (see ProjectedStage) — and pName has to match the bytes. The same reading the Vulkan driver
    /// does.
    /// </remarks>
    private static NriShaderDesc SpirvShaderDesc(StageBits stage, byte[] code) =>
        new(stage, code, SpirvModule.Read(code).EntryPoints[0].Name);

    /// <summary>Each attribute in its own stream, at offset zero.</summary>
    /// <remarks>
    /// How attributes pack into streams is a renderer decision the projection leaves at zero for
    /// <c>with</c> (see ProjectedVertexAttribute). One stream per attribute adds the fewest
    /// assumptions of this harness's own, and a 128-byte stride covers the widest attribute any
    /// format can describe.
    /// </remarks>
    private static NriVertexInputDesc? VertexInput(NriPipelineProjection projection)
    {
        if (projection.VertexAttributes.Count == 0) return null;

        return new NriVertexInputDesc
        {
            Attributes = projection.VertexAttributes
                .Select((a, i) => a.Desc with { StreamIndex = (ushort)i })
                .ToArray(),
            Streams = projection.VertexAttributes
                .Select((_, i) => new VertexStreamDesc
                {
                    bindingSlot = (ushort)i,
                    stepRate = VertexStreamStepRate.PER_VERTEX,
                    stride = 128,
                })
                .ToArray(),
        };
    }

    /// <summary>
    /// One colour attachment per fragment output location, with a format of the right numeric
    /// class.
    /// </summary>
    /// <remarks>
    /// The layers check that a fragment shader writing integers is not pointed at a float
    /// attachment, so the class has to come from what the shader writes. The width does not: a
    /// shader returning <c>float4</c> writes correctly into <c>RGBA8_UNORM</c>, which is the
    /// distinction ProjectedFragmentOutput.Format documents. A location the shader skips stays
    /// <c>UNKNOWN</c>, which NRI passes on as <c>VK_FORMAT_UNDEFINED</c> — how dynamic rendering
    /// spells an unused attachment.
    /// </remarks>
    private static ColorAttachmentDesc[] ColourAttachments(NriPipelineProjection projection)
    {
        if (projection.FragmentOutputs.Count == 0) return [];

        var colours = new ColorAttachmentDesc[projection.FragmentOutputs.Max(o => o.Location) + 1];
        foreach (var o in projection.FragmentOutputs)
        {
            var name = o.Format.ToString();
            colours[o.Location] = new ColorAttachmentDesc
            {
                format = name.EndsWith("_UINT", StringComparison.Ordinal) ? Format.RGBA8_UINT
                    : name.EndsWith("_SINT", StringComparison.Ordinal) ? Format.RGBA8_SINT
                    : Format.RGBA8_UNORM,
                colorWriteMask = ColorWriteBits.RGBA,
            };
        }

        return colours;
    }

    // ---------------------------------------------------------------- 3. data reaches the shader

    [NriVulkanFact]
    public void A_compute_dispatch_bound_through_the_projection_writes_what_the_shader_says()
    {
        // End to end along the path an engine takes: bake, ship, project the baked layout, bind by
        // the names the projection reports, dispatch, read back. Every index used below — the set
        // index, the range index, the shader desc's entry point — comes out of PrismSlang; none is
        // written by hand, so a wrong one is a wrong projection rather than a wrong test.
        //
        // compute-fixed writes 1 to cfOutput[tid.x] from an 8x4x2 group. One group is dispatched,
        // so tid.x runs 0..7 and every one of the eight floats is written (by eight threads each,
        // all writing the same value). A zero anywhere is a descriptor that did not reach the
        // shader.
        const int Count = 8;
        var c = ShaderCorpus.Case("compute-fixed");

        // Default target options: the module calls its entry point `main`, and the shader desc
        // carries that from BakedEntryPoint.ModuleEntryPointName rather than the Slang name.
        var package = Bake(c.Source, c.EntryPoints[0]);
        var shader = package.EntryPoints[0].ToNriShaderDesc();
        Assert.Equal(StageBits.COMPUTE_SHADER, shader.Stage);

        var projection = package.Layout.ToNri(Capped);
        var (set, range) = projection.Range("cfOutput")
            ?? throw new InvalidOperationException("the projection has no range named cfOutput");
        Assert.Equal(DescriptorType.STORAGE_STRUCTURED_BUFFER, range.Desc.descriptorType);

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        var words = DispatchOnce<float>(fixture.Device, projection, shader, set, range, Count, sizeof(float),
            record: null);

        for (var i = 0; i < Count; i++) Assert.Equal(1.0f, words[i]);
        RequireNoErrors(fixture);
    }

    /// <summary>
    /// One <c>uint</c> of push constants and a buffer to write it to, for the root-constant
    /// dispatches on both backends: the same source, a SPIR-V push-constant range on Vulkan and a
    /// promoted <c>b0</c> on D3D12.
    /// </summary>
    private const string PushConstantShader = """
        struct Pc { uint value; };
        [vk::push_constant] ConstantBuffer<Pc> pc;
        RWStructuredBuffer<uint> outBuf;

        [shader("compute")]
        [numthreads(64, 1, 1)]
        void main(uint id : SV_DispatchThreadID) { outBuf[id] = pc.value + id; }
        """;

    [NriVulkanFact]
    public void Root_constants_projected_from_a_push_constant_block_reach_the_shader()
    {
        // The other channel. A root constant is not a descriptor, so nothing in the test above
        // exercises it: what is checked is that the block's Index is the rootConstantIndex NRI
        // wants, that its size and stages make the push constant range the module reads, and that
        // the value lands at offset 0 of the block. Each thread adds its own id, so a value that
        // arrived as zero, or a write landing at the wrong index, is visible per element.
        const int Count = 64;
        const uint Value = 0xC0FFEE00u;

        var package = Bake(PushConstantShader, "main");
        var shader = package.EntryPoints[0].ToNriShaderDesc();
        var projection = package.Layout.ToNri(Capped);

        var block = Assert.Single(projection.RootConstants);
        Assert.Equal(StageBits.COMPUTE_SHADER, block.Desc.shaderStages);
        // Measured: index 0, size 4 — the one uint and nothing more. Not rounded to 16 as the
        // fragment-stage block in NriProjectionTests is, because that block's size comes from a
        // float4x4 member's alignment and this one has none wider than four bytes.
        output.WriteLine($"root constant block '{block.Name}': index {block.Index}, size {block.Desc.size}");

        var (set, range) = projection.Range("outBuf")
            ?? throw new InvalidOperationException("the projection has no range named outBuf");

        // A local, because SetRootConstants takes the value by reference and a const has no address.
        var value = Value;

        using var fixture = NriTestDevice.Create(GraphicsAPI.VK);
        var words = DispatchOnce<uint>(fixture.Device, projection, shader, set, range, Count, sizeof(uint),
            record: cmd => cmd.SetRootConstants(block.Index, in value, 0, BindPoint.COMPUTE));

        for (var i = 0; i < Count; i++) Assert.Equal(Value + (uint)i, words[i]);
        RequireNoErrors(fixture);
    }

    /// <summary>
    /// Creates the layout and pipeline from the projection, binds one storage structured buffer to
    /// <paramref name="range"/>, dispatches one group and returns what the shader wrote.
    /// </summary>
    private static T[] DispatchOnce<T>(
        NriDevice device,
        NriPipelineProjection projection,
        NriShaderDesc shader,
        ProjectedDescriptorSet set,
        ProjectedDescriptorRange range,
        int count,
        uint stride,
        Action<NriCommandBuffer>? record)
        where T : unmanaged
    {
        var size = (ulong)count * stride;

        using var layout = device.CreatePipelineLayout(projection.Layout);
        using var pipeline = device.CreateComputePipeline(new NriComputePipelineDesc
        {
            PipelineLayout = layout,
            Shader = shader,
        });
        using var pool = device.CreateDescriptorPool(new DescriptorPoolDesc
        {
            descriptorSetMaxNum = 1,
            storageStructuredBufferMaxNum = 1,
        });
        using var buffer = device.CreateCommittedBuffer(MemoryLocation.DEVICE, new BufferDesc
        {
            size = size,
            structureStride = stride,
            usage = BufferUsageBits.SHADER_RESOURCE_STORAGE,
        });
        using var view = buffer.CreateView(BufferView.STORAGE_STRUCTURED_BUFFER, structureStride: stride);
        using var readback = device.CreateCommittedBuffer(MemoryLocation.HOST_READBACK, new BufferDesc { size = size });

        var descriptors = pool.AllocateDescriptorSet(layout, set.SetIndex);
        descriptors.Update(range.RangeIndex, 0, view);

        var nothing = new AccessStage { access = AccessBits.NONE, stages = StageBits.NONE };
        var storage = new AccessStage { access = AccessBits.SHADER_RESOURCE_STORAGE, stages = StageBits.COMPUTE_SHADER };
        var copySource = new AccessStage { access = AccessBits.COPY_SOURCE, stages = StageBits.COPY };

        var queue = device.GetQueue(QueueType.GRAPHICS);
        using var allocator = queue.CreateCommandAllocator();
        using var cmd = allocator.CreateCommandBuffer();
        using var fence = device.CreateFence();

        cmd.Begin(pool);
        cmd.Barrier(new NriBufferBarrier(buffer, nothing, storage));
        cmd.SetPipelineLayout(BindPoint.COMPUTE, layout);
        cmd.SetPipeline(pipeline);
        cmd.SetDescriptorSet(set.SetIndex, descriptors, BindPoint.COMPUTE);
        record?.Invoke(cmd);
        cmd.Dispatch(1);
        cmd.Barrier(new NriBufferBarrier(buffer, storage, copySource));
        cmd.CopyBuffer(readback, 0, buffer, 0, size);
        cmd.End();

        queue.Submit(cmd, fence, 1);
        WaitFor(fence, 1);

        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(readback.Map()).ToArray();
        readback.Unmap();

        Assert.True(words.Length >= count, $"the readback mapped {words.Length} elements, expected {count}");
        return words;
    }

    /// <summary>
    /// Returns once the fence has reached the value; fails after two minutes.
    /// </summary>
    /// <remarks>
    /// Not <c>NriFence.Wait</c>. NRI's wait gives up after its own five-second timeout, and
    /// PrismNRI reports that as an exception — but the reason it exists is lavapipe JIT-compiling
    /// shaders while it executes the command buffer, which on a cold cache and a loaded runner
    /// passes five seconds. PrismNRI's GpuWork.Wait polls for the same reason, with the same cap.
    /// </remarks>
    private static void WaitFor(NriFence fence, ulong value)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (fence.Value < value)
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(2))
                Assert.Fail($"the GPU work did not reach fence value {value} in 2 minutes (the fence is at {fence.Value})");
            Thread.Sleep(1);
        }
    }

    private void RequireNoErrors(NriTestDevice fixture)
    {
        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());

        var errors = fixture.Errors.ToList();
        Assert.True(errors.Count == 0,
            "NRI or the Vulkan validation layers reported errors:\n"
            + string.Join("\n", fixture.Messages.Select(m => "  " + m)));
    }

    // ---------------------------------------------------------------- 4. NRI's format table

    [NriNoneFact]
    public void Projected_vertex_formats_agree_with_nris_own_vulkan_conversion_table()
    {
        // NriProjectionTests compares the two projections' format *names*, derived one from the
        // other by string surgery — which proves they agree with each other and nothing more. NRI
        // ships its own Format-to-VkFormat table, used by its Vulkan backend for exactly these
        // attributes and attachments, and VulkanFormat values are VkFormat numbers by construction
        // (VulkanEnumTests pins them against vk.xml). So this asks a third party: for every vertex
        // attribute and fragment output in the corpus, is the format the NRI projection chose the
        // one that becomes the VkFormat the Vulkan projection chose?
        //
        // A NONE fact rather than a Vulkan one, deliberately: nriConvertNRIFormatToVK is a static
        // table in NRI.dll and needs no device, so gating it on a Vulkan driver would switch it off
        // on hosted CI for nothing.
        var mismatches = new List<string>();
        var compared = 0;
        var unknown = 0;

        foreach (var c in ShaderCorpus.All)
        {
            if (Refused.ContainsKey(c.Name)) continue;

            var layout = NriProjectionTests.Reflect(c.Source, [.. c.EntryPoints]);
            var nri = layout.ToNri(Capped);
            var vulkan = layout.ToVulkan();

            var vertexFormats = vulkan.VertexInputs.ToDictionary(v => v.Name, v => v.Format);
            foreach (var a in nri.VertexAttributes)
                Compare(c.Name, "vertex attribute", a.Name, a.Desc.Format, vertexFormats);

            var outputFormats = vulkan.FragmentOutputs.ToDictionary(o => o.Name, o => o.Format);
            foreach (var o in nri.FragmentOutputs)
                Compare(c.Name, "fragment output", o.Name, o.Format, outputFormats);
        }

        output.WriteLine($"{compared} formats compared through NRI's table, {unknown} UNKNOWN outputs skipped");

        // A comparison that compared nothing would be green. Measured: 30 formats compared, every
        // one agreeing, and no UNKNOWN output in the corpus. The floor is that number, so a loop
        // that stops finding attributes fails rather than shrinking quietly; a corpus that grows
        // only raises the count.
        Assert.True(compared >= 30, $"only {compared} formats were compared; the corpus had 30 when this was written");
        Assert.True(mismatches.Count == 0,
            "NRI's own conversion table disagrees with the Vulkan projection:\n" + string.Join("\n", mismatches));

        void Compare(string shader, string kind, string name, Format format, Dictionary<string, VulkanFormat> vulkanFormats)
        {
            // UNKNOWN is the projection saying NRI has no format for this type (see
            // ProjectedFragmentOutput.Format); there is nothing to put through the table.
            if (format == Format.UNKNOWN)
            {
                unknown++;
                return;
            }

            if (!vulkanFormats.TryGetValue(name, out var expected))
            {
                mismatches.Add($"  {shader}: {kind} '{name}' is in the NRI projection and not the Vulkan one");
                return;
            }

            compared++;
            var converted = PrismNRI.Nri.ConvertNRIFormatToVK(format);
            if (converted != (uint)expected)
                mismatches.Add($"  {shader}: {kind} '{name}' — NRI {format} converts to VkFormat {converted}, "
                             + $"the Vulkan projection chose {expected} ({(uint)expected})");
        }
    }

    // ---------------------------------------------------------------- 5. the same, on D3D12

    /// <summary>
    /// Corpus cases whose pipeline the D3D12 leg cannot build, and why. Each is a property of the
    /// pipeline shape, the same way <see cref="PipelineOutOfReach"/>'s are; what DXIL cannot
    /// express at all is <see cref="DxilTests.CannotCompile"/>, and the device's limits are
    /// <see cref="LayoutUnbuildable"/>.
    /// </summary>
    /// <remarks>
    /// Shorter than the Vulkan list by one, and measured rather than assumed:
    /// <c>subpass-inputs</c> never reaches a device because dxc refuses <c>SubpassInput</c>, so it
    /// is in CannotCompile instead. <c>interpolation-modifiers</c> used to be the second: on
    /// slang-compiler 2026.14.1 it was out on Vulkan because Slang's SPIR-V forgot a capability, a
    /// problem DXIL never had. 2026.21 declares it, and the pipeline is built on both backends now.
    /// </remarks>
    private static readonly Dictionary<string, string> D3D12PipelineOutOfReach = new(StringComparer.Ordinal)
    {
        ["mesh"] = "a mesh pipeline, which NRI creates through its mesh shader extension; see PipelineOutOfReach.",
        ["amplification"] = "half of a mesh pipeline; see PipelineOutOfReach.",
        ["ray-tracing"] =
            "a ray tracing state object with shader groups and a shader table; see PipelineOutOfReach. "
            + "The layout, with its acceleration structure range at a t register, is still built.",
        ["tessellation"] = "hull and domain with no vertex stage; see PipelineOutOfReach.",
        ["geometry"] = "a geometry stage with no vertex stage; see PipelineOutOfReach.",
    };

    /// <summary>
    /// Corpus cases where D3D12 refuses what the projection says, and the words it refuses with:
    /// defects in PrismSlang, pinned so that each fix is a red test rather than a silent change.
    /// Empty now, and kept because the mechanism is the point: the next defect the D3D12 leg finds
    /// goes here with D3D12's own words, and leaves when it is fixed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first run of this leg found four, none visible on Vulkan, and all four are fixed in
    /// the walker and the shared helper rather than in this file. Recorded here because they are
    /// what this oracle is for:
    /// </para>
    /// <list type="bullet">
    /// <item><c>vertex-interface</c>: a <c>float4x4 viInstance : INSTANCE</c> attribute is four
    /// rows, and HLSL numbers them <c>INSTANCE0</c> to <c>INSTANCE3</c>. The projection gave all
    /// four <c>SemanticIndex = 0</c>, and CreateInputLayout refused "have the same Semantic
    /// (INSTANCE 0)". PipelineInterface now advances the index per row and per array element.</item>
    /// <item><c>entry-point-uniforms</c>: on DXIL the entry point's <c>uniform float4 euTint</c>
    /// lives in a constant buffer at <c>b0</c>, reported as an ordinary range — and the projection
    /// also read the uniforms as root constants, as it rightly does on SPIR-V where they are push
    /// constants. The root signature serializer refused "Shader register range of type CBV …
    /// overlaps with another shader register range". PushConstantBlocks now reads the entry
    /// point's base offsets, and reports the block only where it is push-constant storage.</item>
    /// <item><c>parameter-blocks</c> and <c>buffer-arrays</c>: a <c>ParameterBlock</c> with uniform
    /// data has an implicit constant buffer at <c>b0</c> of its own space, and the walker only
    /// recognised it in the Vulkan slot category, so a DXIL layout had the block's textures and
    /// no buffer. D3D12 refused "Shader CBV descriptor range (BaseShaderRegister=0,
    /// NumDescriptors=1, RegisterSpace=1) is not fully bound in root signature", which is exactly
    /// the check this theory exists to make. The walker now accepts the D3D category too.</item>
    /// </list>
    /// <para>
    /// A refusal is asserted by the words D3D12 or NRI used, read off the device's own message
    /// record or the exception, and only for the case it belongs to — not by a filter across the
    /// theory, so an unrelated error in one of these cases still fails, and a fix fails too.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, D3D12Refusal> D3D12ProjectionDefects = new(StringComparer.Ordinal);

    /// <summary>How D3D12 refuses a pinned defect.</summary>
    /// <param name="Words">What the refusal must say, somewhere in the record or the exception.</param>
    /// <param name="Family">
    /// What every error message of this one refusal contains, when it produces more than one;
    /// <paramref name="Words"/> otherwise. An error outside the family fails the case as usual.
    /// </param>
    private sealed record D3D12Refusal(string Words, string? Family = null)
    {
        public bool Owns(NriMessage m) => m.ToString().Contains(Family ?? Words, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="VulkanCorpusValidationTests.PairedWithAVertexStage"/>, with each supplied vertex
    /// stage's <c>SV_Position</c> moved behind the varyings it writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vulkan links a vertex stage to a fragment stage by <c>location</c>, so the corpus's supplied
    /// vertex stages write <c>SV_Position</c> first and nobody minds. D3D12 links them by hardware
    /// register, allocated in declaration order, and a pixel shader that reads <c>INDEX</c> but
    /// not <c>SV_Position</c> has <c>INDEX</c> in register 0 while that vertex stage writes it to
    /// register 1. Measured, before this reordering: <c>descriptor-arrays</c>,
    /// <c>existential-types</c> and <c>buffer-arrays</c> each failed with "Vertex Shader - Pixel
    /// Shader linkage error: Signatures between stages are incompatible. Semantic '…' is defined
    /// for mismatched hardware registers" — a fault in this harness's pairing, not in anything
    /// PrismSlang said.
    /// </para>
    /// <para>
    /// With <c>SV_Position</c> last, the varyings take registers from 0 in the order the pixel
    /// shader declares them, and a pixel shader is allowed to read a prefix of what the vertex
    /// stage writes. All four supplied vertex stages have the one shape this rewrites; a fifth
    /// that does not is refused here rather than paired wrongly.
    /// </para>
    /// </remarks>
    private static (string Source, List<string> EntryPoints) PairedForD3D12(ShaderCase c)
    {
        var (source, entryPoints) = VulkanCorpusValidationTests.PairedWithAVertexStage(c);
        if (c.ValidationVertex is null) return (source, entryPoints);

        var reordered = System.Text.RegularExpressions.Regex.Replace(
            c.ValidationVertex,
            @"struct (\w+) \{ float4 (\w+) : SV_Position; (.+?) \};",
            "struct $1 { $3 float4 $2 : SV_Position; };");

        if (reordered == c.ValidationVertex)
        {
            throw new InvalidOperationException(
                $"'{c.Name}' supplies a vertex stage this harness cannot put in D3D12 register order; "
                + "teach PairedForD3D12 its shape");
        }

        return (source.Replace(c.ValidationVertex, reordered, StringComparison.Ordinal), entryPoints);
    }

    [NriD3D12Theory]
    [MemberData(nameof(Cases))]
    public void A_pipeline_built_from_the_dxil_projection_passes_d3d12_validation(string caseName)
    {
        // The Vulkan theory above, with the shader compiled to DXIL and the device a D3D12 one with
        // the debug layer on. And a stricter oracle than it looks: D3D12 checks the root signature
        // against the shader's own resource bindings when it creates a pipeline state, and refuses
        // the pipeline when a register the DXIL reads is not covered by the layout — so on this
        // backend a pipeline that is created at all is a layout that binds every register the
        // shader uses, in the class and space the shader uses it. Nothing about that needs a
        // validation message to be read; the creation fails.
        var shaderCase = ShaderCorpus.Case(caseName);

        if (DxilTests.CannotCompile.TryGetValue(caseName, out var notDxil))
        {
            // Asserted in DxilTests, where the compile is the subject. Here it only means there is
            // nothing to hand a device.
            output.WriteLine($"not compiled — {notDxil.Reason}");
            return;
        }

        var (source, entryPointNames) = PairedForD3D12(shaderCase);
        var compiled = Compile(caseName, source, entryPointNames, Dxil);

        if (Refused.TryGetValue(caseName, out var construct))
        {
            var e = Assert.Throws<NotSupportedException>(() => compiled.Layout.ToNri(Capped));
            Assert.Contains(construct, e.Message);
            return;
        }

        var projection = compiled.Layout.ToNri(Capped);
        Assert.Equal(entryPointNames.Count, projection.Stages.Count);

        using var fixture = NriTestDevice.Create(GraphicsAPI.D3D12);
        var device = fixture.Device;
        Assert.NotEqual(0, device.Desc.features.shaderBytecodeDXIL);

        if (LayoutUnbuildable(projection, device) is { } reason)
        {
            output.WriteLine($"layout not built on this device — {reason}");
            return;
        }

        Exception? refused = null;
        try
        {
            using var layout = device.CreatePipelineLayout(projection.Layout);
            Assert.Same(projection.Layout, layout.Desc);

            if (D3D12PipelineOutOfReach.TryGetValue(caseName, out var why))
                output.WriteLine($"pipeline layout built; no pipeline — {why}");
            else
                BuildPipelines(device, layout, projection, compiled.Code, DxilShaderDesc);
        }
        catch (Exception e) when (D3D12ProjectionDefects.ContainsKey(caseName))
        {
            // How a refusal arrives depends on where it happens: CreatePipelineLayout returns
            // INVALID_ARGUMENT and PrismNRI throws NriException; a refused pipeline state reaches
            // managed code as an SEHException, because with the debug layer on D3D12 raises its
            // error as a structured exception once the message is recorded. Either way the
            // message record has the words, and only these cases are allowed to throw.
            refused = e;
        }

        foreach (var m in fixture.Messages) output.WriteLine(m.ToString());

        if (D3D12ProjectionDefects.TryGetValue(caseName, out var defect))
        {
            var said = string.Join("\n", fixture.Messages.Select(m => m.ToString()).Append(refused?.Message ?? ""));
            Assert.True(refused is not null && said.Contains(defect.Words, StringComparison.Ordinal),
                $"'{caseName}' is pinned as a projection defect D3D12 refuses with \"{defect.Words}\", and "
                + (refused is null
                    ? "it was built without complaint. Fixed? Remove it from D3D12ProjectionDefects."
                    : $"D3D12 refused it with something else:\n{said}"));

            // Only the pinned error: anything else D3D12 said about this case is still a failure.
            var others = fixture.Errors.Where(m => !defect.Owns(m)).ToList();
            Assert.True(others.Count == 0,
                $"besides the pinned defect, D3D12 reported:\n" + string.Join("\n", others.Select(m => "  " + m)));
            output.WriteLine($"pinned projection defect, refused as recorded — {defect.Words}");
            return;
        }

        // Only errors fail. Two messages arrive on every D3D12 device creation and are not about
        // anything this test did: an INFO "Using ID3D12Device14" and a WARNING that
        // CheckFeatureSupport(options22) failed with E_INVALIDARG — NRI asking the runtime about a
        // feature block newer than it knows. fragment-outputs adds two warnings of its own, both
        // about this harness's attachments rather than the projection: SV_Target2 writes two
        // components into an RGBA8 attachment (the harness picks RGBA8 for every float output,
        // see ColourAttachments), and the pixel shader "expects a Depth Stencil View" because it
        // writes SV_Depth with none bound — legal, as the message itself says. All four are left
        // in the log and none is matched by text, for the reason the Vulkan theory's remarks give.
        var errors = fixture.Errors.ToList();
        Assert.True(errors.Count == 0,
            $"NRI or the D3D12 debug layer rejected what was built from PrismSlang's NRI projection "
            + $"of the DXIL build of the '{caseName}' shader:\n"
            + string.Join("\n", fixture.Messages.Select(m => "  " + m)));
    }

    /// <summary>A DXIL shader desc.</summary>
    /// <remarks>
    /// <c>main</c>, and it does not matter: a DXIL container holds one entry point and D3D12 never
    /// asks its name. It is the name <c>BakedEntryPoint.ModuleEntryPointName</c> reports for a DXIL
    /// package, so the corpus theory and the baked dispatches below hand NRI the same desc.
    /// </remarks>
    private static NriShaderDesc DxilShaderDesc(StageBits stage, byte[] code) => new(stage, code, "main");

    [NriD3D12Fact]
    public void A_compute_dispatch_on_d3d12_bound_through_the_dxil_projection_writes_what_the_shader_says()
    {
        // The Vulkan dispatch, on D3D12, through a DXIL package: bake, project, bind by the indices
        // the projection reports, dispatch, read back. What it adds is the register-class reading:
        // cfOutput is u0 here, not binding 0 of a set, and NRI's D3D12 backend turns the range
        // into a UAV descriptor table entry at u0 from baseRegisterIndex and descriptorType alone.
        const int Count = 8;
        var c = ShaderCorpus.Case("compute-fixed");

        var package = Bake(c.Source, c.EntryPoints[0], Dxil);
        var shader = package.EntryPoints[0].ToNriShaderDesc();
        Assert.Equal(StageBits.COMPUTE_SHADER, shader.Stage);

        var projection = package.Layout.ToNri(Capped);
        var (set, range) = projection.Range("cfOutput")
            ?? throw new InvalidOperationException("the projection has no range named cfOutput");
        Assert.Equal(DescriptorType.STORAGE_STRUCTURED_BUFFER, range.Desc.descriptorType);

        using var fixture = NriTestDevice.Create(GraphicsAPI.D3D12);
        Assert.NotEqual(0, fixture.Device.Desc.features.shaderBytecodeDXIL);
        var words = DispatchOnce<float>(fixture.Device, projection, shader, set, range, Count, sizeof(float),
            record: null);

        for (var i = 0; i < Count; i++) Assert.Equal(1.0f, words[i]);
        RequireNoErrors(fixture);
    }

    [NriD3D12Fact]
    public void A_constant_buffer_promoted_by_name_reaches_the_dxil_shader_as_root_constants()
    {
        // The D3D12 half of the root-constant dispatch, and the reason RootConstantParameters
        // exists. The same source as the Vulkan fact: on DXIL Slang drops [vk::push_constant] and
        // puts `pc` at b0 as an ordinary constant buffer, so left alone the projection describes a
        // descriptor and no root constants at all. Named, the block leaves the set and becomes a
        // RootConstantDesc at b0 of RootRegisterSpace — which is where the DXIL reads it, so a value
        // set with SetRootConstants is the value the shader sees, or the readback says otherwise.
        const int Count = 64;
        const uint Value = 0xC0FFEE00u;

        var package = Bake(PushConstantShader, "main", Dxil);
        var shader = package.EntryPoints[0].ToNriShaderDesc();

        Assert.Empty(package.Layout.ToNri(Capped).RootConstants);

        var projection = package.Layout.ToNri(Capped with { RootConstantParameters = ["pc"] });
        var block = Assert.Single(projection.RootConstants);
        var pc = package.Layout.Parameters.Single(p => p.Name == "pc").PrimaryBinding
            ?? throw new InvalidOperationException("pc has no binding");

        Assert.Equal("pc", block.Name);
        Assert.Equal(pc.Index, block.Desc.registerIndex);
        Assert.Equal(pc.Space, projection.Layout.RootRegisterSpace);
        Assert.Equal(StageBits.COMPUTE_SHADER, block.Desc.shaderStages);
        Assert.Null(projection.Range("pc"));
        output.WriteLine($"root constant block '{block.Name}': index {block.Index}, b{block.Desc.registerIndex} "
                       + $"space {projection.Layout.RootRegisterSpace}, size {block.Desc.size}");

        var (set, range) = projection.Range("outBuf")
            ?? throw new InvalidOperationException("the projection has no range named outBuf");

        // A local, because SetRootConstants takes the value by reference and a const has no address.
        var value = Value;

        using var fixture = NriTestDevice.Create(GraphicsAPI.D3D12);
        var words = DispatchOnce<uint>(fixture.Device, projection, shader, set, range, Count, sizeof(uint),
            record: cmd => cmd.SetRootConstants(block.Index, in value, 0, BindPoint.COMPUTE));

        for (var i = 0; i < Count; i++) Assert.Equal(Value + (uint)i, words[i]);
        RequireNoErrors(fixture);

        // Watched failing before it was trusted to pass, as the Vulkan theory was. With the
        // promoted block dropped from the layout by hand (RootConstants = [], everything else as
        // projected), the root signature covers u0 and nothing at b0, and D3D12 refused the
        // pipeline at creation — an SEHException out of CreateComputePipeline, with the debug
        // layer's record reading:
        //
        //   [882] ID3D12Device::CreateComputePipelineState: Root Signature doesn't match Compute
        //   Shader: Shader CBV descriptor range (BaseShaderRegister=0, NumDescriptors=1,
        //   RegisterSpace=0) is not fully bound in root signature
        //
        // So the pass above is D3D12 agreeing that b0 is covered, not D3D12 not looking; and the
        // same message is what parameter-blocks and buffer-arrays are pinned by in
        // D3D12ProjectionDefects. Reverted; the fault is not kept as a test, because its whole
        // content is the one message recorded here.
    }

    // ---------------------------------------------------------------- compiling

    internal sealed record Compiled(ProgramLayout Layout, byte[][] Code);

    /// <summary>The DXIL target every D3D12 test compiles for: the binding's default profile.</summary>
    /// <remarks>
    /// No profile named, on purpose, so the tests run against what TargetOptions falls back to
    /// (<c>sm_6_5</c>) — the profile a host that did not think about it gets, which is the one most
    /// worth knowing works.
    /// </remarks>
    internal static TargetOptions Dxil => new() { Format = PrismSlang.Native.SlangCompileTarget.SLANG_DXIL };

    /// <summary>Compiles, links and reflects for one target, keeping each entry point's code.</summary>
    internal static Compiled Compile(
        string name, string source, IReadOnlyList<string> entryPointNames, TargetOptions target)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [target] });
        using var module = session.LoadModuleFromSource(name, $"{name}.slang", source);

        var entryPoints = entryPointNames.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);
            var layout = program.Reflect();

            var code = new byte[entryPoints.Count][];
            for (var i = 0; i < code.Length; i++) code[i] = program.GetEntryPointCodeBytes(i);

            return new Compiled(layout, code);
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    /// <summary>Bakes a one-entry-point program; default (SPIR-V) target options unless told otherwise.</summary>
    internal static ShaderPackage Bake(string source, string entryPointName, TargetOptions? target = null)
    {
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions { Targets = [target ?? new TargetOptions()] });
        using var module = session.LoadModuleFromSource("baked", "baked.slang", source);

        using var entryPoint = module.FindEntryPoint(entryPointName);
        using var program = session.Link(module, entryPoint);
        return ShaderPackageBaker.Bake(program, source);
    }
}
