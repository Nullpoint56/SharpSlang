// L2 — session configuration as managed data.
//
// SessionDesc is a nest of raw pointers and parallel counts: char** search paths, a
// PreprocessorMacroDesc array, a TargetDesc array. Building it correctly means pinning several
// managed arrays for exactly the duration of one call, which is the kind of thing that works in
// testing and leaks or dangles in production. This type collects the configuration safely and
// marshals it in one scoped block inside SlangSession.Create.

using PrismSlang.Native;

namespace PrismSlang;

/// <summary>What a compilation target should produce.</summary>
public sealed class TargetOptions
{
    /// <summary>Output format. Defaults to SPIR-V, the only target PrismEngine uses.</summary>
    public SlangCompileTarget Format { get; init; } = SlangCompileTarget.SLANG_SPIRV;

    /// <summary>Profile name, e.g. <c>spirv_1_5</c>. Null lets Slang choose.</summary>
    public string? Profile { get; init; } = "spirv_1_5";

    /// <summary>
    /// Emit SPIR-V from Slang's own backend rather than routing through glslang. On by default,
    /// matching Slang's own <c>kDefaultTargetFlags</c>. Turning this off is almost never right.
    /// </summary>
    public bool EmitSpirvDirectly { get; init; } = true;

    /// <summary>
    /// Give each <c>ParameterBlock&lt;T&gt;</c> its own register space — i.e. its own Vulkan
    /// descriptor set.
    /// </summary>
    /// <remarks>
    /// Off by default, matching Slang, and that default surprises people: without it two
    /// <c>ParameterBlock</c>s are packed into space 0 alongside everything else, and the
    /// descriptor-set structure the renderer reflects is not the one the shader author intended.
    /// Verified by <c>ReflectionTests.ParameterBlock_space_assignment_follows_the_target_flag</c>.
    /// Vulkan renderers that use parameter blocks to model per-frame / per-material / per-draw
    /// sets almost certainly want this on.
    /// </remarks>
    public bool ParameterBlocksUseRegisterSpaces { get; init; }

    public SlangFloatingPointMode FloatingPointMode { get; init; } =
        SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_DEFAULT;

    /// <summary>
    /// Force scalar buffer layout for GLSL-style targets. Irrelevant to a std140/std430 SPIR-V
    /// pipeline; exposed because changing it silently changes every uniform offset.
    /// </summary>
    public bool ForceGlslScalarBufferLayout { get; init; }
}

/// <summary>Configuration for a <see cref="SlangSession"/>.</summary>
public sealed class SessionOptions
{
    /// <summary>Compilation targets, in order. Target index 0 is the default for code retrieval.</summary>
    public IReadOnlyList<TargetOptions> Targets { get; init; } = [new TargetOptions()];

    /// <summary>Directories searched by <c>import</c> and <c>#include</c>.</summary>
    public IReadOnlyList<string> SearchPaths { get; init; } = [];

    /// <summary>
    /// Resolve <c>import</c> and <c>#include</c> through a managed filesystem instead of the OS
    /// one — an engine VFS, an asset pack, or generated sources. Null uses the OS filesystem.
    /// </summary>
    /// <remarks>
    /// The instance must outlive the session: Slang keeps the pointer for the session's lifetime.
    /// </remarks>
    public Hosting.SlangFileSystem? FileSystem { get; init; }

    /// <summary>Preprocessor defines applied to every module in the session.</summary>
    public IReadOnlyDictionary<string, string> PreprocessorMacros { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Matrix layout for the session. Slang's own default is row-major; the generated
    /// <c>SessionDesc</c> zero-initialises to <c>UNKNOWN</c>, which is not the same thing.
    /// See docs §4.5.
    /// </summary>
    public SlangMatrixLayoutMode MatrixLayout { get; init; } =
        SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_ROW_MAJOR;

    /// <summary>Allow GLSL syntax in session sources.</summary>
    public bool AllowGlslSyntax { get; init; }

    /// <summary>
    /// Skip Slang's SPIR-V validation pass. Faster, but only sensible once the shaders are known
    /// good — a validation failure is usually a real bug in the shader or in Slang.
    /// </summary>
    public bool SkipSpirvValidation { get; init; }
}
