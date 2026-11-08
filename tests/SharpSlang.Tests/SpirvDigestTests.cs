// Whether two machines cooking the same shader produce the same asset.
//
// Nothing in this suite compared output *between* platforms. Every oracle here runs inside one
// process on one machine: agreement, the validation layers, the allocators, the API approval. They
// can all be green on four runners that disagree with each other.
//
// That matters for a cook farm specifically. If a developer's Windows box and a Linux CI runner
// emit byte-different modules for the same source, cooked assets depend on which machine cooked
// them — content hashes stop matching, incremental cooks redo everything, and a shipped asset
// differs from the one that was tested. Normally the prior on "a compiler is deterministic" is
// strong enough not to bother; this project has already found three-way *platform splits* in
// Slang's reflection behaviour, so here it is not.
//
// This test produces the evidence; tools/ci/compare-digests.sh is what judges it, across the four
// artifacts the matrix uploads. Deliberately not a golden file committed to the tree: a golden
// answers "did the output change", which goes stale on every version bump and gets regenerated
// whenever it is inconvenient. Comparing the legs to each other answers "do build machines agree",
// which is the question that matters and needs no maintenance.
//
// Both halves of the cooked artifact are digested. The SPIR-V is what the driver consumes; the
// reflection JSON is what the descriptor layout is built from, and a platform that agreed on the
// module while disagreeing on the layout would be worse, not better.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SharpSlang.Tests;

public class SpirvDigestTests(ITestOutputHelper output)
{
    // Two files, because the two halves of a cooked artifact turned out to obey different rules.
    // Every leg agrees on the layout; the module is identical between operating systems and differs
    // between *architectures*, for the three shaders that use SV_VertexID. One file could
    // only ever be compared one way.
    private const string SpirvFile = "spirv-digests.txt";
    private const string LayoutFile = "layout-digests.txt";
    private const string PatchesFile = "payload-patches.txt";

