// Gate for tests that need the real slang-compiler binary.
//
// The drift tests parse a header and need nothing native. These ones drive the compiler, so on a
// machine where tools/fetch-native.ps1 has not run they must skip with a message that says what to
// do — not fail with a DllNotFoundException that reads like a broken build.

namespace SharpSlang.Tests;

/// <summary>A fact that is skipped, with an actionable reason, when the native payload is absent.</summary>
public sealed class NativeFactAttribute : FactAttribute
{
    public NativeFactAttribute()
    {
        if (!SlangNative.Available) Skip = SlangNative.SkipReason;
    }
}

/// <summary>A theory that is skipped, with an actionable reason, when the native payload is absent.</summary>
public sealed class NativeTheoryAttribute : TheoryAttribute
{
    public NativeTheoryAttribute()
    {
        if (!SlangNative.Available) Skip = SlangNative.SkipReason;
    }
}

internal static class SlangNative
{
    internal const string SkipReason =
        "slang-compiler is not loadable. Run: pwsh tools/fetch-native.ps1";

    private static readonly Lazy<bool> Probe = new(() =>
    {
        try
        {
            // Cheapest possible call that proves the library resolved: no session, no allocation.
            return !string.IsNullOrEmpty(SlangGlobalSession.BuildTag);
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    });

    internal static bool Available => Probe.Value;
}
