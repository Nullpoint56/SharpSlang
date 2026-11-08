// L2 — slang::TypeLayoutReflection: a type as laid out for one target, one member per
// spReflectionTypeLayout_* function, in Slang's argument order. Borrowed (SlangReflectionObject).
//
// Binding ranges, descriptor sets and sub-object ranges are the three views Slang gives of the
// same bindings; AGENTS.md#reflection-snapshot records what each reports and what each omits.

using SharpSlang.Interop;
using SharpSlang.Native;

namespace SharpSlang;

/// <summary><c>slang::TypeLayoutReflection</c>: a type's layout — sizes, fields, and its binding, descriptor-set and sub-object ranges.</summary>
public sealed unsafe class SlangTypeLayout : SlangReflectionObject
{
    private SlangTypeLayout(SlangReflectionTypeLayout* p, SlangObject owner)
        : base(p, owner, nameof(SlangTypeLayout)) { }

    internal static SlangTypeLayout? From(SlangReflectionTypeLayout* p, SlangObject owner) =>
        p is null ? null : new SlangTypeLayout(p, owner);

    internal SlangReflectionTypeLayout* Ptr => (SlangReflectionTypeLayout*)Raw;

    /// <summary><c>getType</c>: the type laid out, or null.</summary>
    public SlangType? Type => SlangType.From(SlangApi.spReflectionTypeLayout_GetType(Ptr), Owner);

    /// <summary><c>getKind</c>.</summary>
    public SlangTypeKind Kind => SlangApi.spReflectionTypeLayout_getKind(Ptr);

    /// <summary>
    /// <c>getSize</c>: the footprint in <paramref name="category"/>. <c>SLANG_UNKNOWN_SIZE</c>
    /// (<c>~0 - 1</c>) for an array sized by a specialization constant and what follows it,
    /// <c>SLANG_UNBOUNDED_SIZE</c> for an unbounded one: numbers, not sizes
    /// (AGENTS.md#sizes-slang-does-not-know).
    /// </summary>
    public ulong GetSize(SlangParameterCategory category = SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) =>
        SlangApi.spReflectionTypeLayout_GetSize(Ptr, category);

    /// <summary><c>getStride</c>: the size rounded up to the alignment.</summary>
    public ulong GetStride(SlangParameterCategory category = SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) =>
        SlangApi.spReflectionTypeLayout_GetStride(Ptr, category);

    /// <summary><c>getAlignment</c>.</summary>
    public int GetAlignment(SlangParameterCategory category = SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) =>
        SlangApi.spReflectionTypeLayout_getAlignment(Ptr, category);

    /// <summary><c>getFieldCount</c>.</summary>
    public uint FieldCount => SlangApi.spReflectionTypeLayout_GetFieldCount(Ptr);

