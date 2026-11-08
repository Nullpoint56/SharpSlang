// L2 — an argument for a generic parameter, on the way into Slang.
//
// Slang takes these as a parallel pair of arrays: a SlangReflectionGenericArgType saying which kind
// each is, and a SlangReflectionGenericArg union holding the value. That is two arrays a caller can
// get out of step, and a union whose wrong member is a silent misread — so this is one type that
// carries both halves and can only be built through a factory that sets them together.

using SharpSlang.Native;

namespace SharpSlang;

/// <summary>A concrete argument for one generic parameter.</summary>
/// <remarks>
/// Build with <see cref="OfType"/>, <see cref="OfInt"/> or <see cref="OfBool"/>. The default value
/// is deliberately not usable: a zeroed struct would read as a type argument with a null type.
/// </remarks>
public readonly unsafe struct GenericArgument
{
    private readonly SlangType? _type;
    private readonly long _value;

    private GenericArgument(SlangReflectionGenericArgType kind, SlangType? type, long value)
    {
        Kind = kind;
        _type = type;
        _value = value;
        IsSet = true;
    }

    internal SlangReflectionGenericArgType Kind { get; }

    /// <summary>False for a default-constructed value, which is not a usable argument.</summary>
    internal bool IsSet { get; }

    /// <summary>A concrete type for a <c>&lt;T : IFoo&gt;</c> parameter.</summary>
    public static GenericArgument OfType(SlangType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new GenericArgument(SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE, type, 0);
    }

    /// <summary>A concrete value for a <c>&lt;let N : int&gt;</c> parameter.</summary>
    public static GenericArgument OfInt(long value) =>
        new(SlangReflectionGenericArgType.SLANG_GENERIC_ARG_INT, null, value);

    /// <summary>A concrete value for a <c>&lt;let B : bool&gt;</c> parameter.</summary>
    public static GenericArgument OfBool(bool value) =>
        new(SlangReflectionGenericArgType.SLANG_GENERIC_ARG_BOOL, null, value ? 1 : 0);

    /// <summary>The union Slang reads, with the member its kind selects.</summary>
    internal SlangReflectionGenericArg ToNative() => Kind switch
    {
        SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE =>
            new SlangReflectionGenericArg { typeVal = (SlangReflectionType*)_type!.Handle },
        SlangReflectionGenericArgType.SLANG_GENERIC_ARG_BOOL =>
            new SlangReflectionGenericArg { boolVal = (byte)_value },
        _ => new SlangReflectionGenericArg { intVal = _value },
    };

    public override string ToString() => Kind switch
    {
        SlangReflectionGenericArgType.SLANG_GENERIC_ARG_TYPE => _type?.Name ?? "<unset>",
        SlangReflectionGenericArgType.SLANG_GENERIC_ARG_BOOL => _value != 0 ? "true" : "false",
        _ => _value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
