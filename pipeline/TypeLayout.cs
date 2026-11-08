// L3 — the type tree, as immutable managed data.
//
// This is a snapshot, not a proxy. Every native reflection pointer is read during the walk and
// discarded; nothing here holds one. That is deliberate (docs §4.4): SlangReflectionTypeLayout* is
// an interior pointer owned by the IComponentType that produced it, so a proxy would become a
// use-after-free the moment the program is disposed — presenting as corrupt descriptor bindings
// rather than a clean crash.
//
// Everything is Slang-neutral. Sizes and offsets are what Slang computed for the configured
// target; nothing here interprets them for a specific graphics API. The NRI reading of this
// data lives in the separate SharpSlang.Pipeline.NRI package so that a consumer that does not talk
// to NRI never pays for it.

using System.Text.Json.Serialization;
using SharpSlang.Native;

namespace SharpSlang.Reflection;

/// <summary>What kind of type a layout describes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LayoutKind>))]
public enum LayoutKind
{
    Unknown,
    Scalar,
    Vector,
    Matrix,
    Struct,
    Array,
    Resource,
    SamplerState,
    ConstantBuffer,
    ParameterBlock,
    TextureBuffer,
    ShaderStorageBuffer,

    /// <summary>
    /// An unbound generic parameter — the <c>T</c> of a <c>struct Thing&lt;T&gt;</c> that nothing
    /// has substituted yet.
    /// </summary>
    /// <remarks>
    /// Rare, because a linked program has its generics resolved and that is what linking is for.
    /// It is reachable by reflecting a program with global generic type parameters, which is what
    /// Slang's own <c>tests/reflection/global-type-params.slang</c> does — and until that corpus
    /// was compared against this projection, such a type was reported as <see cref="Unknown"/>,
    /// which said "the walker could not identify this" about something Slang names precisely.
    /// <see cref="TypeLayout.GenericParameterIndex"/> says <em>which</em> parameter it is.
    /// </remarks>
    GenericTypeParameter,

    Interface,
    Specialized,
    Feedback,
    Pointer,
    DynamicResource,
}

/// <summary>
/// A size measured in one of Slang's resource categories. A single type can consume several at
/// once — a struct holding a texture and a float occupies both uniform bytes and a descriptor
/// slot — which is why sizes are reported per category rather than as one number.
/// </summary>
/// <param name="Category">The resource kind being measured.</param>
/// <param name="Size">
/// Units consumed. Bytes for <c>Uniform</c>, slots otherwise. Check <see cref="IsKnown"/> before
/// doing arithmetic with it — two of the values Slang can return here are markers, not counts.
/// </param>
public readonly record struct CategorySize(SlangParameterCategory Category, ulong Size)
{
    /// <summary>Slang's marker for a runtime-sized array: <c>~size_t(0)</c>.</summary>
    public const ulong Unbounded = ulong.MaxValue;

    /// <summary>
    /// Slang's marker for a size that depends on something not yet resolved — a specialization
    /// constant used as an array length, or an unresolved generic parameter.
    /// </summary>
    public const ulong Unknown = ulong.MaxValue - 1;

    /// <summary>
    /// Whether <see cref="Size"/> is a measurement rather than a marker.
    /// </summary>
    /// <remarks>
    /// The markers are near <c>ulong.MaxValue</c>, so using one as a size does not fail loudly —
    /// it allocates 16 exabytes, or wraps, or produces a <c>VkPushConstantRange</c> the driver
    /// rejects with nothing pointing at the shader that caused it.
    /// </remarks>
    public bool IsKnown => Size < Unknown;
}

/// <summary>Base for every node in the type tree.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ScalarLayout), "scalar")]
[JsonDerivedType(typeof(VectorLayout), "vector")]
[JsonDerivedType(typeof(MatrixLayout), "matrix")]
[JsonDerivedType(typeof(StructLayout), "struct")]
[JsonDerivedType(typeof(ArrayLayout), "array")]
[JsonDerivedType(typeof(ResourceLayout), "resource")]
[JsonDerivedType(typeof(ContainerLayout), "container")]
[JsonDerivedType(typeof(OpaqueLayout), "opaque")]
public abstract record TypeLayout
{
    /// <summary>What kind of type this is.</summary>
    public required LayoutKind Kind { get; init; }

