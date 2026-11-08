// L2 — session configuration as managed data.
//
// SessionDesc is a nest of raw pointers and parallel counts: char** search paths, a
// PreprocessorMacroDesc array, a TargetDesc array. Building it correctly means pinning several
// managed arrays for exactly the duration of one call, which is the kind of thing that works in
// testing and leaks or dangles in production. This type collects the configuration safely and
// marshals it in one scoped block inside SlangSession.Create.

using SharpSlang.Native;
using SharpSlang.Spirv;

namespace SharpSlang;

/// <summary>What a compilation target should produce.</summary>
public sealed class TargetOptions
{
    /// <summary>
    /// Output format. Defaults to SPIR-V. <c>SLANG_DXIL</c> is the
    /// other one this binding measures, for D3D12; it needs dxc, which the
    /// <c>SharpSlang.Dxc</c> package copies into the application's output and
    /// <see cref="SlangGlobalSession.RequireDxc"/> checks at session creation. Name its shader
    /// model in <see cref="Profile"/> (<c>sm_6_5</c> for mesh and ray-tracing stages): left
    /// unset, Slang picks one.
    /// </summary>
    public SlangCompileTarget Format { get; init; } = SlangCompileTarget.SLANG_SPIRV;

    /// <summary>
    /// Which SPIR-V version to emit. Effectively 1.5 unless set — but pinned rather than inherited,
    /// because "whatever the compiler felt like" is not a target environment.
    /// </summary>
    /// <remarks>
    /// A Vulkan device accepts SPIR-V up to a version fixed by its API version (1.0 for Vulkan 1.0,
    /// 1.3 for 1.1, 1.5 for 1.2, 1.6 from 1.3, per the Vulkan specification); above it,
    /// <c>vkCreateShaderModule</c> fails with nothing useful to say.
    ///
    /// Measured against slang-compiler 2026.21: the profile is the only knob that moves the
    /// emitted header. Every <c>spirv_1_0</c>..<c>spirv_1_6</c> profile produces exactly the header
    /// version it names; a link-time <c>-capability spirv_1_6</c> is silently ignored. Ignored
    /// unless <see cref="Format"/> is SPIR-V.
    ///
    /// Null — the default — means "not specified", which is not the same as saying 1.5: it lets
    /// <see cref="Profile"/> decide, and only falls back to 1.5 when neither knob is set. Keeping
    /// those two states distinct is what lets an existing caller pin <c>Profile = "spirv_1_6"</c>
    /// and not be told it contradicts a version it never chose.
    /// </remarks>
    public SpirvVersion? SpirvVersion { get; init; }

    /// <summary>
    /// Check the emitted module's header against what was requested, and that it is SPIR-V at all.
    /// </summary>
    /// <remarks>
    /// On by default. It is five words of parsing per generated module, and it converts "the device
    /// rejected the shader" into "Slang emitted 1.6 when 1.4 was requested" at the point of
    /// compilation. Ignored unless <see cref="Format"/> is SPIR-V.
    /// </remarks>
    public bool VerifyEmittedSpirv { get; init; } = true;

    /// <summary>
    /// Name each entry point in the emitted module after its Slang function, instead of <c>main</c>.
    /// </summary>
    /// <remarks>
    /// Off by default, matching Slang. Whichever way this is set, read the resulting name from the
    /// module rather than assuming it (AGENTS.md#layout-and-the-emitted-module).
    ///
    /// Note this must be set here or on the session: measured against slang-compiler 2026.21,
    /// passing <c>VulkanUseEntryPointName</c> to <c>linkWithOptions</c> has no effect at all.
    /// </remarks>
    public bool UseEntryPointNameInSpirv { get; init; }

    /// <summary>
    /// Profile name, e.g. <c>spirv_1_5</c> or <c>glsl_450</c>. Null derives it from
    /// <see cref="SpirvVersion"/> for a SPIR-V target, and leaves it to Slang otherwise.
    /// </summary>
    /// <remarks>
    /// The escape hatch, for profiles <see cref="SpirvVersion"/> cannot name. Setting this alone is
    /// fine and the profile decides; setting <em>both</em> to contradictory SPIR-V versions is
    /// rejected at session creation rather than silently resolved one way or the other.
    /// </remarks>
    public string? Profile { get; init; }

    /// <summary>
    /// Emit SPIR-V from Slang's own backend rather than routing through glslang. On by default,
    /// matching Slang's own <c>kDefaultTargetFlags</c>. Turning this off is almost never right.
    /// </summary>
    public bool EmitSpirvDirectly { get; init; } = true;

