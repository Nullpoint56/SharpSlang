// Numbers in the documentation, checked against the thing they describe.
//
// The documents state measured quantities — a 32-shader corpus, 278 bound exports — and a measured
// quantity in prose has a half-life. Two documents said "27-shader corpus" for as long as the
// corpus had 28 shaders in it, which nothing could notice: check-docs.sh verifies that every
// citation *resolves*, not that any claim is *true*.
//
// This is the narrow slice of that gap which is mechanically closable. A count the test suite can
// compute is a claim the test suite can enforce, so the documents stop being the place a number
// goes to rot. What remains open is everything qualitative — see NativeExportTests for a test whose
// name promised this check for months while its body asserted a 200-wide range.
//
// Each number is checked in the document that states it. Adding a corpus shader fails these by
// design: the failure names the new number and the file to put it in.

using System.Reflection;
using System.Runtime.InteropServices;
using PrismSlang.Native;

namespace PrismSlang.Tests;

public class DocumentedFactsTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            "could not locate the repo root (Directory.Build.props)");
    }

    /// <summary>Asserts a document contains a phrase, saying what the right phrase would be.</summary>
    /// <remarks>
    /// Internal because three of the numbers this guards are computed by tests that own the
    /// measurement — the ABI probe's counts, the vtable slot total, the size of the dispatched
    /// corpus — and pinning them from here would mean measuring them twice, in a class that cannot
    /// skip when the compiler or the device is missing.
    /// </remarks>
    internal static void Documents(string file, string phrase, string what)
    {
        var text = Read(file);
        Assert.True(text.Contains(phrase, StringComparison.Ordinal),
            $"{file} does not say \"{phrase}\".\n"
            + $"{what} — update the document, or the count changed for a reason worth reading.");
    }

    [Fact]
    public void The_corpus_size_the_documents_state_is_the_corpus_size()
    {
        var count = ShaderCorpus.All.Count;

        Documents("README.md", $"{count}-shader corpus",
            $"ShaderCorpus.All holds {count} shaders");
    }

    [Fact]
    public void The_number_of_bound_exports_the_documents_state_is_the_number_bound()
    {
        // Counted from the declarations, not from the library: this is a fact about L0 and holds
        // with no native payload present, which is why it is a Fact and not a NativeFact.
        var count = typeof(SlangApi)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Count(m => m.GetCustomAttribute<DllImportAttribute>() is not null);

        Documents("README.md", $"{count} `DllImport` entry points",
            $"SlangApi declares {count} DllImports");
        Documents("docs/design.md", $"resolves all {count} bound exports",
            $"SlangApi declares {count} DllImports");
    }

    [Fact]
    public void The_test_count_the_readme_states_is_the_test_count()
    {
        // Cases, not methods — a theory with 28 rows is 28 tests, which is what the runner reports.
        // This is the one number here that moves on an ordinary commit, and enforcing it is a
        // deliberate trade: adding a test also means editing one line of README, in exchange for a
        // headline figure that is never quietly wrong. The failure below prints the number to paste.
        var count = 0;

        foreach (var type in typeof(DocumentedFactsTests).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance
                         | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // TheoryAttribute derives from FactAttribute, so theories are asked about first.
                if (method.GetCustomAttribute<TheoryAttribute>() is not null)
                    count += method.GetCustomAttributes<Xunit.Sdk.DataAttribute>()
                                   .Sum(d => d.GetData(method).Count());
                else if (method.GetCustomAttribute<FactAttribute>() is not null)
                    count += 1;
            }
        }

        // One document states it, so one document is checked. The engineering log used to close
        // each section with the count at the time and the last of those was checked too; the log is
        // gone and the README is the only place the figure appears.
        Documents("README.md", $"{count} tests", $"the suite has {count} test cases");
    }

    [Fact]
    public void Every_corpus_shader_has_a_distinct_name()
    {
        // The corpus header says names are unique on purpose, because the agreement diff keys on
        // them and a collision would make two shaders look like one — a silently weaker harness
        // rather than a failure. Stated in a comment for the whole life of the corpus; asserted
        // here.
        var duplicates = ShaderCorpus.All
            .GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "corpus shader names must be unique; these are not: " + string.Join(", ", duplicates));
    }
}
