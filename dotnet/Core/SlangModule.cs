// L2 — a compiled module and its entry points.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>A compiled Slang module.</summary>
public sealed unsafe class SlangModule : SlangObject<IModule>
{
    /// <param name="borrowed">
    /// A module pointer from <c>ISession::loadModule*</c>. Note: <em>borrowed</em>, not owned.
    /// </param>
    /// <param name="name">The module's name, for the wrapper's description in messages.</param>
    /// <param name="owner">The session that owns the module.</param>
    /// <remarks>
    /// This is the one place Slang departs from COM convention, and it is documented nowhere in
    /// slang.h. <c>loadModule</c> and <c>loadModuleFromSourceString</c> return a pointer the
    /// session continues to own — the session keeps every module in a list, which is what
    /// <c>getLoadedModuleCount</c> / <c>getLoadedModule</c> enumerate. Every other object handed
    /// back through an out-parameter carries a reference for the caller; this one does not.
    ///
    /// Releasing it therefore over-releases. Found on linux-x64, where glibc aborts the process
    /// with `free(): corrupted unsorted chunks`; the Windows heap silently tolerated the same
    /// corruption, so the bug was invisible for the whole of this repository's history until the
    /// cross-platform run. Bisected by skipping disposal one object at a time.
    ///
    /// The fix is to take our own reference on adoption, so the wrapper's release balances it and
    /// every SlangObject behaves uniformly. If a future Slang release switches to returning an
    /// owned reference, this leaks a module rather than corrupting the heap — the right way round.
    /// </remarks>
    internal SlangModule(IModule* borrowed, string name, SlangSession owner)
        : base(borrowed, $"SlangModule('{name}')", owner)
    {
        AddReference();
        Name = name;
    }

    /// <summary>The module name it was loaded under.</summary>
    public string Name { get; }

    // Name is assigned in the constructor body rather than an initializer because AddReference()
    // must run first.

    /// <summary>Number of entry points declared with a <c>[shader("...")]</c> attribute.</summary>
    public int EntryPointCount => Ptr->getDefinedEntryPointCount();

    /// <summary>The path this module was loaded from, or null for one compiled from a string.</summary>
    public string? FilePath => Utf8.Read(Ptr->getFilePath());

    /// <summary>
    /// The name Slang knows this module by, which is not necessarily the one it was loaded under.
    /// </summary>
    /// <remarks>
    /// <see cref="Name"/> is what the caller passed; this is what the compiler recorded. Measured
    /// on 2026.21 they are the same even when the file is called something else — a module loaded
    /// from <c>mslib.slang</c> under the name <c>loadedAs</c> reports <c>loadedAs</c>. Bound
    /// anyway, because it is the compiler's answer rather than ours, and an <c>import</c> in
    /// another module has to match this one.
    /// </remarks>
    public string? DeclaredName => Utf8.Read(Ptr->getName());

    /// <summary>
    /// Slang's identity for this module's source, used to decide whether two loads are the same
    /// module.
    /// </summary>
    /// <remarks>
    /// Normally the resolved absolute path. Not a content hash — it does not change when the file
    /// does, so it answers "is this the same module" and never "is this module current". For the
    /// latter hash the files <see cref="DependencyFilePaths"/> names, which is the whole import graph.
    /// </remarks>
    public string? UniqueIdentity => Utf8.Read(Ptr->getUniqueIdentity());

    /// <summary>
    /// Serializes this module to Slang's binary IR, for a precompiled-module cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The point is compile-once: a module parsed and checked at cook time can be reloaded from IR
    /// by <c>SlangSession.LoadModuleFromIRBlob</c> without re-parsing its source or its imports.
    /// For a shader library where twenty entry points import the same six modules, that is the
    /// difference between six parses and a hundred and twenty.
    /// </para>
    /// <para>
    /// The blob is Slang IR, not SPIR-V, and it is tied to the compiler version that produced it —
    /// <c>SlangSession.ReadModuleInfo</c> reads that back out of a blob before trying to load it.
    /// </para>
    /// </remarks>
    /// <returns>An owned blob; dispose it.</returns>
    public SlangBlob Serialize()
    {
        ISlangBlob* blob = null;
        var hr = Ptr->serialize(&blob);
        Diagnostics.ThrowIfFailed(hr, $"IModule::serialize('{Name}')", null);
        Diagnostics.ThrowIfNull(blob, $"IModule::serialize('{Name}')", null);

        return new SlangBlob(blob, this);
    }

