// L2 — the entry point to the compiler.
//
// One global session per process is the intended usage: it owns the core module and the downstream
// compiler registry, and creating one is expensive. Sessions created from it are the cheap,
// per-configuration objects.

using System.Reflection;
using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang;

/// <summary>Configuration for a <see cref="SlangGlobalSession"/>.</summary>
public sealed class GlobalSessionOptions
{
    /// <summary>Allow GLSL syntax as an input language. Off by default, as in Slang.</summary>
    public bool EnableGlsl { get; init; }

    /// <summary>
    /// Fail creation unless the SPIR-V optimiser is loadable.
    /// </summary>
    /// <remarks>
    /// On by default, and the default is the point. Without <c>slang-glslang</c> beside the
    /// compiler, Slang emits unoptimised SPIR-V — 2,048 bytes where the optimiser produces 1,664 on
    /// the shader in <c>LinkOptionsTests</c> — and says so <em>once per global session</em>, on a
    /// successful result. Every shader after the first in a batch is silently oversized with no
    /// diagnostic at all, so the per-call diagnostic policy cannot be the only guard. This one is
    /// deterministic and runs before a single shader is compiled.
    ///
    /// Turn it off only for a deliberately minimal deployment (<c>fetch-native.ps1 -Profile
    /// Minimal</c>) or when compiling at <c>SLANG_OPTIMIZATION_LEVEL_NONE</c> throughout, where
    /// spirv-opt is never invoked and the output is byte-identical either way.
    /// </remarks>
    public bool RequireSpirvOptimizer { get; init; } = true;

    /// <summary>
    /// Fail creation unless the loaded <c>slang-compiler</c> is the release these bindings were
    /// generated from.
    /// </summary>
    /// <remarks>
    /// On by default. Slang states its public ABI is not stable and directs redistributors to ship
    /// the libraries they built against (docs/design.md §3.5), so this is not defensive
    /// housekeeping: <c>dotnet/Native/</c> indexes vtable slots by ordinal, taken from one specific
    /// header. A payload with a reordered interface still links, still calls, and calls the wrong
    /// function — the failure mode that <c>regen</c> guards against upstream and this guards
    /// against downstream.
    ///
    /// The realistic way to hit it is not exotic. <c>runtimes/</c> is gitignored, so bumping
    /// <c>$(SlangVersion)</c> without re-running <c>fetch-native.ps1</c> leaves the previous
    /// release staged and everything builds. That mismatch is a CI failure here
    /// (<c>Build_tag_matches_the_pinned_version</c>) and was nothing at all in a consumer's tree.
    ///
    /// Turn it off only when running a different Slang release is deliberate and the risk is
    /// understood; <see cref="SlangVersionMismatchException"/> is thrown otherwise and names both
    /// versions.
    /// </remarks>
    public bool RequirePinnedSlangVersion { get; init; } = true;
}

/// <summary>
/// A Slang compiler instance. Create one per process and derive <see cref="SlangSession"/>s from it.
/// </summary>
public sealed unsafe class SlangGlobalSession : SlangObject<IGlobalSession>
{
    private SlangGlobalSession(IGlobalSession* owned) : base(owned, nameof(SlangGlobalSession)) { }

