namespace SharpSlang.Native;

/// <summary>Description of a code generation target.</summary>
public unsafe partial struct TargetDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    [NativeTypeName("size_t")]
    public nuint structureSize;

    /// <summary>The target format to generate code for (e.g., SPIR-V, DXIL, etc.)</summary>
    public SlangCompileTarget format;

    /// <summary>The compilation profile supported by the target (e.g., "Shader Model 5.1")</summary>
    public SlangProfileID profile;

    /// <summary>Flags for the code generation target. Currently unused.</summary>
    [NativeTypeName("SlangTargetFlags")]
    public uint flags;

    /// <summary>Default mode to use for floating-point operations on the target. <c>SLANG_FLOATING_POINT_MODE_DEFAULT</c> keeps the mode set by the session's <c>compilerOptionEntries</c> (<c>FloatingPointMode</c>), if any; any other value overrides it. To get the compiler's default mode on this target when the session sets another one, add a <c>FloatingPointMode</c> entry with value <c>SLANG_FLOATING_POINT_MODE_DEFAULT</c> to this target's <c>compilerOptionEntries</c>; that entry takes precedence over this field.</summary>
    public SlangFloatingPointMode floatingPointMode;

    /// <summary>The line directive mode for output source code. <c>SLANG_LINE_DIRECTIVE_MODE_DEFAULT</c> keeps the mode set by the session's <c>compilerOptionEntries</c> (<c>LineDirectiveMode</c>), if any; any other value overrides it. To get the compiler's default mode on this target when the session sets another one, add a <c>LineDirectiveMode</c> entry with value <c>SLANG_LINE_DIRECTIVE_MODE_DEFAULT</c> to this target's <c>compilerOptionEntries</c>; that entry takes precedence over this field.</summary>
    public SlangLineDirectiveMode lineDirectiveMode;

    /// <summary>Whether to force <c>scalar</c> layout for glsl shader storage buffers. <c>true</c> forces it, unless this target's <c>compilerOptionEntries</c> set <c>GLSLForceScalarLayout</c> to 0. <c>false</c> keeps the session's <c>GLSLForceScalarLayout</c> option, if any; to turn scalar layout off for this target only when the session turns it on, add a <c>GLSLForceScalarLayout</c> entry with value 0 to this target's <c>compilerOptionEntries</c>.</summary>
    [NativeTypeName("bool")]
    public byte forceGLSLScalarBufferLayout;

    /// <summary>Pointer to an array of compiler option entries, whose size is compilerOptionEntryCount. They are applied after <c>floatingPointMode</c>, <c>lineDirectiveMode</c> and <c>forceGLSLScalarBufferLayout</c>, so an entry for the same option takes precedence over the field.</summary>
    [NativeTypeName("const CompilerOptionEntry *")]
    public CompilerOptionEntry* compilerOptionEntries;

    /// <summary>Number of additional compiler option entries.</summary>
    [NativeTypeName("uint32_t")]
    public uint compilerOptionEntryCount;
}
