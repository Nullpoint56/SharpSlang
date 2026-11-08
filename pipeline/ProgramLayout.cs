// L3 — the program-level snapshot: parameters, entry points, and the descriptor-set structure.
//
// Still Slang-neutral. The descriptor-set model here is Slang's own — it is what drives D3D12 root
// signatures as much as Vulkan descriptor set layouts — so it belongs in the core package. The
// NRI reading of it lives in SharpSlang.Pipeline.NRI.

using System.Text.Json;
using System.Text.Json.Serialization;
using SharpSlang.Native;

namespace SharpSlang.Reflection;

/// <summary>Where Slang placed a top-level shader parameter.</summary>
/// <param name="Category">Which resource kind the binding is counted in.</param>
/// <param name="Index">Binding / register index within <paramref name="Space"/>.</param>
/// <param name="Space">Register space, i.e. descriptor set index on Vulkan.</param>
public readonly record struct ParameterBinding(SlangParameterCategory Category, uint Index, uint Space);

/// <summary>A top-level shader parameter.</summary>
/// <param name="Name">Declared name.</param>
/// <param name="Type">Its layout.</param>
/// <param name="Bindings">
/// Where it lives, per category. A parameter can appear in more than one — a struct mixing a
/// texture and uniform data consumes both a descriptor slot and bytes.
/// </param>
/// <param name="Semantic">HLSL-style semantic, if declared.</param>
/// <param name="SemanticIndex">Index on that semantic.</param>
/// <param name="Attributes">User attributes declared on the parameter.</param>
/// <param name="Stage">
/// Which stage the parameter belongs to, where Slang attributes it to one — an entry point's
/// varyings do, a global does not.
/// </param>
public sealed record ShaderParameter(
    string Name,
    TypeLayout Type,
    IReadOnlyList<ParameterBinding> Bindings,
    string? Semantic = null,
    uint SemanticIndex = 0,
    IReadOnlyList<UserAttribute>? Attributes = null,
    SlangStage Stage = SlangStage.SLANG_STAGE_NONE)
{
    /// <summary>
    /// Whether this is a specialization constant rather than something bound at draw time.
    /// </summary>
    /// <remarks>
    /// Worth checking before treating any parameter as a descriptor. Slang reports specialization
    /// constants in the same top-level parameter list as textures and buffers, with a binding
    /// index that is really the <c>constant_id</c> and a space that is always 0 — so a
    /// <c>[vk::constant_id(0)]</c> constant and the descriptor at set 0 binding 0 are
    /// indistinguishable by index alone. See <see cref="ProgramLayout.SpecializationConstants"/>
    /// for the extracted form.
    /// </remarks>
    [JsonIgnore]
    public bool IsSpecializationConstant =>
        Bindings.Count > 0
        && Bindings.All(b =>
            b.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SPECIALIZATION_CONSTANT);

    /// <summary>
    /// Whether this parameter actually consumes a slot in a descriptor set.
    /// </summary>
    /// <remarks>
    /// The test to apply before treating a parameter's binding index as a descriptor binding.
    /// Slang puts everything in one top-level list — descriptors, push constants, specialization
    /// constants, varyings — and several of those report an index and a space that look exactly
    /// like a descriptor's without being one. A <c>[vk::push_constant]</c> block and a
    /// <c>[vk::constant_id(0)]</c> constant both claim (space 0, index 0), the same coordinates as
    /// the descriptor at set 0 binding 0, and both sort ahead of it. Two separate bugs came from
    /// asking the question the other way round and excluding kinds one at a time as they turned up.
    /// </remarks>
    [JsonIgnore]
    public bool OccupiesDescriptorSlot => Bindings.Any(b => b.Category
        is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT
        or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER
        or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE
        or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS
        or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SAMPLER_STATE);

    /// <summary>
    /// Which input attachment of the subpass this reads, for a <c>SubpassInput</c>; null otherwise.
    /// </summary>
    /// <remarks>
    /// Distinct from the descriptor binding, and both are needed: the binding says where to write
    /// the descriptor, this says which entry of <c>VkSubpassDescription::pInputAttachments</c> the
    /// shader means. Slang carries it as the offset of the <c>SUBPASS</c> category, matching the
    /// module's <c>InputAttachmentIndex</c> decoration.
    /// </remarks>
    [JsonIgnore]
    public uint? InputAttachmentIndex
    {
        get
        {
            // Indexed: a foreach over the IReadOnlyList boxes its enumerator, 48 bytes a read
            // (AllocationTests, on a layout read back from a package).
            for (var i = 0; i < Bindings.Count; i++)
            {
                if (Bindings[i].Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUBPASS)
                    return Bindings[i].Index;
            }
            return null;
        }
    }

    /// <summary>The primary binding — the first non-uniform one, else the first of any kind.</summary>
    [JsonIgnore]
    public ParameterBinding? PrimaryBinding =>
        Bindings.Count == 0
            ? null
            : Bindings.FirstOrDefault(
                  b => b.Category != SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM,
                  Bindings[0]);

    /// <summary>
    /// The register space — Vulkan descriptor set — this parameter lives in.
    /// </summary>
    /// <remarks>
    /// Not simply <c>PrimaryBinding.Space</c>. A <c>ParameterBlock</c> introduces its own space
    /// and reports it as the <em>offset</em> of its <c>SUB_ELEMENT_REGISTER_SPACE</c> category,
    /// leaving the space field at 0. Reading the space field alone puts every parameter block in
    /// set 0, which is wrong and looks plausible.
    /// </remarks>
    [JsonIgnore]
    public uint DescriptorSet
    {
        get
        {
            // Indexed, as InputAttachmentIndex: a foreach boxed its enumerator, 48 bytes a read.
            for (var i = 0; i < Bindings.Count; i++)
            {
                if (Bindings[i].Category is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE
                                         or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_REGISTER_SPACE)
                {
                    return Bindings[i].Index;
                }
            }
            return PrimaryBinding?.Space ?? 0;
        }
    }
}

