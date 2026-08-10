// The reflection call that does not fail, asserted from outside the process it ends.
//
// §9.12 measured that reflecting a resource array sized by a specialization constant terminates the
// process inside spReflectionTypeLayout_getBindingRangeCount — no exception, no managed stack, no
// diagnostic. A test cannot assert that in-process, so for several rounds it was a sentence in the
// documentation that nothing checked, which is the same category as a comment asserting something
// the code does not do (§9.15). It just happened to be true.
//
// PrismSlang.Prober is a program that performs one such operation and exits. Reading its exit code
// turns the sentence into a test, and — the part worth having — turns the day Slang fixes this into
// a failing test rather than into silence.
//
// The control case matters more than the crashing one. A prober that cannot find its native payload
// also dies, and would make every assertion here pass for the wrong reason, which is why `healthy`
// is asserted first and separately.

using System.Diagnostics;
using System.Reflection;

namespace PrismSlang.Tests;

public class ProberTests
{
    private sealed record Outcome(int ExitCode, string Stdout, string Stderr)
    {
        public override string ToString() =>
            $"exit {ExitCode}\nstdout: {Stdout.Trim()}\nstderr: {Stderr.Trim()}";
    }

    private static Outcome Run(string probe)
    {
        // The apphost, not `dotnet exec`. The obvious spelling was
        // `Environment.ProcessPath exec PrismSlang.Prober.dll`, on the reasoning that the child
        // should use the same runtime as the runner — but under `dotnet test` the current process
        // is testhost.exe, which does not take `exec`, and every probe came back 0x80008083. Both
        // control cases caught it, which is what they are for.
        var host = Path.Combine(
            ProberDirectory(),
            OperatingSystem.IsWindows() ? "PrismSlang.Prober.exe" : "PrismSlang.Prober");

        Assert.True(File.Exists(host),
            $"the prober is not built at {host}. Its project must be in the solution and its "
            + "output directory must reach this test through the ProberOutput assembly metadata.");

        var info = new ProcessStartInfo(host)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = ProberDirectory(),
        };

        info.ArgumentList.Add(probe);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("could not start the prober");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(60_000), "the prober did not exit within a minute");
        return new Outcome(process.ExitCode, stdout, stderr);
    }

    private static string ProberDirectory() =>
        typeof(ProberTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ProberOutput")?.Value
        ?? throw new InvalidOperationException(
            "ProberOutput assembly metadata missing - the test csproj must emit it.");

    [NativeFact]
    public void The_prober_itself_works()
    {
        // Everything below reads "the child died" as a finding. This is what makes that reading
        // legitimate: the same program, the same payload, an ordinary shader, exit 0.
        var outcome = Run("healthy");

        Assert.True(outcome.ExitCode == 0,
            "the prober cannot complete an ordinary reflection, so nothing it does can be "
            + $"evidence about a crashing one:\n{outcome}");
        Assert.Contains("range(s)", outcome.Stdout, StringComparison.Ordinal);
    }

    [NativeFact]
    public void An_unknown_probe_is_refused_rather_than_silently_passing()
    {
        // A misspelled case name must not look like a successful run, and must not look like a
        // crash either. Exit 2 is neither.
        var outcome = Run("no-such-case");

        Assert.Equal(2, outcome.ExitCode);
        Assert.Contains("unknown case", outcome.Stderr, StringComparison.Ordinal);
    }

    [NativeFact]
    public void A_specialization_sized_resource_array_never_reflects_successfully()
    {
        // §9.12: `Texture2D t[kCascades]` where kCascades is a specialization constant. What is
        // true on both platforms is that reflection never returns an answer, and that nothing
        // distinguishes the shader beforehand, so a cooker handed one loses either the shader or
        // itself. Which of those it loses is platform-dependent, and the next test measures it.
        var outcome = Run("specialization-sized-resource-array");

        Assert.True(outcome.ExitCode != 0,
            "reflecting a specialization-sized resource array now succeeds. That is an upstream "
            + "fix and good news: §9.12 says it cannot be guarded because it cannot be detected, "
            + $"and that paragraph is what needs rewriting, not this assertion.\n{outcome}");
    }

    [NativeFact]
    public void How_it_fails_depends_on_the_platform()
    {
        // Measured, and not what §9.12 said. Slang throws a C++ `Slang::InternalError` out through
        // a `extern "C"` boundary, and the two runtimes disagree about what that is:
        //
        //   linux-x64   no handler, std::terminate, SIGABRT — exit 134, "terminate called after
        //               throwing an instance of 'Slang::InternalError'". The process is gone.
        //   win-x64     SEH carries it to the CLR, which surfaces a catchable SEHException. The
        //               cooker loses the shader and keeps running.
        //
        // So "terminates the process" was true of the platform it was measured on. Recording both
        // matters for the engine: a cook farm on Linux dies mid-batch where a developer's Windows
        // machine reports one failed shader — which is the same shape as §9.1, and the reason that
        // finding took a second platform to see.
        var outcome = Run("specialization-sized-resource-array");

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(1, outcome.ExitCode);
            Assert.Contains("SEHException", outcome.Stderr, StringComparison.Ordinal);
        }
        else
        {
            Assert.True(outcome.ExitCode is not (1 or 2),
                "on this platform the C++ exception was caught rather than ending the process, "
                + $"which is the Windows behaviour arriving somewhere new:\n{outcome}");
        }
    }
}
