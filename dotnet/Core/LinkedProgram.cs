// L2 — a linked program: the thing you can generate code from and reflect over.

using SharpSlang.Interop;
using SharpSlang.Native;
using SharpSlang.Reflection;
using SharpSlang.Spirv;

namespace SharpSlang;

/// <summary>
/// A composed and linked program. Code generation and reflection both require linking first,
/// because that is when cross-module references and specialisation are resolved.
/// </summary>
public sealed unsafe class LinkedProgram : SlangObject<IComponentType>
{
    private readonly string[] _entryPointNames;
    private readonly IReadOnlyList<TargetOptions> _targets;

    internal LinkedProgram(
        IComponentType* owned,
        string[] entryPointNames,
        SlangSession owner,
        IReadOnlyList<TargetOptions> targets)
        : base(owned, nameof(LinkedProgram), owner)
    {
        _entryPointNames = entryPointNames;
        _targets = targets;
    }

    /// <summary>
    /// Entry point names <em>as declared in Slang</em>, in the order they were linked. Index into
    /// these for code retrieval.
    /// </summary>
    /// <remarks>
    /// Not the names the emitted SPIR-V uses. Those come from the module, and by default they are
    /// all <c>main</c> (AGENTS.md#layout-and-the-emitted-module).
    /// </remarks>
    public IReadOnlyList<string> EntryPointNames => _entryPointNames;

    /// <summary>
    /// How many generic parameters still need concrete types before this program can generate
    /// code. Non-zero means it was linked without specialisation arguments.
    /// </summary>
    public long SpecializationParameterCount => Ptr->getSpecializationParamCount();

    /// <summary>
    /// Finds a named type in this program's reflection, or null: <see cref="GetLayout"/>(0)'s
    /// <see cref="SlangShaderReflection.FindTypeByName"/>, keeping the name it was asked by.
    /// Borrows from the program.
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
    /// What the compiler knows about the code generated for a target, beyond its interface.
    /// </summary>
    /// <remarks>
    /// Chiefly which parameter locations the emitted code actually reads. See
    /// <see cref="SlangMetadata"/> for why that is not a property of the reflection snapshot.
    /// </remarks>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public SlangMetadata GetTargetMetadata(int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        IMetadata* metadata = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getTargetMetadata(targetIndex, &metadata, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType::getTargetMetadata", diagnostics);
        Diagnostics.ThrowIfNull(metadata, "IComponentType::getTargetMetadata", null);