/// <summary>One binding slot inside a descriptor set, as Slang models it.</summary>
/// <param name="BindingType">What kind of thing is bound.</param>
/// <param name="IndexOffset">Binding index within the set.</param>
/// <param name="DescriptorCount">
/// Array size; 1 for a scalar binding, and 0 when <paramref name="IsUnbounded"/> — the shader did
/// not say, so the host chooses.
/// </param>
/// <param name="Category">The resource category this range is counted in.</param>
/// <param name="Name">The parameter that produced this range, where it could be attributed.</param>
/// <param name="IsUnbounded">
/// Whether the shader declared a runtime-sized array (<c>Texture2D t[];</c>), i.e. bindless.
/// </param>
/// <param name="InputAttachmentIndex">
/// For a subpass input, which input attachment of the subpass it reads — the module's
/// <c>InputAttachmentIndex</c> decoration. Null for everything else.
/// </param>
/// <param name="ImageFormat">
/// The format declared with <c>[format("...")]</c> on a storage image. <c>unknown</c> both when
/// none was declared and when the binding is not an image; a storage image left unknown requires
/// the device's <c>shaderStorageImageReadWithoutFormat</c> / <c>WriteWithoutFormat</c> feature.
/// </param>
/// <remarks>
/// Slang reports an unbounded array's descriptor count as <c>0xFFFFFFFF</c>. That value is not a
/// count and must not reach <c>VkDescriptorSetLayoutBinding::descriptorCount</c>; it is normalised
/// to 0 here with the flag set, because the real cap is a host decision made with
/// <c>VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT</c> and the device's
/// <c>maxPerStageDescriptor*</c> limits. Measured against slang-compiler 2026.21.
/// </remarks>
/// <param name="CounterBinding">
/// For an <c>AppendStructuredBuffer</c> or <c>ConsumeStructuredBuffer</c>, the binding index within
/// this same set of the separate buffer holding its count. Null for everything else.
/// </param>
/// <param name="IsSpecializable">
/// Whether the binding's type is still generic — a range whose descriptor type is only decided by
/// specialization. False for every range in a linked program, which is what linking is for; the
/// flag exists because Slang exposes the question and a host reading a layout from an unlinked
/// component would need the answer.
/// </param>
/// <param name="Stages">
/// The entry point stages whose generated code reads this binding.
/// </param>
/// <remarks>
/// Empty means <em>every</em> stage, not <em>no</em> stage — a binding no stage is known to touch
/// still has to be declared visible somewhere, because Vulkan rejects a
/// <c>VkDescriptorSetLayoutBinding</c> with a non-zero descriptor count and no stage flags. Reading
/// this as a set to intersect with rather than a set to trust is the difference between a tighter
/// descriptor set layout and one the driver refuses.
///
/// Reflection alone cannot produce this: Slang's global-scope view does not attribute a parameter
/// to the entry points that use it. It comes from <c>IMetadata</c>, asked once per stage per
/// binding while the program is still alive, which is also why it is on the snapshot rather
/// than computed later — the compiler is gone by then.
/// </remarks>
/// <param name="IsImplicitGlobalBuffer">
/// Whether this is the constant buffer Slang synthesises for loose global uniforms
/// (<c>uniform float4 gTint;</c> at file scope), which no declaration names.
/// </param>
public sealed record DescriptorRange(
    SlangBindingType BindingType,
    uint IndexOffset,
    uint DescriptorCount,
    SlangParameterCategory Category,
    string? Name = null,
    bool IsUnbounded = false,
    uint? InputAttachmentIndex = null,
    SlangImageFormat ImageFormat = SlangImageFormat.SLANG_IMAGE_FORMAT_unknown,
    uint? CounterBinding = null,
    bool IsSpecializable = false,
    IReadOnlyList<SlangStage>? Stages = null,
    bool IsImplicitGlobalBuffer = false)
{
    /// <summary>
    /// Whether this range is a counter belonging to an append/consume buffer rather than something
    /// the shader declared.
    /// </summary>
    /// <remarks>
    /// An <c>AppendStructuredBuffer</c> costs two descriptors, and Slang reports the second as an
    /// ordinary binding range named <c>counter</c>. A shader with an append buffer and a consume
    /// buffer therefore produces two bindings both called <c>counter</c>, and neither appears in
    /// <see cref="ProgramLayout.Parameters"/> — a host that builds its descriptor set layout from
    /// the parameter list is short two storage buffers the shader will read. Use
    /// <see cref="CounterBinding"/> on the owning range to pair them up; this is only the coarse
    /// test.
    /// </remarks>
    [JsonIgnore]
    public bool IsCounter => string.Equals(Name, "counter", StringComparison.Ordinal);
}