    /// <summary>
    /// Give each <c>ParameterBlock&lt;T&gt;</c> its own register space.
    /// <b>No observable effect on a SPIR-V target</b> — see the remarks before setting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured against slang-compiler 2026.21: setting this produces <em>byte-identical</em>
    /// output and an identical descriptor-set structure, because a <c>ParameterBlock</c> on SPIR-V
    /// introduces a sub-element register space whether the flag is set or not. Kept because it is a
    /// real target flag that other targets honour, and removing it would make the option
    /// unreachable; asserted inert by
    /// <c>OptionEffectTests.ParameterBlocksUseRegisterSpaces_does_nothing_on_a_SPIR_V_target</c>,
    /// which fails if a future release starts honouring it.
    /// </para>
    /// <para>
    /// An earlier version of this comment said the opposite — that without the flag two blocks are
    /// packed into space 0, and that a Vulkan renderer "almost certainly wants this on" — and cited
    /// a test whose name agreed while its body had already recorded the correction. That is the
    /// same failure repeating: a consumer reads the property they are about to set, not the body of
    /// a test somewhere else.
    /// </para>
    /// </remarks>
    public bool ParameterBlocksUseRegisterSpaces { get; init; }

    public SlangFloatingPointMode FloatingPointMode { get; init; } =
        SlangFloatingPointMode.SLANG_FLOATING_POINT_MODE_DEFAULT;

    /// <summary>
    /// Force scalar buffer layout for GLSL-style targets. Irrelevant to a std140/std430 SPIR-V
    /// pipeline; exposed because changing it silently changes every uniform offset.
    /// </summary>
    public bool ForceGlslScalarBufferLayout { get; init; }

    // The property and the type share a name, so `this.` marks every read of the property below and
    // a bare `SpirvVersion` is always the type. C# resolves both, but not readably.

    /// <summary>What a SPIR-V target falls back to when neither knob is set.</summary>
    /// <remarks>
    /// Slang's own default, restated here so it is a decision rather than an inheritance. Vulkan
    /// 1.2 and later all accept it; a device that needs 1.6 features has to say so.
    /// </remarks>
    private const SpirvVersion FallbackSpirvVersion = Spirv.SpirvVersion.V1_5;

    /// <summary>Whether this target emits SPIR-V, and so whether the SPIR-V knobs apply.</summary>
    internal bool IsSpirv => Format == SlangCompileTarget.SLANG_SPIRV;

    /// <summary>The profile name to hand Slang, or null to leave it unset.</summary>
    /// <remarks>
    /// A non-SPIR-V target gets no profile unless <see cref="Profile"/> names one, so Slang's own
    /// choice stands (AGENTS.md#layers: no default but the header's). For DXIL that choice moves
    /// with the compiler: slangc 2026.21 builds an unprofiled compute shader as shader model 6.0,
    /// and a mesh or ray-tracing shader needs <c>sm_6_5</c> named.
    /// </remarks>
    internal string? ResolveProfile() =>
        Profile ?? (IsSpirv ? (this.SpirvVersion ?? FallbackSpirvVersion).ToProfileName() : null);

    /// <summary>
    /// The SPIR-V version the emitted module must carry, or Unspecified when nothing was pinned
    /// and there is therefore nothing to check against.
    /// </summary>
    internal SpirvVersion ExpectedSpirvVersion =>
        !IsSpirv ? Spirv.SpirvVersion.Unspecified
        : Profile is not null ? SpirvVersions.FromProfileName(Profile)
        : this.SpirvVersion ?? FallbackSpirvVersion;

    /// <summary>
    /// Rejects a configuration whose two version knobs disagree.
    /// </summary>
    /// <remarks>
    /// Only when both were actually set — hence the nullable <see cref="SpirvVersion"/>. Picking a
    /// winner silently would mean one of the two things the caller wrote down is a lie, and the
    /// symptom lands much later as a device rejecting a shader module.
    /// </remarks>
    internal void Validate(string paramName)
    {
        if (!IsSpirv || Profile is null) return;
        if (this.SpirvVersion is not { } wanted || wanted == Spirv.SpirvVersion.Unspecified) return;

        var fromProfile = SpirvVersions.FromProfileName(Profile);
        if (fromProfile == Spirv.SpirvVersion.Unspecified || fromProfile == wanted) return;

        throw new ArgumentException(
            $"target profile '{Profile}' is SPIR-V {fromProfile.ToDisplayString()} but SpirvVersion " +
            $"says {wanted.ToDisplayString()}. Set one or the other, not both.",
            paramName);
    }
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
    /// See AGENTS.md#descriptor-defaults-and-result-codes.
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