    /// <summary>Slang's name for the type, where it has one (<c>float3</c>, <c>Uniforms</c>).</summary>
    public string? Name { get; init; }

    /// <summary>Sizes per resource category. Empty for types that consume nothing.</summary>
    public IReadOnlyList<CategorySize> Sizes { get; init; } = [];

    /// <summary>Byte alignment of the uniform footprint, or 0 if it has none.</summary>
    public int Alignment { get; init; }

    /// <summary>
    /// User attributes on the type's own declaration — <c>[MyTag(3)] struct Material</c> — or null.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="FieldLayout.Attributes"/>, which are on a field, and reached by a
    /// different call. A material system tagging its <em>types</em> rather than its fields had no
    /// path to these at all: variables and functions reported their attributes and types did not.
    /// </remarks>
    public IReadOnlyList<UserAttribute>? Attributes { get; init; }

    /// <summary>
    /// Which generic parameter this type <em>is</em>, for a layout standing in for an unbound
    /// <c>T</c>; null for an ordinary type.
    /// </summary>
    /// <remarks>
    /// Slang reports -1 for "not a generic parameter", normalised away here. Non-null has not been
    /// observed on any shader this project compiles — a linked program has its generics resolved,
    /// which is what linking is — so treat it as the marker for a layout obtained before that.
    /// </remarks>
    public uint? GenericParameterIndex { get; init; }

    /// <summary>
    /// Whether Slang attached <em>pending</em> layout data to this type — bytes whose position is
    /// not decided until specialization is.
    /// </summary>
    /// <remarks>
    /// Null on every shader tried here, specialized and unspecialized alike, which is why this is
    /// a flag rather than a projected sub-layout: reporting the shape of something never seen
    /// would be inventing it. If this ever comes back true, the layout it refers to is reachable
    /// through <c>getPendingDataTypeLayout</c> and worth projecting properly.
    /// </remarks>
    public bool HasPendingData { get; init; }

    /// <summary>Convenience: the uniform (byte) footprint, or 0 if this type occupies no bytes.</summary>
    /// <remarks>
    /// Reports Slang's answer as given, markers included — see <see cref="CategorySize.IsKnown"/>
    /// and <see cref="IsUniformSizeKnown"/>. An array whose length is a specialization constant
    /// measures <c>0xFFFF_FFFF_FFFF_FFFE</c> bytes here, which is not a number of bytes.
    /// </remarks>
    [JsonIgnore]
    public ulong UniformSize => UniformCategorySize.Size;

    /// <summary>
    /// Whether <see cref="UniformSize"/> is a byte count rather than one of Slang's markers.
    /// </summary>
    /// <remarks>
    /// False for a type whose footprint depends on a specialization constant. A host cannot size a
    /// buffer for one of those, and neither can this package — so the question has to be asked
    /// before the answer is used, rather than after a driver rejects the range.
    /// </remarks>
    [JsonIgnore]
    public bool IsUniformSizeKnown => UniformCategorySize.IsKnown;

    private CategorySize UniformCategorySize =>
        Sizes.FirstOrDefault(s => s.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
}

/// <summary>A single numeric or boolean value.</summary>
public sealed record ScalarLayout : TypeLayout
{
    public required SlangScalarType ScalarType { get; init; }
}

/// <summary>A fixed-length vector, e.g. <c>float3</c>.</summary>
public sealed record VectorLayout : TypeLayout
{
    public required SlangScalarType ElementType { get; init; }
    public required int ElementCount { get; init; }
}

/// <summary>A matrix. Row/column counts are as declared; storage order follows the session's layout mode.</summary>
/// <remarks>
/// <para>
/// <b>The mode is stated in Slang's terms, and the emitted module decorates the opposite one.</b>
/// A session set to <c>ROW_MAJOR</c> produces members decorated <c>ColMajor</c>, and vice versa.
/// That is not a contradiction: HLSL writes <c>float4x4</c> as rows-by-columns while SPIR-V's
/// <c>OpTypeMatrix</c> is a list of column vectors, so the same bytes are described by opposite
/// words. Both were measured against slang-compiler 2026.21 across four matrix shapes in both
/// modes. Read <see cref="Mode"/> and <see cref="Stride"/> together and ignore the decoration; the
/// pair says exactly where a component goes without anyone having to hold the transposition in
/// their head.
/// </para>
/// </remarks>
public sealed record MatrixLayout : TypeLayout
{
    public required SlangScalarType ElementType { get; init; }
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required SlangMatrixLayoutMode Mode { get; init; }