// IsImplicitGlobalBuffer, and why it is a label rather than a name.
//
// Loose global uniforms — `uniform float4 gTint;` at file scope — become a constant buffer nobody
// declared. Slang's SPIR-V emitter calls it `globalParams`; reflection attributes it to no
// variable, so its range arrives with a null Name. A host that builds descriptors by iterating the
// ranges therefore meets one it cannot identify, and has to correlate it against
// ProgramLayout.GlobalConstantBufferBinding by index to find out what it is.
//
// Naming it was rejected and stays rejected: `globalParams` is a name chosen by one backend's
// emitter, and putting it in the API-neutral layer is wrong the moment a caller declares a
// parameter that collides with it. Docs .
//
// Labelling it is not the same decision. The range still has no name — the agreement gap on the
// `global-uniforms` corpus case is unchanged and still declared — but the null is now explained
// where it is encountered rather than three types away.

/// <summary>A descriptor set (register space) and the ranges within it.</summary>
public sealed record DescriptorSet(uint SpaceOffset, IReadOnlyList<DescriptorRange> Ranges);

/// <summary>
/// A binding range that is itself an object with its own bindings — a <c>ParameterBlock</c> or a
/// <c>ConstantBuffer</c> holding resources.
/// </summary>
/// <remarks>
/// <para>
/// Slang's third view of the same data. The parameter list says <c>soMaterial</c> exists; the
/// descriptor-set view says which set holds it; this says it is a <em>container</em> — something
/// with bindings inside it — rather than a texture or a sampler bound directly.
/// </para>
/// <para>
/// Both kinds of container qualify, which is worth knowing before using it as a
/// <c>ParameterBlock</c> detector: a <c>ConstantBuffer&lt;T&gt;</c> holding nothing but values is
/// reported here alongside the <c>ParameterBlock</c>s. Measured on 2026.21.
/// </para>
/// <para>
/// <strong>The set index is not in this view.</strong> Slang has a
/// <c>getSubObjectRangeSpaceOffset</c> and it reported 0 for a block occupying set 1 and 0 for one
/// occupying set 2; <c>getBindingRangeDescriptorSetIndex</c> reports 0 for both as well, being an
/// index into the enclosing scope's own list rather than an absolute space. So this record does not
/// carry one. Match <see cref="Name"/> against
/// <see cref="ProgramLayout.DescriptorSets"/> for the set a host actually binds — that view is
/// built from the descriptor-set API and is the one that agrees with the emitted module.
/// </para>
/// </remarks>
/// <param name="BindingRangeIndex">
/// Which binding range of the global scope this sub-object occupies.
/// </param>
/// <param name="Name">The parameter that produced it, where Slang could attribute one.</param>
public sealed record SubObjectRange(uint BindingRangeIndex, string? Name = null);

/// <summary>A shader entry point.</summary>
/// <param name="Name">Function name.</param>
/// <param name="Stage">Pipeline stage from its <c>[shader(...)]</c> attribute.</param>
/// <param name="Parameters">Entry-point parameters — varyings for raster stages.</param>
/// <param name="ThreadGroupSize">Compute thread-group dimensions, null for non-compute stages.</param>
/// <param name="UsesAnySampleRateInput">Whether the stage forces sample-rate shading.</param>
/// <param name="ResultType">The return type's layout, where it has one.</param>
/// <param name="ParameterBase">
/// Where the entry point's own parameter block starts, per category. Entry-point
/// <c>uniform</c> parameters report offsets <em>relative</em> to this, so an absolute binding is
/// this plus the parameter's own offset.
/// </param>
/// <param name="UniformSize">
/// Bytes of the implicit buffer holding the entry point's <c>uniform</c> value parameters. Slang
/// places these in a push-constant buffer. 0 when the entry point declares none.
/// </param>
/// <param name="NameOverride">
/// The name the emitted module uses for this entry point, when the shader renamed it with
/// <c>[shader("…")] [vk::name("…")]</c> or Slang chose a different one. Null when the module name
/// and <paramref name="Name"/> agree.
/// </param>
/// <param name="WaveSize">
/// Threads per wave, where the entry point declared <c>[WaveSize(N)]</c>. Null otherwise, which is
/// the usual case and means the device's native wave size applies.
/// </param>
/// <param name="HasDefaultConstantBuffer">
/// Whether Slang wrapped this entry point's <c>uniform</c> parameters in an implicit constant
/// buffer. When false, <paramref name="UniformSize"/> being non-zero means those bytes are laid out
/// somewhere other than a buffer of their own — on Vulkan, in the push-constant range.
/// </param>
/// <param name="Function">
/// The entry point read as an ordinary function declaration: its source-level signature, its
/// modifiers and its user attributes.
/// </param>
/// <remarks>
/// <paramref name="Parameters"/> and <paramref name="Function"/> describe the same declaration and
/// are not redundant. The first is the <em>pipeline interface</em> — varyings and system values
/// with their locations and semantics, which is what a renderer binds against. The second is what
/// the author wrote, including the attributes that are not layout at all: an entry point's
/// <c>[shader(…)]</c> and any user attribute a material system tagged it with.
/// </remarks>
public sealed record EntryPointLayout(
    string Name,
    SlangStage Stage,
    IReadOnlyList<ShaderParameter> Parameters,
    ThreadGroupSize? ThreadGroupSize = null,
    bool UsesAnySampleRateInput = false,
    TypeLayout? ResultType = null,
    IReadOnlyList<ParameterBinding>? ParameterBase = null,
    ulong UniformSize = 0,
    string? NameOverride = null,
    ulong? WaveSize = null,
    bool HasDefaultConstantBuffer = false,
    FunctionSignature? Function = null)
{
    /// <summary>
    /// The entry point's base offset in one category, or 0 if it occupies none. A loop: the LINQ
    /// form's closure over the category was 80 bytes a call (AllocationTests).
    /// </summary>
    public uint BaseOffset(SlangParameterCategory category)
    {
        if (ParameterBase is null) return 0;
        for (var i = 0; i < ParameterBase.Count; i++)
        {
            if (ParameterBase[i].Category == category) return ParameterBase[i].Index;
        }

        return 0;
    }
}

