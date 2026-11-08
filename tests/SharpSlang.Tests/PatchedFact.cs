// Which Slang the tests run against, and the facts that depend on it (AGENTS.md#patches).
//
// A patch in patches/ reaches only a payload built from source (tools/build-native.ps1). Slang's
// release, which tools/fetch-native.ps1 stages and CI tests until the switch (AGENTS.md#decisions),
// carries none. Both scripts write runtimes/<rid>/payload.json saying which it is, with the patches
// applied and the hash of the compiler staged; NativePayloadTests holds the file to the payload in
// the test and prober output directories, and to patches/.
//
//   [PatchedFact("U4")]         the right answer, which only our build gives. Skips on the release,
//                               naming the patch it lacks. Runs on every source build, one built
//                               -WithoutPatches included: that is the red run.
//   [ReleasePayloadFact("U4")]  the release's wrong answer, asserted as wrong, so the release that
//                               fixes it fails the test and names the binding's guard to revisit.
//                               Skips on a source build, whose answer is the patched one.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SharpSlang.Tests;

/// <summary>runtimes/&lt;rid&gt;/payload.json: where the staged payload came from.</summary>
internal static class StagedPayload
{
    internal static string Rid => (OperatingSystem.IsWindows() ? "win-" : OperatingSystem.IsLinux() ? "linux-" : OperatingSystem.IsMacOS() ? "osx-" : "unsupported-")
        + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    internal static string FilePath => Path.Combine(SlangHeader.RepoRoot(), "runtimes", Rid, "payload.json");

    private static readonly Lazy<JsonElement?> Document = new(() =>
        File.Exists(FilePath) ? JsonElement.Parse(File.ReadAllText(FilePath)) : null);

    /// <summary>"release" (Slang's archive), "build" (the submodule), or null when no file says.</summary>
    internal static string? Source => Document.Value?.GetProperty("source").GetString();

    internal static bool IsRelease => Source == "release";

    internal static bool IsBuild => Source == "build";

    /// <summary>The patch files applied, in order; empty for the release.</summary>
    internal static IReadOnlyList<(string File, string Sha256)> Patches =>
        Document.Value is { } root
            ? [.. root.GetProperty("patches").EnumerateArray().Select(p => (p.GetProperty("file").GetString()!, p.GetProperty("sha256").GetString()!))]
            : [];

    internal static (string File, string Sha256)? Compiler =>
        Document.Value is { } root
            ? (root.GetProperty("compiler").GetProperty("file").GetString()!, root.GetProperty("compiler").GetProperty("sha256").GetString()!)
            : null;

    internal static bool Carries(string patchFile) => Patches.Any(p => p.File == patchFile);
}

/// <summary>docs/upstream.json, the patch registry: the patch that carries each defect's fix.</summary>
internal static class PatchRegistry
{
    private static readonly Lazy<JsonElement> Document = new(() =>
        JsonElement.Parse(File.ReadAllText(Path.Combine(SlangHeader.RepoRoot(), "docs", "upstream.json"))));

    internal static string? PatchFor(string defect)
    {
        foreach (var entry in Document.Value.GetProperty("defects").EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == defect)
                return entry.GetProperty("patch").GetString();
        }

        throw new ArgumentException($"{defect} is not in docs/upstream.json", nameof(defect));
    }
}

internal static class PayloadSkips
{
    internal static string? Patched(string defect)
    {
        if (!SlangNative.Available) return SlangNative.SkipReason;
        if (PatchRegistry.PatchFor(defect) is null)
            throw new InvalidOperationException($"[PatchedFact(\"{defect}\")] on a defect docs/upstream.json carries no patch for");
        return StagedPayload.Source switch
        {
            "release" => $"official Slang release payload; patch {defect} not applied",
            "build" => null,
            _ => $"{StagedPayload.FilePath} is absent, so the payload's origin is unknown. Run: pwsh tools/build-native.ps1 -Rid {StagedPayload.Rid}",
        };
    }

    internal static string? Release(string defect)
    {
        if (!SlangNative.Available) return SlangNative.SkipReason;
        return StagedPayload.Source switch
        {
            "release" => null,
            "build" => $"payload built from source; {defect}'s defect is asserted against Slang's release payload, and its fix by the patched test",
            _ => $"{StagedPayload.FilePath} is absent, so the payload's origin is unknown. Run: pwsh tools/fetch-native.ps1 -Rid {StagedPayload.Rid}",
        };
    }
}

/// <summary>
/// A fact asserting what a patch in patches/ makes Slang do; skipped on Slang's release payload, which
/// does not carry it.
/// </summary>
public sealed class PatchedFactAttribute : FactAttribute
{
    /// <param name="defect">The registry id, U and a number.</param>
    /// <param name="sourceFilePath">Filled by the compiler.</param>
    /// <param name="sourceLineNumber">Filled by the compiler.</param>
    public PatchedFactAttribute(string defect, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) => Skip = PayloadSkips.Patched(defect);
}

/// <summary>A fact asserting the defect Slang's release payload has; skipped on a source build.</summary>
public sealed class ReleasePayloadFactAttribute : FactAttribute
{
    /// <param name="defect">The registry id, U and a number.</param>
    /// <param name="sourceFilePath">Filled by the compiler.</param>
    /// <param name="sourceLineNumber">Filled by the compiler.</param>
    public ReleasePayloadFactAttribute(string defect, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber) => Skip = PayloadSkips.Release(defect);
}
