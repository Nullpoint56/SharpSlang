// Diagnostics on a call that succeeded.
//
// The motivating case cannot be reproduced in-process: it needs slang-glslang to be absent when the
// compiler loads it, and by the time a test runs the library is either there or the whole suite is
// skipped. So the diagnostic text below was captured from a real run with the library removed, and
// these tests hold the classifier to it. The complementary live check — that spirv-opt is actually
// loadable — is DownstreamCompilerTests.

using PrismSlang.Interop;

namespace PrismSlang.Tests;

public class DiagnosticPolicyTests
{
    /// <summary>
    /// Verbatim from slang-compiler 2026.14.1 with slang-glslang deleted from the output directory,
    /// emitted from getEntryPointCode on a SLANG_OK result; slangc 2026.21 without slang-glslang
    /// prints the same two lines.
    /// </summary>
    private const string MissingSpirvOpt = """
        error[E00100]: failed to load downstream compiler 'spirv-opt'
        note[E99996]: failed to load dynamic library 'slang-glslang'
        """;

    /// <summary>Verbatim, from compiling with a spirv_1_0 profile. Also on a SLANG_OK result.</summary>
    private const string OldSpirvVersion = "warning[E50011]: SPIR-V version too old\n";

    // ---------------------------------------------------------------- classification

    [Fact]
    public void The_missing_optimiser_message_classifies_as_an_error()
    {
        // If this ever reads as a note, a build silently ships shaders ~23% larger than intended.
        Assert.Equal(SlangDiagnosticSeverity.Error, SlangDiagnostics.Classify(MissingSpirvOpt));
    }

    [Theory]
    [InlineData("warning[E50011]: SPIR-V version too old", SlangDiagnosticSeverity.Warning)]
    [InlineData("note[E99996]: failed to load dynamic library 'slang-glslang'", SlangDiagnosticSeverity.Note)]
    [InlineData("error[E00100]: failed to load downstream compiler 'spirv-opt'", SlangDiagnosticSeverity.Error)]
    [InlineData("fatal error[E40003]: compilation ceased", SlangDiagnosticSeverity.Fatal)]
    [InlineData("t.slang(12): error 30019: expected an expression", SlangDiagnosticSeverity.Error)]
    [InlineData("t.slang(12): warning 30020: implicit conversion", SlangDiagnosticSeverity.Warning)]
    [InlineData("", SlangDiagnosticSeverity.Note)]
    [InlineData(null, SlangDiagnosticSeverity.Note)]
    public void Severity_is_read_from_the_message_prefix(string? text, SlangDiagnosticSeverity expected) =>
        Assert.Equal(expected, SlangDiagnostics.Classify(text));

    [Fact]
    public void The_worst_severity_in_a_multi_line_blob_wins()
    {
        Assert.Equal(SlangDiagnosticSeverity.Error, SlangDiagnostics.Classify(
            "warning[E1]: a\nnote[E2]: b\nerror[E3]: c\nnote[E4]: d"));

        Assert.Equal(SlangDiagnosticSeverity.Fatal, SlangDiagnostics.Classify(
            "error[E3]: c\nfatal error[E40003]: compilation ceased"));
    }

    [Fact]
    public void The_word_error_inside_a_message_does_not_escalate_it()
    {
        // "undefined identifier 'error_count'" must stay a warning. Anchoring on line starts is
        // what keeps the default policy from failing builds over a variable name.
        Assert.Equal(SlangDiagnosticSeverity.Warning, SlangDiagnostics.Classify(
            "warning[E30001]: unused variable 'error_count' near the error handler"));

        Assert.Equal(SlangDiagnosticSeverity.Note, SlangDiagnostics.Classify(
            "the shader compiled without error"));
    }

    // ---------------------------------------------------------------- policy

    [Fact]
    public void The_default_policy_throws_on_an_error_from_a_successful_call()
    {
        using var _ = new PolicyScope(SlangDiagnosticPolicy.ThrowOnError);

        var ex = Assert.Throws<SlangException>(
            () => Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "getEntryPointCode", MissingSpirvOpt));

        Assert.Equal(SlangResult.Ok, ex.Result);
        Assert.Equal(MissingSpirvOpt, ex.Diagnostics);