/// <summary>Compute thread-group dimensions.</summary>
/// <remarks>
/// A dimension of 0 does not mean zero threads; it means Slang could not report one. See
/// <see cref="IsSpecialized"/>, and prefer <see cref="Resolved"/> over reading
/// <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> straight into a divisor.
/// </remarks>
public readonly record struct ThreadGroupSize(ulong X, ulong Y, ulong Z)
{
    /// <summary>
    /// Whether a dimension is driven by a specialization constant, so this is not the launch size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[numthreads(kGroupX, 1, 1)]</c> with <c>kGroupX</c> a specialization constant makes Slang
    /// report <c>[0, 1, 1]</c>: it genuinely does not know, because the value arrives at pipeline
    /// creation. The module is honest about it and emits <c>OpExecutionModeId LocalSizeId</c>
    /// naming the constants, where a fixed size emits <c>OpExecutionMode LocalSize</c> with
    /// literals.
    /// </para>
    /// <para>
    /// The zero is the residue of a fix, which is worth knowing before treating it as an oversight.
    /// This exact call used to access-violate on this exact shader — shader-slang/slang#8738, "AV in
    /// spReflectionEntryPoint_getComputeThreadGroupSize when using specialization constants for
    /// NumThreads attribute", closed December 2025 — and the fix was to check for a specialization
    /// constant before dereferencing the extent. So the crash became a zero, and the zero is what
    /// arrives here.
    /// </para>
    /// <para>
    /// The 0 is the danger. A host computing <c>(width + size.X - 1) / size.X</c> divides by zero,
    /// and a host that guards the divide dispatches a wrong count instead. Read
    /// <c>SpirvModule.Read(code).EntryPoints[i].WorkgroupSize</c> for the constant ids and the
    /// declared defaults; that is the only place the full answer exists.
    /// </para>
    /// </remarks>
    public bool IsSpecialized => X == 0 || Y == 0 || Z == 0;

    /// <summary>The dimensions when all three are known, else null.</summary>
    public (ulong X, ulong Y, ulong Z)? Resolved => IsSpecialized ? null : (X, Y, Z);

    public override string ToString() => IsSpecialized
        ? $"[{Dim(X)}, {Dim(Y)}, {Dim(Z)}]"
        : $"[{X}, {Y}, {Z}]";

    private static string Dim(ulong v) => v == 0 ? "specialized" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// A constant whose value is supplied when the pipeline is created rather than when the shader is
/// compiled — <c>VkSpecializationInfo</c> on Vulkan, <c>OpSpecConstant</c> in the module.
/// </summary>
/// <param name="Name">Declared name.</param>
/// <param name="ConstantId">
/// The id the two sides agree on: <c>constant_id</c> in the shader,
/// <c>VkSpecializationMapEntry::constantID</c> in the host. Explicit when the declaration gave one,
/// assigned by Slang otherwise.
/// </param>
/// <param name="ScalarType">
/// What kind of value it takes. This is where the byte size comes from — see
/// <see cref="ProgramLayout.SpecializationConstants"/>.
/// </param>
/// <param name="DefaultInt">The declared default, for an integral or boolean constant.</param>
/// <param name="DefaultFloat">The declared default, for a floating-point constant.</param>
/// <remarks>
/// Declare one of these as <c>[vk::constant_id(N)] const int kFoo = 4;</c> for an explicit id, or
/// <c>[SpecializationConstant] const int kFoo = 4;</c> to let Slang assign one. Both forms are
/// verified against the emitted module's <c>SpecId</c> decorations by
/// <c>SpecializationConstantTests</c>; a plain <c>const int</c> without an attribute is a compile
/// error in Slang ("global const variable with initializer must be declared static").
/// </remarks>
public sealed record SpecializationConstant(
    string Name,
    uint ConstantId,
    SlangScalarType ScalarType,
    long? DefaultInt = null,
    double? DefaultFloat = null)
{
    /// <summary>
    /// Size in bytes of the value, as the host must supply it.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="ScalarType"/>, not from Slang's reported size. Slang measures a
    /// specialization constant in the <c>SPECIALIZATION_CONSTANT</c> category, where the answer is
    /// always 1 — one constant, not one byte — and its uniform size is 0. Using either would write
    /// a one-byte or zero-byte <c>VkSpecializationMapEntry</c> for a four-byte int.
    ///
    /// Booleans are 4, not 1: Vulkan requires the data for a boolean specialization constant to be
    /// a <c>VkBool32</c>.
    /// </remarks>
    [JsonIgnore]
    public uint ByteSize => ScalarType switch
    {
        SlangScalarType.SLANG_SCALAR_TYPE_BOOL => 4,
        SlangScalarType.SLANG_SCALAR_TYPE_INT8 or SlangScalarType.SLANG_SCALAR_TYPE_UINT8 => 1,
        SlangScalarType.SLANG_SCALAR_TYPE_INT16 or SlangScalarType.SLANG_SCALAR_TYPE_UINT16
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 => 2,
        SlangScalarType.SLANG_SCALAR_TYPE_INT64 or SlangScalarType.SLANG_SCALAR_TYPE_UINT64
            or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64 => 8,
        _ => 4,
    };

    /// <summary>Whether the value is floating point, and so which default field carries it.</summary>
    [JsonIgnore]
    public bool IsFloatingPoint => ScalarType
        is SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16
        or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT32
        or SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64;
}

/// <summary>What a program-level specialisation parameter expects.</summary>
/// <remarks>
/// There is deliberately no <c>Value</c> member. Slang's program-level specialisation API is
/// type-only: a value generic such as <c>struct Buffer&lt;let N : int&gt;</c> produces zero
/// specialisation parameters, and so does a link-time constant
/// (<c>extern static const int kCount;</c>). Measured against slang-compiler 2026.21.
///
/// Value parameters are real, but they live on the generic declaration rather than on the program,
/// so they are surfaced as <see cref="GenericParameter"/> on <see cref="DeclarationNode"/> instead.
/// Keeping a member here that the walker can never emit would be the same defect as a
/// <c>DeclarationKind.Interface</c> that never appears.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SpecializationParameterKind>))]
public enum SpecializationParameterKind
{
    /// <summary>A named type argument, optionally constrained to interfaces.</summary>
    Type,

    /// <summary>
    /// An existential slot: the shader used an interface type directly, so the concrete type is
    /// supplied at bind time rather than at link time. Reported unnamed and unconstrained.
    /// </summary>
    Existential,
}

/// <summary>Whether a generic parameter takes a type or a compile-time value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GenericParameterKind>))]
public enum GenericParameterKind
{
    /// <summary><c>&lt;T : IFoo&gt;</c></summary>
    Type,

    /// <summary><c>&lt;let N : int&gt;</c></summary>
    Value,
}

/// <summary>A parameter of a generic declaration.</summary>
/// <param name="Name">Parameter name as declared.</param>
/// <param name="Kind">Type or value.</param>
/// <param name="Constraints">Interfaces a type parameter must implement; empty for value parameters.</param>
public sealed record GenericParameter(
    string Name,
    GenericParameterKind Kind,
    IReadOnlyList<string> Constraints);

/// <summary>A generic parameter awaiting a concrete argument.</summary>
/// <param name="Index">Position in the argument list passed to <c>LinkOptions</c>.</param>
/// <param name="Name">Parameter name as declared, where Slang reports one.</param>
/// <param name="Kind">What kind of argument it expects.</param>
/// <param name="Constraints">
/// Interfaces a type argument must implement. Check candidates against these before specialising —
/// a mismatch is otherwise only a Slang diagnostic at link time.
/// </param>
public sealed record SpecializationParameter(
    int Index,
    string? Name,
    SpecializationParameterKind Kind,
    IReadOnlyList<string> Constraints);

/// <summary>
/// What a declaration is, mirroring Slang's <c>SlangDeclKind</c>.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Interface</c> member, and this is a genuine gap in Slang's
/// declaration reflection rather than a modelling choice here: an <c>interface</c> declaration
/// comes back as <see cref="Unsupported"/>, with its name intact but its nature unreported.
/// Verified against slang-compiler 2026.21.
///
/// So to find the interfaces a module offers, take the <see cref="Unsupported"/> nodes as
/// candidates, resolve each by name with <c>FindType</c>, and confirm with
/// <c>LinkedProgram.IsSubType</c> — an interface is a type that other types are subtypes of.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DeclarationKind>))]
public enum DeclarationKind
{
    Unsupported,
    Module,
    Struct,
    Func,
    Generic,
    Variable,
    Namespace,
    Enum,
}

/// <summary>One generic parameter, with the argument it was given.</summary>
/// <param name="Parameter">The parameter's declared name — <c>T</c>, <c>N</c>.</param>
/// <param name="Kind">Whether it takes a type or a value.</param>
/// <param name="Type">The concrete type's name, for a <see cref="GenericParameterKind.Type"/>.</param>
/// <param name="Value">The concrete value, for a <see cref="GenericParameterKind.Value"/>.</param>
public readonly record struct GenericArgumentBinding(
    string Parameter,
    GenericParameterKind Kind,
    string? Type = null,
    long? Value = null)
{
    public override string ToString() =>
        $"{Parameter} = {(Kind == GenericParameterKind.Type ? Type ?? "?" : Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?")}";
}