        return new SlangMetadata(metadata, this);
    }

    /// <summary>
    /// The same, for one entry point — which is the useful granularity.
    /// </summary>
    /// <remarks>
    /// A binding read by the fragment stage and not by the vertex stage is used by the program and
    /// unused by one of its stages, and only this call can tell them apart.
    /// </remarks>
    public SlangMetadata GetEntryPointMetadata(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(targetIndex);

        IMetadata* metadata = null;
        ISlangBlob* diagnostics = null;
        var hr = Ptr->getEntryPointMetadata(entryPointIndex, targetIndex, &metadata, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, "IComponentType::getEntryPointMetadata", diagnostics);
        Diagnostics.ThrowIfNull(metadata, "IComponentType::getEntryPointMetadata", null);

        return new SlangMetadata(metadata, this);
    }

    /// <summary>
    /// Slang's own reflection dump for this program, as JSON: <see cref="GetLayout"/>(0)'s
    /// <see cref="SlangShaderReflection.ToJson"/>.
    /// </summary>
    /// <remarks>
    /// The same text <c>slangc -reflection-json</c> writes: the only view that is Slang's own
    /// formatting, a second opinion in a bug report.
    /// </remarks>
    /// <returns>The JSON, or null if Slang produced none.</returns>
    public string? ToJson() => GetLayout(0).ToJson();

    /// <summary>
    /// Whether <paramref name="type"/> is a subtype of <paramref name="baseType"/> — i.e. whether
    /// a concrete type satisfies an interface: <see cref="GetLayout"/>(0)'s
    /// <see cref="SlangShaderReflection.IsSubType"/>. Use before creating a conformance to fail
    /// with a clear message rather than a Slang diagnostic.
    /// </summary>
    public bool IsSubType(SlangType type, SlangType baseType) => GetLayout(0).IsSubType(type, baseType);

    /// <summary>Generated code for one entry point, for the given target.</summary>
    /// <param name="entryPointIndex">Index into <see cref="EntryPointNames"/>.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    /// <returns>An owned blob; dispose it, or use <see cref="GetEntryPointCodeBytes(int, int)"/>.</returns>
    public SlangBlob GetEntryPointCode(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);

        ISlangBlob* code = null;
        ISlangBlob* diagnostics = null;
        var operation = $"getEntryPointCode('{_entryPointNames[entryPointIndex]}')";
        var hr = Ptr->getEntryPointCode(entryPointIndex, targetIndex, &code, &diagnostics);
        Diagnostics.ThrowIfFailed(hr, operation, diagnostics);

        if (code is null)
            throw new SlangException(SlangResult.Fail, $"{operation} produced no code", null);

        var blob = new SlangBlob(code, this);
        try
        {
            Verify(blob.Span, targetIndex, operation);
            return blob;
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Slang's identity hash for one entry point: the source and the options it was compiled
    /// with, which is <b>not</b> a key for "is this the same compiled code".
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>getEntryPointHash</c>, and it was left unbound for a long time because the obvious
    /// use for it was the wrong one. Measured on 2026.14.1, the hash was stable across runs and
    /// changed with the source, but was <b>identical across optimization levels</b> while the
    /// emitted SPIR-V was not, so a pipeline cache keyed on it served stale code with no error.
    /// </para>
    /// <para>
    /// Measured on 2026.21, it follows the options too: every link-time and target option tried
    /// moved it, the optimisation level included. And it follows the options rather than the code.
    /// Naming the default optimisation level explicitly, <c>High</c> against <c>Maximal</c>, and a
    /// link-time <c>DebugInformation</c> each emit byte-identical SPIR-V under a different hash. So
    /// it no longer answers "same shader" across option sets, and as a cache key it errs towards a
    /// miss; nothing measured says it covers the compiler build. Pinned both ways by
    /// <c>LinkOptionsTests</c>, so the next change of meaning is a failing test.
    /// </para>
    /// <para>To key a pipeline cache, hash the bytes from <see cref="GetEntryPointCodeBytes(int, int)"/>.</para>
    /// </remarks>
    /// <param name="entryPointIndex">Index into <see cref="EntryPointNames"/>.</param>
    /// <param name="targetIndex">Index into the session's target list.</param>
    public byte[] GetEntryPointIdentityHash(int entryPointIndex, int targetIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryPointIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(entryPointIndex, _entryPointNames.Length);

        ISlangBlob* hash = null;
        Ptr->getEntryPointHash(entryPointIndex, targetIndex, &hash);

        // Returns void, so there is no result to check and null is the only failure signal there
        // is. Slang produces a hash for every entry point measured; an empty one would mean the
        // identity key silently degrades to "all shaders are the same shader".
        if (hash is null)
        {
            throw new SlangException(
                SlangResult.Fail,
                $"getEntryPointHash('{_entryPointNames[entryPointIndex]}') produced no hash",
                null);
        }

        using var blob = new SlangBlob(hash, this);
        var bytes = blob.ToArray();

        if (bytes.Length == 0)
        {
            throw new SlangException(
                SlangResult.Fail,
                $"getEntryPointHash('{_entryPointNames[entryPointIndex]}') produced an empty hash",
                null);
        }

        return bytes;
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

        var blob = new SlangBlob(code, this);
        try
        {
            Verify(blob.Span, targetIndex, "getTargetCode");
            return blob;
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Checks a generated module against the target it was generated for.
    /// </summary>
    /// <remarks>
    /// Only the header: magic, word alignment, and the version. That is enough to turn the two
    /// failures that otherwise surface at <c>vkCreateShaderModule</c> — "this is not SPIR-V" and
    /// "this is a newer SPIR-V than the device accepts" — into an exception naming the target.
    /// Two words read here, without a SPIR-V reader: the binding names nothing of the pipeline
    /// packages (AGENTS.md#layers).
    /// </remarks>
    private void Verify(ReadOnlySpan<byte> code, int targetIndex, string operation)
    {
        if (targetIndex < 0 || targetIndex >= _targets.Count) return;

        var target = _targets[targetIndex];
        if (!target.IsSpirv || !target.VerifyEmittedSpirv) return;

        const uint SpirvMagic = 0x07230203;
        var isSpirv = code.Length >= 20 && code.Length % 4 == 0
            && System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(code)[0] == SpirvMagic;
        if (!isSpirv)
            throw new SlangException(SlangResult.Ok, operation,
                $"target {targetIndex} is SPIR-V but produced {code.Length} bytes that are not a " +
                $"SPIR-V module. This is a Slang bug or a corrupted blob, not a shader error.");

        var expected = target.ExpectedSpirvVersion;
        if (expected == SpirvVersion.Unspecified) return;

        // The header's version word, with the reserved high and low bytes masked off.
        var actual = (SpirvVersion)(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(code)[1] & 0x00FF_FF00);
        if (actual == expected) return;

        throw new SlangException(SlangResult.Ok, operation,
            $"target {targetIndex} asked for SPIR-V {expected.ToDisplayString()} but Slang emitted " +
            $"{actual.ToDisplayString()}. A module above the device's supported SPIR-V version is " +
            $"rejected by vkCreateShaderModule with no further detail, so it is caught here instead.");
    }

    /// <summary>
    /// <c>IComponentType::getLayout</c>: the program's reflection for one target.
    /// </summary>
    /// <remarks>
    /// Borrowed, not ref-counted: owned by this program, and every member of what it returns throws
    /// <see cref="ObjectDisposedException"/> once the program is disposed
    /// (AGENTS.md#reflection-snapshot).
    /// </remarks>
    /// <param name="targetIndex">Index into the session's target list.</param>
    /// <exception cref="SlangException">Slang produced no layout; its diagnostics are attached.</exception>
    public SlangShaderReflection GetLayout(int targetIndex = 0)
    {
        ISlangBlob* diagnostics = null;
        var layout = Ptr->getLayout(targetIndex, &diagnostics);
        Diagnostics.ThrowIfNull(layout, "IComponentType::getLayout", diagnostics);
        return new SlangShaderReflection((SlangProgramLayout*)layout, this);
    }

    internal IComponentType* AsComponent => Ptr;

    // ---------------------------------------------------------------- L3, acquired on first use

    private SlangComponentType2Extension? _componentType2;
    private bool _componentType2Probed;

    /// <summary>
    /// <c>slang::IComponentType2</c> (<c>queryInterface</c>), or null when this program's
    /// implementation lacks it. Acquired once; released with the program.
    /// </summary>
    public SlangComponentType2Extension? TryComponentType2
    {
        get
        {
            if (!_componentType2Probed)
            {
                _componentType2 = SlangComponentType2Extension.TryAcquire(this, Ptr);
                _componentType2Probed = true;
            }
            return _componentType2;
        }
    }

    /// <summary><c>slang::IComponentType2</c>. Throws <see cref="NotSupportedException"/> when absent.</summary>
    public SlangComponentType2Extension ComponentType2 =>
        TryComponentType2 ?? throw new NotSupportedException("queryInterface(IComponentType2) failed on this program");

    private protected override void BeforeRelease() => _componentType2?.Release();
}
