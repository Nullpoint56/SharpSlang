// L2 — a linked program: the thing you can generate code from and reflect over.

using PrismSlang.Interop;
using PrismSlang.Native;

namespace PrismSlang;

/// <summary>
/// A composed and linked program. Code generation and reflection both require linking first,
/// because that is when cross-module references and specialisation are resolved.
/// </summary>
public sealed unsafe class LinkedProgram : SlangObject<IComponentType>
{
    private readonly string[] _entryPointNames;

    internal LinkedProgram(IComponentType* owned, string[] entryPointNames, SlangSession owner)
        : base(owned, nameof(LinkedProgram), owner) => _entryPointNames = entryPointNames;

    /// <summary>Entry point names, in the order they were linked. Index into these for code retrieval.</summary>
    public IReadOnlyList<string> EntryPointNames => _entryPointNames;

    /// <summary>
    /// How many generic parameters still need concrete types before this program can generate
    /// code. Non-zero means it was linked without specialisation arguments.
    /// </summary>
    public long SpecializationParameterCount => Ptr->getSpecializationParamCount();

    /// <summary>
    /// Finds a named type in this program's reflection, or null. Borrows from the program.
    /// </summary>
    public SlangType? FindType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        using Utf8.Scoped n = new(name);
        var type = SlangApi.spReflection_FindTypeByName((SlangProgramLayout*)layout, n);
        return type is null ? null : new SlangType((TypeReflection*)type, this, name);
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a subtype of <paramref name="baseType"/> — i.e. whether
    /// a concrete type satisfies an interface. Use before creating a conformance to fail with a
    /// clear message rather than a Slang diagnostic.
    /// </summary>
    public bool IsSubType(SlangType type, SlangType baseType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(baseType);

        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(0, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);

        return SlangApi.spReflection_isSubType(
            (SlangProgramLayout*)layout,
            (SlangReflectionType*)type.Handle,
            (SlangReflectionType*)baseType.Handle) != 0;
    }

    /// <summary>Generated code for one entry point, for the given target.</summary>
    /// <param name="entryPointIndex">Index into <see cref="EntryPointNames"/>.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    /// <returns>An owned blob; dispose it, or use <see cref="GetEntryPointCodeBytes"/>.</returns>
    public SlangBlob GetEntryPointCode(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);

        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getEntryPointCode(entryPointIndex, targetIndex, &code, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, $"getEntryPointCode('{_entryPointNames[entryPointIndex]}')", diagnostics);

        if (code is null)
            throw new SlangException(SlangResult.Fail,
                $"getEntryPointCode('{_entryPointNames[entryPointIndex]}') produced no code", null);

        return new SlangBlob(code, this);
    }

    /// <summary>Generated code for one entry point, copied into a managed array.</summary>
    public byte[] GetEntryPointCodeBytes(int entryPointIndex, int targetIndex = 0)
    {
        using var blob = GetEntryPointCode(entryPointIndex, targetIndex);
        return blob.ToArray();
    }

    /// <summary>Generated code for one entry point, resolved by name.</summary>
    public byte[] GetEntryPointCodeBytes(string entryPointName, int targetIndex = 0)
    {
        var i = Array.IndexOf(_entryPointNames, entryPointName);
        if (i < 0)
            throw new ArgumentException(
                $"'{entryPointName}' was not linked into this program. Linked: {string.Join(", ", _entryPointNames)}",
                nameof(entryPointName));
        return GetEntryPointCodeBytes(i, targetIndex);
    }

    /// <summary>
    /// Whole-program code for a target, rather than per entry point. Requires the target to have
    /// been created with whole-program generation; most Vulkan use wants per-entry-point instead.
    /// </summary>
    public SlangBlob GetTargetCode(int targetIndex = 0)
    {
        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getTargetCode(targetIndex, &code, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "getTargetCode", diagnostics);

        if (code is null) throw new SlangException(SlangResult.Fail, "getTargetCode produced no code", null);
        return new SlangBlob(code, this);
    }

    /// <summary>
    /// The native reflection root for this program, for L3's use only.
    /// </summary>
    /// <remarks>
    /// Borrowed, not ref-counted: owned by this program and invalid once it is disposed. It must
    /// not escape the reflection walk — see docs §4.4. Deliberately internal.
    /// </remarks>
    internal ShaderReflection* GetLayout(int targetIndex = 0)
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(targetIndex, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);
        return layout;
    }
}
