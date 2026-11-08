// Specialization constants read as VkSpecializationInfo.
//
// This is the one place SharpSlang.Vulkan produces *data* rather than a description. Everything
// else here says what the shader requires and leaves the renderer to satisfy it; a specialization
// buffer has to be packed by somebody, the packing has to agree with the map entries byte for byte,
// and getting it wrong gives you a pipeline that compiles and renders with the wrong constants.
// Splitting that across the boundary would guarantee two subtly different packers.

using SharpSlang.Native;
using SharpSlang.Reflection;

namespace SharpSlang.Vulkan;

/// <summary>One entry of a <c>VkSpecializationInfo::pMapEntries</c> array.</summary>
/// <param name="ConstantId">Cast to <c>VkSpecializationMapEntry::constantID</c>.</param>
/// <param name="Offset">Byte offset into the data buffer.</param>
/// <param name="Size">Byte size of the value.</param>
/// <param name="Name">The shader constant this came from, for diagnostics.</param>
public readonly record struct VulkanSpecializationMapEntry(
    uint ConstantId,
    uint Offset,
    uint Size,
    string Name);

/// <summary>
/// A packed specialization buffer and the map that describes it — the two halves of
/// <c>VkSpecializationInfo</c>.
/// </summary>
/// <param name="MapEntries">Map entries, ordered by constant id.</param>
/// <param name="Data">The packed values. <c>dataSize</c> is its length.</param>
public sealed record VulkanSpecializationInfo(
    IReadOnlyList<VulkanSpecializationMapEntry> MapEntries,
    byte[] Data)
{
    /// <summary>Nothing specialized — pass a null <c>VkSpecializationInfo</c>.</summary>
    public static readonly VulkanSpecializationInfo Empty = new([], []);

    /// <summary>True when there is nothing to specialize.</summary>
    public bool IsEmpty => MapEntries.Count == 0;
}

/// <summary>Builds <c>VkSpecializationInfo</c> payloads from a reflection snapshot.</summary>
public static class VulkanSpecialization
{
    /// <summary>
    /// Packs values for a program's specialization constants.
    /// </summary>
    /// <param name="layout">The reflection snapshot naming the constants and their ids.</param>
    /// <param name="values">
    /// Values by constant name. A name the shader does not declare is an error rather than a
    /// no-op — it almost always means the shader was renamed and the material data was not.
    /// </param>
    /// <param name="includeDefaults">
    /// Also emit constants the caller did not supply, using the shader's declared default. Off by
    /// default: omitting a constant leaves the module's own <c>OpSpecConstant</c> default in place,
    /// which is the same value by definition and keeps the buffer to what actually varies.
    /// </param>
    /// <remarks>
    /// Values are packed in constant-id order, each at its natural alignment. Vulkan does not
    /// require that — offsets need only lie inside the buffer — but an unaligned read is undefined
    /// on some drivers and free to avoid here.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A supplied name is not a specialization constant of this program, or its value does not fit
    /// the constant's declared type.
    /// </exception>
    public static VulkanSpecializationInfo Pack(
        this ProgramLayout layout,
        IReadOnlyDictionary<string, object> values,
        bool includeDefaults = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(values);

        foreach (var name in values.Keys)
        {
            if (layout.SpecializationConstant(name) is null)
            {
                var declared = layout.SpecializationConstants.Count == 0
                    ? "this program declares none"
                    : $"declared: {string.Join(", ", layout.SpecializationConstants.Select(c => c.Name))}";
                throw new ArgumentException(
                    $"'{name}' is not a specialization constant of this program — {declared}",
                    nameof(values));
            }
        }

        var selected = layout.SpecializationConstants
            .Where(c => values.ContainsKey(c.Name) || includeDefaults)
            .OrderBy(c => c.ConstantId)
            .ToList();

        if (selected.Count == 0) return VulkanSpecializationInfo.Empty;

        var entries = new List<VulkanSpecializationMapEntry>(selected.Count);
        var data = new List<byte>(selected.Sum(c => (int)c.ByteSize) + 8);

        foreach (var constant in selected)
        {
            var size = constant.ByteSize;

            // Natural alignment, capped at the value's own size, so a bool (4) and a double (8)
            // both land where a driver expects to read them.
            while (data.Count % size != 0) data.Add(0);

            var offset = (uint)data.Count;
            data.AddRange(Encode(constant, values.GetValueOrDefault(constant.Name)));
            entries.Add(new VulkanSpecializationMapEntry(constant.ConstantId, offset, size, constant.Name));
        }

        return new VulkanSpecializationInfo(entries, [.. data]);
    }

