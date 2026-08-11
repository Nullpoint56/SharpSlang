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
// Slang's reflection behaviour (§9.20, §9.22, §9.24), so here it is not.
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

using System.Security.Cryptography;
using System.Text;
using PrismSlang.Reflection;

namespace PrismSlang.Tests;

public class SpirvDigestTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>Where the CI job collects it from. One file, so the artifact is just this file.</summary>
    private const string DigestFile = "spirv-digests.txt";

    [NativeFact]
    public void The_corpus_compiles_deterministically_and_records_a_digest_for_every_shader()
    {
        var lines = new List<string>();

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

            lines.Add($"{shaderCase.Name,-32} spirv={first.Spirv} layout={first.Layout}");
        }

        Assert.Equal(ShaderCorpus.All.Count, lines.Count);

        // Written next to the test assembly, which is what the CI upload glob finds. Printed too,
        // so a disagreement can be read straight out of the run that found it rather than by
        // downloading two artifacts and diffing them by hand.
        // Explicit "\n", not WriteAllLines. This file is compared byte-for-byte between four
        // runners, and WriteAllLines uses Environment.NewLine — the measuring instrument had the
        // very defect it was built to find, and would have failed the comparison on line endings
        // while every digest in it matched. Caught by running it on two platforms before wiring up
        // the CI job rather than after.
        var path = Path.Combine(AppContext.BaseDirectory, DigestFile);
        File.WriteAllText(path, string.Concat(lines.Select(l => l + "\n")));

        output.WriteLine($"{lines.Count} shaders -> {path}");
        foreach (var line in lines) output.WriteLine("    " + line);
    }

    private static (string Spirv, string Layout) Compile(ShaderCase shaderCase)
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
            using var blob = program.GetTargetCode();

            return (Digest(blob.ToArray()), Digest(Encoding.UTF8.GetBytes(program.Reflect().ToJson())));
        }
        finally
        {
            foreach (var e in entryPoints) e.Dispose();
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
}
