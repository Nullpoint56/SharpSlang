// L1 — diagnostics and the SlangResult -> exception mapping.
//
// Slang reports two things separately and both matter: a SlangResult, and an optional diagnostic
// blob carrying the actual compiler messages. Dropping the blob is the classic mistake — you get
// "compilation failed (0x80004005)" instead of "undefined identifier 'albedo' at line 12".
//
// Neither the pairing nor the blob's lifetime is expressible in the generated layer: the blob is
// an out-parameter the caller owns and must release.
//
// The subtler half is diagnostics on a *successful* call. Slang emits error-severity messages and
// returns SLANG_OK for anything it can recover from, and "recover" can mean silently degrading the
// output. Those are fatal here by default; see SlangDiagnosticPolicy.

using System.Text.RegularExpressions;
using PrismSlang.Native;

namespace PrismSlang.Interop;

/// <summary>A failed Slang call, with the compiler's own diagnostic text attached.</summary>
public sealed class SlangException(int result, string operation, string? diagnostics)
    : Exception(Compose(result, operation, diagnostics))
{
    /// <summary>The raw <c>SlangResult</c>.</summary>
    public int Result { get; } = result;

    /// <summary>Compiler diagnostics, if Slang produced any.</summary>
    public string? Diagnostics { get; } = diagnostics;

    private static string Compose(int result, string operation, string? diagnostics)
    {
        // A success code here means the call worked and the diagnostics are what condemned it.
        // Saying "failed: SLANG_OK" would send the reader hunting for the wrong thing.
        var lead = SlangResult.Succeeded(result)
            ? $"{operation} succeeded but reported errors"
            : $"{operation} failed: {SlangResult.Describe(result)}";

        return string.IsNullOrWhiteSpace(diagnostics)
            ? lead
            : $"{lead}{Environment.NewLine}{diagnostics}";
    }
}

/// <summary>
/// The loaded <c>slang-compiler</c> is not the release this binding was generated against.
/// </summary>
/// <remarks>
/// Its own exception type because it is not a compiler error and there is no <c>SlangResult</c> to
/// carry: nothing has been asked of Slang yet. It is a deployment fault, and the two recoveries a
/// caller might want — re-stage the payload, or accept the mismatch deliberately — both need to
/// distinguish it from a compile failure. See <c>docs/design.md</c> §3.5.
/// </remarks>
public sealed class SlangVersionMismatchException(string expected, string loaded, string message)
    : Exception(message)
{
    /// <summary>The release this assembly's bindings were generated from.</summary>
    public string Expected { get; } = expected;

    /// <summary>What the loaded library reports via <c>spGetBuildTagString</c>.</summary>
    public string Loaded { get; } = loaded;
}

/// <summary>How severe a Slang diagnostic is, read from its message prefix.</summary>
public enum SlangDiagnosticSeverity
{
    /// <summary>Supplementary detail attached to another diagnostic.</summary>
    Note,

    /// <summary>Something suspicious; the compiler proceeded as written.</summary>
    Warning,

    /// <summary>
    /// Something wrong. On a failed call this explains the failure; on a successful one it means
    /// Slang carried on with degraded output.
    /// </summary>
    Error,

    /// <summary>Compilation stopped. Only ever seen alongside a failed result.</summary>
    Fatal,
}

/// <summary>What to do about diagnostics on a call that nonetheless succeeded.</summary>
/// <remarks>
/// The case that motivates the default is not hypothetical, and it is worse than it first looks.
/// With <c>slang-glslang</c> absent, Slang reports
/// <code>
/// error[E00100]: failed to load downstream compiler 'spirv-opt'
/// note[E99996]: failed to load dynamic library 'slang-glslang'
/// </code>
/// on a <em>successful</em> <c>getEntryPointCode</c>, then emits unoptimised SPIR-V — 2,048 bytes
/// where the optimiser produces 1,664, on the shader in <c>LinkOptionsTests</c>. And it says so
/// exactly once per global session: measured over six compiles across two global sessions, the
/// message appeared twice, so shaders 2..n of every batch are silently fat with no diagnostic at
/// all. A binding that logs and continues therefore loses the whole build.
/// </remarks>
public enum SlangDiagnosticPolicy
{
    /// <summary>
    /// Throw a <see cref="SlangException"/> when a successful call reported an error or worse.
    /// The default: a compiler that says "error" has not done what was asked.
    /// </summary>
    ThrowOnError,

    /// <summary>Hand everything to the handler and carry on, whatever the severity.</summary>
    Report,

    /// <summary>Discard diagnostics from successful calls entirely. Diagnostics on a failed call are still attached to the exception.</summary>
    Ignore,
}

/// <summary>Receives diagnostics Slang produced on a call that succeeded.</summary>
/// <param name="operation">Which Slang call produced them.</param>
/// <param name="severity">
/// The worst severity in <paramref name="diagnostics"/>, already classified. A host under
/// <see cref="SlangDiagnosticPolicy.Report"/> is the one that most needs it — that policy exists to
/// let a build log warnings and fail on errors itself, which is not possible without knowing which
/// is which.
/// </param>
/// <param name="diagnostics">The compiler's text, verbatim.</param>
/// <remarks>
/// The severity is passed rather than left to the handler because the classification is not
/// obvious: Slang has no structured severity, so it comes from parsing message prefixes, and a host
/// re-deriving it would be re-deriving <see cref="SlangDiagnostics.Classify"/> — probably less
/// carefully, and certainly without the regression tests. It is still public for a caller with
/// diagnostic text from somewhere else.
/// </remarks>
public delegate void SlangDiagnosticHandler(
    string operation,
    SlangDiagnosticSeverity severity,
    string diagnostics);

