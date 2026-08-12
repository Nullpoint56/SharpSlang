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

    private static Outcome Run(string probe) => Run(probe, null);

    /// <param name="environment">
    /// Variables for the child; a null value <em>removes</em> the variable rather than setting it.
    /// Used by the allocator probe, which has to control the poisoning itself rather than read
    /// whatever CI happened to configure — a test that only passes when a workflow sets a variable
    /// is testing the workflow.
    /// </param>
    /// <remarks>
    /// Removal is the half that is easy to miss and this test got wrong first: a child inherits the
    /// parent's environment, so under CI — or under tools/ci/verify-linux.sh, which exports
    /// MALLOC_PERTURB_ — the "unpoisoned" control run was poisoned too, and the control assertion
    /// failed on every poisoned run while passing on a bare developer shell.
    /// </remarks>
    private static Outcome Run(string probe, IReadOnlyDictionary<string, string?>? environment)
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

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                if (value is null) info.Environment.Remove(key);
                else info.Environment[key] = value;
            }
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("could not start the prober");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            // Killed rather than left. Disposing the handle does not end the child, and a prober
            // that outlives its test holds a lock on its own executable — which resurfaces as an
            // MSB3027 on somebody's next build, pointing at nothing that is wrong.
            process.Kill(entireProcessTree: true);
            Assert.Fail($"the prober did not exit within a minute running '{probe}'");
        }

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
    public void Specializing_a_non_generic_type_segfaults_only_on_Linux()
    {
        // The fifth measured crash, and it cost a green run to find: the call sat in an ordinary
        // in-process test, passed on Windows, and killed the runner on Linux. Same shape as §9.1 —
        // a defect that only exists on the platform not being developed on — and the same lesson,
        // which is that the second platform is the control and not a formality.
        //
        //   win-x64     returns null. The caller learns nothing and keeps running.
        //   linux-x64   SIGSEGV, exit 139.
        //   osx-arm64   returns null, like Windows.
        //
        // Renamed once macOS was measured: it was ..._is_graceful_on_Windows_and_a_segfault_on_Linux,
        // which stopped being the whole truth the moment a third platform answered.
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
            // osx-arm64, measured from the run that reported it rather than guessed:
            //
            //     exit 0
            //     SnBlock kind=SLANG_TYPE_KIND_STRUCT
            //     refused
            //
            // Windows's answer, not Linux's — which is worth noticing, because the sibling test
            // below measured the opposite grouping on the same platform. "macOS behaves like Linux"
            // would have been the natural guess for both and would have been wrong for one (§9.36).
            Assert.Equal(0, outcome.ExitCode);
            Assert.Contains("refused", outcome.Stdout, StringComparison.Ordinal);
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
    public void Releasing_the_hierarchy_outermost_first_survives()
    {
        // §9.26, and the reason it is here rather than only in LifetimeTests: the in-process
        // version of this teardown has now killed the macOS test host three times, and each run
        // cost fifty-odd tests that never got to report. A dead host names one test and destroys
        // every other signal in the run.
        //
        // Out here the crash is the result. The prober prints the name of each release before it
        // performs it and flushes, so the tail of stdout is the release that faulted — which is
        // the thing three CI runs failed to establish.
        //
        // The control first, for the same reason `healthy` exists: the identical hierarchy torn
        // down innermost-first, which is what every other test in the suite does. If both fail,
        // the finding is not about ordering.
        var control = Run("refcount-ownership");
        Assert.True(control.ExitCode == 0,
            "the ordinary innermost-first teardown ended the prober, so nothing the out-of-order "
            + $"case does is evidence about ordering:\n{control}");

        var outcome = Run("dispose-outermost-first");
        output.WriteLine($"refcount-ownership:\n{control}");
        output.WriteLine($"dispose-outermost-first:\n{outcome}");

        Assert.True(outcome.ExitCode == 0,
            "releasing the hierarchy outermost-first ended the prober. The last 'releasing X ...' "
            + "line with no matching 'ok' is the release that did it, and SlangObject's owner "
            + $"reference is what is supposed to make that release safe.\n{outcome}");
    }

    [NativeFact]
    public void The_allocator_poisoning_is_armed_and_reaches_small_allocations()
    {
        // §9.31. The control for the tripwires the whole cross-platform strategy rests on. Both are
        // environment variables, and a runner can set one, print it in the log, and have it do
        // nothing — which is precisely what MALLOC_CHECK_=3 turned out to be. An unarmed tripwire
        // and one that found nothing are the same green.
        //
        // The probe allocates, writes a 0xC3 sentinel, frees, and reads the same byte back. The
        // sentinel surviving means nothing poisoned anything.
        //
        // Two sizes, because both allocators have a fast path that skips poisoning: glibc's tcache
        // returns from _int_free before free_perturb, and Apple's nano zone does not implement the
        // scribble options at all. Measured on linux-x64, MALLOC_PERTURB_ alone poisons a 4 KB
        // block and leaves a 64 B one untouched — and 64 bytes is where an AST node lives.
        var poisoned = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MALLOC_PERTURB_"] = "165",
            ["GLIBC_TUNABLES"] = "glibc.malloc.tcache_count=0",
            ["MallocScribble"] = "1",
            ["MallocPreScribble"] = "1",
            ["MallocNanoZone"] = "0",
        };

        // Both runs set every variable explicitly, the control by removing them. Inheriting the
        // ambient environment is what made the first version of this test fail under CI and pass on
        // a developer shell — the control run was poisoned because the parent was.
        var unpoisoned = poisoned.ToDictionary(e => e.Key, _ => (string?)null, StringComparer.Ordinal);

        var clean = Run("allocator-poisoning", unpoisoned);
        var armed = Run("allocator-poisoning", poisoned);

        output.WriteLine($"no poisoning:\n{clean}");
        output.WriteLine($"poisoned:\n{armed}");

        Assert.Equal(0, clean.ExitCode);
        Assert.Equal(0, armed.ExitCode);

        // The control. Without the variables nothing may claim to be live, or the probe is reading
        // something other than what it thinks.
        Assert.DoesNotContain("is live", clean.Stdout, StringComparison.Ordinal);

        if (OperatingSystem.IsLinux())
        {
            // Both sizes, which is the whole point of the tunable. One "is live" would be the
            // half-armed state this test exists to reject.
            Assert.Equal(2, armed.Stdout.Split("MALLOC_PERTURB_ is live").Length - 1);
        }
        else if (OperatingSystem.IsWindows())
        {
            // No poisoning allocator here. Windows is covered by the other three legs, and saying
            // so is better than a branch that quietly asserts nothing.
            Assert.DoesNotContain("is live", armed.Stdout, StringComparison.Ordinal);
        }
        else
        {
            // macOS, and no longer unmeasured. The first osx-arm64 run reported:
            //
            //     small (64 B)   fresh=0xAA  after free=0x00
            //     large (4 KB)   fresh=0xAA  after free=0x55   <- MallocScribble is live
            //
            // fresh=0xAA on a 64 B block is MallocPreScribble, which Apple's nano zone does not
            // implement — so MallocNanoZone=0 did its job and that allocation is going through the
            // scalable zone. The 0x00 was the probe reading a byte of the tiny allocator's *tail*
            // free-list metadata, which is fixed now (§9.34).
            //
            // Asserted: the free poisoning is live. Not yet asserted for both sizes, because the
            // reading that would show it came from a probe that has since changed and this branch
            // is not going to claim a measurement it has not seen. The next macOS run turns this
            // into the Linux assertion above.
            Assert.Contains("MallocScribble is live", armed.Stdout, StringComparison.Ordinal);
            output.WriteLine("macOS: both sizes are expected to scribble now — see §9.34.");
        }
    }

    [NativeFact]
    public void Threads_with_their_own_global_session_agree_with_a_serial_compile()
    {
        // §9.28. slang.h: "Distinct global sessions may be used from different threads in
        // parallel." Eight threads, eight global sessions, one barrier so they are all inside the
        // compiler at once, and every result compared byte-for-byte against a serial baseline.
        //
        // Agreement matters more than survival here. A race that produces *different SPIR-V* is
        // worse than one that crashes, because the crash stops the cook and the difference ships.
        var outcome = Run("threads-own-global-session");
        output.WriteLine(outcome.ToString());

        AssertThreadsAgreed(outcome,
            "compiling on distinct global sessions in parallel is what slang.h documents as "
            + "allowed, and it is the pattern docs/guide.md recommends for a cooker");
    }

    [NativeFact]
    public void The_back_end_may_be_parallelised_once_the_front_end_has_finished()
    {
        // slang.h, on getTargetCode and friends: "after a component type has been fully specialized
        // and linked, this method is supported for concurrent backend code generation, including
        // from multiple threads compiling different linked IComponentType instances", while "front
        // end operations such as loading modules, specialization, and linking still require
        // external synchronization".
        //
        // Marked experimental upstream, which is the reason to measure it rather than repeat it.
        var outcome = Run("threads-parallel-backend");
        output.WriteLine(outcome.ToString());

        AssertThreadsAgreed(outcome,
            "linking serially and then generating code in parallel is the pattern that lets a "
            + "cooker use its cores without paying for a global session per thread. slang.h calls "
            + "it supported but experimental, so if it has stopped holding, guide.md's threading "
            + "section is what needs changing");
    }

    [NativeFact]
    public void Sharing_one_global_session_across_threads_is_never_silently_wrong()
    {
        // The case slang.h says requires external synchronization: "A single global session object
        // is currently *not* thread-safe."
        //
        // Three outcomes have now been measured on win-x64 alone: an access violation inside
        // loadModuleFromSourceString, a clean SlangException out of one thread while the other
        // seven agreed, and once all eight getting away with it. Every one of those is either loud
        // or harmless, and which one a run draws is the machine's business.
        //
        // So neither the exit code nor the throw count is asserted. The property is the one in the
        // name and nothing else: no thread may return *success* with bytes that disagree with a
        // serial compile. That is the outcome nobody would catch.
        //
        // This test used to require "0 threw" on the surviving path, and failed the first time
        // win-x64 threw instead of crashing — the same measurement, differently shaped. Both of its
        // branches matched strings transcribed from an observed run rather than the invariant, and
        // the crash branch had the mirror defect: a probe that printed "0 disagreed" and then died
        // in teardown would have failed a test whose property held (§9.37).
        var outcome = Run("threads-shared-global-session");
        output.WriteLine(outcome.ToString());

        // The baseline is printed before any thread starts, so requiring it stops a probe that died
        // on startup — a missing native library, a bad argument — from passing this test by never
        // having measured anything. Without it the assertion below is satisfied by empty output.
        Assert.Contains("baseline ", outcome.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("SILENTLY WRONG", outcome.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void The_disagreement_report_has_been_seen_to_fire()
    {
        // Three threading probes are read through one token, and the unsafe one asserts only that
        // the token is absent. That assertion has never failed and — if everything keeps working —
        // never will, which makes it indistinguishable from an assertion that *cannot* fail. §9.31
        // is a whole finding about the difference, so this fires it deliberately: fabricated
        // results, the real reporting path, and the exact text the other tests match on.
        //
        // No [NativeFact]. Nothing here touches the compiler, and a self-test of the instrument
        // should not be skipped on a machine that has no native payload — that is precisely the
        // machine where the instrument is all there is.
        var outcome = Run("report-a-disagreement");
        output.WriteLine(outcome.ToString());

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("2 disagreed with the serial baseline  <- SILENTLY WRONG",
            outcome.Stdout, StringComparison.Ordinal);

        // And that the detail is usable, because a report that only says "they differ" sends
        // whoever reads it back to a diff they cannot run: the process that produced the bytes is
        // gone by then.
        Assert.Contains("thread 2 disagreed: 8 bytes against the baseline's 8, first difference at "
            + "byte 4", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("thread 3 disagreed: 3 bytes against the baseline's 8, identical for the "
            + "first 3 bytes", outcome.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A threading probe that slang.h documents as safe must survive and produce identical bytes.
    /// </summary>
    /// <remarks>
    /// The literal match is right here and wrong one test up. These two patterns are documented as
    /// allowed, so all three of surviving, not throwing and not disagreeing are required and a
    /// weaker assertion would be throwing away the finding. The unsafe pattern is documented as
    /// undefined, where only the last of the three is a property of anything (§9.37).
    /// </remarks>
    private static void AssertThreadsAgreed(Outcome outcome, string why)
    {
        Assert.True(outcome.ExitCode == 0, $"the prober did not survive. {why}.\n{outcome}");
        Assert.Contains("0 threw, 0 disagreed", outcome.Stdout, StringComparison.Ordinal);
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
            // osx-arm64, measured:
            //
            //     exit 134
            //     stderr: libc++abi: terminating due to uncaught exception of type
            //             Slang::InternalError
            //
            // The Itanium ABI answer, and Linux's — the opposite grouping to the sibling test
            // above, which measured macOS siding with Windows on the same runner. So there is no
            // "macOS is like Linux" rule to lean on; each call has to be asked (§9.36).
            //
            // stderr is asserted as well as the exit code, because "the process died" and "the
            // process died of this" are different facts and only the second one dates.
            Assert.True(outcome.ExitCode is not (0 or 1 or 2),
                "on macOS the C++ exception was caught rather than ending the process, which is "
                + $"the Windows behaviour arriving somewhere new:\n{outcome}");
            Assert.Contains("Slang::InternalError", outcome.Stderr, StringComparison.Ordinal);
        }
    }

}
