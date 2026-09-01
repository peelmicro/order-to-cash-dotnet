using System.Globalization;
using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// A strictly positive integer count of units (specs/shared/requirements.md
/// R3; domain-model.md §2.2). Zero, negative and fractional quantities are
/// refused at construction — the catalogue is sold in whole units only.
/// </summary>
public readonly record struct Quantity
{
    public Quantity(int value)
    {
        if (value <= 0)
        {
            throw new QuantityMustBePositiveError(value.ToString(CultureInfo.InvariantCulture));
        }

        Value = value;
    }

    public int Value { get; }

    /// <summary>
    /// Validates a numeric input that may be fractional before it ever
    /// becomes a whole-unit <see cref="Quantity"/> — the boundary this type
    /// exists to guard, since a upstream source (a parsed EDI field, an
    /// inbound JSON number) is not itself constrained to integers the way a
    /// C# <c>int</c> constructor parameter already is.
    /// </summary>
    public static Quantity From(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Floor(value)
            || value < int.MinValue || value > int.MaxValue)
        {
            // The range check runs before the cast rather than relying on
            // `checked` to catch an out-of-range value: `checked` throws
            // System.OverflowException, a framework exception with no
            // stable Code, from inside the domain layer — the refusal must
            // be a DomainError like every other refusal this type raises
            // (specs/shared/requirements.md's own vocabulary: "a refusal
            // raised inside the domain layer carrying a stable code").
            // See progress/review_shared_kernel.md defect D4, observed
            // directly: Quantity.From(1e18) raised OverflowException before
            // this check existed.
            throw new QuantityMustBePositiveError(value.ToString(CultureInfo.InvariantCulture));
        }

        return new Quantity((int)value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