    /// <summary>
    /// Bytes between one row and the next under <c>ROW_MAJOR</c>, or one column and the next under
    /// <c>COLUMN_MAJOR</c>. 0 for a matrix with no uniform footprint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The member a host cannot do without and Slang does not offer:
    /// <c>spReflectionTypeLayout_GetStride</c> on a matrix returns the whole matrix, and
    /// <c>GetElementStride</c> returns 0. So this is derived — total size divided by the number of
    /// major-axis entries — and <c>UniformLayoutTests</c> asserts it against the module's own
    /// <c>MatrixStride</c> decoration for four shapes in both layout modes, so the derivation
    /// cannot quietly stop matching.
    /// </para>
    /// <para>
    /// It is almost never the packed width. A <c>float3x3</c> is 48 bytes, not 36: three rows on a
    /// 16-byte stride. A row-major <c>float4x2</c> is 64 bytes, not 32. Writing the components
    /// contiguously produces a matrix that is wrong in a way that looks like a bad transform rather
    /// than like bad memory.
    /// </para>
    /// </remarks>
    public ulong Stride { get; init; }

    /// <summary>
    /// Byte offset of one component within this matrix, relative to the matrix's own start.
    /// </summary>
    /// <param name="row">Row index, as declared in the shader.</param>
    /// <param name="column">Column index, as declared in the shader.</param>
    /// <remarks>
    /// Handles the major-axis choice so a caller does not have to. Add this to the field's
    /// <see cref="FieldLayout.UniformOffset"/> to get the offset within the enclosing buffer.
    /// </remarks>
    public ulong ComponentOffset(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, ColumnCount);

        var scalar = ScalarSize(ElementType);
        return Mode == SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR
            ? ((ulong)column * Stride) + ((ulong)row * scalar)
            : ((ulong)row * Stride) + ((ulong)column * scalar);
    }

    private static ulong ScalarSize(SlangScalarType type) => type switch
    {
        SlangScalarType.SLANG_SCALAR_TYPE_FLOAT64 or SlangScalarType.SLANG_SCALAR_TYPE_INT64
            or SlangScalarType.SLANG_SCALAR_TYPE_UINT64 => 8,
        SlangScalarType.SLANG_SCALAR_TYPE_FLOAT16 or SlangScalarType.SLANG_SCALAR_TYPE_INT16
            or SlangScalarType.SLANG_SCALAR_TYPE_UINT16 => 2,
        SlangScalarType.SLANG_SCALAR_TYPE_INT8 or SlangScalarType.SLANG_SCALAR_TYPE_UINT8 => 1,
        _ => 4,
    };
}

/// <summary>A field of a struct, with the offsets Slang assigned it.</summary>
/// <param name="Name">Field name as declared.</param>
/// <param name="Type">The field's layout.</param>
/// <param name="Offsets">Offset per category. Bytes for <c>Uniform</c>, slot index otherwise.</param>
/// <param name="Semantic">HLSL-style semantic, e.g. <c>SV_Position</c>, if declared.</param>
/// <param name="SemanticIndex">Index on the semantic, e.g. 0 for <c>TEXCOORD0</c>.</param>
/// <param name="Attributes">User attributes declared on the field.</param>
public sealed record FieldLayout(
    string Name,
    TypeLayout Type,
    IReadOnlyList<CategoryOffset> Offsets,
    string? Semantic = null,
    uint SemanticIndex = 0,
    IReadOnlyList<UserAttribute>? Attributes = null)
{
    /// <summary>Convenience: byte offset within the enclosing uniform block.</summary>
    /// <remarks>
    /// Reports Slang's answer as given, markers included — check
    /// <see cref="IsUniformOffsetKnown"/> before writing to it.
    /// </remarks>
    [JsonIgnore]
    public ulong UniformOffset => UniformCategoryOffset.Offset;

    /// <summary>
    /// Whether this field has a byte offset at all.
    /// </summary>
    /// <remarks>
    /// False for a field declared after an array whose length is a specialization constant: where
    /// it lands depends on a value that arrives at pipeline creation, so there is no answer until
    /// then. Slang reports the marker rather than a position, and a host that writes at
    /// <c>0xFFFF_FFFF_FFFF_FFFE</c> is writing nowhere near the buffer.
    /// </remarks>
    [JsonIgnore]
    public bool IsUniformOffsetKnown => UniformCategoryOffset.IsKnown;

    private CategoryOffset UniformCategoryOffset =>
        Offsets.FirstOrDefault(o => o.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM);
}

