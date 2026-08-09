// L1 — diagnostics and the SlangResult -> exception mapping.
//
// Slang reports two things separately and both matter: a SlangResult, and an optional diagnostic
// blob carrying the actual compiler messages. Dropping the blob is the classic mistake — you get
// "compilation failed (0x80004005)" instead of "undefined identifier 'albedo' at line 12".
//
// Neither the pairing nor the blob's lifetime is expressible in the generated layer: the blob is
// an out-parameter the caller owns and must release.

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

    private static string Compose(int result, string operation, string? diagnostics) =>
        string.IsNullOrWhiteSpace(diagnostics)
            ? $"{operation} failed: {SlangResult.Describe(result)}"
            : $"{operation} failed: {SlangResult.Describe(result)}{Environment.NewLine}{diagnostics}";
}

/// <summary>
/// Diagnostics Slang produced on a call that nonetheless succeeded.
/// </summary>
/// <remarks>
/// This is not a hypothetical. The Tier 0 spike found Slang returning
/// <c>error[E00100]: failed to load downstream compiler 'spirv-opt'</c> on a <em>successful</em>
/// result, then silently emitting unoptimised SPIR-V. A binding that only reads the diagnostic
/// blob on failure loses that entirely, so the callback fires on both paths.
/// </remarks>
public delegate void SlangDiagnosticHandler(string operation, string diagnostics);

internal static unsafe class Diagnostics
{
    /// <summary>
    /// Where non-fatal diagnostics go. Defaults to stderr; hosts can redirect into their own log.
    /// </summary>
    internal static SlangDiagnosticHandler Handler { get; set; } =
        static (op, text) => Console.Error.WriteLine($"[slang] {op}:{Environment.NewLine}{text}");

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

        if (text is not null) Handler(operation, text);
    }

    /// <summary>Throw if a call that reports failure by returning null produced null.</summary>
    internal static void ThrowIfNull(void* p, string operation, ISlangBlob* diagnostics)
    {
        var text = Consume(diagnostics);

        if (p is null) throw new SlangException(SlangResult.Fail, operation, text);

        if (text is not null) Handler(operation, text);
    }
}