    /// <summary>Creates a global session against the loaded <c>slang-compiler</c> library.</summary>
    /// <param name="options">Configuration; null takes the defaults, which include requiring spirv-opt.</param>
    /// <exception cref="SlangException">
    /// The compiler could not be initialised, or a required downstream compiler is missing.
    /// </exception>
    /// <exception cref="DllNotFoundException">The native payload is not next to the executable.</exception>
    public static SlangGlobalSession Create(GlobalSessionOptions? options = null)
    {
        options ??= new GlobalSessionOptions();

        // Before anything else, and deliberately: spGetBuildTagString is a flat `const char*()`
        // with no receiver and no vtable, so it is the one call whose ABI cannot be wrong. Every
        // later call's correctness depends on the answer.
        if (options.RequirePinnedSlangVersion) RequirePinnedVersion();

        // Defaults.GlobalSessionDesc restores the C++ default member initializers ClangSharp drops
        // — notably structureSize, which Slang uses to version the struct. See docs §4.5.
        var desc = Defaults.GlobalSessionDesc();
        desc.enableGLSL = options.EnableGlsl ? (byte)1 : (byte)0;

        IGlobalSession* p = null;
        var hr = SlangApi.slang_createGlobalSession2(&desc, &p);
        if (SlangResult.Failed(hr) || p is null)
            throw new SlangException(hr, "slang_createGlobalSession2", LastInternalError());

        var session = new SlangGlobalSession(p);
        try
        {
            if (options.RequireSpirvOptimizer) session.RequireSpirvOptimizer();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether Slang can load a given downstream compiler right now.
    /// </summary>
    /// <remarks>
    /// Cheap, deterministic, and answerable before anything is compiled — unlike the diagnostic
    /// Slang emits when it first needs one and cannot find it, which arrives mid-compile, on a
    /// successful result, and only once per global session.
    /// </remarks>
    public bool IsDownstreamCompilerAvailable(SlangPassThrough compiler) =>
        SlangResult.Succeeded(Ptr->checkPassThroughSupport(compiler));

    /// <summary>
    /// Throws unless the SPIR-V optimiser is loadable, naming the fix.
    /// </summary>
    /// <exception cref="SlangException">
    /// <c>slang-glslang</c>, which carries spirv-opt, is not beside the compiler.
    /// </exception>
    public void RequireSpirvOptimizer()
    {
        var hr = Ptr->checkPassThroughSupport(SlangPassThrough.SLANG_PASS_THROUGH_SPIRV_OPT);
        if (SlangResult.Succeeded(hr)) return;

        throw new SlangException(hr, "SPIR-V optimiser unavailable", """
            Slang cannot load spirv-opt, which lives in the 'slang-glslang' shared library.

            Every shader compiled above SLANG_OPTIMIZATION_LEVEL_NONE will be emitted unoptimised
            — measurably larger, and Slang reports it only once per global session, on a successful
            result, so the rest of the batch fails silently.

            Stage the library beside the compiler:

                pwsh tools/fetch-native.ps1 -Profile Spirv

            If a minimal deployment without it is deliberate, pass
            new GlobalSessionOptions { RequireSpirvOptimizer = false } to Create.
            """);
    }

    /// <summary>
    /// Resolves a capability name such as <c>spvRayQueryKHR</c> or <c>SPIRV_1_5</c>.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="FindProfile"/>: a profile is a bundle of capabilities, and a
    /// target's diagnostics name the individual ones. Returns
    /// <c>SLANG_CAPABILITY_UNKNOWN</c> for a name Slang does not know, which is how to tell a typo
    /// from a capability this build does not have.
    /// </remarks>
    public SlangCapabilityID FindCapability(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return SlangApi.spFindCapability(Ptr, n);
    }

    /// <summary>Whether this build can generate code for a target at all.</summary>
    /// <remarks>
    /// About the <em>compiler</em>, not the device: a payload built without a backend answers no
    /// here, which is a different failure from a device lacking a feature and worth separating
    /// before a cook blames the wrong thing.
    /// </remarks>
    public bool SupportsTarget(SlangCompileTarget target) =>
        SlangResult.Succeeded(SlangApi.spSessionCheckCompileTargetSupport(Ptr, target));

    /// <summary>
    /// Slang's own hash of a string, as <c>getStringHash</c> computes it in a shader.
    /// </summary>
    /// <remarks>
    /// The other end of <c>ProgramLayout.HashedStrings</c>. A shader calling
    /// <c>getStringHash("albedo")</c> compiles to a constant, and the host holding a name has no
    /// way to match it against that constant without computing the same hash — which is this.
    /// </remarks>
    public static uint HashString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        fixed (byte* p = bytes)
        {
            return SlangApi.spComputeStringHash((sbyte*)p, (nuint)bytes.Length);
        }
    }

    /// <summary>The Slang build this binding is talking to, e.g. <c>2026.14.1</c>.</summary>
    public static string BuildTag => Utf8.Read(SlangApi.spGetBuildTagString()) ?? "unknown";

    /// <summary>
    /// The Slang release these bindings were generated from, e.g. <c>2026.14.1</c> — the pin in
    /// <c>Directory.Build.props</c>, baked in at build time.
    /// </summary>
    public static string PinnedBuildTag { get; } =
        typeof(SlangGlobalSession).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SlangVersion")?.Value?.TrimStart('v')
        // Not reachable from a package this repo built: the metadata comes from $(SlangVersion) in
        // Directory.Build.props, which every project imports. Reachable from a fork that dropped
        // it, and saying so beats degrading to a check that silently passes.
        ?? throw new InvalidOperationException(
            "PrismSlang was built without SlangVersion assembly metadata, so the pinned Slang "
            + "release is unknown and cannot be verified against the loaded library. The csproj "
            + "must emit <AssemblyMetadata Include=\"SlangVersion\" Value=\"$(SlangVersion)\" />.");

    /// <summary>
    /// Throws unless the loaded library is the pinned release.
    /// </summary>
    /// <exception cref="SlangVersionMismatchException">It is some other release.</exception>
    public static void RequirePinnedVersion() => Verify(PinnedBuildTag, BuildTag);

    // Split out so the mismatch path is reachable from a test. Provoking it for real would mean
    // staging a second Slang release beside the first; what can go wrong here is the comparison
    // and the message, and both are testable with two strings.
    internal static void Verify(string pinned, string loaded)
    {
        if (loaded == pinned) return;

        throw new SlangVersionMismatchException(pinned, loaded, $$"""
            PrismSlang was generated against Slang {{pinned}}, but the loaded
            slang-compiler reports {{loaded}}.

            Slang does not promise a stable ABI between releases. These bindings call COM vtables
            by slot ordinal, taken from {{pinned}}'s headers; against a different release those
            ordinals may name different functions, and the result is not a clean failure.

            Stage the pinned payload:

                pwsh tools/fetch-native.ps1 -Profile Spirv

            If runtimes/ holds a payload from an earlier pin, that is the usual cause — the
            directory is gitignored, so bumping the version alone does not replace it.

            If running {{loaded}} is deliberate, pass
            new GlobalSessionOptions { RequirePinnedSlangVersion = false } to Create.
            """);
    }

    /// <summary>
    /// Resolves a profile name such as <c>spirv_1_5</c> or <c>glsl_450</c>.
    /// Returns <see cref="SlangProfileID.SLANG_PROFILE_UNKNOWN"/> if Slang does not recognise it.
    /// </summary>
    public SlangProfileID FindProfile(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using Utf8.Scoped n = new(name);
        return Ptr->findProfile(n);
    }

    /// <summary>
    /// Throws unless dxc is loadable, naming the fix.
    /// </summary>
    /// <remarks>
    /// Slang emits DXIL by handing HLSL to dxc, and its own release ships no dxc: on a machine
    /// without <c>dxcompiler.dll</c> on the library search path a DXIL target fails inside the
    /// first compile with Slang's "failed to load dynamic library" note, on a path that otherwise
    /// looks like a shader error. Asked here instead, before any shader, the way
    /// <see cref="RequireSpirvOptimizer"/> asks about spirv-opt. <c>dxil.dll</c> is not checked:
    /// without it dxc still compiles and emits an unsigned container, which this runtime accepted
    /// in measurement, and which the validator in the package makes signed.
    /// </remarks>
    /// <exception cref="SlangException">dxc is not beside the compiler.</exception>
    public void RequireDxc()
    {
        var hr = Ptr->checkPassThroughSupport(SlangPassThrough.SLANG_PASS_THROUGH_DXC);
        if (SlangResult.Succeeded(hr)) return;

        throw new SlangException(hr, "dxc unavailable", """
            Slang cannot load dxc ('dxcompiler.dll'), which it needs to emit DXIL. Slang's own
            release does not ship it.

            Add the PrismSlang.Native.Dxc package, which puts dxcompiler.dll and dxil.dll beside
            the compiler on win-x64 from Microsoft's own Microsoft.Direct3D.DXC package. In this
            repository the test project already does that; restore it.

            DXIL is only needed for D3D12. A Vulkan-only build never sees this.
            """);
    }

    /// <summary>Creates a session. See <see cref="SessionOptions"/> for what it configures.</summary>
    /// <exception cref="SlangException">
    /// A target asks for DXIL and dxc is not loadable — see <see cref="RequireDxc"/>.
    /// </exception>
    public SlangSession CreateSession(SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Checked per session rather than per global session, because this is where the target
        // is named: a cooker that only ever emits SPIR-V must not be refused for a dxc it never
        // asked for, and one that asks for DXIL should hear about dxc before its first shader.
        if (options.Targets.Any(t => t.Format == SlangCompileTarget.SLANG_DXIL)) RequireDxc();

        return SlangSession.Create(this, options);
    }

    internal IGlobalSession* Handle => Ptr;

    private static string? LastInternalError()
    {
        var msg = Utf8.Read(SlangApi.slang_getLastInternalErrorMessage());
        return string.IsNullOrWhiteSpace(msg) ? null : msg;
    }
}