    /// <summary>Serializes this module straight to a file.</summary>
    /// <param name="path">Destination. Slang overwrites it.</param>
    public void WriteToFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using Utf8.Scoped p = new(path);
        Diagnostics.ThrowIfFailed(Ptr->writeToFile(p), $"IModule::writeToFile('{path}')", null);
    }

    /// <summary>
    /// Every file this module was compiled from — itself, everything it <c>import</c>s, and
    /// everything <c>#include</c>d, transitively.
    /// </summary>
    /// <remarks>
    /// This is what makes a staleness check honest. Hashing only the file a shader was named by
    /// leaves a bake claiming to be current after the module it imports has changed underneath it,
    /// and the symptom is a shader that behaves like the version before last. Verified against
    /// slang-compiler 2026.21: a module that imports one <c>.slang</c> and includes one
    /// <c>.hlsli</c> reports all three paths.
    ///
    /// Paths are as Slang resolved them, so they are absolute for anything found through a search
    /// path. A module compiled from a string reports the synthetic path it was given, which will
    /// not exist on disk.
    /// </remarks>
    public IReadOnlyList<string> DependencyFilePaths
    {
        get
        {
            var count = Ptr->getDependencyFileCount();
            if (count <= 0) return [];

            var paths = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var path = Utf8.Read(Ptr->getDependencyFilePath(i));
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            return paths;
        }
    }

    /// <summary>Resolves an entry point by name.</summary>
    /// <exception cref="SlangException">No such entry point in this module.</exception>
    public SlangEntryPoint FindEntryPoint(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        IEntryPoint* ep = null;
        int hr;
        using (Utf8.Scoped n = new(name))
        {
            hr = Ptr->findEntryPointByName(n, &ep);
        }

        if (SlangResult.Failed(hr) || ep is null)
            throw new SlangException(hr, $"findEntryPointByName('{name}') in module '{Name}'", null);

        return new SlangEntryPoint(ep, name, this);
    }

    /// <summary>Resolves an entry point by name and stage, for a function with no attribute.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="FindEntryPoint(string)"/> only sees functions carrying <c>[shader("...")]</c>.
    /// A plain <c>float4 main() : SV_Target</c> is invisible to it, and to
    /// <see cref="EntryPointCount"/>, because neither the name nor the semantic says which stage
    /// the function is for. This overload supplies the stage the attribute would have carried,
    /// which is what <c>slangc -stage fragment -entry main</c> does.
    /// </para>
    /// <para>
    /// Slang type-checks the function against the stage here rather than at link time, so a
    /// mismatch is reported with diagnostics naming the shader.
    /// </para>
    /// </remarks>
    /// <exception cref="SlangException">No such function, or it is not valid for that stage.</exception>
    public SlangEntryPoint FindEntryPoint(string name, SlangStage stage)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        IEntryPoint* ep = null;
        ISlangBlob* diagnostics = null;
        int hr;
        using (Utf8.Scoped n = new(name))
        {
            hr = Ptr->findAndCheckEntryPoint(n, stage, &ep, &diagnostics);
        }

        // The blob goes to the first call only. ThrowIfFailed consumes it — passing it to both
        // would release it twice, which is the ownership bug this repository has already paid for
        // once (docs/guide.md, the borrowed loadModule pointer).
        var what = $"findAndCheckEntryPoint('{name}', {stage}) in module '{Name}'";
        Diagnostics.ThrowIfFailed(hr, what, diagnostics);
        Diagnostics.ThrowIfNull(ep, what, null);

        return new SlangEntryPoint(ep, name, this);
    }

    /// <summary>Every entry point the module declares, in declaration order.</summary>
    public SlangEntryPoint[] EntryPoints()
    {
        var count = EntryPointCount;
        var result = new SlangEntryPoint[count];
        var created = 0;
        try
        {
            for (; created < count; created++)
            {
                IEntryPoint* ep = null;
                var hr = Ptr->getDefinedEntryPoint(created, &ep);
                if (SlangResult.Failed(hr) || ep is null)
                    throw new SlangException(hr, $"getDefinedEntryPoint({created}) in module '{Name}'", null);
                result[created] = new SlangEntryPoint(ep, EntryPointName(ep), this);
            }
            return result;
        }
        catch
        {
            // Do not leak the ones already taken if a later index fails.
            for (var i = 0; i < created; i++) result[i]?.Dispose();
            throw;
        }
    }

    private static string EntryPointName(IEntryPoint* ep)
    {
        // getFunctionReflection() is a borrowed pointer owned by the entry point — see the
        // ownership note in SlangObject. Read the name out; never retain the pointer.
        var fn = ep->getFunctionReflection();
        return fn is null
            ? "<unnamed>"
            : Utf8.Read(SlangApi.spReflectionFunction_GetName((SlangReflectionFunction*)fn)) ?? "<unnamed>";
    }

    /// <summary>
    /// Finds a named type — a struct, interface, or generic — declared in or imported by this
    /// module. Returns null if there is no such type.
    /// </summary>
    /// <remarks>
    /// The result borrows from this module's reflection and is invalid once the module is
    /// disposed. Use it to build specialisation arguments or type conformances.
    /// </remarks>
    public SlangType? FindType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, $"IModule::getLayout for '{Name}'", diagnostics);

        using Utf8.Scoped n = new(name);
        var type = SlangApi.spReflection_FindTypeByName((SlangProgramLayout*)layout, n);
        return type is null ? null : new SlangType((TypeReflection*)type, this, name);
    }

    /// <summary>Finds a named type, or throws naming what was searched for.</summary>
    public SlangType RequireType(string name) =>
        FindType(name) ?? throw new SlangException(
            SlangResult.NotFound, $"type '{name}' not found in module '{Name}'", null);

    /// <summary>
    /// <c>IModule::getModuleReflection</c>: the root of this module's declaration tree, or null.
    /// Borrowed from the module (AGENTS.md#reflection-snapshot).
    /// </summary>
    public SlangDecl? GetModuleReflection() =>
        SlangDecl.From((SlangReflectionDecl*)Ptr->getModuleReflection(), this);

    /// <summary>
    /// <c>IComponentType::getLayout</c> on the module alone: its reflection for one target, before
    /// any link. Borrowed from the module.
    /// </summary>
    /// <exception cref="SlangException">Slang produced no layout; its diagnostics are attached.</exception>
    public SlangShaderReflection GetLayout(int targetIndex = 0)
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(targetIndex, &diagnostics);
        Diagnostics.ThrowIfNull(layout, $"IModule::getLayout for '{Name}'", diagnostics);
        return new SlangShaderReflection((SlangProgramLayout*)layout, this);
    }

    internal IComponentType* AsComponent => (IComponentType*)Ptr;
}

/// <summary>A shader entry point within a module.</summary>
public sealed unsafe class SlangEntryPoint : SlangObject<IEntryPoint>
{
    internal SlangEntryPoint(IEntryPoint* owned, string name, SlangModule owner)
        : base(owned, $"SlangEntryPoint('{name}')", owner) => Name = name;

    /// <summary>The entry point's function name.</summary>
    public string Name { get; }

    /// <summary>
    /// <c>IEntryPoint::getFunctionReflection</c>: the entry point's function declaration, or null.
    /// Borrowed from the entry point.
    /// </summary>
    public SlangFunction? FunctionReflection =>
        SlangFunction.From((SlangReflectionFunction*)Ptr->getFunctionReflection(), this);

    internal IComponentType* AsComponent => (IComponentType*)Ptr;
}
