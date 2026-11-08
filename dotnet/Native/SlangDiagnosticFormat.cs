namespace SharpSlang.Native;

/// <summary>Selects the presentation of human-readable diagnostics. Machine-readable diagnostics</summary>
/// <remarks>
/// <para>take precedence over this setting.</para>
/// </remarks>
public enum SlangDiagnosticFormat
{
    SLANG_DIAGNOSTIC_FORMAT_DEFAULT = 0,
    SLANG_DIAGNOSTIC_FORMAT_VISUAL_STUDIO = 1,
}
