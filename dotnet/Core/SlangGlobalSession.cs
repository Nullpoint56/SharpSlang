// L2 — the entry point to the compiler.
//
// One global session per process is the intended usage: it owns the core module and the downstream
// compiler registry, and creating one is expensive. Sessions created from it are the cheap,
// per-configuration objects.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

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
    /// the libraries they built against (AGENTS.md#pin-and-payload), so this is not defensive
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
    /// <exception cref="SlangVersionMismatchException">
    /// The loaded <c>slang-compiler</c> is another Slang release (unless
    /// <see cref="GlobalSessionOptions.RequirePinnedSlangVersion"/> is off), or the loaded
    /// <c>SharpSlang.Descs</c> is another Slang release or package version than the one SharpSlang
    /// depends on.
    /// </exception>
    /// <exception cref="DllNotFoundException">
    /// No <c>slang-compiler</c> could be loaded; the message names the file, the RID, the
    /// directories looked in and where the library comes from.
    /// </exception>
    public static SlangGlobalSession Create(GlobalSessionOptions? options = null) =>
        NativeLibraryHint.Explained(() => CreateCore(options ?? new GlobalSessionOptions()));

    private static SlangGlobalSession CreateCore(GlobalSessionOptions options)
    {
        // Before anything else, and deliberately. The descs: a SharpSlang.Descs of another Slang
        // release or package version makes every enum and struct crossing the boundary another
        // release's, whatever the native library is, so it is refused with or without the opt-out
        // below. Then the native library: spGetBuildTagString is a flat `const char*()` with no
        // receiver and no vtable, so it is the one call whose ABI cannot be wrong. Every later
        // call's correctness depends on the answers.
        RequireDescs();
        if (options.RequirePinnedSlangVersion) RequirePinnedNative();

        // Defaults.GlobalSessionDesc restores the C++ default member initializers ClangSharp drops
        // — notably structureSize, which Slang uses to version the struct. See AGENTS.md#descriptor-defaults-and-result-codes.
        var desc = Defaults.GlobalSessionDesc();
        desc.enableGLSL = options.EnableGlsl ? (byte)1 : (byte)0;

        // Before Slang can look for a companion library by name, which on Windows searches the
        // executable's directory rather than the compiler's: every portable package consumer lost
        // spirv-opt, and `dotnet app.dll` started elsewhere lost dxc too. See CompanionLibraries.
        CompanionLibraries.Preload();

        IGlobalSession* p = null;
        var hr = SlangApi.slang_createGlobalSession2(&desc, &p);
        if (SlangResult.Failed(hr) || p is null)
            throw new SlangException(hr, "slang_createGlobalSession2", Diagnostics.NewInternalError());

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
    /// The other end of <see cref="SlangShaderReflection.GetHashedString"/>. A shader calling
    /// <c>getStringHash("albedo")</c> compiles to a constant, and the host holding a name has no
    /// way to match it against that constant without computing the same hash — which is this.
    /// </remarks>
    public static uint HashString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return NativeLibraryHint.Explained(() =>
        {
            fixed (byte* p = bytes)
            {
                return SlangApi.spComputeStringHash((sbyte*)p, (nuint)bytes.Length);
            }
        });
    }

    /// <summary>The Slang build this binding is talking to, e.g. <c>2026.21</c>.</summary>
    /// <exception cref="DllNotFoundException">No <c>slang-compiler</c> could be loaded; the message says where it was looked for.</exception>
    public static string BuildTag => NativeLibraryHint.Explained(LoadedBuildTag);

    private static string LoadedBuildTag() => Utf8.Read(SlangApi.spGetBuildTagString()) ?? "unknown";

    /// <summary>
    /// The Slang release these bindings were generated from, e.g. <c>2026.21</c> — the pin in
    /// <c>Directory.Build.props</c>, baked in at build time. The same as
    /// <see cref="Slang.BindingSlangVersion"/>.
    /// </summary>
    public static string PinnedBuildTag => SlangVersionCheck.Built;

    /// <summary>
    /// Throws unless what is loaded is what SharpSlang was built against: the native library is
    /// the pinned release, and the loaded <c>SharpSlang.Descs</c> was generated from that release
    /// and is the package version SharpSlang depends on.
    /// </summary>
    /// <exception cref="SlangVersionMismatchException">Something loaded is another version; the message names both and the file.</exception>
    /// <exception cref="DllNotFoundException">No <c>slang-compiler</c> could be loaded.</exception>
    public static void RequirePinnedVersion() => NativeLibraryHint.Explained(() =>
    {
        RequireDescs();
        RequirePinnedNative();
        return 0;
    });

    private static void RequireDescs()
    {
        if (SlangVersionCheck.DescsMismatch(SlangVersionCheck.Descs, SlangVersionCheck.DescsPackage) is { } e) throw e;
    }

    private static void RequirePinnedNative() => Verify(PinnedBuildTag, LoadedBuildTag());

    // Split out so the mismatch path is reachable from a test. Provoking it for real would mean
    // staging a second Slang release beside the first; what can go wrong here is the comparison
    // and the message, and both are testable with two strings.
    internal static void Verify(string pinned, string loaded)
    {
        if (SlangVersionCheck.NativeMismatch(pinned, loaded) is { } e) throw e;
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
    /// <see cref="RequireSpirvOptimizer"/> asks about spirv-opt. <c>dxil.dll</c> is not checked,
    /// because the dxc that <c>SharpSlang.Dxc</c> pins does not use it: dxc 1.9.2609.5
    /// signs with the validator built into <c>dxcompiler.dll</c>. Measured with dxil.dll deleted,
    /// so that no module of that name was in the process: a compute shader came out byte for byte
    /// as it did with dxil.dll present, carrying the digest dxil.dll's own validator writes, and
    /// D3D12 built a pipeline from it under the debug layer and dispatched it correctly.
    /// </remarks>
    /// <exception cref="SlangException">Slang cannot load dxc.</exception>
    public void RequireDxc()
    {
        var hr = Ptr->checkPassThroughSupport(SlangPassThrough.SLANG_PASS_THROUGH_DXC);
        if (SlangResult.Succeeded(hr)) return;

        throw new SlangException(hr, "dxc unavailable", """
            Slang cannot load dxc ('dxcompiler.dll'), which it needs to emit DXIL. Slang's own
            release does not ship it.

            Add the SharpSlang.Dxc package, which puts dxcompiler.dll and dxil.dll in the
            application's output on win-x64 from Microsoft's own Microsoft.Direct3D.DXC package.
            In this repository the test project already does that; restore it.

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

    /// <summary>
    /// <c>parseCommandLineArguments</c> then <c>createSession</c>: a session configured by
    /// <c>slangc</c> options (<c>-target</c>, <c>-profile</c>, <c>-I</c>, <c>-D</c>, <c>-O</c>, ...).
    /// </summary>
    /// <remarks>
    /// Slang parses the options; the binding adds nothing and checks nothing about them beyond
    /// dxc for a DXIL target. The emitted SPIR-V version is not verified, since no
    /// <see cref="TargetOptions.SpirvVersion"/> was given: pass <c>-profile spirv_1_5</c> to pin it.
    /// </remarks>
    /// <param name="arguments">The options, one per element, as a shell would split them.</param>
    /// <exception cref="SlangException">Slang refused the options; its diagnostics are attached.</exception>
    public SlangSession CreateSessionFromCommandLine(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var argv = new nint[Math.Max(arguments.Length, 1)];
        try
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                ArgumentNullException.ThrowIfNull(arguments[i], nameof(arguments));
                argv[i] = (nint)Utf8.Alloc(arguments[i]);
            }

            // structureSize first: Slang refuses a zeroed descriptor as too small.
            var desc = Defaults.SessionDesc();
            ISlangUnknown* aux = null;
            int hr;
            fixed (nint* p = argv)
            {
                hr = Ptr->parseCommandLineArguments(arguments.Length, (sbyte**)p, &desc, &aux);
            }

            try
            {
                Diagnostics.ThrowIfFailed(hr, "IGlobalSession::parseCommandLineArguments", null);

                // Kept so a linked program reads the same target list as from SessionOptions.
                var targets = new TargetOptions[desc.targetCount];
                for (var i = 0; i < targets.Length; i++)
                {
                    targets[i] = new TargetOptions { Format = desc.targets[i].format, VerifyEmittedSpirv = false };
                }

                if (targets.Any(t => t.Format == SlangCompileTarget.SLANG_DXIL)) RequireDxc();

                ISession* session = null;
                hr = Ptr->createSession(&desc, &session);
                if (SlangResult.Failed(hr) || session is null)
                    throw new SlangException(hr, "IGlobalSession::createSession", Diagnostics.NewInternalError());

                return SlangSession.Adopt(session, this, targets);
            }
            finally
            {
                // The descriptor's arrays live in this allocation; Slang copied what it needs.
                if (aux is not null) aux->release();
            }
        }
        finally
        {
            foreach (var p in argv) Utf8.Free((sbyte*)p);
        }
    }

    /// <summary>
    /// <c>getSessionDescDigest</c>: Slang's digest of the session <paramref name="options"/>
    /// describe. Equal options give equal digests, so it is the options half of a cache key.
    /// </summary>
    /// <exception cref="SlangException">Slang could not compute it.</exception>
    public byte[] GetSessionDescDigest(SessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return SlangSession.WithSessionDesc(this, options, desc =>
        {
            ISlangBlob* blob = null;
            var hr = Ptr->getSessionDescDigest(desc, &blob);
            if (SlangResult.Failed(hr) || blob is null)
            {
                if (blob is not null) blob->release();
                throw new SlangException(hr, "IGlobalSession::getSessionDescDigest", Diagnostics.NewInternalError());
            }

            using var owned = new SlangBlob(blob);
            return owned.ToArray();
        });
    }

    /// <summary>
    /// <c>getCompilerElapsedTime</c>: time this global session has spent compiling, in total and in
    /// downstream compilers (dxc, spirv-opt, ...).
    /// </summary>
    public (TimeSpan Total, TimeSpan Downstream) CompilerElapsedTime
    {
        get
        {
            double total, downstream;
            Ptr->getCompilerElapsedTime(&total, &downstream);
            return (TimeSpan.FromSeconds(total), TimeSpan.FromSeconds(downstream));
        }
    }

    /// <summary>
    /// <c>setDownstreamCompilerPath</c>: the directory Slang loads a downstream compiler from,
    /// instead of searching for it by name. Set it before the first compile that needs that compiler.
    /// </summary>
    /// <param name="compiler">The downstream compiler.</param>
    /// <param name="directory">Its directory; null or empty restores the search by name.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compiler"/> names no compiler.</exception>
    public void SetDownstreamCompilerPath(SlangPassThrough compiler, string? directory)
    {
        // Slang indexes a per-compiler array with it, checked only by an assert.
        RequireCompiler(compiler);
        using Utf8.Scoped p = new(directory ?? "");
        Ptr->setDownstreamCompilerPath(compiler, p);
    }

    /// <summary>
    /// <c>getDownstreamCompilerPath</c>: the file of the downstream compiler Slang will use, or
    /// null when it cannot load one.
    /// </summary>
    /// <remarks>
    /// Slang answers by loading the compiler, from the directory <see cref="SetDownstreamCompilerPath"/>
    /// set or by its usual search, so this names the library compiles will use, not the setting.
    /// </remarks>
    public string? GetDownstreamCompilerPath(SlangPassThrough compiler)
    {
        ISlangBlob* blob = null;
        var hr = Ptr->getDownstreamCompilerPath(compiler, &blob);
        if (blob is null) return null;

        using var owned = new SlangBlob(blob);
        return SlangResult.Failed(hr) ? null : NullIfEmpty(owned.AsText());
    }

    /// <summary>
    /// <c>setLanguagePrelude</c>: text Slang puts before the code it generates in
    /// <paramref name="language"/> (C++, CUDA, HLSL, ...), replacing Slang's own prelude.
    /// </summary>
    public void SetLanguagePrelude(SlangSourceLanguage language, string prelude)
    {
        ArgumentNullException.ThrowIfNull(prelude);
        using Utf8.Scoped p = new(prelude);
        Ptr->setLanguagePrelude(language, p);
    }

    /// <summary><c>getLanguagePrelude</c>: the prelude for <paramref name="language"/>, or null for none.</summary>
    public string? GetLanguagePrelude(SlangSourceLanguage language)
    {
        ISlangBlob* blob = null;
        Ptr->getLanguagePrelude(language, &blob);
        if (blob is null) return null;

        using var owned = new SlangBlob(blob);
        return NullIfEmpty(owned.AsText());
    }

    /// <summary>
    /// <c>setDefaultDownstreamCompiler</c>: which downstream compiler Slang hands code in
    /// <paramref name="language"/> to.
    /// </summary>
    /// <exception cref="SlangException">Slang refused the pairing.</exception>
    public void SetDefaultDownstreamCompiler(SlangSourceLanguage language, SlangPassThrough compiler) =>
        Diagnostics.ThrowIfFailed(
            Ptr->setDefaultDownstreamCompiler(language, compiler),
            $"IGlobalSession::setDefaultDownstreamCompiler({language}, {compiler})", null);

    /// <summary><c>getDefaultDownstreamCompiler</c>.</summary>
    public SlangPassThrough GetDefaultDownstreamCompiler(SlangSourceLanguage language) =>
        Ptr->getDefaultDownstreamCompiler(language);

    /// <summary>
    /// <c>setDownstreamCompilerForTransition</c>: which downstream compiler turns
    /// <paramref name="source"/> code into <paramref name="target"/> code (HLSL to DXIL, say).
    /// </summary>
    public void SetDownstreamCompilerForTransition(
        SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler) =>
        Ptr->setDownstreamCompilerForTransition(source, target, compiler);

    /// <summary><c>getDownstreamCompilerForTransition</c>: the compiler Slang will use for it.</summary>
    public SlangPassThrough GetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target) =>
        Ptr->getDownstreamCompilerForTransition(source, target);

    /// <summary>
    /// <c>setSPIRVCoreGrammar</c>: a <c>spirv.core.grammar.json</c> Slang reads when it parses
    /// and checks SPIR-V written inline in a shader, in place of the grammar built into it.
    /// </summary>
    /// <exception cref="SlangException">Slang could not load the file.</exception>
    public void SetSpirvCoreGrammar(string jsonPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(jsonPath);
        using Utf8.Scoped p = new(jsonPath);
        Diagnostics.ThrowIfFailed(Ptr->setSPIRVCoreGrammar(p), $"IGlobalSession::setSPIRVCoreGrammar('{jsonPath}')", null);
    }

    private Hosting.SlangSharedLibraryLoader? _loader;

    /// <summary>
    /// <c>setSharedLibraryLoader</c>: Slang loads every library it loads by name (spirv-opt's
    /// <c>slang-glslang</c>, dxc, ...) through <paramref name="loader"/>; null restores Slang's own
    /// loader. Set it before the first compile that needs one: a library already loaded stays.
    /// </summary>
    /// <remarks>
    /// Create the global session with <see cref="GlobalSessionOptions.RequireSpirvOptimizer"/> off
    /// when the loader is to provide spirv-opt, since that check loads it during creation.
    /// </remarks>
    public void SetSharedLibraryLoader(Hosting.SlangSharedLibraryLoader? loader)
    {
        Ptr->setSharedLibraryLoader(loader is null ? null : loader.Handle);
        _loader = loader;
    }

    /// <summary>
    /// <c>getSharedLibraryLoader</c>: the loader <see cref="SetSharedLibraryLoader"/> set, or null
    /// for Slang's own.
    /// </summary>
    /// <exception cref="InvalidOperationException">Slang holds a loader this wrapper did not set.</exception>
    public Hosting.SlangSharedLibraryLoader? SharedLibraryLoader
    {
        get
        {
            // Borrowed: Session::getSharedLibraryLoader returns its pointer without a reference.
            var current = Ptr->getSharedLibraryLoader();
            if (current is null) return null;
            if (_loader is not null && current == _loader.Handle) return _loader;
            throw new InvalidOperationException("Slang holds a shared library loader set outside this wrapper");
        }
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static void RequireCompiler(SlangPassThrough compiler)
    {
        if (compiler <= SlangPassThrough.SLANG_PASS_THROUGH_NONE || compiler >= SlangPassThrough.SLANG_PASS_THROUGH_COUNT_OF)
            throw new ArgumentOutOfRangeException(nameof(compiler), compiler, "not a downstream compiler");
    }

    internal IGlobalSession* Handle => Ptr;
}