/// <summary>
/// A generic declaration with concrete arguments supplied, and the layout that results.
/// </summary>
/// <remarks>
/// <para>
/// The answer to "what would <c>Wrapper&lt;Lambert&gt;</c> lay out as" for a host that has not
/// written a shader using it. A material system choosing an implementation needs the byte offsets
/// before it can fill a buffer, and until this existed the only way to get them was to author a
/// shader that declared the specialization and reflect that.
/// </para>
/// <para>
/// A snapshot like everything else in this namespace: no native pointer survives the call, so it
/// outlives the program it came from.
/// </para>
/// </remarks>
/// <param name="Name">The specialized type's full name, e.g. <c>Wrapper&lt;Lambert&gt;</c>.</param>
/// <param name="Arguments">Each generic parameter and what it was bound to.</param>
/// <param name="Layout">
/// The specialized type's layout — field offsets, sizes, strides. Null when the generic wraps
/// something that is not a type; see <paramref name="Kind"/>.
/// </param>
/// <param name="Kind">
/// What the generic wraps. A generic in Slang is a declaration of some kind with parameters bolted
/// on, and <c>struct Wrapper&lt;T&gt;</c> and <c>float3 shade&lt;T&gt;(…)</c> are both generics —
/// so a caller specializing one by name has to be told which it got.
/// </param>
/// <param name="Function">
/// The specialized signature, when <paramref name="Kind"/> is <see cref="DeclarationKind.Func"/>.
/// Null otherwise. This is where a generic function's parameter types come back <em>substituted</em>
/// rather than symbolic.
/// </param>
public sealed record SpecializedType(
    string Name,
    IReadOnlyList<GenericArgumentBinding> Arguments,
    TypeLayout? Layout,
    DeclarationKind Kind = DeclarationKind.Struct,
    FunctionSignature? Function = null)
{
    public override string ToString() => Name;
}

