// L2 — an unlinked slang::IComponentType: what ISession::createCompositeComponentType and
// IComponentType::specialize return, before IComponentType::link.
//
// Link's three steps, each one call (AGENTS.md#layers). The wrapper takes a native reference on
// every component it was made from and gives them back after its own release, so a component the
// caller disposes first cannot go away under it whatever Slang retains itself (measured in
// CompositeTests). It is a child of its session, so the session refuses Dispose while it lives.

using System.Runtime.InteropServices;
using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary>
/// A Slang object that is an <c>IComponentType</c>: a module, an entry point, a type conformance,
/// a composite or specialized component type, or a linked program. What
/// <see cref="SlangSession.CreateCompositeComponentType"/> composes.
/// </summary>
/// <remarks>Implemented by the binding's wrappers only; any other implementation is refused.</remarks>
public interface ISlangComponentType
{
    /// <summary>The native interface pointer (<see cref="SlangObject.NativeHandle"/>).</summary>
    nint NativeHandle { get; }
}

/// <summary>
/// An unlinked component type: a composite of modules, entry points and conformances, or one
/// specialized. <see cref="Link"/> or <see cref="LinkWithOptions"/> makes it a
/// <see cref="LinkedProgram"/>.
/// </summary>
public sealed unsafe class SlangComponentType : SlangObject<IComponentType>, ISlangComponentType
{
    private readonly SlangSession _session;
    private readonly nint[] _held;
    private readonly string[] _entryPointNames;

    internal SlangComponentType(IComponentType* owned, SlangSession session, string what, nint[] held, string[] entryPointNames)
        : base(owned, what, session)
    {
        _session = session;
        _entryPointNames = entryPointNames;

        // Ours, released after this object's own release and before its ancestors' (AfterRelease).
        foreach (var p in held) ((ISlangUnknown*)p)->addRef();
        _held = held;
    }

    /// <summary>
    /// The entry points composed in, by their Slang names, in composition order: the indices
    /// <see cref="LinkedProgram.GetEntryPointCode"/> takes after the link.
    /// </summary>
    public IReadOnlyList<string> EntryPointNames => _entryPointNames;

    /// <summary><c>getSpecializationParamCount</c>: generic parameters still waiting for a <see cref="Specialize"/>.</summary>
    public long SpecializationParameterCount => Ptr->getSpecializationParamCount();

    /// <summary>
    /// <c>IComponentType::getLayout</c> before the link. Borrowed from this object
    /// (AGENTS.md#reflection-snapshot records what it reports against the linked program's).
    /// </summary>
    /// <exception cref="SlangException">Slang produced no layout; its diagnostics are attached.</exception>
    public SlangShaderReflection GetLayout(int targetIndex)
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(targetIndex, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);
        return new SlangShaderReflection((SlangProgramLayout*)layout, this);
    }

    /// <summary>
    /// <c>IComponentType::specialize</c>: concrete types for the generic parameters, in
    /// declaration order, as a new component type. This one is unchanged.
    /// </summary>
    /// <exception cref="SlangException">Slang refused the arguments; its diagnostics are attached.</exception>
    public SlangComponentType Specialize(params ReadOnlySpan<SlangType> arguments)
    {
        var args = new SpecializationArg[Math.Max(arguments.Length, 1)];
        for (var i = 0; i < arguments.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(arguments[i]);
            args[i] = SpecializationArg.fromType(arguments[i].Handle);
        }

        IComponentType* specialized = null;
        ISlangBlob* diagnostics = null;
        int hr;
        fixed (SpecializationArg* p = args)
        {
            hr = Ptr->specialize(p, arguments.Length, &specialized, &diagnostics);
        }

        Diagnostics.ThrowIfFailed(hr, "IComponentType::specialize", diagnostics);
        Diagnostics.ThrowIfNull(specialized, "IComponentType::specialize", null);
        return new SlangComponentType(specialized, _session, "SlangComponentType(specialized)", [(nint)Ptr], _entryPointNames);
    }

    /// <summary><c>IComponentType::link</c>: resolves the remaining dependencies into a program.</summary>
    /// <exception cref="SlangException">The link failed; Slang's diagnostics are attached.</exception>
    public LinkedProgram Link()
    {
        IComponentType* linked = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->link(&linked, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "link", diagnostics);
        Diagnostics.ThrowIfNull(linked, "link", null);
        return new LinkedProgram(linked, _entryPointNames, _session, _session.Targets);
    }

    /// <summary>
    /// <c>IComponentType::linkWithOptions</c>: the same, with compiler options for this link.
    /// Slang accepts every option here and ignores most (AGENTS.md#compilation-linking-and-diagnostics).
    /// </summary>
    /// <exception cref="SlangException">The link failed; Slang's diagnostics are attached.</exception>
    public LinkedProgram LinkWithOptions(LinkTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // String values are allocated for the call and freed after it: Slang copies what it needs.
        var entries = options.ToEntries();
        var native = (CompilerOptionEntry*)NativeMemory.AllocZeroed(
            (nuint)Math.Max(entries.Count, 1), (nuint)sizeof(CompilerOptionEntry));
        var strings = new List<nint>();

        IComponentType* linked = null;
        ISlangBlob* diagnostics = null;
        int hr;
        try
        {
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                native[i].name = e.Name;
                native[i].value = Defaults.CompilerOptionValue();

                if (e.StringValue is not null)
                {
                    var p = Utf8.Alloc(e.StringValue);
                    strings.Add((nint)p);
                    native[i].value.kind = CompilerOptionValueKind.String;
                    native[i].value.stringValue0 = p;
                }
                else
                {
                    native[i].value.kind = CompilerOptionValueKind.Int;
                    native[i].value.intValue0 = e.IntValue;
                }
            }

            hr = Ptr->linkWithOptions(&linked, (uint)entries.Count, native, &diagnostics);
        }
        finally
        {
            foreach (var p in strings) Utf8.Free((sbyte*)p);
            NativeMemory.Free(native);
        }

        Diagnostics.ThrowIfFailed(hr, "linkWithOptions", diagnostics);
        Diagnostics.ThrowIfNull(linked, "linkWithOptions", null);
        return new LinkedProgram(linked, _entryPointNames, _session, _session.Targets);
    }

    private protected override void AfterRelease()
    {
        foreach (var p in _held) ((ISlangUnknown*)p)->release();
    }

    internal IComponentType* AsComponent => Ptr;
}