/// <summary>An offset in one resource category.</summary>
/// <param name="Category">The resource kind.</param>
/// <param name="Offset">
/// Bytes for <c>Uniform</c>; a binding/slot index otherwise. Check <see cref="IsKnown"/> first: a
/// field placed after an array whose length is a specialization constant has no offset, and Slang
/// says so with the same near-<c>ulong.MaxValue</c> marker it uses for sizes.
/// </param>
/// <param name="Space">Register space / descriptor set the offset is relative to.</param>
public readonly record struct CategoryOffset(SlangParameterCategory Category, ulong Offset, uint Space)
{
    /// <summary>Whether <see cref="Offset"/> is a position rather than one of Slang's markers.</summary>
    public bool IsKnown => Offset < CategorySize.Unknown;
}

/// <summary>A struct, with its fields in declaration order.</summary>
public sealed record StructLayout : TypeLayout
{
    public required IReadOnlyList<FieldLayout> Fields { get; init; }

    /// <summary>Finds a field by name, or null. Ordinal comparison, as Slang is case-sensitive.</summary>
    public FieldLayout? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}

/// <summary>An array. <see cref="ElementCount"/> is null when the length is not a fixed number.</summary>
public sealed record ArrayLayout : TypeLayout
{
    public required TypeLayout ElementType { get; init; }

    /// <summary>
    /// Elements, or null when the length is not statically known — a runtime-sized array, or one
    /// whose length is a specialization constant. <see cref="IsSpecializationSized"/> separates the
    /// two.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Absent in the layout JSON means null. It must not be <c>required</c>: <c>ToJson</c> drops
    /// nulls, and a <c>required</c> member that is absent is refused on read. It was, and every
    /// package containing an unbounded array baked and wrote fine and then failed in
    /// <c>ReadFrom</c> with "JSON deserialization for type 'SharpSlang.Reflection.ArrayLayout' was
    /// missing required properties including: 'ElementCount'."
    /// </para>
    /// <para>
    /// Fixed on the reading side rather than with <c>[JsonIgnore(Condition = Never)]</c>, which
    /// would write <c>"ElementCount": null</c> instead. This way the written bytes stay identical,
    /// so packages cooked before the fix become readable without a rebake, and "absent means null"
    /// stays the one convention of the whole layout JSON.
    /// </para>
    /// </remarks>
    public int? ElementCount { get; init; }

    /// <summary>
    /// Whether the length is a specialization constant, so it is fixed at pipeline creation rather
    /// than being runtime-sized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported, and reported without asking Slang how many elements there are — because asking is
    /// fatal. <c>spReflectionType_GetElementCount</c> and its
    /// <c>GetSpecializedElementCount</c> counterpart both access-violate on an array of this shape,
    /// measured against slang-compiler 2026.21. The signal used instead is the size marker:
    /// such an array reports <see cref="CategorySize.Unknown"/> for its uniform footprint.
    /// </para>
    /// <para>
    /// That signal only exists for an array with a uniform footprint. A <em>resource</em> array
    /// sized by a specialization constant — <c>Texture2D t[kCount]</c> — is indistinguishable from
    /// a fixed-size one until something asks, and reflecting a shader containing one terminates the
    /// process inside <c>getBindingRangeCount</c>. It is not guarded because it cannot be detected;
    /// see docs . It is also not usable on Vulkan, where <c>descriptorCount</c> is fixed when
    /// the descriptor set layout is created, before any specialization value exists.
    /// </para>
    /// </remarks>
    public bool IsSpecializationSized { get; init; }

    /// <summary>Byte stride between elements, where the array has a uniform footprint.</summary>
    public required ulong ElementStride { get; init; }
}

