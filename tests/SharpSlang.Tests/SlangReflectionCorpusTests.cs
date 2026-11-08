// The Slang team's own answer, against this binding's.
//
// tests/reflection in shader-slang/slang is a set of shaders each carrying a `//TEST:REFLECTION:`
// directive and a checked-in .expected file holding the reflection JSON that directive must
// produce. It is the only oracle available to this repository that was authored by the people who
// wrote the compiler, rather than derived from the compiler by this repository.
//
// What it proves here is narrow and worth being precise about. `spReflection_ToJson` is Slang's own
// serializer, so a match does not say the projection in SharpSlang.Reflection is right — it says
// the *program* was built right. Session options, target, profile, matrix layout, preprocessor
// macros, which entry points were composed and in what order: every one of those changes the
// layout, and every one of them is plumbing this binding wrote. A case that matches is a case where
// the binding assembled exactly the program slangc assembles from the same command line.
//
// The projection itself is checked against the same files separately.
//
// Cases the binding cannot set up are reported with the reason and counted, never skipped quietly:
// a corpus harness that matches nothing is green, and looks identical to one that passes.

namespace SharpSlang.Tests;

public sealed class SlangReflectionCorpusTests(ITestOutputHelper output)
{
    /// <summary>What happened when one directive was reproduced through the binding.</summary>
    private enum Outcome
    {
        /// <summary>The JSON is what the Slang team recorded.</summary>
        Matched,

        /// <summary>It ran and produced different JSON.</summary>
        Differed,

        /// <summary>The binding cannot express this command line, for a stated reason.</summary>
        OutOfReach,

        /// <summary>Setting the case up threw.</summary>
        Threw,
    }

    private sealed record Result(SlangTestCorpus.ReflectionCase Case, Outcome Outcome, string Detail);

    // ---------------------------------------------------------------- running one directive

    private static Result Run(SlangTestCorpus.ReflectionCase test)
    {
        // `"used": 0` in the expected output is post-emit analysis: which parameters the generated
        // code actually reads. spReflection_ToJson reports it from the ICompileRequest it is
        // given, and LinkedProgram.ToJson passes null — that API is the legacy compile-request
        // surface this binding does not bind (AGENTS.md#unbound-surface). Requesting the entry
        // point code does not help; the analysis lives on the request, not on the program.
        //
        // Keyed on the property rather than on a file name, so a release that starts reporting it
        // without a request moves these cases back into reach on their own.
        if (test.ExpectedJson.Contains("\"used\":", StringComparison.Ordinal))
            return new Result(test, Outcome.OutOfReach,
                "the expected output carries post-emit `used` flags, which spReflection_ToJson only "
                + "produces from an ICompileRequest; LinkedProgram.ToJson passes null");

        try
        {
            using var built = SlangTestCorpus.Build(test);

            // Without -no-codegen slangc actually compiles, and reflection carries what only the
            // emit knows. Requesting the code here is what makes those cases comparable at all.
            if (!test.Has("-no-codegen"))
                for (var i = 0; i < built.EntryPointCount; i++)
                    built.Program.GetEntryPointCodeBytes(i, 0);

            var produced = built.Program.ToJson(0);
            if (string.IsNullOrWhiteSpace(produced))
                return new Result(test, Outcome.Threw, "spReflection_ToJson produced nothing");

            return Normalize(produced) == Normalize(test.ExpectedJson)
                ? new Result(test, Outcome.Matched, $"{test.ExpectedJson.Length} chars")
                : new Result(test, Outcome.Differed, FirstDifference(test.ExpectedJson, produced));
        }
        catch (SlangTestCorpus.OutOfReachException e)
        {
            return new Result(test, Outcome.OutOfReach, e.Message);
        }
        catch (Exception e)
        {
            return new Result(test, Outcome.Threw, $"{e.GetType().Name}: {Oneline(e.Message)}");
        }
    }

    /// <summary>Line endings and trailing space only. Anything else would be hiding a difference.</summary>
    private static string Normalize(string json) =>
        string.Join('\n', json.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Select(l => l.TrimEnd())).Trim();