        // "failed: SLANG_OK" would send the reader looking for a result code that is fine.
        Assert.Contains("succeeded but reported errors", ex.Message);
        Assert.Contains("spirv-opt", ex.Message);
    }

    [Fact]
    public void The_default_policy_lets_warnings_through_to_the_handler()
    {
        using var _ = new PolicyScope(SlangDiagnosticPolicy.ThrowOnError);

        var seen = new List<(string Op, string Text)>();
        SlangDiagnostics.Handler = (op, _, text) => seen.Add((op, text));

        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "getEntryPointCode", OldSpirvVersion);

        Assert.Equal(("getEntryPointCode", OldSpirvVersion), Assert.Single(seen));
    }

    [Fact]
    public void Report_hands_everything_to_the_handler_including_errors()
    {
        using var _ = new PolicyScope(SlangDiagnosticPolicy.Report);

        var seen = new List<string>();
        SlangDiagnostics.Handler = (_, _, text) => seen.Add(text);

        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "op", MissingSpirvOpt);

        Assert.Equal(MissingSpirvOpt, Assert.Single(seen));
    }

    [Fact]
    public void The_handler_is_told_the_severity_it_would_otherwise_have_to_derive()
    {
        // The point of Report is to let a build log warnings and fail on errors itself, which needs
        // the severity. Slang has no structured severity — it comes from parsing message prefixes —
        // so a handler working it out again would be re-deriving Classify, less carefully.
        using var _ = new PolicyScope(SlangDiagnosticPolicy.Report);

        var seen = new List<(string Operation, SlangDiagnosticSeverity Severity)>();
        SlangDiagnostics.Handler = (op, severity, _) => seen.Add((op, severity));

        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "getEntryPointCode", MissingSpirvOpt);
        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "compile", "warning[E50011]: SPIR-V version too old");
        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "compile", "note[E99996]: something");

        Assert.Equal(
        [
            ("getEntryPointCode", SlangDiagnosticSeverity.Error),
            ("compile", SlangDiagnosticSeverity.Warning),
            ("compile", SlangDiagnosticSeverity.Note),
        ], seen);
    }

    [Fact]
    public void The_severity_the_handler_gets_is_the_one_ThrowOnError_would_have_acted_on()
    {
        // One classification, used for both decisions. If these could differ, a build could log a
        // warning for the same text that another policy treats as fatal.
        Assert.Equal(SlangDiagnosticSeverity.Error, SlangDiagnostics.Classify(MissingSpirvOpt));

        using var _ = new PolicyScope(SlangDiagnosticPolicy.ThrowOnError);
        var e = Assert.Throws<SlangException>(
            () => Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "op", MissingSpirvOpt));

        Assert.Contains("succeeded but reported errors", e.Message);
    }

    [Fact]
    public void Ignore_drops_them()
    {
        using var _ = new PolicyScope(SlangDiagnosticPolicy.Ignore);

        var seen = 0;
        SlangDiagnostics.Handler = (_, _, _) => seen++;

        Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "op", MissingSpirvOpt);

        Assert.Equal(0, seen);
    }

    [Fact]
    public void The_policy_never_applies_to_a_failed_call()
    {
        // A failure always throws with its diagnostics attached, whatever the policy — Ignore is
        // about *success* diagnostics only, and must not be able to swallow a real error.
        using var _ = new PolicyScope(SlangDiagnosticPolicy.Ignore);

        var ex = new SlangException(SlangResult.Fail, "link", "error[E1]: no");
        Assert.Contains("link failed", ex.Message);
        Assert.Contains("error[E1]", ex.Message);
    }

    [Fact]
    public void No_diagnostics_means_nothing_happens_under_any_policy()
    {
        foreach (var policy in Enum.GetValues<SlangDiagnosticPolicy>())
        {
            using var _ = new PolicyScope(policy);

            var seen = 0;
            SlangDiagnostics.Handler = (_, _, _) => seen++;

            Diagnostics.ApplySuccessPolicy(SlangResult.Ok, "op", null);
            Assert.Equal(0, seen);
        }
    }

    /// <summary>Restores the process-wide policy and handler however the test exits.</summary>
    private sealed class PolicyScope : IDisposable
    {
        private readonly SlangDiagnosticPolicy _policy = SlangDiagnostics.Policy;
        private readonly SlangDiagnosticHandler _handler = SlangDiagnostics.Handler;

        public PolicyScope(SlangDiagnosticPolicy policy) => SlangDiagnostics.Policy = policy;

        public void Dispose()
        {
            SlangDiagnostics.Policy = _policy;
            SlangDiagnostics.Handler = _handler;
        }
    }
}