/// <summary>How a function parameter is passed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParameterDirection>))]
public enum ParameterDirection
{
    /// <summary>Read by the function. Slang's default, and what an undecorated parameter is.</summary>
    In,

    /// <summary>Written by the function; its incoming value is not read.</summary>
    Out,

    /// <summary>Both.</summary>
    InOut,
}

/// <summary>One parameter of a function declaration.</summary>
/// <param name="Name">As declared.</param>
/// <param name="Type">
/// The full type name, generic arguments included — <c>StructuredBuffer&lt;float4&gt;</c> rather
/// than <c>StructuredBuffer</c>.
/// </param>
/// <param name="Direction">Whether the function reads it, writes it, or both.</param>
/// <param name="Attributes">User attributes on the parameter, if any.</param>
/// <remarks>
/// Deliberately carries no semantic. <c>GetSemanticName</c> exists only on a <em>variable
/// layout</em>, and an ordinary function parameter has no layout — a semantic is a property of a
/// pipeline interface, which is what <see cref="EntryPointLayout"/> reports. A nullable
/// <c>Semantic</c> here would be a field that is always null, which is worse than its absence.
/// </remarks>
public sealed record FunctionParameter(
    string Name,
    string Type,
    ParameterDirection Direction,
    IReadOnlyList<UserAttribute>? Attributes = null);

