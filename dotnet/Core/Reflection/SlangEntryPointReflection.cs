// L2 — slang::EntryPointReflection (EntryPointLayout), one member per spReflectionEntryPoint_*
// function, in Slang's argument order. Borrowed (SlangReflectionObject).

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary><c>slang::EntryPointReflection</c>: one entry point of a program's layout — its stage, parameters and workgroup.</summary>
public sealed unsafe class SlangEntryPointReflection : SlangReflectionObject
{
    private SlangEntryPointReflection(SlangEntryPointLayout* p, SlangObject owner)
        : base(p, owner, nameof(SlangEntryPointReflection)) { }

    internal static SlangEntryPointReflection? From(SlangEntryPointLayout* p, SlangObject owner) =>
        p is null ? null : new SlangEntryPointReflection(p, owner);

    internal SlangEntryPointLayout* Ptr => (SlangEntryPointLayout*)Raw;

    /// <summary><c>getName</c>: the Slang function name, not the emitted module's (AGENTS.md#layout-and-the-emitted-module).</summary>
    public string? Name => Utf8.Read(SlangApi.spReflectionEntryPoint_getName(Ptr));

    /// <summary><c>getNameOverride</c>, or null.</summary>
    public string? NameOverride => Utf8.Read(SlangApi.spReflectionEntryPoint_getNameOverride(Ptr));

    /// <summary><c>getParameterCount</c>: the entry point's own parameters.</summary>
    public uint ParameterCount => SlangApi.spReflectionEntryPoint_getParameterCount(Ptr);

    /// <summary><c>getParameterByIndex</c>.</summary>
    public SlangVariableLayout? GetParameterByIndex(uint index) =>
        SlangVariableLayout.From(SlangApi.spReflectionEntryPoint_getParameterByIndex(Ptr, index), Owner);

    /// <summary><c>getFunction</c>: the entry point as a function declaration, or null.</summary>
    public SlangFunction? Function => SlangFunction.From(SlangApi.spReflectionEntryPoint_getFunction(Ptr), Owner);

    /// <summary><c>getStage</c>.</summary>
    public SlangStage Stage => SlangApi.spReflectionEntryPoint_getStage(Ptr);

    /// <summary>
    /// <c>getComputeThreadGroupSize</c>: threads per group along each axis, one per element of
    /// <paramref name="sizeAlongAxis"/> (the axis count Slang takes). A dimension driven by a
    /// specialization constant reports 0 (AGENTS.md#sizes-slang-does-not-know).
    /// </summary>
    public void GetComputeThreadGroupSize(Span<ulong> sizeAlongAxis)
    {
        fixed (ulong* p = sizeAlongAxis)
        {
            SlangApi.spReflectionEntryPoint_getComputeThreadGroupSize(Ptr, (ulong)sizeAlongAxis.Length, p);
        }
    }

    /// <summary><c>getComputeWaveSize</c>: the <c>[WaveSize(N)]</c>, 0 when none was declared.</summary>
    public ulong ComputeWaveSize
    {
        get
        {
            ulong size;
            SlangApi.spReflectionEntryPoint_getComputeWaveSize(Ptr, &size);
            return size;
        }
    }

    /// <summary><c>usesAnySampleRateInput</c>.</summary>
    public bool UsesAnySampleRateInput => SlangApi.spReflectionEntryPoint_usesAnySampleRateInput(Ptr) != 0;

    /// <summary><c>getVarLayout</c>: the entry point's parameter block in the global numbering, or null.</summary>
    public SlangVariableLayout? VarLayout => SlangVariableLayout.From(SlangApi.spReflectionEntryPoint_getVarLayout(Ptr), Owner);

    /// <summary><c>getResultVarLayout</c>, or null.</summary>
    public SlangVariableLayout? ResultVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflectionEntryPoint_getResultVarLayout(Ptr), Owner);

    /// <summary><c>hasDefaultConstantBuffer</c>.</summary>
    public bool HasDefaultConstantBuffer => SlangApi.spReflectionEntryPoint_hasDefaultConstantBuffer(Ptr) != 0;
}