    /// <summary>
    /// The map entries alone, describing every constant the shader declares at its natural offset.
    /// </summary>
    /// <remarks>
    /// For a host that keeps its own value buffer in a fixed layout and only needs to know the ids
    /// and offsets. <see cref="Pack"/> produces both halves together and is the usual entry point.
    /// </remarks>
    public static IReadOnlyList<VulkanSpecializationMapEntry> SpecializationMap(this ProgramLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var entries = new List<VulkanSpecializationMapEntry>();
        var offset = 0u;

        foreach (var c in layout.SpecializationConstants.OrderBy(c => c.ConstantId))
        {
            while (offset % c.ByteSize != 0) offset++;
            entries.Add(new VulkanSpecializationMapEntry(c.ConstantId, offset, c.ByteSize, c.Name));
            offset += c.ByteSize;
        }

        return entries;
    }

    /// <summary>
    /// Encodes one value in the constant's declared type, or its default when none was supplied.
    /// </summary>
    /// <remarks>
    /// Conversions are checked. A material that supplies 300 for a <c>uint8</c> constant, or a
    /// float for an <c>int</c>, is a data error worth naming — silently truncating it produces a
    /// shader that runs and looks slightly wrong.
    /// </remarks>
    private static byte[] Encode(SpecializationConstant constant, object? value)
    {
        var size = constant.ByteSize;

        if (constant.ScalarType == SlangScalarType.SLANG_SCALAR_TYPE_BOOL)
        {
            var flag = value switch
            {
                null => constant.DefaultInt is not 0 and not null,
                bool b => b,
                _ => throw Mismatch(constant, value, "bool"),
            };
            return BitConverter.GetBytes(flag ? 1u : 0u);
        }

        if (constant.IsFloatingPoint)
        {
            var d = value switch
            {
                null => constant.DefaultFloat ?? 0.0,
                float f => f,
                double dd => dd,
                // An integer literal for a float constant is a routine thing to write in data.
                sbyte or byte or short or ushort or int or uint or long =>
                    Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture),
                _ => throw Mismatch(constant, value, "float"),
            };

            return size switch
            {
                8 => BitConverter.GetBytes(d),
                2 => BitConverter.GetBytes((Half)d),
                _ => BitConverter.GetBytes((float)d),
            };
        }

        // A uint64 constant can legitimately hold a value above long.MaxValue, which the signed
        // path below cannot carry. Encode it directly rather than rejecting a value that fits.
        if (constant.ScalarType == SlangScalarType.SLANG_SCALAR_TYPE_UINT64 && value is ulong wide)
            return BitConverter.GetBytes(wide);

        var n = value switch
        {
            null => constant.DefaultInt ?? 0L,
            bool b => b ? 1L : 0L,
            sbyte or byte or short or ushort or int or uint or long =>
                Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
            ulong u when u <= long.MaxValue => (long)u,
            _ => throw Mismatch(constant, value, "integer"),
        };

        var signed = constant.ScalarType
            is SlangScalarType.SLANG_SCALAR_TYPE_INT8
            or SlangScalarType.SLANG_SCALAR_TYPE_INT16
            or SlangScalarType.SLANG_SCALAR_TYPE_INT32
            or SlangScalarType.SLANG_SCALAR_TYPE_INT64;

        RequireRange(constant, n, size, signed);

        var bytes = BitConverter.GetBytes(n);
        return bytes[..(int)size];
    }

    private static void RequireRange(SpecializationConstant constant, long value, uint size, bool signed)
    {
        if (size >= 8) return;

        var bits = (int)size * 8;
        var (min, max) = signed
            ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1)
            : (0L, (1L << bits) - 1);

        if (value < min || value > max)
        {
            throw new ArgumentException(
                $"specialization constant '{constant.Name}' is {constant.ScalarType} " +
                $"({min}..{max}); {value} does not fit");
        }
    }

    private static ArgumentException Mismatch(SpecializationConstant constant, object value, string expected) =>
        new($"specialization constant '{constant.Name}' is {constant.ScalarType}, " +
            $"which needs {expected}, but a {value.GetType().Name} was supplied");
}
