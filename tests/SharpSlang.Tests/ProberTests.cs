// The reflection call that does not fail, asserted from outside the process it ends.
//
// An earlier round (AGENTS.md#sizes-slang-does-not-know, U2) measured that reflecting a resource array sized by a specialization constant terminates the
// process inside spReflectionTypeLayout_getBindingRangeCount — no exception, no managed stack, no
// diagnostic. A test cannot assert that in-process, so for several rounds it was a sentence in the
// documentation that nothing checked, which is the same category as a comment asserting something
// the code does not do. It just happened to be true.
//
// SharpSlang.Prober is a program that performs one such operation and exits. Reading its exit code
// turns the sentence into a test, and — the part worth having — turns the day Slang fixes this into
// a failing test rather than into silence.
//
// The control case matters more than the crashing one. A prober that cannot find its native payload
// also dies, and would make every assertion here pass for the wrong reason, which is why `healthy`
// is asserted first and separately.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SharpSlang.Tests;

public class ProberTests(ITestOutputHelper output)
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
    /// <param name="probe">The scenario the prober runs, by name.</param>
    private static Outcome Run(string probe, IReadOnlyDictionary<string, string?>? environment)
    {
        // The apphost, not `dotnet exec`. The obvious spelling was
        // `Environment.ProcessPath exec SharpSlang.Prober.dll`, on the reasoning that the child
        // should use the same runtime as the runner — but under `dotnet test` the current process
        // was testhost.exe on VSTest, and is the SharpSlang.Tests executable itself on the test
        // platform; neither takes `exec`, and every probe came back 0x80008083. Both control cases
        // caught it, which is what they are for.
        var host = Path.Combine(
            ProberDirectory(),
            OperatingSystem.IsWindows() ? "SharpSlang.Prober.exe" : "SharpSlang.Prober");

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

        return Execute(info, probe);
    }

    /// <summary>
    /// The prober in <paramref name="proberDirectory"/> as <c>dotnet SharpSlang.Prober.dll</c>,
    /// working in <paramref name="workingDirectory"/>.
    /// </summary>
    /// <remarks>
    /// The launch every other probe avoids, and the one a consumer gets from `dotnet app.dll`: the
    /// executable is the dotnet host, so the process's own directory is the host's, and the
    /// working directory is whatever the caller says. Windows searches both for a library loaded by
    /// bare name and neither is where the prober and its payload are.
    /// </remarks>
    private static Outcome RunUnderMuxer(string probe, string proberDirectory, string workingDirectory, bool withoutPreload = false)
    {
        var dll = Path.Combine(proberDirectory, "SharpSlang.Prober.dll");
        Assert.True(File.Exists(dll), $"the prober is not built at {dll}.");

        var info = new ProcessStartInfo(Muxer())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        info.ArgumentList.Add(dll);
        info.ArgumentList.Add(probe);
        if (withoutPreload) info.Environment["SHARPSLANG_TEST_NO_COMPANION_PRELOAD"] = "1";

        return Execute(info, probe);
    }

    /// <summary>The dotnet host that owns the runtime this test is running on.</summary>
    /// <remarks>
    /// Not <c>Environment.ProcessPath</c>, which under `dotnet test` is the test executable (it was
    /// testhost.exe on VSTest), the mistake Run's comment records. The shared runtime sits at
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/</c> and the host at the root,
    /// so the child runs on the same runtime as the runner without a PATH lookup that could find
    /// another one.
    /// </remarks>
    private static string Muxer()
    {
        var runtime = RuntimeEnvironment.GetRuntimeDirectory();
        var muxer = Path.Combine(
            Path.GetFullPath(Path.Combine(runtime, "..", "..", "..")),
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

        Assert.True(File.Exists(muxer),
            $"no dotnet host at {muxer}, three directories above the runtime at {runtime}");
        return muxer;
    }

    private static Outcome Execute(ProcessStartInfo info, string probe)
    {
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

    [ReleasePayloadFact("U2")]
    public void A_specialization_sized_resource_array_never_reflects_successfully()
    {
        // U2: `Texture2D t[kCascades]` where kCascades is a specialization constant. What is
        // true on both platforms is that reflection never returns an answer, and that nothing
        // distinguishes the shader beforehand, so a cooker handed one loses either the shader or
        // itself. Which of those it loses is platform-dependent, and the next test measures it.
        var outcome = Run("specialization-sized-resource-array");

        Assert.True(outcome.ExitCode != 0,
            "reflecting a specialization-sized resource array now succeeds. That is an upstream "
            + "fix and good news: AGENTS.md#sizes-slang-does-not-know says it cannot be guarded because it cannot be detected, "
            + $"and that paragraph is what needs rewriting, not this assertion.\n{outcome}");
    }

    [ReleasePayloadFact("U3")]
    public void A_mismatched_generic_argument_kind_crashes_on_two_platforms_and_diagnoses_on_the_third()
    {
        // U3 (AGENTS.md#reflection-snapshot), corrected by the first macOS run. This was called
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

    [ReleasePayloadFact("U3")]
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
    /// <param name="outcome">What the prober reported for the mismatched specialization.</param>
    private static void AssertMismatchRefused(Outcome outcome, string expectedDiagnostic)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Assert.True(outcome.ExitCode is not (0 or 1 or 2),
                "specializing a generic with a mismatched argument list no longer ends the process "
                + "here. That is an upstream fix, and U3 in AGENTS.md plus the guard in SpecializeGeneric are "
                + $"what need revisiting — not this assertion.\n{outcome}");
            return;
        }

        // macOS. The prober prints the diagnostic blob, which is the whole point of it being
        // measured rather than assumed.
        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains(expectedDiagnostic, outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("error", outcome.Stdout, StringComparison.Ordinal);
    }

    [ReleasePayloadFact("U4")]
    public void Specializing_a_non_generic_type_segfaults_only_on_Linux()
    {
        // The fifth measured crash, and it cost a green run to find: the call sat in an ordinary
        // in-process test, passed on Windows, and killed the runner on Linux. Same shape as the borrowed pointer (AGENTS.md#ownership-and-lifetime):
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
                + $"now returns null there too, the split recorded as U4 in AGENTS.md is closed.\n{outcome}");
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
            // would have been the natural guess for both and would have been wrong for one.
            Assert.Equal(0, outcome.ExitCode);
            Assert.Contains("refused", outcome.Stdout, StringComparison.Ordinal);
        }
    }

    [ReleasePayloadFact("U1")]
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
            + "upstream fix U1 in AGENTS.md has been waiting for: the size marker guard in ReflectionWalker "
            + $"could be relaxed, and ArrayLayout.IsSpecializationSized reconsidered.\n{outcome}");

        Assert.Contains("SLANG_TYPE_KIND_ARRAY", outcome.Stdout, StringComparison.Ordinal);
    }

    [PatchedFact("U4")]
    public void Specializing_a_non_generic_type_is_refused_with_a_diagnostic()
    {
        // U4 (AGENTS.md#reflection-snapshot), patch 0001. The answer slang.h's error path gives,
        // on every platform: null, and a diagnostic saying the type takes no arguments. The
        // release's three answers are the wrong ones asserted above: null with nothing said
        // (Windows, macOS), SIGSEGV (Linux), and, built by GCC 13.3 without the patch, a type.
        var outcome = Run("specialize-a-non-generic-type");
        output.WriteLine(outcome.ToString());

        Assert.True(outcome.ExitCode == 0, $"specializeType on a non-generic type ended the process:\n{outcome}");
        Assert.Contains("SnBlock kind=SLANG_TYPE_KIND_STRUCT", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("\nrefused", outcome.Stdout.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("\nspecialized", outcome.Stdout.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("E38025", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("wrong number of specialization arguments", outcome.Stdout, StringComparison.Ordinal);
    }

    [PatchedFact("U1")]
    public void The_element_count_of_a_specialization_sized_array_is_unknown()
    {
        // U1 (AGENTS.md#sizes-slang-does-not-know), patch 0002. What slang.h:2633-2642 documents
        // for a size that depends on a link-time constant: SLANG_UNKNOWN_SIZE, from both the
        // resolving call and the deprecated one, where the release ends the process.
        var outcome = Run("specialized-element-count");
        output.WriteLine(outcome.ToString());

        const ulong unknown = ulong.MaxValue - 1;   // SLANG_UNKNOWN_SIZE
        Assert.True(outcome.ExitCode == 0, $"asking the element count of a specialization-sized array ended the process:\n{outcome}");
        Assert.Contains("array kind=SLANG_TYPE_KIND_ARRAY", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains($"GetSpecializedElementCount -> {unknown}", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains($"GetElementCount -> {unknown}", outcome.Stdout, StringComparison.Ordinal);
    }

    [PatchedFact("U2")]
    public void A_specialization_sized_resource_array_has_binding_ranges_of_unknown_count()
    {
        // U2 (AGENTS.md#sizes-slang-does-not-know), patch 0003. The binding ranges are there and
        // the array's count is SLANG_UNKNOWN_SIZE, the answer the type layout already gives for its
        // size, where the release throws Slang::InternalError out of getBindingRangeCount.
        var outcome = Run("specialization-sized-resource-array-ranges");
        output.WriteLine(outcome.ToString());

        // SLANG_UNKNOWN_SIZE, which getBindingRangeBindingCount returns as a SlangInt: -2.
        const long unknown = -2;
        Assert.True(outcome.ExitCode == 0, $"reflecting the binding ranges of a specialization-sized resource array failed:\n{outcome}");
        Assert.Contains("2 binding range(s)", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains($"scArray type=SLANG_BINDING_TYPE_TEXTURE bindings={unknown}", outcome.Stdout, StringComparison.Ordinal);
        Assert.Contains("scSampler type=SLANG_BINDING_TYPE_SAMPLER bindings=1", outcome.Stdout, StringComparison.Ordinal);
    }

    [PatchedFact("U3")]
    public void A_mismatched_generic_argument_list_is_refused_with_a_diagnostic()
    {
        // U3 (AGENTS.md#reflection-snapshot), patch 0004. The diagnostic macOS's release already
        // reaches, without the non-null generic it returned beside it, on every platform: an int
        // where a type parameter belongs, and two type arguments for one parameter.
        foreach (var (probe, diagnostic) in new[]
                 {
                     ("generic-argument-kind-mismatch", "expected a type"),
                     ("generic-argument-count-mismatch", "too many arguments"),
                 })
        {
            var outcome = Run(probe);
            output.WriteLine($"{probe}: {outcome}");

            Assert.True(outcome.ExitCode == 0, $"'{probe}' ended the process:\n{outcome}");
            Assert.Contains("\nrefused", "\n" + outcome.Stdout.ReplaceLineEndings("\n"), StringComparison.Ordinal);
            Assert.Contains("error", outcome.Stdout, StringComparison.Ordinal);
            Assert.Contains(diagnostic, outcome.Stdout, StringComparison.Ordinal);
        }
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
                // abnormally, that is the crasher, and AGENTS.md#tests-and-skips can stop saying "on suspicion".
                Assert.True(outcome.ExitCode is 0 or 1,
                    $"'{probe}' ended the prober on this platform — which makes it the candidate "
                    + $" could not name.\n{outcome}");
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
        // raw buffers and an acceleration structure. 2026.21 answers the same.
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
    public void Releasing_the_hierarchy_outermost_first_is_refused_and_the_teardown_survives()
    {
        // AGENTS.md#ownership-and-lifetime (U13), and the reason it is here rather than only in LifetimeTests: the in-process
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
            + "line with no matching 'ok' or 'refused' is the release that did it.\n{outcome}");

        // The outermost-first order is refused, three times, and the hierarchy stays whole for the
        // innermost-first teardown after it (AGENTS.md#ownership-and-lifetime).
        foreach (var refused in new[] { "global", "session", "module" })
        {
            Assert.Contains($"releasing {refused} refused:", outcome.Stdout, StringComparison.Ordinal);
        }

        foreach (var released in new[] { "program", "entry", "module", "session", "global" })
        {
            Assert.Contains($"releasing {released} ok", outcome.Stdout, StringComparison.Ordinal);
        }
    }

    [NativeFact]
    public void The_allocator_poisoning_is_armed_and_reaches_small_allocations()
    {
        // AGENTS.md#allocator-poisoning-and-crash-diagnosis.
        // The control for the tripwires the whole cross-platform strategy rests on. Both are
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
        // something other than what it thinks. Note the phrasing: "nothing is live" is not the same
        // as "nothing happened", and on macOS those come apart — see below.
        Assert.DoesNotContain("is live", clean.Stdout, StringComparison.Ordinal);

        // Both bytes, both sizes, both runs. Matching the sentence the probe printed was enough
        // while every leg's answer was one of two, and stopped being enough the moment a leg
        // answered with a third thing.
        var (armedSmall, armedLarge) = (Poisoning(armed, Small), Poisoning(armed, Large));
        var (cleanSmall, cleanLarge) = (Poisoning(clean, Small), Poisoning(clean, Large));

        if (OperatingSystem.IsLinux())
        {
            // Both sizes, which is the whole point of the tunable: glibc's tcache returns from
            // _int_free before free_perturb, so without it the 64 B block keeps its contents. One
            // size poisoned would be the half-armed state this test exists to reject. Measured
            // identically on linux-x64 and linux-arm64.
            Assert.Equal(Perturb, armedSmall.Freed);
            Assert.Equal(Perturb, armedLarge.Freed);
            Assert.Equal(Sentinel, cleanSmall.Freed);
            Assert.Equal(Sentinel, cleanLarge.Freed);
        }
        else if (OperatingSystem.IsWindows())
        {
            // No poisoning allocator here, at either size, armed or not. Windows is covered by the
            // other three legs, and saying that is better than a branch that asserts nothing.
            Assert.Equal(Sentinel, armedSmall.Freed);
            Assert.Equal(Sentinel, armedLarge.Freed);
            Assert.Equal(Sentinel, cleanSmall.Freed);
            Assert.Equal(Sentinel, cleanLarge.Freed);
        }
        else
        {
            // macOS, measured on osx-arm64, and not what an earlier round predicted. It predicted 0x55 at both
            // sizes once the probe stopped reading the tiny allocator's tail metadata. What the
            // fixed probe reported:
            //
            //                    control                armed
            //     small (64 B)   fresh 0x00 free 0x00    fresh 0xAA free 0x00
            //     large (4 KB)   fresh 0x00 free 0xC3    fresh 0xAA free 0x55
            //
            // fresh=0xAA at both sizes is MallocPreScribble, which the nano zone does not implement
            // — so MallocNanoZone=0 worked and both allocations are in the scalable zone. The nano
            // zone was never the reason.
            Assert.Equal(PreScribble, armedSmall.Fresh);
            Assert.Equal(PreScribble, armedLarge.Fresh);
            Assert.Equal(Scribble, armedLarge.Freed);
            Assert.Equal(0x00, armedSmall.Freed);

            // Which leaves the question the control answers: who zeroed the small block. The 4 KB
            // block keeps the sentinel with the variables removed, so the 0x55 above is
            // MallocScribble doing its job. The 64 B block comes back zeroed *either way*, so that
            // one is the platform, and it would happen on a runner that set nothing at all.
            //
            // Both are poisoning, so freed memory does not survive at either size — but only one of
            // them is ours, and a tripwire nobody armed is one nobody can disarm by mistake either.
            Assert.Equal(Sentinel, cleanLarge.Freed);
            Assert.Equal(0x00, cleanSmall.Freed);
        }
    }

    private const string Small = "small (64 B)";
    private const string Large = "large (4 KB)";
    private const byte Sentinel = 0xC3;      // what the probe writes before freeing
    private const byte Perturb = 0xA5;       // MALLOC_PERTURB_=165
    private const byte Scribble = 0x55;      // MallocScribble
    private const byte PreScribble = 0xAA;   // MallocPreScribble

    /// <summary>The fresh and post-free bytes the poisoning probe reported for one size.</summary>
    private static (byte Fresh, byte Freed) Poisoning(Outcome outcome, string size)
    {
        var reading = Regex.Match(
            outcome.Stdout,
            Regex.Escape(size) + @"\s+fresh=0x(?<fresh>[0-9A-F]{2})\s+after free=0x(?<freed>[0-9A-F]{2})");

        Assert.True(reading.Success, $"the probe printed no reading for {size}:\n{outcome}");

        return (Convert.ToByte(reading.Groups["fresh"].Value, 16),
                Convert.ToByte(reading.Groups["freed"].Value, 16));
    }

    [NativeFact]
    public void Threads_with_their_own_global_session_agree_with_a_serial_compile()
    {
        // AGENTS.md#threads.
        // slang.h: "Distinct global sessions may be used from different threads in
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

    // No test runs the shared-global-session probe ("threads-shared-global-session"). slang.h says
    // "A single global session object is currently *not* thread-safe", and the probe has now
    // measured every outcome undefined behaviour allows: an access violation, a SlangException out
    // of one thread, all eight agreeing, and, on the GitHub-hosted windows-latest runner in run
    // 42437636618 (2026-11-08, Slang v2026.21), five of eight threads returning success with
    // 1,492 bytes of SPIR-V where the serial compile gave 1,436. The test that stood here asserted
    // that last outcome never happens. It was the tripwire for exactly that, and it fired. No
    // property of this pattern holds on every run, so any assertion about it is a transcript of one
    // run. The rule is in docs/guide.md ("Threads"): a global session per thread, or a lock. The
    // probe stays for diagnosis: `dotnet run --project tests/SharpSlang.Prober --
    // threads-shared-global-session`.

    [Fact]
    public void The_disagreement_report_has_been_seen_to_fire()
    {
        // The threading probes are read through one token, and the two supported patterns assert
        // only that it is absent. Such an assertion that never fails is indistinguishable from one
        // that *cannot* fail (the unsupported pattern's tripwire did fire, on 2026-11-08).
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
    /// undefined, where only the last of the three is a property of anything.
    /// </remarks>
    private static void AssertThreadsAgreed(Outcome outcome, string why)
    {
        Assert.True(outcome.ExitCode == 0, $"the prober did not survive. {why}.\n{outcome}");
        Assert.Contains("0 threw, 0 disagreed", outcome.Stdout, StringComparison.Ordinal);
    }

    [ReleasePayloadFact("U2")]
    public void How_it_fails_depends_on_the_platform()
    {
        // Measured, and not what an earlier round said. Slang throws a C++ `Slang::InternalError` out through
        // a `extern "C"` boundary, and the two runtimes disagree about what that is:
        //
        //   linux-x64   no handler, std::terminate, SIGABRT — exit 134, "terminate called after
        //               throwing an instance of 'Slang::InternalError'". The process is gone.
        //   win-x64     SEH carries it to the CLR, which surfaces a catchable SEHException. The
        //               cooker loses the shader and keeps running.
        //
        // So "terminates the process" was true of the platform it was measured on. Recording both
        // matters for a cooker: a cook farm on Linux dies mid-batch where a developer's Windows
        // machine reports one failed shader — which is the same shape as the borrowed pointer (AGENTS.md#ownership-and-lifetime), and the reason that
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
            // "macOS is like Linux" rule to lean on; each call has to be asked.
            //
            // stderr is asserted as well as the exit code, because "the process died" and "the
            // process died of this" are different facts and only the second one dates.
            Assert.True(outcome.ExitCode is not (0 or 1 or 2),
                "on macOS the C++ exception was caught rather than ending the process, which is "
                + $"the Windows behaviour arriving somewhere new:\n{outcome}");
            Assert.Contains("Slang::InternalError", outcome.Stderr, StringComparison.Ordinal);
        }
    }

    [PatchedFact("U9")]
    public void Without_the_optimiser_every_compile_warns_and_none_reports_an_error()
    {
        // U9, patched (0017): with slang-glslang absent, each SPIR-V compile in a global session
        // warns E00118 that its module was not optimized, and no compile reports E00100 on success.
        var copy = Directory.CreateTempSubdirectory("sharpslang-no-optimiser-");
        try
        {
            foreach (var file in Directory.EnumerateFiles(ProberDirectory(), "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(file);
                if (name.Contains("slang-glslang", StringComparison.Ordinal)) continue;
                var target = Path.Combine(copy.FullName, Path.GetRelativePath(ProberDirectory(), file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            var outcome = RunUnderMuxer("missing-optimiser", copy.FullName, copy.FullName, withoutPreload: true);
            output.WriteLine(outcome.ToString());
            Assert.Equal(0, outcome.ExitCode);
            Assert.Contains("compile 1:", outcome.Stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("E00100 True", outcome.Stdout, StringComparison.Ordinal);
            Assert.Equal(2, outcome.Stdout.Split("W00118 True").Length - 1);
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }
    [PatchedFact("U10")]
    public void Slang_finds_its_companions_beside_itself_without_the_bindings_preload()
    {
        // U10, patched (0015): with CompanionLibraries' preload turned off, Slang's own bare-name
        // load falls back to its own directory on Windows, so the launch from elsewhere still gets
        // spirv-opt. Elsewhere the payload's rpaths always did this, so the test has nothing to say
        // there and skips: a pass off Windows would read as the release carrying the fix.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "U10 is Windows' library search; Linux and macOS find the companions by rpath with or without the patch");
        var elsewhere = Directory.CreateTempSubdirectory("sharpslang-elsewhere-");
        try
        {
            var outcome = RunUnderMuxer("downstream-compilers", ProberDirectory(), elsewhere.FullName, withoutPreload: true);
            output.WriteLine(outcome.ToString());
            Assert.Equal(0, outcome.ExitCode);
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }
    [NativeFact]
    public void Launched_by_dotnet_from_another_directory_the_compiler_still_finds_its_companions()
    {
        // The defect CompanionLibraries fixes, asserted the way it was found. Slang loads
        // slang-glslang and dxcompiler itself, by bare name, and on Windows that search covers the
        // executable's directory and the working directory, not the compiler's. Every other probe
        // here runs the apphost from its own output directory, which is exactly the launch where
        // the search succeeds by accident; this one runs `dotnet SharpSlang.Prober.dll` from an
        // empty directory, so the executable is the dotnet host and neither place holds anything.
        //
        // Measured on win-x64 with CompanionLibraries.Preload made to return at once:
        //
        //     exit 1
        //     stderr: SlangException: SPIR-V optimiser unavailable failed: SLANG_E_NOT_FOUND
        //             Slang cannot load spirv-opt, which lives in the 'slang-glslang' shared library.
        //
        // With it: exit 0, 2,048 bytes of SPIR-V optimised to 1,664, and a signed DXIL container.
        //
        // Not Windows-only, though the preload is. The launch is the same everywhere and so is what
        // a consumer needs from it; elsewhere it is the payload's own search path that has to pass
        // it. Run by hand under WSL, linux-x64 passed with no preload at all, through
        // libslang-compiler.so's $ORIGIN runpath. On osx-arm64 the same is read from the dylib's
        // @loader_path rpaths and not yet measured; this test is what measures it, on that leg's
        // next run.
        var elsewhere = Directory.CreateTempSubdirectory("sharpslang-elsewhere-");
        try
        {
            var outcome = RunUnderMuxer("downstream-compilers", ProberDirectory(), elsewhere.FullName);
            output.WriteLine(outcome.ToString());

            AssertLaunchedElsewhere(outcome, ProberDirectory());
            Assert.True(outcome.ExitCode == 0,
                "started by the dotnet host from another directory, the prober could not create a "
                + "global session that finds spirv-opt, or could not compile with it. Every portable "
                + "consumer of the package and every `dotnet app.dll` launch is in this position; "
                + $"CompanionLibraries is what should have made the libraries findable.\n{outcome}");
            Assert.Contains("spirv-opt available: True", outcome.Stdout, StringComparison.Ordinal);

            // Smaller at maximal, which is spirv-opt having run rather than merely loaded.
            var sizes = Regex.Match(outcome.Stdout,
                @"spirv (?<maximal>\d+) bytes at maximal, (?<none>\d+) at none");
            Assert.True(sizes.Success, $"the probe printed no SPIR-V sizes:\n{outcome}");
            Assert.True(int.Parse(sizes.Groups["maximal"].Value, System.Globalization.CultureInfo.InvariantCulture) < int.Parse(sizes.Groups["none"].Value, System.Globalization.CultureInfo.InvariantCulture),
                "the optimised module is not smaller than the unoptimised one, so spirv-opt did not "
                + $"run:\n{outcome}");

            // DXIL wherever the targets file staged dxc beside the prober, which is a win-x64 host
            // and nowhere else. There its absence is a broken build, not a reason to skip the half
            // of this that a D3D12 cooker depends on.
            var dxcStaged = File.Exists(Path.Combine(ProberDirectory(), "dxcompiler.dll"));
            if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                Assert.True(dxcStaged,
                    "no dxcompiler.dll beside the prober on win-x64, where the Microsoft.Direct3D.DXC "
                    + "reference and the targets import in SharpSlang.Prober.csproj put it");
            }

            if (dxcStaged)
                Assert.Contains("DXBC, signed", outcome.Stdout, StringComparison.Ordinal);
            else
                Assert.Contains("dxil not attempted", outcome.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }

    [NativeFact]
    public void Without_slang_compiler_the_failure_names_the_file_the_rid_and_where_it_was_looked_for()
    {
        // The DllNotFoundException a deployment without the compiler gets, from the first call an
        // application makes (SlangGlobalSession.Create): explained by NativeLibraryHint, with the
        // runtime's own message inside. A copy of the prober's output with slang-compiler left out,
        // launched by the dotnet host from another directory, so nothing else on the load path can
        // supply it.
        var copy = Directory.CreateTempSubdirectory("sharpslang-no-compiler-");
        var elsewhere = Directory.CreateTempSubdirectory("sharpslang-elsewhere-");
        try
        {
            CopyDirectory(ProberDirectory(), copy.FullName, skip: name =>
                name.Contains("slang-compiler", StringComparison.OrdinalIgnoreCase));

            var outcome = RunUnderMuxer("healthy", copy.FullName, elsewhere.FullName);
            output.WriteLine(outcome.ToString());

            var file = OperatingSystem.IsWindows() ? "slang-compiler.dll" : OperatingSystem.IsMacOS() ? "libslang-compiler.dylib" : "libslang-compiler.so";
            Assert.True(outcome.ExitCode == 1, $"with no slang-compiler, Create should have thrown, and exit 1 is that:\n{outcome}");
            Assert.Contains($"DllNotFoundException: SharpSlang could not load the Slang compiler library ({file}) for {RuntimeInformation.RuntimeIdentifier}.", outcome.Stderr, StringComparison.Ordinal);
            Assert.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(copy.FullName)), outcome.Stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No DllImport resolver or ResolvingUnmanagedDll handler supplied one.", outcome.Stderr, StringComparison.Ordinal);
            Assert.Contains($"pwsh tools/fetch-native.ps1 -Rid {RuntimeInformation.RuntimeIdentifier}", outcome.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            copy.Delete(recursive: true);
            elsewhere.Delete(recursive: true);
        }
    }

    [NativeFact]
    public void Without_slang_glslang_the_optimiser_guard_still_refuses_after_the_preload()
    {
        // The other half: loading what is present must change nothing about what is absent. A copy
        // of the prober's output with slang-glslang left out, launched the same way, has to fail
        // as a broken deployment always has, out of Create with RequireSpirvOptimizer's message
        // naming the fix: not with an exception out of the preload, and not by passing on a copy
        // of the library found somewhere else. dxc is left out too; nothing here reaches it.
        var copy = Directory.CreateTempSubdirectory("sharpslang-no-glslang-");
        var elsewhere = Directory.CreateTempSubdirectory("sharpslang-elsewhere-");
        try
        {
            CopyDirectory(ProberDirectory(), copy.FullName, skip: name =>
                name.Contains("slang-glslang", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("dxcompiler", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("dxil", StringComparison.OrdinalIgnoreCase));

            var outcome = RunUnderMuxer("downstream-compilers", copy.FullName, elsewhere.FullName);
            output.WriteLine(outcome.ToString());

            AssertLaunchedElsewhere(outcome, copy.FullName);
            Assert.True(outcome.ExitCode == 1,
                $"with no slang-glslang anywhere, Create should have refused, and exit 1 is that:\n{outcome}");
            Assert.Contains("SPIR-V optimiser unavailable", outcome.Stderr, StringComparison.Ordinal);
            Assert.Contains("fetch-native.ps1 -Profile Spirv", outcome.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            copy.Delete(recursive: true);
            elsewhere.Delete(recursive: true);
        }
    }

    /// <summary>
    /// That the child really was the dotnet host, working somewhere other than its own directory.
    /// </summary>
    /// <remarks>
    /// Both tests above rest on it. A launch that quietly became the apphost, or a working
    /// directory that resolved to the output, would pass with the preload deleted: that is the
    /// launch every other probe makes, and the one where nothing was ever wrong.
    /// </remarks>
    private static void AssertLaunchedElsewhere(Outcome outcome, string proberDirectory)
    {
        var process = Regex.Match(outcome.Stdout, @"^\s*process\s+(?<path>.+)$", RegexOptions.Multiline);
        var working = Regex.Match(outcome.Stdout, @"^\s*working in\s+(?<path>.+)$", RegexOptions.Multiline);
        Assert.True(process.Success && working.Success, $"the probe did not report how it was launched:\n{outcome}");

        var executable = process.Groups["path"].Value.Trim();
        Assert.True(Path.GetFileNameWithoutExtension(executable) == "dotnet",
            $"the child's executable was {executable}, not the dotnet host:\n{outcome}");

        var directory = working.Groups["path"].Value.Trim();
        Assert.False(
            string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                          Path.TrimEndingDirectorySeparator(Path.GetFullPath(proberDirectory)),
                          OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
            $"the child worked in its own output directory, which is the launch that always worked:\n{outcome}");
    }

    private static void CopyDirectory(string from, string to, Func<string, bool> skip)
    {
        foreach (var file in Directory.EnumerateFiles(from))
        {
            if (!skip(Path.GetFileName(file))) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            var target = Directory.CreateDirectory(Path.Combine(to, Path.GetFileName(directory)));
            CopyDirectory(directory, target.FullName, skip);
        }
    }

}
