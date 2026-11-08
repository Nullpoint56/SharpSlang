namespace SharpSlang.Native;

[NativeTypeName("SlangPassThroughIntegral")]
public enum SlangPassThrough
{
    SLANG_PASS_THROUGH_NONE = 0,
    SLANG_PASS_THROUGH_FXC = 1,
    SLANG_PASS_THROUGH_DXC = 2,
    SLANG_PASS_THROUGH_GLSLANG = 3,
    SLANG_PASS_THROUGH_SPIRV_DIS = 4,
    /// <summary>Clang C/C++ compiler</summary>
    SLANG_PASS_THROUGH_CLANG = 5,
    /// <summary>Visual studio C/C++ compiler</summary>
    SLANG_PASS_THROUGH_VISUAL_STUDIO = 6,
    /// <summary>GCC C/C++ compiler</summary>
    SLANG_PASS_THROUGH_GCC = 7,
    /// <summary>Generic C or C++ compiler, which is decided by</summary>
    /// <remarks>
    /// <para>the source type</para>
    /// </remarks>
    SLANG_PASS_THROUGH_GENERIC_C_CPP = 8,
    /// <summary>NVRTC Cuda compiler</summary>
    SLANG_PASS_THROUGH_NVRTC = 9,
    /// <summary>LLVM 'compiler' - includes LLVM and Clang</summary>
    SLANG_PASS_THROUGH_LLVM = 10,
    /// <summary>SPIRV-opt</summary>
    SLANG_PASS_THROUGH_SPIRV_OPT = 11,
    /// <summary>Metal compiler</summary>
    SLANG_PASS_THROUGH_METAL = 12,
    /// <summary>Tint WGSL compiler</summary>
    SLANG_PASS_THROUGH_TINT = 13,
    /// <summary>SPIRV-link</summary>
    SLANG_PASS_THROUGH_SPIRV_LINK = 14,
    SLANG_PASS_THROUGH_COUNT_OF,
}
