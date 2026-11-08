// L2 — a configured compilation context.
//
// The delicate part is Create: SessionDesc points at three separately-allocated native arrays
// (targets, search paths, macros) that must stay alive across exactly one createSession call and
// be freed afterwards regardless of outcome. Slang copies what it needs, so nothing here needs to
// outlive the call — but getting that wrong either dangles or leaks, and neither is visible in a
// test that passes.

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>A compilation context: targets, search paths, macros, and the modules loaded into it.</summary>
public sealed unsafe class SlangSession : SlangObject<ISession>
{
    // Owner references keep Slang's hierarchy alive in the GC's eyes: a session cannot be
    // collected while a module from it lives, and so on down. See SlangObject for why this
    // replaces a finalizer rather than supplementing one.
    private readonly Hosting.SlangFileSystem? _fileSystem;

    private SlangSession(
        ISession* owned, SlangGlobalSession owner, IReadOnlyList<TargetOptions> targets, Hosting.SlangFileSystem? fileSystem)
        : base(owned, nameof(SlangSession), owner)
    {
        Targets = targets;
        _fileSystem = fileSystem;
        fileSystem?.SessionCreated();
    }

    // Slang holds the file system for the session's lifetime, so it refuses Dispose until then.
    private protected override void OnReleased() => _fileSystem?.SessionReleased();

    /// <summary>
    /// The targets this session was created with, in order. Kept so a linked program can check what
    /// Slang emitted against what was asked for.
    /// </summary>
    internal IReadOnlyList<TargetOptions> Targets { get; }

    internal static SlangSession Create(SlangGlobalSession global, SessionOptions options)
    {
        if (options.Targets.Count == 0)
            throw new ArgumentException("a session needs at least one target", nameof(options));

        foreach (var t in options.Targets) t.Validate(nameof(options));

        // Everything allocated here is freed in the finally. Slang deep-copies the descriptor.
        var targets = (TargetDesc*)NativeMemory.AllocZeroed(
            (nuint)options.Targets.Count, (nuint)sizeof(TargetDesc));
        var searchPaths = stackalloc sbyte*[Math.Max(1, options.SearchPaths.Count)];
        var macros = (PreprocessorMacroDesc*)NativeMemory.AllocZeroed(
            (nuint)Math.Max(1, options.PreprocessorMacros.Count), (nuint)sizeof(PreprocessorMacroDesc));

        // One CompilerOptionEntry per target that needs one. Allocated as a single block so the
        // cleanup is a single free; each target points at its own slot.
        var targetOptions = (CompilerOptionEntry*)NativeMemory.AllocZeroed(
            (nuint)options.Targets.Count, (nuint)sizeof(CompilerOptionEntry));

        var owned = new List<nint>();
        sbyte* Own(string? s)
        {
            var p = Utf8.Alloc(s);
            if (p is not null) owned.Add((nint)p);
            return p;
        }

        try
        {
            for (var i = 0; i < options.Targets.Count; i++)
            {
                var t = options.Targets[i];

                // Start from Defaults, never `default` — structureSize and the direct-SPIR-V flag
                // both live in C++ initializers that the generated struct does not carry.
                var desc = Defaults.TargetDesc();
                desc.format = t.Format;
                desc.floatingPointMode = t.FloatingPointMode;
                desc.forceGLSLScalarBufferLayout = t.ForceGlslScalarBufferLayout ? (byte)1 : (byte)0;

                if (!t.EmitSpirvDirectly)
                    desc.flags &= ~(uint)SlangApi.SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY;

                if (t.ParameterBlocksUseRegisterSpaces)
                    desc.flags |= (uint)SlangApi.SLANG_TARGET_FLAG_PARAMETER_BLOCKS_USE_REGISTER_SPACES;

                var profileName = t.ResolveProfile();
                if (!string.IsNullOrEmpty(profileName))
                {
                    desc.profile = global.FindProfile(profileName);
                    if (desc.profile == SlangProfileID.SLANG_PROFILE_UNKNOWN)
                        throw new ArgumentException($"unknown Slang profile '{profileName}'", nameof(options));
                }

                // VulkanUseEntryPointName is honoured here and on the session, and nowhere else:
                // passing it to linkWithOptions is a measured no-op. See TargetOptions.
                if (t.UseEntryPointNameInSpirv)
                {
                    targetOptions[i].name = CompilerOptionName.VulkanUseEntryPointName;
                    targetOptions[i].value = Defaults.CompilerOptionValue();
                    targetOptions[i].value.kind = CompilerOptionValueKind.Int;
                    targetOptions[i].value.intValue0 = 1;

                    desc.compilerOptionEntries = &targetOptions[i];
                    desc.compilerOptionEntryCount = 1;
                }

                targets[i] = desc;
            }

            for (var i = 0; i < options.SearchPaths.Count; i++)
                searchPaths[i] = Own(options.SearchPaths[i]);

            var m = 0;
            foreach (var (name, value) in options.PreprocessorMacros)
            {
                macros[m].name = Own(name);
                macros[m].value = Own(value);
                m++;
            }

            var sessionDesc = Defaults.SessionDesc();
            sessionDesc.targets = targets;
            sessionDesc.targetCount = options.Targets.Count;
            sessionDesc.defaultMatrixLayoutMode = options.MatrixLayout;
            sessionDesc.searchPaths = options.SearchPaths.Count > 0 ? searchPaths : null;
            sessionDesc.searchPathCount = options.SearchPaths.Count;
            sessionDesc.preprocessorMacros = options.PreprocessorMacros.Count > 0 ? macros : null;
            sessionDesc.preprocessorMacroCount = options.PreprocessorMacros.Count;
            // Written out rather than `options.FileSystem?.Handle`: Roslyn accepts lifting a
            // conditional access to a pointer type, but ReSharper flags it and the explicit form
            // is clearer about "null means use the OS filesystem" anyway.
            sessionDesc.fileSystem = options.FileSystem is { } vfs ? vfs.Handle : null;
            sessionDesc.allowGLSLSyntax = options.AllowGlslSyntax ? (byte)1 : (byte)0;
            sessionDesc.skipSPIRVValidation = options.SkipSpirvValidation ? (byte)1 : (byte)0;

            ISession* p = null;
            var hr = global.Handle->createSession(&sessionDesc, &p);
            if (SlangResult.Failed(hr) || p is null)
                throw new SlangException(hr, "IGlobalSession::createSession", null);

            return new SlangSession(p, global, [.. options.Targets], options.FileSystem);
        }
        finally
        {
            foreach (var p in owned) Utf8.Free((sbyte*)p);
            NativeMemory.Free(targets);
            NativeMemory.Free(macros);
            NativeMemory.Free(targetOptions);
        }
    }