/// <summary>
/// Process-wide policy for diagnostics Slang produces on successful calls.
/// </summary>
/// <remarks>
/// Static because Slang's diagnostic blobs are per-call, not per-session: there is no object to
/// hang this off that would cover every path. An asset cooker sets it once at startup.
/// </remarks>
public static partial class SlangDiagnostics
{
    /// <summary>
    /// Where diagnostics from successful calls go. Defaults to stderr; hosts should redirect this
    /// into their own log.
    /// </summary>
    public static SlangDiagnosticHandler Handler { get; set; } =
        static (op, severity, text) =>
            Console.Error.WriteLine($"[slang {severity}] {op}:{Environment.NewLine}{text}");

    /// <summary>What to do about them. Defaults to <see cref="SlangDiagnosticPolicy.ThrowOnError"/>.</summary>
    public static SlangDiagnosticPolicy Policy { get; set; } = SlangDiagnosticPolicy.ThrowOnError;

    /// <summary>
    /// The worst severity mentioned in a diagnostic blob.
    /// </summary>
    /// <remarks>
    /// Slang has no structured severity on the blob — it is one string holding the whole
    /// conversation — so this reads the message prefixes. The shapes it must handle, all taken
    /// from real output:
    /// <code>
    /// error[E00100]: failed to load downstream compiler 'spirv-opt'
    /// warning[E50011]: SPIR-V version too old
    /// note[E99996]: failed to load dynamic library 'slang-glslang'
    /// t.slang(12): error 30019: expected an expression of type 'float'
    /// fatal error[E40003]: compilation ceased
    /// </code>
    /// Anything unrecognised is treated as a note, so a future message shape cannot spuriously
    /// fail a build that is actually fine. The cost of that choice is that a genuinely new error
    /// shape would be under-reported — which is why the escalation is anchored on the words Slang
    /// has used consistently since it had a diagnostic sink at all.
    /// </remarks>
    public static SlangDiagnosticSeverity Classify(string? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(diagnostics)) return SlangDiagnosticSeverity.Note;

        var worst = SlangDiagnosticSeverity.Note;
        foreach (Match match in SeverityWord().Matches(diagnostics))
        {
            var severity = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "fatal error" or "internal error" => SlangDiagnosticSeverity.Fatal,
                "error" => SlangDiagnosticSeverity.Error,
                "warning" => SlangDiagnosticSeverity.Warning,
                _ => SlangDiagnosticSeverity.Note,
            };

            if (severity > worst) worst = severity;
            if (worst == SlangDiagnosticSeverity.Fatal) break;
        }

        return worst;
    }

    // A severity word only counts at the start of a line or straight after "path(line): ", so the
    // word "error" inside a message body cannot escalate anything.
    [GeneratedRegex(
        @"(?:^|\n|\):\s)(fatal error|internal error|error|warning|note)(?=\s*(?:\[[^\]]*\])?\s*[:0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeverityWord();
}

internal static unsafe class Diagnostics
{
    /// <summary>Reads a diagnostic blob and releases it. Null and empty are both normal.</summary>
    internal static string? Consume(ISlangBlob* blob)
    {
        if (blob is null) return null;
        using var owned = new SlangBlob(blob);
        var text = owned.AsText();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Throw if the call failed; consume the diagnostic blob either way.</summary>
    internal static void ThrowIfFailed(int result, string operation, ISlangBlob* diagnostics)
    {
        var text = Consume(diagnostics);

        if (SlangResult.Failed(result))
            throw new SlangException(result, operation, text);

        ApplySuccessPolicy(result, operation, text);
    }

    /// <summary>Throw if a call that reports failure by returning null produced null.</summary>
    internal static void ThrowIfNull(void* p, string operation, ISlangBlob* diagnostics)
    {
        var text = Consume(diagnostics);

        if (p is null) throw new SlangException(SlangResult.Fail, operation, text);

        ApplySuccessPolicy(SlangResult.Ok, operation, text);
    }

    /// <summary>
    /// Applies the policy to diagnostics that arrived alongside a success.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so tests can drive it with the exact text Slang produced when
    /// spirv-opt was missing. That case cannot be reproduced in-process — it needs the shared
    /// library to be absent when the compiler loads — so the captured string is the closest thing
    /// to the real input, and the classifier is what has to keep working against it.
    /// </remarks>
    internal static void ApplySuccessPolicy(int result, string operation, string? text)
    {
        if (text is null) return;
        if (SlangDiagnostics.Policy == SlangDiagnosticPolicy.Ignore) return;

        // Classified once, then either acted on or handed over. A host under Report needs the same
        // answer the ThrowOnError branch uses, not its own guess at it.
        var severity = SlangDiagnostics.Classify(text);

        if (SlangDiagnostics.Policy == SlangDiagnosticPolicy.ThrowOnError
            && severity >= SlangDiagnosticSeverity.Error)
        {
            throw new SlangException(result, operation, text);
        }

        SlangDiagnostics.Handler(operation, severity, text);
    }
}