/// <summary>
/// A function declaration's signature, as Slang reports it.
/// </summary>
/// <remarks>
/// <para>
/// The declaration tree says a function <em>exists</em>; this says what it looks like. That is the
/// difference between a material system that can list a shader's hooks and one that can check a
/// candidate function actually matches the hook it is being bound to.
/// </para>
/// <para>
/// Not the same thing as <see cref="EntryPointLayout"/>. An entry point's parameters are a
/// <em>pipeline interface</em> — varyings, system values and uniforms with bindings — and reflection
/// reports them through the layout API. These are ordinary function parameters, with no binding and
/// no layout, and they exist for every function in the module rather than only the entry points.
/// </para>
/// </remarks>
/// <param name="Name">As declared.</param>
/// <param name="ResultType">Full name of the return type; <c>void</c> for none.</param>
/// <param name="Parameters">In declaration order.</param>
/// <param name="Modifiers">
/// Slang's declaration modifiers that are present — <c>static</c>, <c>mutating</c>,
/// <c>differentiable</c>, <c>[no_diff]</c> and so on. Reported as the names Slang's
/// <c>SlangModifierID</c> uses, lowercased.
/// </param>
/// <param name="Attributes">User attributes on the function, if any.</param>
/// <param name="Overloads">
/// The other declarations sharing this name, when the name is overloaded. Slang reports an
/// overloaded name as a single function with the real declarations underneath, and the outer one's
/// own signature is <em>empty</em> — <c>void overloaded()</c>, which is not a signature any
/// declaration in the source has. So a caller that reads <see cref="Parameters"/> without checking
/// <see cref="IsOverloaded"/> gets a function that appears to take nothing.
///
/// Only ever populated by <c>LinkedProgram.FindFunction</c>. The declaration tree reports the same
/// two declarations as two sibling nodes with real signatures and this field null, which is the
/// more useful view and the reason both exist.
/// </param>
public sealed record FunctionSignature(
    string Name,
    string ResultType,
    IReadOnlyList<FunctionParameter> Parameters,
    IReadOnlyList<string>? Modifiers = null,
    IReadOnlyList<UserAttribute>? Attributes = null,
    IReadOnlyList<FunctionSignature>? Overloads = null)
{
    /// <summary>Whether Slang reported more than one declaration under this name.</summary>
    [JsonIgnore]
    public bool IsOverloaded => Overloads is { Count: > 0 };

    /// <summary>C-like rendering, for diagnostics and for reading a tree dump.</summary>
    public override string ToString() =>
        $"{ResultType} {Name}({string.Join(", ", Parameters.Select(p =>
            (p.Direction == ParameterDirection.In ? "" : p.Direction.ToString().ToLowerInvariant() + " ")
            + $"{p.Type} {p.Name}"))})";
}

/// <summary>
/// A node in a module's declaration tree. Lets tooling enumerate what a shader offers — which
/// structs implement which interfaces, which functions are available — without parsing source.
/// </summary>
/// <param name="Name">Declared name; empty for anonymous scopes.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Children">Nested declarations, in declaration order.</param>
/// <param name="GenericParameters">
/// For a <see cref="DeclarationKind.Generic"/> node, its type and value parameters. Empty
/// otherwise. This is where value generics surface — see
/// <see cref="SpecializationParameterKind"/> for why they are not program-level parameters.
/// </param>
/// <param name="Function">
/// For a <see cref="DeclarationKind.Func"/> node, its signature. Null otherwise.
/// </param>
/// <param name="QualifiedName">
/// The name with its enclosing declarations, dotted — <c>Materials.Lambert.shade</c>. Built by
/// walking <c>getParent</c> from the declaration itself rather than by joining the tree on the way
/// down, so it is the compiler's own nesting and not this walk's.
/// </param>
/// <param name="Modifiers">
/// Slang's declaration modifiers present on this declaration, lowercased — <c>static</c>,
/// <c>extern</c>, <c>differentiable</c>. Null when it carries none.
/// </param>
/// <param name="VariableType">
/// For a <see cref="DeclarationKind.Variable"/> node, the declared type's full name. Null
/// otherwise.
/// </param>
/// <param name="OuterGeneric">
/// For a <see cref="DeclarationKind.Generic"/> node nested inside another generic, the outer one's
/// name. Null at the top level, which is the usual case.
/// </param>
public sealed record DeclarationNode(
    string Name,
    DeclarationKind Kind,
    IReadOnlyList<DeclarationNode> Children,
    IReadOnlyList<GenericParameter>? GenericParameters = null,
    FunctionSignature? Function = null,
    string? QualifiedName = null,
    IReadOnlyList<string>? Modifiers = null,
    string? VariableType = null,
    string? OuterGeneric = null)
{
    /// <summary>
    /// Whether this is a compiler-synthesised declaration rather than something the author wrote.
    /// </summary>
    /// <remarks>
    /// Slang's tree includes internals — <c>$init</c> constructors, <c>$inheritance</c> markers,
    /// and the <c>This</c> type inside an interface. They are kept rather than filtered so the
    /// tree stays a faithful report, but a type picker or documentation generator wants to skip
    /// them.
    /// </remarks>
    [JsonIgnore]
    public bool IsSynthetic =>
        Name.StartsWith('$') || string.Equals(Name, "This", StringComparison.Ordinal);

    /// <summary>Every descendant of a given kind, depth-first. Synthetic nodes are skipped.</summary>
    public IEnumerable<DeclarationNode> Descendants(DeclarationKind kind)
    {
        foreach (var child in Children)
        {
            if (child.Kind == kind && !child.IsSynthetic) yield return child;
            foreach (var nested in child.Descendants(kind)) yield return nested;
        }
    }
}

/// <summary>
/// A complete, immutable snapshot of one linked program's layout. Safe to keep after the
/// <c>LinkedProgram</c> is disposed, and serializable so the asset pipeline can bake it
/// beside the SPIR-V and keep the compiler out of shipped builds.
/// </summary>
public sealed record ProgramLayout
{
    /// <summary>Top-level (global scope) shader parameters.</summary>
    public required IReadOnlyList<ShaderParameter> Parameters { get; init; }

    /// <summary>Entry points, in the order they were linked.</summary>
    public required IReadOnlyList<EntryPointLayout> EntryPoints { get; init; }