    [NativeFact]
    public void The_corpus_compiles_deterministically_and_records_a_digest_for_every_shader()
    {
        var spirv = new List<string>();
        var layouts = new List<string>();

        foreach (var shaderCase in ShaderCorpus.All.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            // Twice, each from a fresh global session, because determinism *within* a platform is
            // the precondition for the cross-platform comparison meaning anything. A digest that
            // varies run to run would make the CI job flap and would be a far worse finding than
            // the platforms disagreeing.
            var first = Compile(shaderCase);
            var second = Compile(shaderCase);

            Assert.True(first.Spirv == second.Spirv,
                $"'{shaderCase.Name}' compiled to different SPIR-V twice in the same process. "
                + "Codegen is not deterministic, which makes every cooked asset unreproducible "
                + "and the cross-platform digest comparison meaningless.");

            Assert.True(first.Layout == second.Layout,
                $"'{shaderCase.Name}' reflected differently twice in the same process. A layout "
                + "that varies run to run cannot be cached, and ProgramLayout.ToJson is what the "
                + "bake format is built on.");

            // words and ops are the discriminator, and they are the reason a disagreement is
            // diagnosable from the CI log instead of needing a machine of each architecture. `ops`
            // digests the sorted opcode histogram, so if two legs differ on `spirv` while agreeing
            // on `words` and `ops`, the modules hold the same instructions in a different order or
            // with different result IDs — a renumbering. If those differ too, the code is genuinely
            // not the same.
            spirv.Add(
                $"{shaderCase.Name,-32} spirv={first.Spirv} words={first.Words,-5} ops={first.Ops}");
            layouts.Add($"{shaderCase.Name,-32} layout={first.Layout}");
        }

        Assert.Equal(ShaderCorpus.All.Count, spirv.Count);

        // Written next to the test assembly, which is what the CI upload glob finds. Printed too,
        // so a disagreement can be read straight out of the run that found it rather than by
        // downloading two artifacts and diffing them by hand.
        // Explicit "\n", not WriteAllLines. This file is compared byte-for-byte between four
        // runners, and WriteAllLines uses Environment.NewLine — the measuring instrument had the
        // very defect it was built to find, and would have failed the comparison on line endings
        // while every digest in it matched. Caught by running it on two platforms before wiring up
        // the CI job rather than after.
        Write(SpirvFile, spirv);
        Write(LayoutFile, layouts);
        // The patches the staged payload carries, one file name a line (none for Slang's release),
        // because which shaders may differ between architectures depends on them: 0021 removes the
        // SV_VertexID difference tools/ci/cross-arch-differences.txt declares for the release.
        Write(PatchesFile, [.. StagedPayload.Patches.Select(p => p.File)]);
        WriteModules();

        output.WriteLine($"{spirv.Count} shaders");
        foreach (var line in spirv) output.WriteLine("    " + line);
        foreach (var line in layouts) output.WriteLine("    " + line);

        void Write(string name, List<string> lines)
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));
            output.WriteLine($"-> {path}");
        }

        // The modules themselves, so a cross-platform disagreement can be taken apart rather than
        // only detected.  established that x64 and arm64 produce the same word count and the
        // same opcode histogram for the three SV_VertexID shaders — strong evidence of a
        // renumbering, but evidence, not proof. Proof needs the words, and the words only exist on
        // a machine of each architecture.
        //
        // All of them, not just the three known to differ: about 2 KB each, 64 KB for the corpus,
        // and picking a subset would mean keeping that subset in step with
        // tools/ci/cross-arch-differences.txt forever. tools/ci/spirv-worddiff.py is what reads
        // them.
        void WriteModules()
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "spirv-modules");
            Directory.CreateDirectory(directory);

            foreach (var shaderCase in ShaderCorpus.All)
            {
                File.WriteAllBytes(
                    Path.Combine(directory, $"{shaderCase.Name}.spv"), Compile(shaderCase).Code);
            }

            output.WriteLine($"-> {directory}");
        }
    }

    // The three corpus shaders that read SV_VertexID, which tools/ci/cross-arch-differences.txt
    // names as the ones x86-64 and aarch64 builds of Slang's release disagree on (U17).
    private static readonly string[] VertexIdShaders = ["stage-narrowing", "system-values", "vertex-interface"];

    [PatchedFact("U17")]
    public void SV_VertexID_builtins_are_numbered_in_source_order()
    {
        // U17, patched (0021): VertexIndex is loaded before BaseVertex (and InstanceIndex before
        // BaseInstance) whatever order the host compiler evaluates arguments in, so the builtin
        // that is read first is numbered first, on every architecture.
        foreach (var name in VertexIdShaders)
        {
            var ids = BuiltinIds(Compile(ShaderCorpus.Case(name)).Code);
            Assert.True(ids[VertexIndex] < ids[BaseVertex],
                $"{name}: VertexIndex is %{ids[VertexIndex]}, BaseVertex %{ids[BaseVertex]}");
            if (ids.TryGetValue(InstanceIndex, out var instanceIndex) && ids.TryGetValue(BaseInstance, out var baseInstance))
                Assert.True(instanceIndex < baseInstance, $"{name}: InstanceIndex is %{instanceIndex}, BaseInstance %{baseInstance}");
        }
    }

    [ReleasePayloadFact("U17")]
    public void SV_VertexID_builtins_are_numbered_by_the_host_compilers_argument_order()
    {
        // U17, asserted as wrong on Slang's release: the two loads are the arguments of one call, so
        // the order they are emitted in, and the builtins numbered in, is the host compiler's. The
        // x86-64 builds number BaseVertex first and the aarch64 builds VertexIndex first, which is
        // the cross-architecture difference tools/ci/cross-arch-differences.txt declares.
        var vertexIndexFirst = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        foreach (var name in VertexIdShaders)
        {
            var ids = BuiltinIds(Compile(ShaderCorpus.Case(name)).Code);
            Assert.True(vertexIndexFirst == ids[VertexIndex] < ids[BaseVertex],
                $"{name} on {RuntimeInformation.ProcessArchitecture}: VertexIndex is %{ids[VertexIndex]}, BaseVertex %{ids[BaseVertex]}");
        }
    }

    private const uint VertexIndex = 42, InstanceIndex = 43, BaseVertex = 4424, BaseInstance = 4425;

    /// <summary>The result ID each <c>OpDecorate ... BuiltIn</c> names, by builtin.</summary>
    private static Dictionary<uint, uint> BuiltinIds(byte[] code)
    {
        var words = MemoryMarshal.Cast<byte, uint>(code);
        var ids = new Dictionary<uint, uint>();
        for (var i = 5; i < words.Length;)
        {
            var count = (int)(words[i] >> 16);
            if (count == 0) break;
            if ((words[i] & 0xFFFF) == 71 && count >= 4 && words[i + 2] == 11) // OpDecorate, BuiltIn
                ids[words[i + 3]] = words[i + 1];
            i += count;
        }
        return ids;
    }

    private static (string Spirv, int Words, string Ops, string Layout, byte[] Code) Compile(ShaderCase shaderCase)
    {
        // The same settings ModuleAgreementTests uses, so the digest covers the module that test
        // already checks rather than a differently-configured one.
        using var global = SlangGlobalSession.Create();
        using var session = global.CreateSession(new SessionOptions
        {
            Targets = [new TargetOptions { UseEntryPointNameInSpirv = true }],
        });

        using var module = session.LoadModuleFromSource(
            shaderCase.Name, $"{shaderCase.Name}.slang", shaderCase.Source);

        var entryPoints = shaderCase.EntryPoints.Select(module.FindEntryPoint).ToList();
        try
        {
            using var program = session.Link(module, [.. entryPoints]);
            using var blob = program.GetTargetCode(0);

            var code = blob.ToArray();
            var (words, ops) = Structure(code);

            return (
                Digest(code),
                words,
                ops,
                Digest(Encoding.UTF8.GetBytes(program.Reflect(0).ToJson())),
                code);
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    /// <summary>Word count and a digest of the sorted opcode histogram.</summary>
    /// <remarks>
    /// Order-independent on purpose. Two modules with the same histogram and the same length hold
    /// the same instructions, so a `spirv=` difference between them is a renumbering or a
    /// reordering rather than different code — which is the distinction that decides whether a
    /// cross-platform disagreement is cosmetic or serious, and it is otherwise only answerable by
    /// having one machine of each architecture to disassemble on.
    /// </remarks>
    private static (int Words, string Ops) Structure(byte[] code)
    {
        var words = MemoryMarshal.Cast<byte, uint>(code);
        Assert.Equal(0x07230203u, words[0]);          // little-endian SPIR-V, or the walk is nonsense

        var histogram = new SortedDictionary<uint, int>();
        for (var i = 5; i < words.Length;)            // 5-word header
        {
            var length = (int)(words[i] >> 16);
            if (length <= 0) break;                   // malformed; the digest of what was read still differs
            var opcode = words[i] & 0xFFFF;
            histogram[opcode] = histogram.GetValueOrDefault(opcode) + 1;
            i += length;
        }

        var flattened = string.Join(",", histogram.Select(e => $"{e.Key}:{e.Value}"));
        return (words.Length, Digest(Encoding.UTF8.GetBytes(flattened)));
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
}
