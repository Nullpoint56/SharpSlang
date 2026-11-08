namespace SharpSlang.Native;

[NativeTypeName("SlangDebugInfoLevelIntegral")]
public enum SlangDebugInfoLevel : uint
{
    /// <summary>Don't emit debug information at all.</summary>
    SLANG_DEBUG_INFO_LEVEL_NONE = 0,
    /// <summary>Emit as little debug information as possible, while still supporting stack trackers.</summary>
    SLANG_DEBUG_INFO_LEVEL_MINIMAL = 1,
    /// <summary>Emit whatever is the standard level of debug information for each target.</summary>
    SLANG_DEBUG_INFO_LEVEL_STANDARD = 2,
    /// <summary>Emit as much debug information as possible for each target.</summary>
    SLANG_DEBUG_INFO_LEVEL_MAXIMAL = 3,
}