    /// <summary>Compiles a module from source text held in memory.</summary>
    /// <param name="moduleName">Logical module name, as other modules would <c>import</c> it.</param>
    /// <param name="path">Path reported in diagnostics. Need not exist on disk.</param>
    /// <param name="source">Slang source.</param>
    /// <exception cref="SlangException">Compilation failed; the message carries Slang's diagnostics.</exception>
    public SlangModule LoadModuleFromSource(string moduleName, string path, string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentNullException.ThrowIfNull(source);

        ISlangBlob* diagnostics = null;
        IModule* module;
        using (Utf8.Scoped n = new(moduleName), p = new(path), s = new(source))
        {
            module = Ptr->loadModuleFromSourceString(n, p, s, &diagnostics);
        }

        Diagnostics.ThrowIfNull(module, $"load module '{moduleName}'", diagnostics);
        return new SlangModule(module, moduleName, this);
    }

    /// <summary>Loads a module by name, resolved through the session's search paths.</summary>
    public SlangModule LoadModule(string moduleName)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);

        ISlangBlob* diagnostics = null;
        IModule* module;
        using (Utf8.Scoped n = new(moduleName))
        {
            module = Ptr->loadModule(n, &diagnostics);
        }

        Diagnostics.ThrowIfNull(module, $"load module '{moduleName}'", diagnostics);
        return new SlangModule(module, moduleName, this);
    }

    /// <summary>
    /// Loads a module from the binary IR <c>SlangModule.Serialize</c> produced, rather than from
    /// source.
    /// </summary>
    /// <remarks>
    /// The read side of a precompiled-module cache. Slang does not re-parse or re-check the
    /// module; a cook that serializes its shared modules once pays for parsing them once, not once
    /// per shader that imports them.
    ///
    /// The blob carries the compiler version that wrote it and Slang rejects one from a different
    /// build. <see cref="ReadModuleInfo"/> reads that out without loading, and
    /// <see cref="IsBinaryModuleUpToDate"/> answers the whole question in one call.
    /// </remarks>
    /// <param name="moduleName">Name to load it under, as other modules would <c>import</c> it.</param>
    /// <param name="path">Path reported in diagnostics. Need not exist on disk.</param>
    /// <param name="ir">Bytes from <c>SlangModule.Serialize</c>.</param>
    /// <exception cref="SlangException">The blob is not loadable — wrong compiler version, or not IR.</exception>
    public SlangModule LoadModuleFromIRBlob(string moduleName, string path, ReadOnlySpan<byte> ir)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        if (ir.IsEmpty) throw new ArgumentException("no IR bytes", nameof(ir));

        ISlangBlob* diagnostics = null;
        IModule* module;

        fixed (byte* p = ir)
        {
            using var blob = Blob(p, ir.Length);
            using Utf8.Scoped n = new(moduleName), f = new(path);
            module = Ptr->loadModuleFromIRBlob(n, f, blob.Handle, &diagnostics);
        }

        Diagnostics.ThrowIfNull(module, $"load module '{moduleName}' from IR", diagnostics);
        return new SlangModule(module, moduleName, this);
    }

    /// <summary>
    /// What a serialized module says about itself, without loading it.
    /// </summary>
    /// <remarks>
    /// Cheap enough to run over a whole cache directory at startup. The compiler version is the
    /// field that matters: an IR blob is only loadable by the build that wrote it, so a cache
    /// entry from a previous Slang release is not stale, it is unusable.
    /// </remarks>
    /// <returns>The module's own name, its version, and the compiler build that serialized it.</returns>
    /// <exception cref="SlangException">The blob is not a serialized Slang module.</exception>
    public (string? Name, long Version, string? CompilerVersion) ReadModuleInfo(ReadOnlySpan<byte> ir)
    {
        if (ir.IsEmpty) throw new ArgumentException("no IR bytes", nameof(ir));

        long version;
        sbyte* compilerVersion;
        sbyte* name;

        fixed (byte* p = ir)
        {
            using var blob = Blob(p, ir.Length);
            var hr = Ptr->loadModuleInfoFromIRBlob(blob.Handle, &version, &compilerVersion, &name);
            Diagnostics.ThrowIfFailed(hr, "ISession::loadModuleInfoFromIRBlob", null);
        }

        return (Utf8.Read(name), version, Utf8.Read(compilerVersion));
    }

    /// <summary>
    /// Whether a serialized module is still current for the source at <paramref name="modulePath"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slang's own staleness check for an IR cache entry: about a cached <em>module</em>, not a
    /// compiled shader.
    /// </para>
    /// <para>
    /// <strong>Ask it from a session that has not loaded the module.</strong> It is answered
    /// against the session's own view of the file system, and a session populates that view when
    /// it loads a file. So the natural sequence — load, serialize, ask — returns true
    /// unconditionally, and keeps returning true after the source has changed on disk; it will
    /// even return true for a path that has no file behind it at all. A fresh session reads disk
    /// and answers correctly, which is what a cook has at startup and the only place this call is
    /// worth making. Measured, not inferred: see
    /// <c>The_staleness_check_answers_from_the_sessions_file_cache_not_from_disk</c>.
    /// </para>
    /// <para>
    /// A malformed blob is rejected either way — that judgement is about the bytes rather than the
    /// file.
    /// </para>
    /// </remarks>
    public bool IsBinaryModuleUpToDate(string modulePath, ReadOnlySpan<byte> ir)
    {
        ArgumentException.ThrowIfNullOrEmpty(modulePath);
        if (ir.IsEmpty) return false;

        fixed (byte* p = ir)
        {
            using var blob = Blob(p, ir.Length);
            using Utf8.Scoped m = new(modulePath);
            return Ptr->isBinaryModuleUpToDate(m, blob.Handle);
        }
    }

    /// <summary>
    /// Wraps managed bytes in an <c>ISlangBlob</c> Slang can read.
    /// </summary>
    /// <remarks>
    /// <c>slang_createBlob</c> copies, so the caller's pinning only has to last the call. Owned,
    /// so it must be released — hence the wrapper rather than a raw pointer.
    /// </remarks>
    private static SlangBlob Blob(byte* data, int length)
    {
        var blob = SlangApi.slang_createBlob(data, (nuint)length);
        Diagnostics.ThrowIfNull(blob, "slang_createBlob", null);
        return new SlangBlob(blob);
    }

    /// <summary>
    /// <c>ISession::createCompositeComponentType</c>: modules, entry points, conformances and
    /// component types composed into one, in the order given, which is the order their parameters
    /// are laid out in. Not yet linked: <see cref="SlangComponentType.Link"/> is the next step.
    /// </summary>
    /// <param name="components">The binding's component-type wrappers: <see cref="SlangModule"/>,
    /// <see cref="SlangEntryPoint"/>, <see cref="TypeConformance"/>, <see cref="SlangComponentType"/>
    /// or <see cref="LinkedProgram"/>.</param>
    /// <exception cref="ArgumentException">No components, or one that is not the binding's.</exception>
    /// <exception cref="SlangException">Slang refused the composition; its diagnostics are attached.</exception>
    public SlangComponentType CreateCompositeComponentType(params ReadOnlySpan<ISlangComponentType> components)
    {
        if (components.Length == 0)
            throw new ArgumentException("a composite needs at least one component", nameof(components));

        var pointers = new nint[components.Length];
        var names = new List<string>();
        for (var i = 0; i < components.Length; i++)
        {
            pointers[i] = components[i] switch
            {
                null => throw new ArgumentNullException(nameof(components), $"component {i} is null"),
                SlangModule m => (nint)m.AsComponent,
                SlangEntryPoint e => (nint)e.AsComponent,
                TypeConformance t => (nint)t.AsComponent,
                SlangComponentType c => (nint)c.AsComponent,
                LinkedProgram l => (nint)l.AsComponent,
                var other => throw new ArgumentException(
                    $"component {i} is a {other.GetType().Name}, not one of the binding's component types", nameof(components)),
            };

            switch (components[i])
            {
                case SlangEntryPoint e: names.Add(e.Name); break;
                case SlangComponentType c: names.AddRange(c.EntryPointNames); break;
                case LinkedProgram l: names.AddRange(l.EntryPointNames); break;
            }
        }

        IComponentType* composed = null;
        ISlangBlob* diagnostics = null;
        int hr;
        fixed (nint* p = pointers)
        {
            hr = Ptr->createCompositeComponentType((IComponentType**)p, pointers.Length, &composed, &diagnostics);
        }

        Diagnostics.ThrowIfFailed(hr, "createCompositeComponentType", diagnostics);
        Diagnostics.ThrowIfNull(composed, "createCompositeComponentType", null);
        return new SlangComponentType(composed, this, "SlangComponentType(composite)", pointers, [.. names]);
    }

    /// <summary>
    /// Composes a module with entry points into one program and links it:
    /// <see cref="CreateCompositeComponentType"/> then <see cref="SlangComponentType.Link"/>.
    /// </summary>
    public LinkedProgram Link(SlangModule module, params ReadOnlySpan<SlangEntryPoint> entryPoints) =>
        Link(module, entryPoints, options: null);

    /// <summary>
    /// Composes, optionally specializes, and links: <see cref="CreateCompositeComponentType"/>,
    /// <see cref="SlangComponentType.Specialize"/> when <see cref="LinkOptions.SpecializationArguments"/>
    /// has any, then <see cref="SlangComponentType.Link"/> or <see cref="SlangComponentType.LinkWithOptions"/>.
    /// </summary>
    /// <remarks>
    /// A composite of the three calls, each reachable on its own, and nothing else: the
    /// intermediates are disposed before this returns. The order is Slang's: everything the
    /// program needs (module, entry points, conformances) is composed first, because the
    /// generic parameters to specialize are the composite's and a conformance has to be in it to
    /// be dispatched to; specialization substitutes the concrete types; linking resolves what is
    /// left. Specializing after linking is not the same operation and does not resolve generics.
    /// </remarks>
    public LinkedProgram Link(
        SlangModule module,
        ReadOnlySpan<SlangEntryPoint> entryPoints,
        LinkOptions? options)
    {
        ArgumentNullException.ThrowIfNull(module);

        var conformances = options?.TypeConformances ?? [];
        var components = new ISlangComponentType[1 + entryPoints.Length + conformances.Count];
        var c = 0;
        components[c++] = module;
        foreach (var e in entryPoints) components[c++] = e;
        foreach (var t in conformances) components[c++] = t;

        using var composite = CreateCompositeComponentType(components);

        var arguments = options?.SpecializationArguments ?? [];
        if (arguments.Count == 0) return LinkOne(composite, options?.CompilerOptions);

        using var specialized = composite.Specialize([.. arguments]);
        return LinkOne(specialized, options?.CompilerOptions);

        static LinkedProgram LinkOne(SlangComponentType component, LinkTimeOptions? compilerOptions) =>
            compilerOptions is null || compilerOptions.IsEmpty ? component.Link() : component.LinkWithOptions(compilerOptions);
    }

    /// <summary>
    /// Registers that <paramref name="type"/> implements <paramref name="interfaceType"/>, so a
    /// program linking this conformance can dispatch to it dynamically.
    /// </summary>
    /// <param name="type">The concrete type.</param>
    /// <param name="interfaceType">The interface it implements.</param>
    /// <param name="conformanceId">
    /// Stable id written into the shader's dynamic-dispatch table. Supply one if the host needs to
    /// select an implementation by id at run time; omit to let Slang assign it.
    /// </param>
    public TypeConformance CreateTypeConformance(
        SlangType type,
        SlangType interfaceType,
        int? conformanceId = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(interfaceType);

        ITypeConformance* conformance = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->createTypeConformanceComponentType(
            type.Handle, interfaceType.Handle, &conformance, conformanceId ?? -1, &diagnostics);

        Diagnostics.ThrowIfFailed(
            hr, $"createTypeConformance({type.Name} : {interfaceType.Name})", diagnostics);

        if (conformance is null)
            throw new SlangException(SlangResult.Fail,
                $"createTypeConformance({type.Name} : {interfaceType.Name}) produced nothing", null);

        return new TypeConformance(conformance, this, $"TypeConformance({type.Name}:{interfaceType.Name})");
    }
}
