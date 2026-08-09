// L3 — the type tree, as immutable managed data.
//
// This is a snapshot, not a proxy. Every native reflection pointer is read during the walk and
// discarded; nothing here holds one. That is deliberate (docs §4.4): SlangReflectionTypeLayout* is
// an interior pointer owned by the IComponentType that produced it, so a proxy would become a
// use-after-free the moment the program is disposed — presenting as corrupt descriptor bindings
// rather than a clean crash.
//
// Everything is Slang-neutral. Sizes and offsets are what Slang computed for the configured
// target; nothing here interprets them for a specific graphics API. The Vulkan reading of this
// data lives in the separate PrismSlang.Vulkan package so that a D3D12 or Metal consumer never
// pays for it.

using System.Text.Json.Serialization;
using PrismSlang.Native;

namespace PrismSlang.Reflection;

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
/// <param name="Size">Units consumed. Bytes for <c>Uniform</c>, slots otherwise.</param>
public readonly record struct CategorySize(SlangParameterCategory Category, ulong Size);

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

    /// <summary>Convenience: the uniform (byte) footprint, or 0 if this type occupies no bytes.</summary>
    [JsonIgnore]
    public ulong UniformSize =>
        Sizes.FirstOrDefault(s => s.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM).Size;
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
public sealed record MatrixLayout : TypeLayout
{
    public required SlangScalarType ElementType { get; init; }
    public required int RowCount { get; init; }
    public required int ColumnCount { get; init; }
    public required SlangMatrixLayoutMode Mode { get; init; }
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
    [JsonIgnore]
    public ulong UniformOffset =>
        Offsets.FirstOrDefault(o => o.Category == SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM).Offset;
}

/// <summary>An offset in one resource category.</summary>
/// <param name="Category">The resource kind.</param>
/// <param name="Offset">Bytes for <c>Uniform</c>; a binding/slot index otherwise.</param>
/// <param name="Space">Register space / descriptor set the offset is relative to.</param>
public readonly record struct CategoryOffset(SlangParameterCategory Category, ulong Offset, uint Space);

/// <summary>A struct, with its fields in declaration order.</summary>
public sealed record StructLayout : TypeLayout
{
    public required IReadOnlyList<FieldLayout> Fields { get; init; }

    /// <summary>Finds a field by name, or null. Ordinal comparison, as Slang is case-sensitive.</summary>
    public FieldLayout? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
}

/// <summary>An array. <see cref="ElementCount"/> is null for an unbounded (runtime-sized) array.</summary>
public sealed record ArrayLayout : TypeLayout
{
    public required TypeLayout ElementType { get; init; }
    public required int? ElementCount { get; init; }

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

    /// <summary>The element type sampled or loaded from the resource, if it has one.</summary>
    public TypeLayout? ResultType { get; init; }
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
/// A user-defined attribute, e.g. <c>[format("rgba8")]</c>. The engine's material system reads
/// these to drive data-driven behaviour without hardcoding shader knowledge in C#.
/// </summary>
/// <param name="Name">Attribute name without brackets.</param>
/// <param name="Arguments">Positional arguments, boxed as int, float, or string.</param>
public sealed record UserAttribute(string Name, IReadOnlyList<object?> Arguments);
