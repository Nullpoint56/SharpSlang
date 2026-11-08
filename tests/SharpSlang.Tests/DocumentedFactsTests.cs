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
using SharpSlang.Native;

namespace SharpSlang.Tests;

public class DocumentedFactsTests
{
    // The rows one data attribute gives a theory: one per InlineData, and as many as the member a
    // MemberData names yields. Another kind fails, so it is taught here rather than miscounted.
    private static int Rows(MethodInfo method, Xunit.v3.DataAttribute data)
    {
        switch (data)
        {
            case InlineDataAttribute:
                return 1;
            case MemberDataAttribute member:
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                var source = member.MemberType ?? method.DeclaringType!;
                var value = source.GetProperty(member.MemberName, flags)?.GetValue(null)
                    ?? source.GetField(member.MemberName, flags)?.GetValue(null)
                    ?? source.GetMethod(member.MemberName, flags)?.Invoke(null, member.Arguments);
                Assert.True(value is System.Collections.IEnumerable,
                    $"{method.DeclaringType!.Name}.{method.Name}: MemberData({member.MemberName}) on {source.Name} yields no rows");
                return ((System.Collections.IEnumerable)value!).Cast<object>().Count();
            default:
                Assert.Fail($"{method.DeclaringType!.Name}.{method.Name}: {data.GetType().Name} is not counted; teach Rows about it");
                return 0;
        }
    }

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
        Documents("AGENTS.md", $"resolves all {count} bound exports",
            $"SlangApi declares {count} DllImports");
    }

    [Fact]
    public void The_test_count_the_readme_states_is_the_test_count()
    {
        // Cases, not methods — a theory with 28 rows is 28 tests, which is what the runner reports:
        // every fact once and every data row of a theory once, the total `dotnet test` prints on a
        // machine where nothing skips (a skipped theory is one result, not one per row). This is
        // the one number here that moves on an ordinary commit, and enforcing it is a deliberate
        // trade: adding a test also means editing one line of README, in exchange for a headline
        // figure that is never quietly wrong. The failure below prints the number to paste.
        //
        // The rows are counted from the data attributes themselves, not through xunit.v3's
        // DataAttribute.GetData: called outside a test run it returns no row for any MemberData
        // (measured: 468 of the 761 cases the runner ran, every MemberData theory at 0).
        var count = 0;

        foreach (var type in typeof(DocumentedFactsTests).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance
                         | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                // TheoryAttribute derives from FactAttribute, so theories are asked about first.
                if (method.GetCustomAttribute<TheoryAttribute>() is not null)
                    count += method.GetCustomAttributes<Xunit.v3.DataAttribute>().Sum(d => Rows(method, d));
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
    public void The_dxc_targets_copy_from_the_package_version_the_pin_names()
    {
        // SharpSlang.Dxc depends on Microsoft.Direct3D.DXC at DxcPackageVersion, and its
        // targets file copies dxcompiler.dll and dxil.dll from that package's folder in the NuGet
        // cache — a path that has to restate the version, because NuGet hands a consumer no path
        // property for a transitive package. Two statements of one number, in two files, and a bump
        // that touches one would restore a package the targets never look at: the build would fail
        // on every consumer with the targets' own error, which is better than silence but worse
        // than failing here.
        var props = Read("Directory.Build.props");
        var match = System.Text.RegularExpressions.Regex.Match(props, "<DxcPackageVersion>([^<]+)</DxcPackageVersion>");
        Assert.True(match.Success, "Directory.Build.props does not pin DxcPackageVersion");
        var pinned = match.Groups[1].Value.Trim();

        Documents("dxc/buildTransitive/SharpSlang.Dxc.targets", $">{pinned}<",
            $"the pin is Microsoft.Direct3D.DXC {pinned}, and the targets file must copy from that version's folder");
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
