// The corpus, put in front of the validation layers.
//
// Three oracles in this suite are ultimately Slang describing itself: the emitted module, an
// invariant, a header. The fourth is not — the Khronos layers are an independent implementation of
// the specification, and they found a real defect in the first minute they ran. They were also
// reading three hand-written shaders while ModuleAgreementTests read twenty-eight, which made the
// most valuable oracle the narrowest one in the suite by an order of magnitude.
//
// So the same move that turned three rounds of hand-picked comparisons into : stop choosing
// what to look at. Every corpus shader is compiled, projected, and built into a real
// VkPipeline — descriptor set layouts, push constant ranges, vertex attributes and all — and the
// layers decide.
//
// What this asks that ModuleAgreementTests cannot: agreement proves the layout and the module say
// the same thing. It cannot prove that what they agree on is *legal*, because both sides come from
// Slang. Stage visibility on a descriptor, a set index with a hole in it, a push constant range
// overlapping another, a vertex format the shader cannot actually use — all of those survive
// agreement and none of them survive here.
//
// A case that cannot be built states why, in OutOfReach below, and the reasons are checked: a stale
// entry is a failing test, so the list cannot quietly grow to cover a regression.

using PrismSlang.Reflection;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;
using Vortice.Vulkan;

namespace PrismSlang.Tests;

