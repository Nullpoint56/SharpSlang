namespace SharpSlang.Native;

/// <summary>A "container format" describes the way that the outputs for multiple files, entry points, targets, etc. should be combined into a single artifact for output.</summary>
[NativeTypeName("SlangContainerFormatIntegral")]
public enum SlangContainerFormat
{
    /// <summary>Don't generate a container.</summary>
    SLANG_CONTAINER_FORMAT_NONE = 0,
    /// <summary>Generate a container in the <c>.slang-module</c> format, which includes reflection information, compiled kernels, etc.</summary>
    SLANG_CONTAINER_FORMAT_SLANG_MODULE = 1,
}
