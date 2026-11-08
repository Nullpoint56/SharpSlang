// Declared, laid out, and never read.
//
// Every other oracle in this suite compares reflection against the emitted module, and for this
// question they agree by being equally wrong: a parameter the generated code never touches is
// still declared in the SPIR-V and still reported by reflection, because it is still part of the
// shader's interface. IMetadata is the only thing that knows the difference, and until now it was
// bound in L0 and reachable from nothing.

using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Tests;

public class MetadataTests(ITestOutputHelper output)
{
    // Two textures and two samplers. The fragment stage reads one pair; the vertex stage reads
    // neither. `mdUnread` is referenced by nothing at all.
    private const string Source = """
        Texture2D    mdRead;
        Texture2D    mdUnread;
        SamplerState mdSampler;

        struct VOut { float4 clip : SV_Position; float2 uv : TEXCOORD0; };

        [shader("vertex")]
        VOut mdVertex(float3 pos : POSITION)
        {
            VOut o; o.clip = float4(pos, 1); o.uv = pos.xy; return o;
        }

        [shader("fragment")]
        float4 mdFragment(VOut i) : SV_Target { return mdRead.Sample(mdSampler, i.uv); }
        """;

    private static (SlangGlobalSession, SlangSession, SlangModule) Load()
    {
        var global = SlangGlobalSession.Create();
        var session = global.CreateSession(new SessionOptions { Targets = [new TargetOptions()] });
        var module = session.LoadModuleFromSource("md", "md.slang", Source);
        return (global, session, module);
    }

    [NativeFact]
    public void Reflection_reports_a_parameter_the_code_never_reads()
    {
        // The premise, asserted rather than assumed. If Slang ever started stripping these from
        // reflection, the metadata query below would be answering a question nobody asked.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var vertex = module.FindEntryPoint("mdVertex");
            using var fragment = module.FindEntryPoint("mdFragment");
            using var program = session.Link(module, vertex, fragment);

            var layout = program.Reflect();
            Assert.Contains(layout.Parameters, p => p.Name == "mdUnread");
        }
    }

    [NativeFact]
    public void The_metadata_says_which_of_them_the_generated_code_touches()
    {
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var vertex = module.FindEntryPoint("mdVertex");
            using var fragment = module.FindEntryPoint("mdFragment");
            using var program = session.Link(module, vertex, fragment);

            var layout = program.Reflect();
            using var metadata = program.GetTargetMetadata();

            var unused = metadata.UnusedBindings(layout);
            foreach (var b in unused) output.WriteLine($"unused: {b.Category} space {b.Space} index {b.Index}");

            var unusedNames = layout.Parameters
                .Where(p => p.Bindings.All(b => unused.Contains(b)))
                .Select(p => p.Name)
                .ToList();

            Assert.Contains("mdUnread", unusedNames);
            Assert.DoesNotContain("mdRead", unusedNames);
            Assert.DoesNotContain("mdSampler", unusedNames);
        }
    }

    [NativeFact]
    public void Usage_is_per_entry_point_which_is_the_whole_reason_it_is_not_in_the_snapshot()
    {
        // `mdRead` is used by the program and not by its vertex stage. A ProgramLayout describes
        // every stage at once and has nowhere to put an answer that differs between them, which is
        // why this lives on LinkedProgram rather than on the snapshot.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var vertex = module.FindEntryPoint("mdVertex");
            using var fragment = module.FindEntryPoint("mdFragment");
            using var program = session.Link(module, vertex, fragment);

            var layout = program.Reflect();
            var read = layout.Parameters.Single(p => p.Name == "mdRead").PrimaryBinding;
            Assert.NotNull(read);

            using var whole = program.GetTargetMetadata();
            using var vertexOnly = program.GetEntryPointMetadata(0);
            using var fragmentOnly = program.GetEntryPointMetadata(1);

            Assert.True(whole.IsUsed(read.Value.Category, read.Value.Space, read.Value.Index));
            Assert.True(fragmentOnly.IsUsed(read.Value.Category, read.Value.Space, read.Value.Index));
            Assert.False(vertexOnly.IsUsed(read.Value.Category, read.Value.Space, read.Value.Index));
        }
    }

    [NativeFact]
    public void A_location_no_parameter_occupies_is_answered_not_refused()
    {
        // Measured, because the first version of this test was named for the opposite and asserted
        // nothing. Slang does not fail on a location outside the shader's layout — it answers
        // false, the same as it does for a declared-but-unread one. So an out-of-range query is
        // indistinguishable from a dead binding, which is fine for the one caller that matters
        // (UnusedBindings only ever asks about bindings the layout reported) and worth knowing for
        // anyone else.
        //
        // The failure path in IsUsed is therefore not exercised here and cannot be: it belongs to
        // targets whose backend does not track usage at all, and SPIR-V does. It throws rather
        // than defaulting for the reason given on IsUsed.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var fragment = module.FindEntryPoint("mdFragment");
            using var program = session.Link(module, fragment);
            using var metadata = program.GetTargetMetadata();

            Assert.False(metadata.IsUsed(
                SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT, 7, 99));
        }
    }

    [NativeFact]
    public void Metadata_outlives_nothing_and_disposes_cleanly()
    {
        // It holds a managed reference to the program, so disposal order does not matter — the
        // rule the whole of SlangObject exists to enforce.
        var (global, session, module) = Load();
        using (global) using (session) using (module)
        {
            using var fragment = module.FindEntryPoint("mdFragment");
            var program = session.Link(module, fragment);
            var metadata = program.GetTargetMetadata();

            program.Dispose();
            metadata.Dispose();
            metadata.Dispose();
        }
    }
}
