namespace SharpSlang.Native;

/// <summary>Describes the debugging information format produced during a compilation.</summary>
[NativeTypeName("SlangDebugInfoFormatIntegral")]
public enum SlangDebugInfoFormat : uint
{
    /// <summary>Use the default debugging format for the target</summary>
    SLANG_DEBUG_INFO_FORMAT_DEFAULT = 0,
    /// <summary>CodeView C7 format (typically means debugging</summary>
    /// <remarks>
    /// <para>information is embedded in the binary)</para>
    /// </remarks>
    SLANG_DEBUG_INFO_FORMAT_C7 = 1,
    /// <summary>Program database</summary>
    SLANG_DEBUG_INFO_FORMAT_PDB = 2,
    /// <summary>Stabs</summary>
    SLANG_DEBUG_INFO_FORMAT_STABS = 3,
    /// <summary>COFF debug info</summary>
    SLANG_DEBUG_INFO_FORMAT_COFF = 4,
    /// <summary>DWARF debug info (we may want to support specifying</summary>
    /// <remarks>
    /// <para>the version)</para>
    /// </remarks>
    SLANG_DEBUG_INFO_FORMAT_DWARF = 5,
    SLANG_DEBUG_INFO_FORMAT_COUNT_OF,
}
