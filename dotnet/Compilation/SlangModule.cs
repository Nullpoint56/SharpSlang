// L2 — a compiled module and its entry points.

using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang;

/// <summary>A compiled Slang module.</summary>
public sealed unsafe class SlangModule : SlangObject<IModule>
{
    /// <param name="borrowed">
    /// A module pointer from <c>ISession::loadModule*</c>. Note: <em>borrowed</em>, not owned.
    /// </param>
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

    /// <summary>The root of this module's declaration tree, borrowed. For L3's use.</summary>
    internal DeclReflection* GetModuleReflection() => Ptr->getModuleReflection();

    internal IComponentType* AsComponent => (IComponentType*)Ptr;
}

/// <summary>A shader entry point within a module.</summary>
public sealed unsafe class SlangEntryPoint : SlangObject<IEntryPoint>
{
    internal SlangEntryPoint(IEntryPoint* owned, string name, SlangModule owner)
        : base(owned, $"SlangEntryPoint('{name}')", owner) => Name = name;

    /// <summary>The entry point's function name.</summary>
    public string Name { get; }

    internal IComponentType* AsComponent => (IComponentType*)Ptr;
}