    private static string FirstDifference(string expected, string actual)
    {
        var want = Normalize(expected).Split('\n');
        var got = Normalize(actual).Split('\n');

        for (var i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            var w = i < want.Length ? want[i] : "(end)";
            var g = i < got.Length ? got[i] : "(end)";
            if (w != g) return $"line {i + 1}: slang \"{w.Trim()}\" / binding \"{g.Trim()}\"";
        }

        return $"{want.Length} vs {got.Length} lines, no differing line found";
    }

    private static string Oneline(string s)
    {
        var text = s.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ');
        return text.Length > 160 ? text[..160] + "..." : text;
    }

    // ---------------------------------------------------------------- the tests

    private static readonly Lazy<IReadOnlyList<Result>> LazyResults =
        new(() => [.. SlangTestCorpus.Reflection.Select(Run)]);

    private static IReadOnlyList<Result> Results => LazyResults.Value;

    [NativeFact]
    public void The_corpus_has_cases_and_they_were_all_attempted()
    {
        // Both halves matter. An empty corpus is a download that failed, and every other test in
        // this file passes vacuously on one.
        var cases = SlangTestCorpus.Reflection;
        output.WriteLine($"tests/reflection at {SlangTestCorpus.Version}: {cases.Count} directives with an "
            + "expected result");

        Assert.InRange(cases.Count, 20, 200);
        Assert.Equal(cases.Count, Results.Count);
    }

    [NativeFact]
    public void The_binding_reproduces_the_reflection_the_Slang_team_recorded()
    {
        foreach (var group in Results.GroupBy(r => r.Outcome).OrderBy(g => g.Key))
            output.WriteLine($"{group.Key}: {group.Count()}");

        output.WriteLine("");
        foreach (var r in Results)
            output.WriteLine($"  {r.Outcome,-11} {r.Case,-42} {r.Detail}");

        var wrong = Results.Where(r => r.Outcome is Outcome.Differed or Outcome.Threw).ToList();

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {Results.Count} directive(s) did not reproduce the result Slang's own "
            + $"test suite records at {SlangTestCorpus.Version}:\n"
            + string.Join("", wrong.Select(r => $"    {r.Outcome} {r.Case}\n        {r.Detail}\n")));
    }

    /// <summary>Directives this binding cannot reproduce, and why.</summary>
    /// <remarks>
    /// Declared rather than counted, in the shape <c>SurfaceCoverageTests</c> uses, and checked
    /// both ways. Without the second direction "out of reach" becomes a way to make a failing case
    /// pass, which is the one thing a corpus harness must not offer.
    /// </remarks>
    private static readonly Dictionary<string, string> OutOfReachCases = new(StringComparer.Ordinal)
    {
        ["actual-global.slang"] =
            "-target host-callable is CPU code through slang-llvm, the 80 MB library the payload "
            + "deliberately excludes (tools/fetch-native.ps1)",

        ["multi-file.hlsl"] =
            "two translation units composed into one program, with an entry point from each. "
            + "SlangSession.Link composes entry points into a single module; a composite of two "
            + "modules needs createCompositeComponentType, which L2 does not surface",

        ["texture-resource-type.slang"] =
            "expects post-emit `used` flags, which need an ICompileRequest",

        ["used-parameters.slang"] =
            "as above — the file exists upstream to test exactly that analysis",
    };

    [NativeFact]
    public void Nothing_is_out_of_reach_but_the_cases_declared_to_be()
    {
        var actual = Results
            .Where(r => r.Outcome == Outcome.OutOfReach)
            .ToDictionary(r => r.Case.ToString(), r => r.Detail, StringComparer.Ordinal);

        foreach (var (name, reason) in actual) output.WriteLine($"out of reach: {name}\n    {reason}");

        var undeclared = actual.Keys.Where(k => !OutOfReachCases.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var stale = OutOfReachCases.Keys.Where(k => !actual.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            $"{undeclared.Count} directive(s) became unreproducible without being declared:\n"
            + string.Join("", undeclared.Select(k => $"    {k}: {actual[k]}\n")));

        Assert.True(stale.Count == 0,
            $"{stale.Count} directive(s) are declared out of reach and now run. Remove them:\n"
            + string.Join("", stale.Select(k => $"    {k} — was: {OutOfReachCases[k]}\n")));
    }
}
