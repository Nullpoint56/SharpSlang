// L3 — what the compiler knows about the *generated code* that reflection does not.
//
// Reflection describes the shader's interface: every parameter the author declared, with the
// binding Slang assigned it. That is the right answer for a descriptor set layout and the wrong one
// for a descriptor set: a parameter can be declared, laid out, and then never read by the code
// actually emitted for a target — dead behind a specialisation constant, or referenced only from a
// branch the optimiser removed. Reflection still reports it, because it is still part of the
// interface.
//
// IMetadata is the compiler's answer to "did the emitted code touch this location", and it is
// per-target and optionally per-entry-point, which is why it cannot live on ProgramLayout: the
// answer differs between the vertex and fragment stages of one program, and the layout describes
// both at once.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang.Reflection;

/// <summary>
/// Which parameter locations the code generated for one target actually uses.
/// </summary>
/// <remarks>
/// Obtained from <c>LinkedProgram.GetTargetMetadata</c> or <c>GetEntryPointMetadata</c>. Dispose
/// it; it is a ref-counted Slang object like every other.
/// </remarks>
public sealed unsafe class SlangMetadata : SlangObject<IMetadata>
{
    internal SlangMetadata(IMetadata* owned, SlangObject? owner = null)
        : base(owned, nameof(SlangMetadata), owner) { }

    /// <summary>
    /// Whether the generated code reads or writes the parameter at this location.
    /// </summary>
    /// <remarks>
    /// False means the descriptor can be left unbound: nothing in the emitted code refers to it.
    /// It does <em>not</em> mean the binding can be removed from the descriptor set layout — the
    /// layout still has to match what the SPIR-V declares, and Slang emits the declaration either
    /// way. What it saves is the descriptor write and whatever resource would have backed it.
    /// </remarks>
    /// <param name="category">Which resource kind the index is counted in.</param>
    /// <param name="space">Register space, i.e. descriptor set index on Vulkan.</param>
    /// <param name="index">Binding / register index within the space.</param>
    /// <exception cref="SlangException">
    /// Slang cannot answer for this target — its backend does not track usage.
    /// </exception>
    public bool IsUsed(SlangParameterCategory category, uint space, uint index)
    {
        bool used;
        var hr = Ptr->isParameterLocationUsed(category, space, index, &used);

        // Throws rather than defaulting. Defaulting to "used" is the safe direction and it is
        // still the wrong behaviour: a caller on a target with no usage tracking would get an
        // empty unused-list and conclude everything is live, which is indistinguishable from the
        // answer on a shader that reads all of its bindings. A location nothing occupies is not
        // this case — Slang answers that one, with false.
        if (SlangResult.Failed(hr))
        {
            throw new SlangException(hr, "IMetadata::isParameterLocationUsed",
                $"Slang cannot say whether {category} at space {space}, index {index} is used by "
                + "the generated code. This is a property of the target's backend, not of the "
                + "shader, so it will fail for every location on that target.");
        }

        return used;
    }

    /// <summary>
    /// The bindings in <paramref name="layout"/> that the generated code never touches.
    /// </summary>
    /// <remarks>
    /// The question a cooker asks. Reflection cannot answer it and the emitted module cannot
    /// either — a declared-but-unread binding is present in both.
    /// </remarks>
    public IReadOnlyList<ParameterBinding> UnusedBindings(ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var unused = new List<ParameterBinding>();
        foreach (var parameter in layout.Parameters)
        {
            foreach (var binding in parameter.Bindings)
            {
                if (!IsUsed(binding.Category, binding.Space, binding.Index)) unused.Add(binding);
            }
        }

        return unused;
    }

    /// <summary>
    /// The identifier tying this build to its debug information, where the target emits one.
    /// </summary>
    /// <remarks>
    /// Null on most targets. On SPIR-V with debug info enabled it is what a symbol server would
    /// key on, so a captured frame can be matched back to the source it was cooked from.
    /// </remarks>
    public string? DebugBuildIdentifier => Utf8.Read(Ptr->getDebugBuildIdentifier());

    public override string ToString() =>
        IsDisposed ? "SlangMetadata (disposed)" : "SlangMetadata";
}
