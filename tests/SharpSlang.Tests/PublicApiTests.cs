// The public surface of all three packages that ship an assembly, written down.
//
// These are libraries an engine builds against, and the moment one of them is 1.0 its surface is a
// promise. Nothing was checking the promise. A member removed, a parameter reordered, a type
// quietly made internal — none of it fails a build here, and all of it fails a consumer's.
//
// So the surface is rendered as C# and checked against a file in the repository. A change to the
// public API becomes a diff in that file, which someone has to look at and accept. That is the
// whole mechanism: it does not prevent breaking changes, it prevents *unnoticed* ones.
//
// Regenerating: run with SHARPSLANG_APPROVE_API=1 and the approved files are rewritten. Read the
// resulting diff before committing it — that is the entire point, and rewriting them because a
// test is red defeats it.

using System.Reflection;
using PublicApiGenerator;

namespace SharpSlang.Tests;

public class PublicApiTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly ApiGeneratorOptions Options = new()
    {
        // Attributes that say nothing about the shape of the API and everything about how it was
        // built — they would make the approved files churn on a version bump rather than on a
        // change anyone cares about.
        ExcludeAttributes =
        [
            "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
            "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
            "System.Reflection.AssemblyMetadataAttribute",
            "System.Runtime.Versioning.TargetFrameworkAttribute",
            "System.Reflection.AssemblyCompanyAttribute",
            "System.Reflection.AssemblyConfigurationAttribute",
            "System.Reflection.AssemblyFileVersionAttribute",
            "System.Reflection.AssemblyInformationalVersionAttribute",
            "System.Reflection.AssemblyProductAttribute",
            "System.Reflection.AssemblyTitleAttribute",
            "System.Reflection.AssemblyVersionAttribute",
        ],
    };

    private static readonly string[] Packages =
        ["SharpSlang", "SharpSlang.Pipeline", "SharpSlang.Pipeline.NRI"];

    public static TheoryData<string> Assemblies()
    {
        var data = new TheoryData<string>();
        foreach (var name in Packages) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void The_public_surface_is_the_one_that_was_approved(string name)
    {
        var assembly = Load(name);
        var actual = assembly.GeneratePublicApi(Options).ReplaceLineEndings("\n").TrimEnd() + "\n";
        var path = ApprovedPath(name);

        if (Environment.GetEnvironmentVariable("SHARPSLANG_APPROVE_API") == "1")
        {
            File.WriteAllText(path, actual);
            output.WriteLine($"rewrote {path} ({actual.Split('\n').Length} lines)");
            return;
        }

        Assert.True(File.Exists(path),
            $"no approved surface for {name}. Generate one with SHARPSLANG_APPROVE_API=1 and read "
            + "the result before committing it.");

        var approved = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd() + "\n";
        if (approved == actual) return;

        // A whole-file Assert.Equal on a 1,000-line string is unreadable. The first differing
        // lines are what a reviewer needs, and the instruction for producing the full diff.
        var a = approved.Split('\n');
        var b = actual.Split('\n');
        var differences = new List<string>();

        for (var i = 0; i < Math.Max(a.Length, b.Length) && differences.Count < 40; i++)
        {
            var left = i < a.Length ? a[i] : "<end of approved>";
            var right = i < b.Length ? b[i] : "<end of actual>";
            if (left != right) differences.Add($"    line {i + 1}\n      approved: {left}\n      actual:   {right}");
        }

        Assert.Fail(
            $"{name}'s public surface differs from {Path.GetFileName(path)}:\n"
            + string.Join("\n", differences)
            + $"\n\n    approved {a.Length} lines, actual {b.Length} lines."
            + "\n    If the change is intended: SHARPSLANG_APPROVE_API=1 dotnet test --filter "
            + "PublicApiTests, then read the diff.");
    }

    [Fact]
    public void Every_package_has_an_approved_surface()
    {
        // A missing file would make the test above fail with a clear message, but only for the
        // package someone remembered to list. This catches another package added without one.
        foreach (var name in Packages)
            Assert.True(File.Exists(ApprovedPath(name)), $"{name} has no approved surface on disk");
    }

    private static Assembly Load(string name) => name switch
    {
        "SharpSlang" => typeof(SlangGlobalSession).Assembly,
        "SharpSlang.Pipeline" => typeof(Reflection.ProgramLayout).Assembly,
        "SharpSlang.Pipeline.NRI" => typeof(Pipeline.NRI.NriPipelineProjection).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "not a package of this repo"),
    };

    /// <summary>
    /// Where the approved surface lives. <c>.txt</c>, not <c>.cs</c>.
    /// </summary>
    /// <remarks>
    /// The first version used <c>.cs</c> — it is C#, after all — and MSBuild promptly globbed the
    /// files into the test assembly's compilation. Two of the three even compiled, which would have
    /// been the worse outcome; the third does not, because PublicApiGenerator renders a
    /// <c>ref readonly Guid</c> property as <c>System.Guid&amp;</c>. These are a record of a shape,
    /// not source, and the extension now says so.
    /// </remarks>
    private static string ApprovedPath(string name) =>
        Path.Combine(SourceDirectory(), "ApprovedApi", $"{name}.approved.txt");

    /// <summary>
    /// This file's own directory, so the approved files sit beside the test rather than in the
    /// build output where they would be rewritten and lost.
    /// </summary>
    private static string SourceDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);

        return Path.Combine(
            dir ?? throw new InvalidOperationException("could not locate the repo root"),
            "tests", "SharpSlang.Tests");
    }
}
