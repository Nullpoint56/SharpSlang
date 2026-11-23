// L2 — an argument for IComponentType::specialize, on the way into Slang.
//
// slang::SpecializationArg is a kind and a union of a type pointer and an expression string, so a
// wrong member is a silent misread. This carries exactly one of the two and is built only through
// the factories.

namespace SharpSlang;

/// <summary>
/// A specialization argument: a type, or a Slang expression that names one
/// (<see cref="SlangComponentType.Specialize(System.ReadOnlySpan{SpecializationArgument})"/>).
/// </summary>
/// <remarks>The default value is unset, and <c>Specialize</c> refuses it.</remarks>
public readonly struct SpecializationArgument
{
    private SpecializationArgument(SlangType? type, string? expression)
    {
        Type = type;
        Expression = expression;
    }

    /// <summary>The type, for an argument made by <see cref="OfType"/>.</summary>
    public SlangType? Type { get; }

    /// <summary>The expression, for an argument made by <see cref="OfExpression"/>.</summary>
    public string? Expression { get; }

    /// <summary>A type (<c>SpecializationArg::fromType</c>).</summary>
    public static SpecializationArgument OfType(SlangType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new SpecializationArgument(type, null);
    }

    /// <summary>
    /// A Slang expression naming a type or value (<c>SpecializationArg::Kind::Expr</c>), such as
    /// <c>"MyMaterial"</c>, resolved by Slang in the scope of the component type being specialized.
    /// </summary>
    public static SpecializationArgument OfExpression(string expression)
    {
        ArgumentException.ThrowIfNullOrEmpty(expression);
        return new SpecializationArgument(null, expression);
    }

    /// <summary>The same as <see cref="OfType"/>.</summary>
    public static implicit operator SpecializationArgument(SlangType type) => OfType(type);

    /// <inheritdoc />
    public override string ToString() => Type?.Name ?? Expression ?? "<unset>";
}