    /// <summary>Descriptor sets derived from the global scope.</summary>
    public required IReadOnlyList<DescriptorSet> DescriptorSets { get; init; }

    /// <summary>Bytes of implicit global constant buffer, if the shader has loose global uniforms.</summary>
    public required ulong GlobalConstantBufferSize { get; init; }

    /// <summary>Binding index of that implicit buffer, when one exists.</summary>
    public required uint GlobalConstantBufferBinding { get; init; }

    /// <summary>Hashed string table, populated when the shader uses <c>getStringHash</c>.</summary>
    public IReadOnlyList<string> HashedStrings { get; init; } = [];

    /// <summary>
    /// Specialization constants, ordered by id — everything needed to build a
    /// <c>VkSpecializationInfo</c> without either side hardcoding an id.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="Parameters"/> rather than replacing them: a specialization
    /// constant genuinely is a top-level parameter as far as Slang is concerned, and the parameter
    /// list stays a faithful report of what Slang said. This is the usable form.
    /// </remarks>
    public IReadOnlyList<SpecializationConstant> SpecializationConstants { get; init; } = [];

    /// <summary>
    /// Generic parameters still awaiting concrete types. Non-empty means the program was linked
    /// without specialisation and cannot generate code yet.
    /// </summary>
    /// <remarks>
    /// This is the hook a data-driven material system reads: it tells you what the shader is
    /// asking to be told, so the host can supply types per material rather than compiling a
    /// pipeline per <c>#ifdef</c> combination.
    /// </remarks>
    public IReadOnlyList<SpecializationParameter> SpecializationParameters { get; init; } = [];

    /// <summary>
    /// The register space Slang reserved for bindless resources, or null if the program uses none.
    /// </summary>
    /// <remarks>
    /// Distinct from an unbounded descriptor array, which is an ordinary binding whose count the
    /// host chooses. A bindless <em>heap</em> is a whole space Slang sets aside, and a host that
    /// allocates descriptor sets by index has to know not to use it. Slang reports -1 for "none",
    /// normalised to null here so it cannot be read as a space number.
    /// </remarks>
    public uint? BindlessSpaceIndex { get; init; }

    /// <summary>
    /// The global scope's binding ranges that are themselves objects with their own bindings.
    /// </summary>
    /// <remarks>
    /// Empty for a shader whose parameters are all bound directly. See <see cref="SubObjectRange"/>
    /// for why this is the only view that connects a space back to the parameter that owns it.
    /// </remarks>
    public IReadOnlyList<SubObjectRange> SubObjectRanges { get; init; } = [];

    /// <summary>
    /// The module's declaration tree — its structs, functions, interfaces and generics. Present
    /// for tooling and material authoring; the renderer does not need it.
    /// </summary>
    public DeclarationNode? Declarations { get; init; }

    /// <summary>Finds a top-level parameter by name, or null. A loop: allocates nothing.</summary>
    public ShaderParameter? Parameter(string name)
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            if (string.Equals(Parameters[i].Name, name, StringComparison.Ordinal)) return Parameters[i];
        }

        return null;
    }

    /// <summary>Finds an entry point by name, or null. A loop: allocates nothing.</summary>
    public EntryPointLayout? EntryPoint(string name)
    {
        for (var i = 0; i < EntryPoints.Count; i++)
        {
            if (string.Equals(EntryPoints[i].Name, name, StringComparison.Ordinal)) return EntryPoints[i];
        }

        return null;
    }

    /// <summary>Finds a specialization constant by name, or null. A loop: allocates nothing.</summary>
    public SpecializationConstant? SpecializationConstant(string name)
    {
        for (var i = 0; i < SpecializationConstants.Count; i++)
        {
            if (string.Equals(SpecializationConstants[i].Name, name, StringComparison.Ordinal)) return SpecializationConstants[i];
        }

        return null;
    }

    /// <summary>Serializes for offline baking. Round-trips through <see cref="FromJson"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, ProgramLayoutJson.Default.ProgramLayout);

    /// <summary>Reads a baked layout back.</summary>
    public static ProgramLayout FromJson(string json) =>
        JsonSerializer.Deserialize(json, ProgramLayoutJson.Default.ProgramLayout)
        ?? throw new ArgumentException("not a serialized ProgramLayout", nameof(json));
}

/// <summary>
/// The layout's JSON, generated at compile time: no reflection, so the reader trims and runs under
/// Native AOT (the reflection-based serializer it replaced was the publish's IL2104 and IL3053).
/// </summary>
/// <remarks>
/// NewLine is pinned, because this string is written into the shader package and a package is a
/// build artifact. WriteIndented newlines default to Environment.NewLine, so the same shader baked
/// on Windows and on Linux produced packages differing by one byte per line — 214 of them on an
/// ordinary layout. Nothing was wrong with either package and both loaded fine; what broke was
/// equality: a content-addressed asset cache misses across build machines. Found by digesting the
/// corpus on two platforms and diffing: all 28 SPIR-V modules were byte-identical and all 28
/// layouts differed.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ProgramLayout))]
internal sealed partial class ProgramLayoutJson : JsonSerializerContext;
