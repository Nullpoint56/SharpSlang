namespace SharpSlang.Native;

/// <summary>A warning "level" (group), modeled on the clang/gcc -Wall/-Wextra/-Wpedantic groups. Each group is enabled independently: a warning tagged with a group is emitted only when that group has been enabled, while warnings in the implicit Default group are always emitted.</summary>
[NativeTypeName("SlangWarningLevelIntegral")]
public enum SlangWarningLevel
{
    /// <summary>Always emitted; this is the baseline group and is not something a caller enables explicitly.</summary>
    SLANG_WARNING_LEVEL_DEFAULT = 0,
    /// <summary>Warnings enabled by -Wall.</summary>
    SLANG_WARNING_LEVEL_ALL = 1,
    /// <summary>Warnings enabled by -Wextra.</summary>
    SLANG_WARNING_LEVEL_EXTRA = 2,
    /// <summary>Warnings enabled by -Wpedantic.</summary>
    SLANG_WARNING_LEVEL_PEDANTIC = 3,
}
