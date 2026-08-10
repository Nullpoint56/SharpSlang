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

public class ProberTests(Xunit.Abstractions.ITestOutputHelper output)
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
    public void A_mismatched_generic_argument_kind_crashes_on_two_platforms_and_diagnoses_on_the_third()
    {
        // §9.20, corrected by the first macOS run. This was called
        // ..._ends_the_process_on_every_platform, and it does not.
        //
        //   win-x64      0xC0000005, which the CLR does not convert into anything catchable
        //   linux-*      access violation, both architectures
        //   osx-arm64    error[E30060]: expected a type — a real diagnostic, exit 0
        //
        // Same Slang version, same source, three platforms, and one of them reports the mistake
        // properly. That is the interesting half: the diagnostic path exists and is reached
        // somewhere, so the other two are not crashing because the case is undiagnosable.
        //
        // It also makes SpecializeGeneric's guard load-bearing everywhere for two different
        // reasons. On Windows and Linux it prevents a crash. On macOS it prevents something worse
        // than a crash: the call returns a **non-null** generic *alongside* the error, so a caller
        // checking only for null proceeds with a specialization Slang has already rejected.
        AssertMismatchRefused(Run("generic-argument-kind-mismatch"), "expected a type");
    }

    [NativeFact]
    public void The_argument_count_has_to_match_too_not_only_the_kinds()
    {
        // The other half of SpecializeGeneric's guard: two type arguments for one type parameter —
        // the right kind, real types, nothing dangling. Measured separately because guarding both
        // on the strength of measuring one is how a guard ends up wider than its evidence, and it
        // splits by platform exactly as the wrong kind does.
        AssertMismatchRefused(Run("generic-argument-count-mismatch"), "too many arguments");
    }

    /// <summary>
    /// A mismatched argument list must not quietly produce a usable specialization, however the
    /// platform expresses that.
    /// </summary>
    /// <param name="expectedDiagnostic">
    /// Substring of the message the platform that diagnoses is expected to produce. Asserted rather
    /// than accepting any diagnostic, so "Slang complained about something" cannot pass for
    /// "Slang complained about this".
    /// </param>
    private void AssertMismatchRefused(Outcome outcome, string expectedDiagnostic)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Assert.True(outcome.ExitCode is not (0 or 1 or 2),
                "specializing a generic with a mismatched argument list no longer ends the process "
                + "here. That is an upstream fix, and §9.20 plus the guard in SpecializeGeneric are "
                + $"what need revisiting — not this assertion.\n{outcome}");
            return;
        }

        // macOS. The prober prints the diagnostic blob, which is the whole point of it being
        // measured rather than assumed.
        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains(expectedDiagnostic, outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("error", outcome.Stdout, StringComparison.Ordinal);
    }

    [NativeFact]
    public void Specializing_a_non_generic_type_is_graceful_on_Windows_and_a_segfault_on_Linux()
    {
        // The fifth measured crash, and it cost a green run to find: the call sat in an ordinary
        // in-process test, passed on Windows, and killed the runner on Linux. Same shape as §9.1 —
        // a defect that only exists on the platform not being developed on — and the same lesson,
        // which is that the second platform is the control and not a formality.
        //
        //   win-x64     returns null. The caller learns nothing and keeps running.
        //   linux-x64   SIGSEGV, exit 139.
        var outcome = Run("specialize-a-non-generic-type");

        Assert.Contains("SLANG_TYPE_KIND_STRUCT", outcome.Stdout, StringComparison.Ordinal);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(0, outcome.ExitCode);
            Assert.Contains("refused", outcome.Stdout, StringComparison.Ordinal);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.True(outcome.ExitCode is not (0 or 1 or 2),
                "specializeType on a non-generic type no longer ends the process on Linux. If it "
                + $"now returns null there too, the split in §9.22 is closed.\n{outcome}");
        }
        else
        {
            // macOS is a third thing and nobody has measured it. Asserting Linux's answer here
            // would be a green that means nothing — the shape of claim this document keeps
            // catching. The first run records what actually happens; then this branch becomes an
            // assertion like the other two.
            output.WriteLine($"UNMEASURED PLATFORM — {Environment.OSVersion.Platform}:\n{outcome}");
            Assert.NotEqual("specialized", outcome.Stdout.Trim());
        }
    }

    [NativeFact]
    public void The_resolving_element_count_is_the_one_call_the_library_will_not_make()
    {
        // Every other function in Slang's reflection API is now reached from somewhere — the
        // library, or a test using it as a second opinion. This one is not, and the exclusion is
        // deliberate: spReflectionType_GetSpecializedElementCount is documented as the version
        // that copes with "size depends on unresolved generic parameters or link-time constants",
        // and that is exactly the input it dies on. The walk reads the size marker first and never
        // asks.
        //
        // Called here so the exclusion rests on a measurement rather than on the memory of one.
        var outcome = Run("specialized-element-count");

        Assert.True(outcome.ExitCode != 0,
            "GetSpecializedElementCount now survives a specialization-sized array. That is the "
            + "upstream fix §9.12 has been waiting for: the size marker guard in ReflectionWalker "
            + $"could be relaxed, and ArrayLayout.IsSpecializationSized reconsidered.\n{outcome}");

        Assert.Contains("SLANG_TYPE_KIND_ARRAY", outcome.Stdout, StringComparison.Ordinal);
    }

    [NativeFact]
    public void Two_questions_moved_out_of_the_runner_on_suspicion_answer_here()
    {
        // Honest framing: neither of these is *proven* to be what killed the first macOS run. Both
        // are benign on Windows and Linux — argument 0 of an empty list returns null, and applying
        // an unspecialized generic to itself returns non-null — and both were being asked
        // in-process on the strength of exactly those two data points.
        //
        // Asking a native API for an element it has just said does not exist is a caller mistake
        // whoever survives it, and a question with no obvious right answer does not belong in a
        // process that four hundred other tests are sharing. They are measured here instead, which
        // costs nothing and cannot end a run.
        foreach (var probe in (string[])["specialized-type-arg-out-of-range", "apply-generic-to-itself"])
        {
            var outcome = Run(probe);
            output.WriteLine($"{probe}: {outcome}");

            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                Assert.Equal(0, outcome.ExitCode);
                Assert.Contains("completed", outcome.Stdout, StringComparison.Ordinal);
            }
            else
            {
                // Unmeasured on macOS, and this is where the answer arrives. If either exits
                // abnormally, that is the crasher and §9.24 can stop saying "on suspicion".
                Assert.True(outcome.ExitCode is 0 or 1,
                    $"'{probe}' ended the prober on this platform — which makes it the candidate "
                    + $"§9.24 could not name.\n{outcome}");
            }
        }
    }

    [NativeFact]
    public void The_image_format_query_no_longer_crashes_on_any_binding_range_shape()
    {
        // The opposite direction, and the reason this test exists at all: ReflectionWalker carried
        // a comment saying getBindingRangeImageFormat access-violates on a PARAMETER_BLOCK binding
        // range, "measured against slang-compiler 2026.14.1". Re-measured against 2026.14.1 it
        // returns unknown — for a parameter block, a nested one, a constant buffer, a sampler, two
        // raw buffers and an acceleration structure.
        //
        // The guard stays; the claim is now checked. If a future Slang brings the crash back this
        // fails, which is the whole point of pinning a claim that used to be true.
        var outcome = Run("image-format-on-parameter-block");

        Assert.True(outcome.ExitCode == 0,
            "asking for the image format of a non-texture binding range ended the prober. The "
            + "guard in ReflectionWalker is load-bearing again and its remarks need the old "
            + $"wording back.\n{outcome}");

        Assert.Contains("SLANG_BINDING_TYPE_PARAMETER_BLOCK", outcome.Stdout, StringComparison.Ordinal);
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
        else if (OperatingSystem.IsLinux())
        {
            Assert.True(outcome.ExitCode is not (1 or 2),
                "on Linux the C++ exception was caught rather than ending the process, which is "
                + $"the Windows behaviour arriving somewhere new:\n{outcome}");
        }
        else
        {
            // Unmeasured, for the reason given in the test above. Slang throws a C++ exception
            // through an extern "C" boundary; whether Apple's runtime turns that into a managed
            // exception the way SEH does, or terminates the way the Itanium ABI does, is a
            // question and not a guess to encode.
            output.WriteLine($"UNMEASURED PLATFORM — {Environment.OSVersion.Platform}:\n{outcome}");
            Assert.NotEqual(0, outcome.ExitCode);
        }
    }
}
