namespace SharpSlang.Native;

/// <summary>Options to control floating-point precision guarantees for a target.</summary>
[NativeTypeName("SlangFloatingPointModeIntegral")]
public enum SlangFloatingPointMode : uint
{
    SLANG_FLOATING_POINT_MODE_DEFAULT = 0,
    SLANG_FLOATING_POINT_MODE_FAST = 1,
    SLANG_FLOATING_POINT_MODE_PRECISE = 2,
}
