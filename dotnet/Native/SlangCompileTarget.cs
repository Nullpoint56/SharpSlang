namespace SharpSlang.Native;

/// <summary>NOTE! To keep binary compatibility care is needed with this enum!</summary>
/// <remarks>
/// <para>To add value, only add at the bottom (before COUNT_OF) To remove a value, add _DEPRECATED as a suffix, but leave in the list</para>
/// <para>This will make the enum values stable, and compatible with libraries that might not use the latest enum values.</para>
/// </remarks>
[NativeTypeName("SlangCompileTargetIntegral")]
public enum SlangCompileTarget
{
    SLANG_TARGET_UNKNOWN = 0,
    SLANG_TARGET_NONE = 1,
    SLANG_GLSL = 2,
    /// <summary>deprecated and removed: just use <c>SLANG_GLSL</c>.</summary>
    SLANG_GLSL_VULKAN_DEPRECATED = 3,
    /// <summary>deprecated and removed.</summary>
    SLANG_GLSL_VULKAN_ONE_DESC_DEPRECATED = 4,
    SLANG_HLSL = 5,
    SLANG_SPIRV = 6,
    SLANG_SPIRV_ASM = 7,
    SLANG_DXBC = 8,
    SLANG_DXBC_ASM = 9,
    SLANG_DXIL = 10,
    SLANG_DXIL_ASM = 11,
    /// <summary>The C language</summary>
    SLANG_C_SOURCE = 12,
    /// <summary>C++ code for shader kernels.</summary>
    SLANG_CPP_SOURCE = 13,
    /// <summary>Standalone binary executable (for hosting CPU/OS)</summary>
    SLANG_HOST_EXECUTABLE = 14,
    /// <summary>A shared library/Dll for shader kernels (for hosting</summary>
    /// <remarks>
    /// <para>CPU/OS)</para>
    /// </remarks>
    SLANG_SHADER_SHARED_LIBRARY = 15,
    /// <summary>A CPU target that makes the compiled shader code</summary>
    /// <remarks>
    /// <para>available to be run immediately</para>
    /// </remarks>
    SLANG_SHADER_HOST_CALLABLE = 16,
    /// <summary>Cuda source</summary>
    SLANG_CUDA_SOURCE = 17,
    /// <summary>PTX</summary>
    SLANG_PTX = 18,
    /// <summary>Object code that contains CUDA functions.</summary>
    SLANG_CUDA_OBJECT_CODE = 19,
    /// <summary>Object code that can be used for later linking (kernel/shader)</summary>
    SLANG_OBJECT_CODE = 20,
    /// <summary>C++ code for host library or executable.</summary>
    SLANG_HOST_CPP_SOURCE = 21,
    /// <summary>Host callable host code (ie non kernel/shader)</summary>
    SLANG_HOST_HOST_CALLABLE = 22,
    /// <summary>C++ PyTorch binding code.</summary>
    SLANG_CPP_PYTORCH_BINDING = 23,
    /// <summary>Metal shading language</summary>
    SLANG_METAL = 24,
    /// <summary>Metal library</summary>
    SLANG_METAL_LIB = 25,
    /// <summary>Metal library assembly</summary>
    SLANG_METAL_LIB_ASM = 26,
    /// <summary>A shared library/Dll for host code (for hosting CPU/OS)</summary>
    SLANG_HOST_SHARED_LIBRARY = 27,
    /// <summary>WebGPU shading language</summary>
    SLANG_WGSL = 28,
    /// <summary>SPIR-V assembly via WebGPU shading language</summary>
    SLANG_WGSL_SPIRV_ASM = 29,
    /// <summary>SPIR-V via WebGPU shading language</summary>
    SLANG_WGSL_SPIRV = 30,
    /// <summary>Bytecode that can be interpreted by the Slang VM</summary>
    SLANG_HOST_VM = 31,
    /// <summary>C++ header for shader kernels.</summary>
    SLANG_CPP_HEADER = 32,
    /// <summary>Cuda header</summary>
    SLANG_CUDA_HEADER = 33,
    /// <summary>Host object code</summary>
    SLANG_HOST_OBJECT_CODE = 34,
    /// <summary>Host LLVM IR assembly</summary>
    SLANG_HOST_LLVM_IR = 35,
    /// <summary>Host LLVM IR assembly (kernel/shader)</summary>
    SLANG_SHADER_LLVM_IR = 36,
    SLANG_TARGET_COUNT_OF,
}