/// <summary>A texture, buffer, or acceleration structure.</summary>
public sealed record ResourceLayout : TypeLayout
{
    /// <summary>Texture dimensionality and flags (array, multisample, and so on).</summary>
    public required SlangResourceShape Shape { get; init; }

    /// <summary>Read, read-write, or one of the specialised access modes.</summary>
    public required SlangResourceAccess Access { get; init; }

    /// <summary>
    /// The element type sampled or loaded from the resource, described structurally — no sizes and
    /// no offsets, because Slang hands this one out as a bare type rather than a type layout.
    /// </summary>
    /// <remarks>
    /// Use <see cref="ElementType"/> instead for anything that has to touch memory. This member
    /// stays because it names the type (<c>Instance</c>) even where there is no layout to report,
    /// as on a <c>Texture2D&lt;float4&gt;</c>.
    /// </remarks>
    public TypeLayout? ResultType { get; init; }

    /// <summary>
    /// The element's full layout — field byte offsets, sizes, alignment — for a resource that has
    /// one. Null for textures, samplers, byte-address buffers, typed buffers, subpass inputs and
    /// acceleration structures, which have no element layout to report.
    /// </summary>
    /// <remarks>
    /// This is what a host fills a <c>StructuredBuffer&lt;T&gt;</c> from. <see cref="ResultType"/>
    /// names <c>T</c> but reports a size of 0 and no field offsets, because
    /// <c>spReflectionType_GetResourceResultType</c> returns a type and layout questions can only
    /// be asked of a type <em>layout</em> — so a consumer with only <c>ResultType</c> can see that
    /// an instance buffer holds an <c>Instance</c> and still have no idea how many bytes that is or
    /// where <c>xform</c> sits inside it.
    /// </remarks>
    public TypeLayout? ElementType { get; init; }

    /// <summary>
    /// Bytes from one element to the next, for a resource with an <see cref="ElementType"/>;
    /// 0 otherwise. This is the value that sizes an upload and strides an index.
    /// </summary>
    /// <remarks>
    /// Not the same as <c>ElementType.UniformSize</c> in general — the stride includes any tail
    /// padding needed to keep the next element aligned. Asserted against the module's own
    /// <c>ArrayStride</c> decoration by <c>UniformLayoutTests</c>.
    /// </remarks>
    public ulong ElementStride { get; init; }
}

/// <summary>
/// A type that wraps another: <c>ConstantBuffer&lt;T&gt;</c>, <c>ParameterBlock&lt;T&gt;</c>,
/// <c>StructuredBuffer&lt;T&gt;</c>. The interesting content is <see cref="ElementType"/>.
/// </summary>
public sealed record ContainerLayout : TypeLayout
{
    public required TypeLayout ElementType { get; init; }
}

/// <summary>
/// A type the walker recognised but does not model in detail — interfaces, existential values,
/// specialised generics. Kept rather than dropped so a caller can see that something is there.
/// </summary>
public sealed record OpaqueLayout : TypeLayout;

/// <summary>
/// A user-defined attribute, e.g. <c>[format("rgba8")]</c>. A material system can read
/// these to drive data-driven behaviour without hardcoding shader knowledge in C#.
/// </summary>
/// <param name="Name">Attribute name without brackets.</param>
/// <param name="Arguments">Positional arguments, boxed as int, float, or string.</param>
/// <param name="ArgumentTypes">
/// The declared type name of each argument, positionally — <c>int</c>, <c>float</c>,
/// <c>String</c>. Null when Slang reported none.
/// </param>
/// <remarks>
/// <see cref="Arguments"/> used to be built by probing: try the int accessor, then the float one,
/// then the string one, and take whichever succeeded. That works and it guesses — a
/// <c>float</c> argument whose value happens to be integral is a case where the guess and the
/// declaration can disagree. <see cref="ArgumentTypes"/> is what Slang says the argument
/// <em>is</em>, so a consumer that cares can read the declaration rather than the boxed result.
/// The probe still fills <see cref="Arguments"/>, because the accessors are the only way to get a
/// value and they are keyed by type rather than returning a variant.
/// </remarks>
public sealed record UserAttribute(
    string Name,
    IReadOnlyList<object?> Arguments,
    IReadOnlyList<string>? ArgumentTypes = null);
