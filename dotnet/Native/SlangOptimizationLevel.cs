namespace SharpSlang.Native;

[NativeTypeName("SlangOptimizationLevelIntegral")]
public enum SlangOptimizationLevel : uint
{
    /// <summary>Don't optimize at all.</summary>
    SLANG_OPTIMIZATION_LEVEL_NONE = 0,
    /// <summary>Default optimization level: balance code quality and compilation time.</summary>
    SLANG_OPTIMIZATION_LEVEL_DEFAULT = 1,
    /// <summary>Optimize aggressively.</summary>
    SLANG_OPTIMIZATION_LEVEL_HIGH = 2,
    /// <summary>Include optimizations that may take a very long time, or may involve severe space-vs-speed tradeoffs</summary>
    SLANG_OPTIMIZATION_LEVEL_MAXIMAL = 3,
}