    /// <summary><c>getFieldByIndex</c>.</summary>
    public SlangVariableLayout? GetFieldByIndex(uint index) =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_GetFieldByIndex(Ptr, index), Owner);

    /// <summary><c>findFieldIndexByName</c>: the field's index, or -1.</summary>
    public long FindFieldIndexByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var bytes = System.Text.Encoding.UTF8.GetBytes(name);
        fixed (byte* p = bytes)
        {
            return SlangApi.spReflectionTypeLayout_findFieldIndexByName(Ptr, (sbyte*)p, (sbyte*)(p + bytes.Length));
        }
    }

    /// <summary><c>getExplicitCounter</c>: the counter of an append or consume buffer, or null.</summary>
    public SlangVariableLayout? ExplicitCounter =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_GetExplicitCounter(Ptr), Owner);

    /// <summary><c>getExplicitCounterBindingRangeOffset</c>: the counter's binding range, relative to this layout's.</summary>
    public long ExplicitCounterBindingRangeOffset => SlangApi.spReflectionTypeLayout_getExplicitCounterBindingRangeOffset(Ptr);

    /// <summary><c>getElementStride</c>: an array's element stride in <paramref name="category"/>.</summary>
    public ulong GetElementStride(SlangParameterCategory category = SlangParameterCategory.SLANG_PARAMETER_CATEGORY_UNIFORM) =>
        SlangApi.spReflectionTypeLayout_GetElementStride(Ptr, category);

    /// <summary><c>getElementTypeLayout</c>: an array's, container's or structured buffer's element, or null.</summary>
    public SlangTypeLayout? ElementTypeLayout => From(SlangApi.spReflectionTypeLayout_GetElementTypeLayout(Ptr), Owner);

    /// <summary><c>getElementVarLayout</c>: a container's element as a variable, or null.</summary>
    public SlangVariableLayout? ElementVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_GetElementVarLayout(Ptr), Owner);

    /// <summary><c>getContainerVarLayout</c>: what a parameter block or constant buffer occupies for itself, or null.</summary>
    public SlangVariableLayout? ContainerVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_getContainerVarLayout(Ptr), Owner);

    /// <summary><c>getContentVarLayout</c> (the flat <c>GetContentVarLayout</c>), or null.</summary>
    public SlangVariableLayout? ContentVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_GetContentVarLayout(Ptr), Owner);

    /// <summary><c>getParameterCategory</c>: the one category this layout occupies, or <c>MIXED</c>.</summary>
    public SlangParameterCategory ParameterCategory => SlangApi.spReflectionTypeLayout_GetParameterCategory(Ptr);

    /// <summary><c>getCategoryCount</c>: how many categories this layout occupies.</summary>
    public uint CategoryCount => SlangApi.spReflectionTypeLayout_GetCategoryCount(Ptr);

    /// <summary><c>getCategoryByIndex</c>.</summary>
    public SlangParameterCategory GetCategoryByIndex(uint index) =>
        SlangApi.spReflectionTypeLayout_GetCategoryByIndex(Ptr, index);

    /// <summary><c>getMatrixLayoutMode</c>.</summary>
    public SlangMatrixLayoutMode MatrixLayoutMode => SlangApi.spReflectionTypeLayout_GetMatrixLayoutMode(Ptr);

    /// <summary><c>getGenericParamIndex</c>: which generic parameter this layout stands in for, or a negative value.</summary>
    public int GenericParamIndex => SlangApi.spReflectionTypeLayout_getGenericParamIndex(Ptr);

    /// <summary><c>getPendingDataTypeLayout</c>, or null.</summary>
    public SlangTypeLayout? PendingDataTypeLayout => From(SlangApi.spReflectionTypeLayout_getPendingDataTypeLayout(Ptr), Owner);

    /// <summary><c>getSpecializedTypePendingDataVarLayout</c>, or null.</summary>
    public SlangVariableLayout? SpecializedTypePendingDataVarLayout =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_getSpecializedTypePendingDataVarLayout(Ptr), Owner);

    // ---------------------------------------------------------------- binding ranges

    /// <summary>
    /// <c>getBindingRangeCount</c>.
    /// </summary>
    /// <remarks>
    /// On a resource array sized by a specialization constant (<c>Texture2D t[kCount]</c>) Slang
    /// throws <c>Slang::InternalError</c> through <c>extern "C"</c>: a catchable
    /// <c>SEHException</c> on win-x64, <c>std::terminate</c> on Linux and macOS (v2026.21; U2).
    /// Not guardable: every other query answers as for a fixed-size array
    /// (AGENTS.md#sizes-slang-does-not-know). Patch 0003 makes the array's binding count
    /// <c>SLANG_UNKNOWN_SIZE</c> in a payload built from source (AGENTS.md#patches).
    /// </remarks>
    public long BindingRangeCount => SlangApi.spReflectionTypeLayout_getBindingRangeCount(Ptr);

    /// <summary><c>getBindingRangeType</c>.</summary>
    public SlangBindingType GetBindingRangeType(long index) => SlangApi.spReflectionTypeLayout_getBindingRangeType(Ptr, index);

    /// <summary><c>isBindingRangeSpecializable</c>: an interface-typed range whose concrete type is chosen later.</summary>
    public bool IsBindingRangeSpecializable(long index) => SlangApi.spReflectionTypeLayout_isBindingRangeSpecializable(Ptr, index) != 0;

    /// <summary><c>getBindingRangeBindingCount</c>.</summary>
    public long GetBindingRangeBindingCount(long index) => SlangApi.spReflectionTypeLayout_getBindingRangeBindingCount(Ptr, index);

    /// <summary><c>getFieldBindingRangeOffset</c>: the first binding range of field <paramref name="fieldIndex"/>.</summary>
    public long GetFieldBindingRangeOffset(long fieldIndex) => SlangApi.spReflectionTypeLayout_getFieldBindingRangeOffset(Ptr, fieldIndex);

    /// <summary><c>getBindingRangeLeafTypeLayout</c>, or null.</summary>
    public SlangTypeLayout? GetBindingRangeLeafTypeLayout(long index) =>
        From(SlangApi.spReflectionTypeLayout_getBindingRangeLeafTypeLayout(Ptr, index), Owner);

    /// <summary><c>getBindingRangeLeafVariable</c>: the declaration behind the range, or null.</summary>
    public SlangVariable? GetBindingRangeLeafVariable(long index) =>
        SlangVariable.From(SlangApi.spReflectionTypeLayout_getBindingRangeLeafVariable(Ptr, index), Owner);

    /// <summary>
    /// <c>getBindingRangeImageFormat</c>. Returns <c>unknown</c> for every non-texture range
    /// shape at v2026.14.1 and v2026.21; once recorded as access-violating
    /// (<c>ProberTests.The_image_format_query_no_longer_crashes_on_any_binding_range_shape</c>).
    /// </summary>
    public SlangImageFormat GetBindingRangeImageFormat(long index) =>
        SlangApi.spReflectionTypeLayout_getBindingRangeImageFormat(Ptr, index);

    /// <summary><c>getBindingRangeDescriptorSetIndex</c>: an index into this layout's own descriptor sets, -1 for a range that is a space of its own.</summary>
    public long GetBindingRangeDescriptorSetIndex(long index) =>
        SlangApi.spReflectionTypeLayout_getBindingRangeDescriptorSetIndex(Ptr, index);

    /// <summary><c>getBindingRangeFirstDescriptorRangeIndex</c>.</summary>
    public long GetBindingRangeFirstDescriptorRangeIndex(long index) =>
        SlangApi.spReflectionTypeLayout_getBindingRangeFirstDescriptorRangeIndex(Ptr, index);

    /// <summary><c>getBindingRangeDescriptorRangeCount</c>.</summary>
    public long GetBindingRangeDescriptorRangeCount(long index) =>
        SlangApi.spReflectionTypeLayout_getBindingRangeDescriptorRangeCount(Ptr, index);

    // ---------------------------------------------------------------- descriptor sets

    /// <summary><c>getDescriptorSetCount</c>: the sets for what is bound directly in this layout, not in parameter blocks inside it.</summary>
    public long DescriptorSetCount => SlangApi.spReflectionTypeLayout_getDescriptorSetCount(Ptr);

    /// <summary><c>getDescriptorSetSpaceOffset</c>.</summary>
    public long GetDescriptorSetSpaceOffset(long setIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetSpaceOffset(Ptr, setIndex);

    /// <summary><c>getDescriptorSetDescriptorRangeCount</c>.</summary>
    public long GetDescriptorSetDescriptorRangeCount(long setIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCount(Ptr, setIndex);

    /// <summary><c>getDescriptorSetDescriptorRangeIndexOffset</c>.</summary>
    public long GetDescriptorSetDescriptorRangeIndexOffset(long setIndex, long rangeIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeIndexOffset(Ptr, setIndex, rangeIndex);

    /// <summary><c>getDescriptorSetDescriptorRangeDescriptorCount</c>: -1 for a runtime-sized array.</summary>
    public long GetDescriptorSetDescriptorRangeDescriptorCount(long setIndex, long rangeIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeDescriptorCount(Ptr, setIndex, rangeIndex);

    /// <summary><c>getDescriptorSetDescriptorRangeType</c>.</summary>
    public SlangBindingType GetDescriptorSetDescriptorRangeType(long setIndex, long rangeIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeType(Ptr, setIndex, rangeIndex);

    /// <summary><c>getDescriptorSetDescriptorRangeCategory</c>.</summary>
    public SlangParameterCategory GetDescriptorSetDescriptorRangeCategory(long setIndex, long rangeIndex) =>
        SlangApi.spReflectionTypeLayout_getDescriptorSetDescriptorRangeCategory(Ptr, setIndex, rangeIndex);

    // ---------------------------------------------------------------- sub-object ranges

    /// <summary><c>getSubObjectRangeCount</c>.</summary>
    public long SubObjectRangeCount => SlangApi.spReflectionTypeLayout_getSubObjectRangeCount(Ptr);

    /// <summary><c>getSubObjectRangeBindingRangeIndex</c>.</summary>
    public long GetSubObjectRangeBindingRangeIndex(long subObjectRangeIndex) =>
        SlangApi.spReflectionTypeLayout_getSubObjectRangeBindingRangeIndex(Ptr, subObjectRangeIndex);

    /// <summary>
    /// <c>getSubObjectRangeSpaceOffset</c>. Reports 0 for blocks in set 1 and set 2 (v2026.21; U15).
    /// </summary>
    public long GetSubObjectRangeSpaceOffset(long subObjectRangeIndex) =>
        SlangApi.spReflectionTypeLayout_getSubObjectRangeSpaceOffset(Ptr, subObjectRangeIndex);

    /// <summary>
    /// <c>getSubObjectRangeOffset</c>: a variable layout with no variable behind it (v2026.21; U15),
    /// or null.
    /// </summary>
    public SlangVariableLayout? GetSubObjectRangeOffset(long subObjectRangeIndex) =>
        SlangVariableLayout.From(SlangApi.spReflectionTypeLayout_getSubObjectRangeOffset(Ptr, subObjectRangeIndex), Owner);
}
