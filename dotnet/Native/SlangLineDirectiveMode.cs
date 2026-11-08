namespace SharpSlang.Native;

/// <summary>Options to control emission of <c>#line</c> directives</summary>
[NativeTypeName("SlangLineDirectiveModeIntegral")]
public enum SlangLineDirectiveMode : uint
{
    /// <summary>Default behavior: pick behavior base on target.</summary>
    SLANG_LINE_DIRECTIVE_MODE_DEFAULT = 0,
    /// <summary>Don't emit line directives at all.</summary>
    SLANG_LINE_DIRECTIVE_MODE_NONE = 1,
    /// <summary>Emit standard C-style <c>#line</c> directives.</summary>
    SLANG_LINE_DIRECTIVE_MODE_STANDARD = 2,
    /// <summary>Emit GLSL-style directives with file *number* instead of name</summary>
    SLANG_LINE_DIRECTIVE_MODE_GLSL = 3,
    /// <summary>Use a source map to track line mappings (ie no #line will appear in emitting source)</summary>
    SLANG_LINE_DIRECTIVE_MODE_SOURCE_MAP = 4,
}
