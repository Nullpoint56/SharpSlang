namespace SharpSlang.Native;

public enum SlangDiagnosticColor
{
    /// <summary>Use color if output sink is a tty</summary>
    SLANG_DIAGNOSTIC_COLOR_AUTO = 0,
    /// <summary>Always use color</summary>
    SLANG_DIAGNOSTIC_COLOR_ALWAYS = 1,
    /// <summary>Never use color</summary>
    SLANG_DIAGNOSTIC_COLOR_NEVER = 2,
}
