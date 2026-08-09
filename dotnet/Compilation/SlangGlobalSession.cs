// L2 — the entry point to the compiler.
//
// One global session per process is the intended usage: it owns the core module and the downstream
// compiler registry, and creating one is expensive. Sessions created from it are the cheap,
// per-configuration objects.

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

    /// <summary>The Slang build this binding is talking to, e.g. <c>2026.14.1</c>.</summary>
    public static string BuildTag => Utf8.Read(SlangApi.spGetBuildTagString()) ?? "unknown";

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

    /// <summary>Creates a session. See <see cref="SessionOptions"/> for what it configures.</summary>
    public SlangSession CreateSession(SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SlangSession.Create(this, options);
    }

    internal IGlobalSession* Handle => Ptr;

    private static string? LastInternalError()
    {
        var msg = Utf8.Read(SlangApi.slang_getLastInternalErrorMessage());
        return string.IsNullOrWhiteSpace(msg) ? null : msg;
    }
}
