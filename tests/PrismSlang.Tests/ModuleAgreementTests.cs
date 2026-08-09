// Everything the module states, and everything the layout states, compared as sets.
//
// The twelve defects found by hand split into two kinds. A few were a wrong value — reflection said
// 2 where the module said 5 — and a field-by-field comparison finds those. Most were *missing*:
// reflection said nothing at all about a subpass input, a nested block, a structured buffer's
// element. Comparing fields cannot find those, because you only compare the fields you thought of.
//
// So this compares sets, both directions. Every fact the module states must appear in the layout,
// and every fact the layout states must appear in the module. A fact nobody thought to assert is
// still in the set, and a fact one side simply does not have is a difference rather than a silence.
//
// Differences that are understood are declared on the corpus case with a reason, and the assertion
// is that the difference is *exactly* the declared set — so a known gap can neither grow quietly nor
// be fixed without someone removing it from the list.

using PrismSlang.Native;
using PrismSlang.Reflection;
using PrismSlang.Spirv;
using PrismSlang.Vulkan;

namespace PrismSlang.Tests;

public class ModuleAgreementTests
{
    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in ShaderCorpus.All) data.Add(c.Name);
        return data;
    }

    [NativeTheory]
    [MemberData(nameof(Cases))]
    public void The_layout_and_the_module_state_the_same_facts(string caseName)
    {
        var shaderCase = ShaderCorpus.Case(caseName);

        // The whole program in one module, via getTargetCode: a pipeline layout describes every
        // stage at once, and a per-entry-point module would legitimately omit whatever that stage
        // does not reference — which would read as a difference without being one.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            // So the module names its entry points as the shader does. Without it a single-entry
            // module calls the entry point `main` and the stage facts cannot be keyed by name.
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });
        using var module = session.LoadModuleFromSource(caseName, $"{caseName}.slang", shaderCase.Source);

        var entryPoints = shaderCase.EntryPoints.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);
            using var blob = program.GetTargetCode();
            var code = blob.ToArray();
            var layout = program.Reflect();

            // Varyings come from the individual stage's own module. The whole-program module holds
            // every stage's inputs and outputs at once, and the layout describes only the vertex
            // stage's inputs and the fragment stage's outputs — so comparing them against the
            // combined module would report every other stage's varyings as a difference.
            var varyings = new Dictionary<SlangStage, byte[]>();
            for (var i = 0; i < layout.EntryPoints.Count; i++)
            {
                var stage = layout.EntryPoints[i].Stage;
                if (stage is SlangStage.SLANG_STAGE_VERTEX or SlangStage.SLANG_STAGE_FRAGMENT)
                    varyings[stage] = program.GetEntryPointCodeBytes(i);
            }

            var fromModule = ModuleFacts(code, varyings);
            var fromLayout = LayoutFacts(layout);

            var missing = fromModule.Except(fromLayout).ToList();
            var extra = fromLayout.Except(fromModule).ToList();
            var declared = shaderCase.KnownGaps ?? [];

            var unexplained = missing.Concat(extra)
                .Where(f => !declared.Any(g => f.StartsWith(g.Fact, StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToList();

            Assert.True(unexplained.Count == 0,
                $"'{caseName}': module and layout disagree.\n" +
                $"  in the module, not in the layout:\n" +
                string.Join("", missing.Order(StringComparer.Ordinal).Select(f => $"    {f}\n")) +
                $"  in the layout, not in the module:\n" +
                string.Join("", extra.Order(StringComparer.Ordinal).Select(f => $"    {f}\n")));

            // A gap that no longer exists is a fix nobody wrote down. Fail so the list stays true.
            foreach (var gap in declared)
            {
                Assert.True(
                    missing.Concat(extra).Any(f => f.StartsWith(gap.Fact, StringComparison.Ordinal)),
                    $"'{caseName}' declares a known gap '{gap.Fact}' that no longer occurs. " +
                    $"Remove it from the corpus. Reason it gave: {gap.Reason}");
            }
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    // ---------------------------------------------------------------- the module's account

    private static HashSet<string> ModuleFacts(byte[] code, Dictionary<SlangStage, byte[]> varyings)
    {
        var facts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var v in SpirvDecorations.Read(code))
        {
            if (v is { Set: not null, Binding: not null })
                facts.Add($"descriptor {v.LeafName} = set {v.Set} binding {v.Binding}");
        }

        if (varyings.TryGetValue(SlangStage.SLANG_STAGE_VERTEX, out var vertex))
            foreach (var (name, location) in SpirvDecorations.InputLocations(vertex))
                facts.Add($"input {Declaration(name)} = location {location}");

        if (varyings.TryGetValue(SlangStage.SLANG_STAGE_FRAGMENT, out var fragment))
            foreach (var (name, location) in SpirvDecorations.OutputLocations(fragment))
                facts.Add($"output {Declaration(name)} = location {location}");

        foreach (var (name, index) in SpirvDecorations.InputAttachmentIndices(code))
            facts.Add($"attachment {Leaf(name)} = {index}");

        foreach (var (name, id) in SpirvDecorations.SpecializationIds(code))
            facts.Add($"specialization {Leaf(name)} = id {id}");

        foreach (var (name, member) in SpirvDecorations.Members(code))
        {
            if (IsSynthetic(name)) continue;
            facts.Add($"member {name} = offset {member.Offset}");
            if (member.MatrixStride is { } stride) facts.Add($"member {name} = stride {stride}");
        }

        foreach (var (name, model) in SpirvDecorations.EntryPointModels(code))
            facts.Add($"stage {name} = {(SpirvExecutionModel)model}");

        foreach (var (name, size) in SpirvDecorations.WorkgroupSizes(code))
        {
            facts.Add(size.ById
                ? $"workgroup {name} = specialized"
                : $"workgroup {name} = [{size.X}, {size.Y}, {size.Z}]");
        }

        return facts;
    }

    // ---------------------------------------------------------------- the layout's account

    private static HashSet<string> LayoutFacts(ProgramLayout layout)
    {
        var facts = new HashSet<string>(StringComparer.Ordinal);
        var vulkan = layout.ToVulkan();

        foreach (var set in vulkan.DescriptorSets)
        {
            foreach (var b in set.Bindings)
            {
                facts.Add($"descriptor {b.Name ?? "(unnamed)"} = set {set.Set} binding {b.Binding}");
                if (b.InputAttachmentIndex is { } index)
                    facts.Add($"attachment {b.Name ?? "(unnamed)"} = {index}");
            }
        }

        // A field spanning several locations is one variable in the module and several attributes
        // here — a float4x4 becomes name[0]..name[3]. Compare the declaration, at its base location.
        foreach (var group in vulkan.VertexInputs.GroupBy(v => Base(v.Name)))
            facts.Add($"input {group.Key} = location {group.Min(v => v.Location)}");

        foreach (var group in vulkan.FragmentOutputs.GroupBy(o => Base(o.Name)))
            facts.Add($"output {group.Key} = location {group.Min(o => o.Location)}");

        foreach (var c in layout.SpecializationConstants)
            facts.Add($"specialization {c.Name} = id {c.ConstantId}");

        foreach (var (name, member) in Members(layout))
        {
            facts.Add($"member {name} = offset {member.UniformOffset}");
            if (member.Type is MatrixLayout m) facts.Add($"member {name} = stride {m.Stride}");
        }

        // Loose uniform values are members of a block the module synthesises — the entry point's
        // implicit push-constant buffer, or the global scope's implicit constant buffer — so they
        // belong beside the struct fields above rather than being reported only as parameters.
        foreach (var p in layout.Parameters.Concat(layout.EntryPoints.SelectMany(e => e.Parameters)))
        {
            foreach (var b in p.Bindings)
            {
                if (b.Category != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) continue;
                facts.Add($"member {p.Name} = offset {b.Index}");
            }
        }

        foreach (var e in layout.EntryPoints)
        {
            facts.Add($"stage {e.Name} = {ExecutionModel(e.Stage)}");
            if (e.ThreadGroupSize is { } t)
            {
                facts.Add(t.IsSpecialized
                    ? $"workgroup {e.Name} = specialized"
                    : $"workgroup {e.Name} = [{t.X}, {t.Y}, {t.Z}]");
            }
        }

        return facts;
    }

    /// <summary>
    /// Every struct member the layout places in memory, from every buffer in the program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by member name alone, which is why the corpus keeps them unique across shaders: the
    /// module's <c>OpMemberDecorate</c> is keyed by type id, and matching type ids to reflected
    /// types is a harder join than the corpus is worth.
    /// </para>
    /// <para>
    /// Only fields that genuinely have a <c>UNIFORM</c> offset count. Reading
    /// <c>FieldLayout.UniformOffset</c> unconditionally puts every varying and every resource field
    /// at offset 0 — which is the same defaulting-an-absent-offset mistake the vertex-input
    /// projection made, committed here by the harness meant to catch it.
    /// </para>
    /// </remarks>
    private static Dictionary<string, FieldLayout> Members(ProgramLayout layout)
    {
        var result = new Dictionary<string, FieldLayout>(StringComparer.Ordinal);
        var seen = new HashSet<TypeLayout>();

        foreach (var p in layout.Parameters) Walk(p.Type);
        foreach (var e in layout.EntryPoints)
            foreach (var p in e.Parameters) Walk(p.Type);

        return result;

        void Walk(TypeLayout? type)
        {
            if (type is null || !seen.Add(type)) return;

            switch (type)
            {
                case StructLayout s:
                    foreach (var f in s.Fields)
                    {
                        if (HasUniformOffset(f)) result[f.Name] = f;
                        Walk(f.Type);
                    }
                    return;

                case ContainerLayout c: Walk(c.ElementType); return;
                case ResourceLayout r: Walk(r.ElementType); return;
                case ArrayLayout a: Walk(a.ElementType); return;
                default: return;
            }
        }

        static bool HasUniformOffset(FieldLayout field) => field.Offsets.Any(
            o => o.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
    }

    // ---------------------------------------------------------------- shared

    /// <summary>Slang's stage as the execution model the module will name it by.</summary>
    private static SpirvExecutionModel ExecutionModel(SlangStage stage) => stage switch
    {
        SlangStage.SLANG_STAGE_VERTEX => SpirvExecutionModel.Vertex,
        SlangStage.SLANG_STAGE_HULL => SpirvExecutionModel.TessellationControl,
        SlangStage.SLANG_STAGE_DOMAIN => SpirvExecutionModel.TessellationEvaluation,
        SlangStage.SLANG_STAGE_GEOMETRY => SpirvExecutionModel.Geometry,
        SlangStage.SLANG_STAGE_FRAGMENT => SpirvExecutionModel.Fragment,
        SlangStage.SLANG_STAGE_COMPUTE => SpirvExecutionModel.GLCompute,
        SlangStage.SLANG_STAGE_RAY_GENERATION => SpirvExecutionModel.RayGenerationKHR,
        SlangStage.SLANG_STAGE_INTERSECTION => SpirvExecutionModel.IntersectionKHR,
        SlangStage.SLANG_STAGE_ANY_HIT => SpirvExecutionModel.AnyHitKHR,
        SlangStage.SLANG_STAGE_CLOSEST_HIT => SpirvExecutionModel.ClosestHitKHR,
        SlangStage.SLANG_STAGE_MISS => SpirvExecutionModel.MissKHR,
        SlangStage.SLANG_STAGE_CALLABLE => SpirvExecutionModel.CallableKHR,
        SlangStage.SLANG_STAGE_AMPLIFICATION => SpirvExecutionModel.TaskEXT,
        SlangStage.SLANG_STAGE_MESH => SpirvExecutionModel.MeshEXT,
        _ => throw new NotSupportedException($"no execution model mapped for {stage}"),
    };

    /// <summary>Strips the <c>[i]</c> a multi-location attribute is split by.</summary>
    private static string Base(string name)
    {
        var bracket = name.IndexOf('[');
        return bracket < 0 ? name : name[..bracket];
    }

    /// <summary>The last dotted segment, as reflection reports declarations.</summary>
    private static string Leaf(string name) => name[(name.LastIndexOf('.') + 1)..];

    /// <summary>
    /// The declaration a module-level varying name refers to.
    /// </summary>
    /// <remarks>
    /// An entry point returning a bare value rather than a struct has no field to name the output
    /// after, so Slang synthesises <c>entryPointParam_&lt;function&gt;</c>. The layout reports that
    /// attachment under the entry point's own name, which is the same thing said differently.
    /// </remarks>
    private static string Declaration(string name)
    {
        const string Synthesised = "entryPointParam_";
        var leaf = Leaf(name);
        return leaf.StartsWith(Synthesised, StringComparison.Ordinal)
            ? leaf[Synthesised.Length..]
            : leaf;
    }

    /// <summary>
    /// Whether a member name is one Slang synthesised rather than one the shader author wrote.
    /// </summary>
    /// <remarks>
    /// A wrapper struct around a single value gets <c>__member0</c>, and the block Slang wraps a
    /// constant buffer's contents in holds them under <c>data</c>. Neither has a declaration on the
    /// reflection side to correspond to. The corpus avoids both names for real fields.
    /// </remarks>
    private static bool IsSynthetic(string name) =>
        name.StartsWith("__", StringComparison.Ordinal)
        || string.Equals(name, "data", StringComparison.Ordinal);
}
