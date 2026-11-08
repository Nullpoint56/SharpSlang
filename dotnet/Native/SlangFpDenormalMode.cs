namespace SharpSlang.Native;

/// <summary>Options to control floating-point denormal handling mode for a target.</summary>
[NativeTypeName("SlangFpDenormalModeIntegral")]
public enum SlangFpDenormalMode : uint
{
    SLANG_FP_DENORM_MODE_ANY = 0,
    SLANG_FP_DENORM_MODE_PRESERVE = 1,
    SLANG_FP_DENORM_MODE_FTZ = 2,
}
