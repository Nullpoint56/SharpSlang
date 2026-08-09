// L2 — the entry point to the compiler.
//
// One global session per process is the intended usage: it owns the core module and the downstream
// compiler registry, and creating one is expensive. Sessions created from it are the cheap,
// per-configuration objects.

using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang;

/// <summary>
/// A Slang compiler instance. Create one per process and derive <see cref="SlangSession"/>s from it.
/// </summary>
public sealed unsafe class SlangGlobalSession : SlangObject<IGlobalSession>
{
    private SlangGlobalSession(IGlobalSession* owned) : base(owned, nameof(SlangGlobalSession)) { }

    /// <summary>Creates a global session against the loaded <c>slang-compiler</c> library.</summary>
    /// <param name="enableGlsl">Allow GLSL syntax as an input language. Off by default, as in Slang.</param>
    /// <exception cref="SlangException">The compiler could not be initialised.</exception>
    /// <exception cref="DllNotFoundException">The native payload is not next to the executable.</exception>
    public static SlangGlobalSession Create(bool enableGlsl = false)
    {
        // Defaults.GlobalSessionDesc restores the C++ default member initializers ClangSharp drops
        // — notably structureSize, which Slang uses to version the struct. See docs §4.5.
        var desc = Defaults.GlobalSessionDesc();
        desc.enableGLSL = enableGlsl ? (byte)1 : (byte)0;

        IGlobalSession* p = null;
        var hr = SlangApi.slang_createGlobalSession2(&desc, &p);
        if (SlangResult.Failed(hr) || p is null)
            throw new SlangException(hr, "slang_createGlobalSession2", LastInternalError());

        return new SlangGlobalSession(p);
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
