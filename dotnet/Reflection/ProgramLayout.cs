// L3 — the program-level snapshot: parameters, entry points, and the descriptor-set structure.
//
// Still Slang-neutral. The descriptor-set model here is Slang's own — it is what drives D3D12 root
// signatures as much as Vulkan descriptor set layouts — so it belongs in the core package. The
// Vulkan reading of it lives in PrismSlang.Vulkan.

using System.Text.Json;
using System.Text.Json.Serialization;
using PrismSlang.Native;

namespace PrismSlang.Reflection;

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
public sealed record ShaderParameter(
    string Name,
    TypeLayout Type,
    IReadOnlyList<ParameterBinding> Bindings,
    string? Semantic = null,
    uint SemanticIndex = 0,
    IReadOnlyList<UserAttribute>? Attributes = null)
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
            foreach (var b in Bindings)
            {
                if (b.Category is SlangParameterCategory.SLANG_PARAMETER_CATEGORY_SUB_ELEMENT_REGISTER_SPACE
                               or SlangParameterCategory.SLANG_PARAMETER_CATEGORY_REGISTER_SPACE)
                {
                    return b.Index;
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
/// <remarks>
/// Slang reports an unbounded array's descriptor count as <c>0xFFFFFFFF</c>. That value is not a
/// count and must not reach <c>VkDescriptorSetLayoutBinding::descriptorCount</c>; it is normalised
/// to 0 here with the flag set, because the real cap is a host decision made with
/// <c>VK_DESCRIPTOR_BINDING_VARIABLE_DESCRIPTOR_COUNT_BIT</c> and the device's
/// <c>maxPerStageDescriptor*</c> limits. Measured against slang-compiler 2026.14.1.
/// </remarks>
public sealed record DescriptorRange(
    SlangBindingType BindingType,
    uint IndexOffset,
    uint DescriptorCount,
    SlangParameterCategory Category,
    string? Name = null,
    bool IsUnbounded = false);

/// <summary>A descriptor set (register space) and the ranges within it.</summary>
public sealed record DescriptorSet(uint SpaceOffset, IReadOnlyList<DescriptorRange> Ranges);

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
public sealed record EntryPointLayout(
    string Name,
    SlangStage Stage,
    IReadOnlyList<ShaderParameter> Parameters,
    ThreadGroupSize? ThreadGroupSize = null,
    bool UsesAnySampleRateInput = false,
    TypeLayout? ResultType = null,
    IReadOnlyList<ParameterBinding>? ParameterBase = null,
    ulong UniformSize = 0)
{
    /// <summary>The entry point's base offset in one category, or 0 if it occupies none.</summary>
    public uint BaseOffset(SlangParameterCategory category) =>
        ParameterBase?.FirstOrDefault(b => b.Category == category).Index ?? 0;
}

/// <summary>Compute thread-group dimensions.</summary>
public readonly record struct ThreadGroupSize(ulong X, ulong Y, ulong Z)
{
    public override string ToString() => $"[{X}, {Y}, {Z}]";
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
/// (<c>extern static const int kCount;</c>). Measured against slang-compiler 2026.14.1.
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
/// Verified against slang-compiler 2026.14.1.
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
public sealed record DeclarationNode(
    string Name,
    DeclarationKind Kind,
    IReadOnlyList<DeclarationNode> Children,
    IReadOnlyList<GenericParameter>? GenericParameters = null)
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
/// <see cref="LinkedProgram"/> is disposed, and serializable so the asset pipeline can bake it
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
    /// The module's declaration tree — its structs, functions, interfaces and generics. Present
    /// for tooling and material authoring; the renderer does not need it.
    /// </summary>
    public DeclarationNode? Declarations { get; init; }

    /// <summary>Finds a top-level parameter by name, or null.</summary>
    public ShaderParameter? Parameter(string name) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Finds an entry point by name, or null.</summary>
    public EntryPointLayout? EntryPoint(string name) =>
        EntryPoints.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    /// <summary>Finds a specialization constant by name, or null.</summary>
    public SpecializationConstant? SpecializationConstant(string name) =>
        SpecializationConstants.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes for offline baking. Round-trips through <see cref="FromJson"/>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Reads a baked layout back.</summary>
    public static ProgramLayout FromJson(string json) =>
        JsonSerializer.Deserialize<ProgramLayout>(json, JsonOptions)
        ?? throw new ArgumentException("not a serialized ProgramLayout", nameof(json));
}