public sealed unsafe class VulkanCorpusValidationTests(Xunit.Abstractions.ITestOutputHelper output)
    : IDisposable
{
    private readonly VulkanTestDevice? _vulkan =
        VulkanTestDevice.Unavailable is null && SlangNative.Available ? new VulkanTestDevice() : null;

    private VulkanPipelines? _pipelines;

    public void Dispose() => _vulkan?.Dispose();

    private VulkanTestDevice Vulkan => _vulkan ?? throw new InvalidOperationException("no device");

    private VulkanPipelines Pipelines => _pipelines ??= new VulkanPipelines(Vulkan);

    /// <summary>
    /// Corpus cases this cannot put in front of the layers, and why.
    /// </summary>
    /// <remarks>
    /// Each of these is a property of the pipeline shape or of the projection, not of the device —
    /// device limits are handled separately below, because they differ between a CI runner and a
    /// workstation and are not something to record as permanent.
    /// </remarks>
    private static readonly Dictionary<string, string> OutOfReach = new(StringComparer.Ordinal)
    {
        ["mesh"] =
            "a mesh pipeline needs VK_EXT_mesh_shader, which VulkanTestDevice does not enable and "
            + "lavapipe does not offer. The stage is projected and ModuleAgreementTests checks it "
            + "against the module; nothing checks it against a driver.",
        ["amplification"] =
            "a task shader is half of a mesh pipeline, so it is out for the same reason as `mesh`.",
        ["ray-tracing"] =
            "a ray tracing pipeline needs VK_KHR_ray_tracing_pipeline and a shader binding table. "
            + "Neither is a layout question, and lavapipe has no ray tracing at all.",
        ["wide-vertex-inputs"] =
            "the projection refuses this shader on purpose — a three- or four-component 64-bit "
            + "vector consumes two locations and Slang numbers it as one, so ToVulkan throws rather "
            + "than emit an overlap. VulkanValidationTests bypasses the refusal to put the question "
            + "to the layers directly;",
        ["tessellation"] =
            "hull and domain with no vertex stage. A graphics pipeline needs one, and a supplied "
            + "vertex stage would have to produce this hull shader's exact input patch — which is "
            + "writing a second shader to test the first. The descriptor side has nothing in it.",
        ["geometry"] =
            "a geometry stage with no vertex stage, for the same reason as `tessellation`: the "
            + "supplied vertex stage would have to match the geometry shader's input primitive.",
    };

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    [Fact]
    public void Every_declared_exclusion_names_a_shader_that_exists()
    {
        // The list is only trustworthy while it is exact. A renamed corpus case would leave a dead
        // entry here that excuses nothing, and the shader it used to name would start being tested
        // without anyone deciding that — or, worse, a typo would silently exclude nothing while
        // reading as though it excluded something.
        var unknown = OutOfReach.Keys
            .Where(name => ShaderCorpus.All.All(c => c.Name != name))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "OutOfReach names shaders the corpus does not have: " + string.Join(", ", unknown));
    }

    [NativeFact]
    public void The_sample_interpolation_modifier_declares_SampleRateShading()
    {
        // Slang's `sample` interpolation modifier becomes `OpDecorate … Sample`, and on
        // slang-compiler 2026.14.1 the module did not declare OpCapability SampleRateShading (35)
        // alongside it. spirv-val rejected that as malformed — not as unsupported: it was a
        // property of the module, so no device feature and no enabled extension changed the
        // answer. Found by the corpus driver on its first complete run, and the third thing in this
        // project the validation layers said about Slang's own output rather than about PrismSlang's
        // reading of it.
        //
        // Recorded rather than reported upstream, and pinned the other way round until 2026.21,
        // which declares the capability. The case is now in the corpus theory above like any other,
        // and this keeps the module-level fact, which does not depend on which layers are installed.
        var shaderCase = ShaderCorpus.Case("interpolation-modifiers");

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource(
            "im", "im.slang", shaderCase.Source);

        var entryPoints = shaderCase.EntryPoints.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);

            for (var i = 0; i < entryPoints.Count; i++)
            {
                var code = program.GetEntryPointCodeBytes(i);
                if (!Decorations(code).Contains(SampleDecoration)) continue;

                Assert.Contains(SampleRateShadingCapability, Capabilities(code));
                return;
            }

            Assert.Fail(
                "no stage of interpolation-modifiers carries the Sample decoration any more. "
                + "Either the shader changed or Slang stopped emitting it, and this no longer tests "
                + "what it is named for.");
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    [NativeFact]
    public void A_matrix_vertex_input_carries_no_member_Offset()
    {
        // VUID-StandaloneSpirv-Offset-12509: "the Input interface variable must not have Offset on
        // any member decoration". slang-compiler 2026.14.1 broke it on vertex-interface's float4x4
        // input, wrapping the matrix in a _MatrixStorage struct and decorating its member Offset 0;
        // 2026.21 does not, and the SDK's spirv-val passes its module where it refuses 2026.14.1's.
        // Measured with Vulkan SDK 1.4.363.0, whose layers check the rule, and the check below
        // finds the struct in the module slangc 2026.14.1 emits for this shader, row- and
        // column-major alike, and nothing in 2026.21's.
        //
        // Pinned here as well as left to the corpus theory, because the theory only sees it where
        // the installed layers know the rule, and Ubuntu's packaged layers, which CI uses, predate
        // it. So this is the check that holds on every leg. Recorded rather than reported upstream.
        var shaderCase = ShaderCorpus.Case("vertex-interface");

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource("vi", "vi.slang", shaderCase.Source);
        using var entryPoint = module.FindEntryPoint(Assert.Single(shaderCase.EntryPoints));
        using var program = session.Link(module, entryPoint);

        Assert.Empty(SpirvDecorations.InputStructsWithMemberOffsets(program.GetEntryPointCodeBytes(0)));
    }

    /// <summary><c>Capability::SampleRateShading</c>.</summary>
    private const uint SampleRateShadingCapability = 35;

    /// <summary><c>Decoration::Sample</c>. Its neighbours are Centroid (16) and Invariant (18).</summary>
    private const uint SampleDecoration = 17;

    /// <summary>Every <c>OpCapability</c> operand in the module.</summary>
    private static HashSet<uint> Capabilities(byte[] spirv) => Operands(spirv, opcode: 17, operand: 0);

    /// <summary>Every decoration id used by <c>OpDecorate</c>.</summary>
    private static HashSet<uint> Decorations(byte[] spirv) => Operands(spirv, opcode: 71, operand: 1);

    /// <summary>One operand of every instruction with this opcode.</summary>
    private static HashSet<uint> Operands(byte[] spirv, ushort opcode, int operand)
    {
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(spirv);
        var found = new HashSet<uint>();

        for (var i = 5; i < words.Length;)
        {
            var count = (int)(words[i] >> 16);
            if (count <= 0 || i + count > words.Length) break;

            if ((ushort)(words[i] & 0xFFFF) == opcode && count > operand + 1)
                found.Add(words[i + 1 + operand]);

            i += count;
        }

        return found;
    }

    [VulkanValidationTheory]
    [MemberData(nameof(Cases))]
    public void A_pipeline_built_from_the_projection_passes_validation(string caseName)
    {
        if (OutOfReach.ContainsKey(caseName)) return;

        var shaderCase = ShaderCorpus.Case(caseName);
        var (source, entryPointNames) = PairedWithAVertexStage(shaderCase);

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            // So each stage's module names its entry point as the shader does, which is what pName
            // has to match. Without it every stage is called `main` and a two-stage pipeline cannot
            // be told apart from itself —
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource(caseName, $"{caseName}.slang", source);

        var entryPoints = entryPointNames.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);
            var reflection = program.Reflect();
            var layout = reflection.ToVulkan();

            Assert.Equal(reflection.EntryPoints.Count, layout.Stages.Count);
            RequireTheAddedStageChangedNothing(shaderCase, layout);

            var code = new byte[layout.Stages.Count][];
            for (var i = 0; i < layout.Stages.Count; i++) code[i] = program.GetEntryPointCodeBytes(i);

            if (Unbuildable(layout) is { } reason)
            {
                // A device limit rather than a permanent exclusion, so it is not in OutOfReach: the
                // same shader is testable on a workstation and not on a software rasteriser.
                // Written to the test output, because a test that covers nothing and a test that
                // covers everything are both green and the log is the only thing that tells them
                // apart — the same reason ci.yml counts the validation tests that ran.
                output.WriteLine($"not built on this device — {reason}");
                return;
            }

            using var capture = VulkanTestDevice.Collect();
            Build(layout, code);

            var errors = capture.Errors.ToList();
            var deviceGaps = errors.Where(IsADeviceGap).ToList();
            foreach (var gap in deviceGaps) output.WriteLine($"device gap, not a defect — {gap.Text}");

            Assert.True(errors.Count == deviceGaps.Count,
                $"the validation layers rejected a pipeline built from PrismSlang's projection of "
                + $"the '{caseName}' shader. Nothing was adjusted on the way through, so each of "
                + $"these is something the projection said:\n{capture.Report()}");
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    /// <summary>
    /// Whether a validation error is this device lacking something, rather than the projection
    /// being wrong.
    /// </summary>
    /// <remarks>
    /// Two VUIDs, and it stays at two. Both are about a SPIR-V capability or extension the
    /// <em>module</em> declared and the device does not have, and the projection provably cannot
    /// cause either: it does not choose what the shader compiles to. A layout defect appears as a
    /// VUID about a binding, a stage, a location or a range, and neither of these can.
    ///
    /// The first version was wider, matching phrases like "requires the extensions" and
    /// "limit (0)". Two implementations then worded the same rejection differently — the SDK
    /// layers said an unrecognised descriptor type "requires the extensions
    /// VK_KHR_acceleration_structure" where lavapipe said it "is not an extension added token" —
    /// which is the moment to stop matching prose and gate on the condition instead. Anything the
    /// device cannot do is now decided before the pipeline is built, in
    /// <see cref="Unbuildable"/>, where the answer does not depend on how a layer phrases it.
    /// </remarks>
    private static bool IsADeviceGap(ValidationMessage message) =>
        message.Id.Contains("pCode-08740", StringComparison.Ordinal)      // capability, no feature
        || message.Id.Contains("pCode-08742", StringComparison.Ordinal);  // extension not enabled

    // ---------------------------------------------------------------- pairing a vertex stage

    /// <summary>The entry point name of the vertex stage this harness supplies.</summary>
    private const string SuppliedVertex = "pvValidationVertex";

    /// <summary>
    /// A vertex stage that writes only <c>SV_Position</c> — enough to make a pipeline out of a
    /// fragment stage that reads no varyings.
    /// </summary>
    private const string TrivialVertex = $$"""

        [shader("vertex")]
        float4 {{SuppliedVertex}}() : SV_Position { return float4(0, 0, 0, 1); }
        """;

    /// <summary>
    /// The case's source and entry points, with a vertex stage added if it needs one.
    /// </summary>
    /// <remarks>
    /// Sixteen corpus shaders declare a fragment stage and nothing else. That is a perfectly good
    /// thing to reflect and it is not a Vulkan graphics pipeline, so without this the driver would
    /// build nothing for most of the corpus and report success — the shape of failure this project
    /// has hit before, where a gate that excludes everything is indistinguishable from a gate that
    /// excludes nothing.
    ///
    /// The added stage is not free of consequence and is checked for consequence: see
    /// <see cref="RequireTheAddedStageChangedNothing"/>.
    /// </remarks>
    internal static (string Source, List<string> EntryPoints) PairedWithAVertexStage(ShaderCase c)
    {
        var stages = c.Source;
        var needsVertex =
            stages.Contains("[shader(\"fragment\")]", StringComparison.Ordinal)
            && !stages.Contains("[shader(\"vertex\")]", StringComparison.Ordinal);

        if (!needsVertex) return (c.Source, [.. c.EntryPoints]);

        var vertex = c.ValidationVertex is null
            ? TrivialVertex
            : $"\n{c.ValidationVertex}\n";

        // The case's own name for it when it supplied one, so the declaration in ShaderCorpus reads
        // as ordinary Slang rather than as something with a magic name.
        var name = c.ValidationVertex is null
            ? SuppliedVertex
            : VertexEntryPointNameIn(c.ValidationVertex);

        return (c.Source + vertex, [.. c.EntryPoints, name]);
    }

    /// <summary>The function name following the <c>[shader("vertex")]</c> attribute.</summary>
    private static string VertexEntryPointNameIn(string slang)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            slang, @"\[shader\(""vertex""\)\]\s*\S+\s+(\w+)\s*\(");

        if (!match.Success)
            throw new InvalidOperationException(
                "a ShaderCase.ValidationVertex must declare exactly one [shader(\"vertex\")] entry "
                + "point on the line after the attribute; this one does not:\n" + slang);

        return match.Groups[1].Value;
    }

    /// <summary>
    /// The descriptor sets, push constants and fragment outputs are the ones the case produces on
    /// its own.
    /// </summary>
    /// <remarks>
    /// Adding an entry point to a program is not obviously inert — Slang assigns bindings across
    /// the whole linked program, and a stage that consumed a register would shift everything after
    /// it. The supplied vertex stages touch no resources, so they should shift nothing, and
    /// "should" is exactly the word that means this needs asserting: without it the driver could be
    /// validating a layout no consumer will ever see.
    /// </remarks>
    private static void RequireTheAddedStageChangedNothing(ShaderCase c, VulkanPipelineLayout paired)
    {
        var names = c.EntryPoints;
        if (paired.Stages.Count == names.Count) return;   // nothing was added

        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource(c.Name, $"{c.Name}.slang", c.Source);

        var entryPoints = names.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);
            var alone = program.Reflect().ToVulkan();

            Assert.Equal(Describe(alone), Describe(paired));
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    /// <summary>Everything about a layout the added vertex stage must not have moved.</summary>
    private static string Describe(VulkanPipelineLayout layout) => string.Join("\n",
        layout.DescriptorSets
            .SelectMany(s => s.Bindings.Select(b =>
                $"set {s.Set} binding {b.Binding} {b.DescriptorType} x{b.DescriptorCount} {b.Name}"))
            .Order(StringComparer.Ordinal)
            .Concat(layout.PushConstants
                .Select(p => $"push {p.Offset}+{p.Size} {p.Name}")
                .Order(StringComparer.Ordinal))
            .Concat(layout.FragmentOutputs
                .Select(o => $"output {o.Location} {o.Format} {o.Name}")
                .Order(StringComparer.Ordinal)));

    // ---------------------------------------------------------------- shaping the pipeline

    private const VulkanShaderStages GraphicsStages =
        VulkanShaderStages.Vertex | VulkanShaderStages.TessellationControl
        | VulkanShaderStages.TessellationEvaluation | VulkanShaderStages.Geometry
        | VulkanShaderStages.Fragment;

    /// <summary>Why this device cannot build the pipeline this layout describes, or null.</summary>
    private string? Unbuildable(VulkanPipelineLayout layout)
    {
        var present = layout.Stages.Aggregate(VulkanShaderStages.None, (a, s) => a | s.Stage);

        var exotic = present & ~(GraphicsStages | VulkanShaderStages.Compute);
        if (exotic != VulkanShaderStages.None)
            return $"the program has a {exotic} stage, which needs a pipeline type this harness "
                 + "does not build";

        if ((present & VulkanShaderStages.Geometry) != 0 && !Vulkan.SupportsGeometryShaders)
            return "the device has no geometry shader support";

        if ((present & (VulkanShaderStages.TessellationControl
                        | VulkanShaderStages.TessellationEvaluation)) != 0
            && !Vulkan.SupportsTessellationShaders)
            return "the device has no tessellation shader support";

        // VulkanTestDevice enables no ray tracing extensions — lavapipe has none to enable, and
        // adding them for one binding would mean VK_KHR_acceleration_structure, VK_KHR_ray_query
        // and VK_KHR_deferred_host_operations on a device that exists to answer layout questions.
        // Without them VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR is not a token the layers
        // recognise, so the whole set layout is refused and the other sixteen descriptor types in
        // descriptor-kinds go unexamined with it. That is the cost, and it is stated rather than
        // filtered out of the message stream.
        if (layout.DescriptorSets.Any(s => s.Bindings.Any(
                b => b.DescriptorType == VulkanDescriptorType.AccelerationStructureKhr)))
        {
            return "the shader binds an acceleration structure and this device enables no ray "
                 + "tracing extensions, so the descriptor type is not a token the layers accept";
        }

        foreach (var input in layout.VertexInputs)
        {
            if (!Vulkan.SupportsVertexFormat((VkFormat)input.Format))
                return $"the device does not accept {(VkFormat)input.Format} "
                     + $"(vertex input '{input.Name}') in a vertex buffer";
        }

        // A graphics pipeline needs a vertex stage. A corpus shader with only a fragment entry
        // point is a legitimate thing to reflect and not a pipeline.
        if ((present & GraphicsStages) != 0 && (present & VulkanShaderStages.Vertex) == 0)
            return "the program has raster stages but no vertex stage, so there is no graphics "
                 + "pipeline to build";

        return null;
    }

    private void Build(VulkanPipelineLayout layout, byte[][] code)
    {
        // The descriptor set layouts and the pipeline layout are built for every case without
        // exception — they are the projection's main output, and the bug this driver found on its
        // first run was rejected here rather than at pipeline creation.
        using var built = Pipelines.BuildLayout(layout);

        // An input attachment cannot be used with plain dynamic rendering; it wants a render pass,
        // or Vulkan 1.4's dynamicRenderingLocalRead. That is a property of this harness's choice of
        // dynamic rendering, so the pipeline is skipped and the layout — the part under test — is
        // not. Said out loud rather than folded into the device-gap filter, because a filter that
        // silently swallows a whole category is how an oracle stops being one.
        if (layout.DescriptorSets.Any(
                s => s.Bindings.Any(b => b.DescriptorType == VulkanDescriptorType.InputAttachment)))
        {
            output.WriteLine(
                "descriptor set layouts and pipeline layout built and accepted; the graphics "
                + "pipeline is skipped because an input attachment needs a render pass and this "
                + "harness uses dynamic rendering.");
            return;
        }


        var modules = new StageModule[layout.Stages.Count];
        for (var i = 0; i < layout.Stages.Count; i++)
        {
            var handle = Pipelines.CreateModule(code[i]);
            built.Modules.Add(handle);

            // The entry point name comes out of the emitted module rather than from the layout.
            // The layout carries the Slang function name and deliberately not the module's — that
            // is a property of the bytes (see VulkanStage) — and pName has to match the bytes.
            var name = SpirvModule.Read(code[i]).EntryPoints[0].Name;
            modules[i] = new StageModule((VkShaderStageFlags)layout.Stages[i].Stage, handle, name);
        }

        var graphics = modules
            .Where((_, i) => (layout.Stages[i].Stage & GraphicsStages) != 0)
            .ToList();

        if (graphics.Count > 0)
        {
            var tessellating = graphics.Any(
                s => (s.Stage & (VkShaderStageFlags.TessellationControl
                                 | VkShaderStageFlags.TessellationEvaluation)) != 0);

            Pipelines.CreateGraphicsPipeline(
                built,
                graphics,
                VertexAttributes(layout),
                ColourFormats(layout),
                patchControlPoints: tessellating ? 3u : 0u);
        }

        for (var i = 0; i < modules.Length; i++)
        {
            if (layout.Stages[i].Stage != VulkanShaderStages.Compute) continue;

            // Each compute entry point is its own pipeline, sharing the program-wide layout. A
            // pipeline layout may declare bindings for stages the pipeline does not have; it may
            // not omit one the pipeline does have, which is the direction being tested.
            using var computeOnly = Pipelines.BuildLayout(layout);
            Pipelines.CreateComputePipeline(computeOnly, modules[i]);
        }
    }

    /// <summary>Each attribute in its own binding, at offset zero.</summary>
    /// <remarks>
    /// The projection reports locations and formats and stops there, because how attributes are
    /// packed into buffers is a renderer decision (see <c>VulkanVertexInput</c>). This picks the
    /// arrangement that adds the fewest assumptions of its own.
    /// </remarks>
    private static VkVertexInputAttributeDescription[] VertexAttributes(VulkanPipelineLayout layout) =>
        layout.VertexInputs
            .Select(v => new VkVertexInputAttributeDescription
            {
                location = v.Location,
                binding = v.Location,
                format = (VkFormat)v.Format,
                offset = 0,
            })
            .ToArray();

    /// <summary>
    /// One colour attachment per fragment output location, with a format of the right numeric
    /// class.
    /// </summary>
    /// <remarks>
    /// The layers check that a fragment shader writing integers is not pointed at a float
    /// attachment, so the class has to come from what the shader writes. The width does not: a
    /// shader returning <c>float4</c> writes correctly into <c>R8G8B8A8_UNORM</c>, which is the
    /// distinction <c>VulkanFragmentOutput.Format</c> documents. Locations the shader skips are
    /// <c>VK_FORMAT_UNDEFINED</c>, which is how dynamic rendering spells an unused attachment.
    /// </remarks>
    private static VkFormat[] ColourFormats(VulkanPipelineLayout layout)
    {
        if (layout.FragmentOutputs.Count == 0) return [];

        var formats = new VkFormat[layout.FragmentOutputs.Max(o => o.Location) + 1];
        foreach (var output in layout.FragmentOutputs)
        {
            var name = output.Format.ToString();
            formats[output.Location] =
                name.EndsWith("Uint", StringComparison.Ordinal) ? VkFormat.R8G8B8A8Uint
                : name.EndsWith("Sint", StringComparison.Ordinal) ? VkFormat.R8G8B8A8Sint
                : VkFormat.R8G8B8A8Unorm;
        }

        return formats;
    }
}
