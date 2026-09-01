using System.Globalization;
using System.Text.RegularExpressions;
using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// The human-readable, unique, immutable business reference of an order —
/// `ORD-` followed by a zero-padded sequence, e.g. `ORD-000001`
/// (domain-model.md §2.3). Assigned once when the order is placed and never
/// reassigned; allocating the underlying sequence under a row lock is a
/// persistence concern outside the shared kernel.
/// </summary>
public readonly partial record struct OrderNumber
{
    public const string Prefix = "ORD-";
    private const int MinimumSequenceDigits = 6;

    private OrderNumber(string value) => Value = value;

    public OrderNumber(long sequence)
        : this(FormatFromSequence(sequence))
    {
    }

    /// <summary>The full business reference, e.g. `ORD-000001`.</summary>
    public string Value { get; }

    /// <summary>Parses a previously-assigned reference, validating the `ORD-######` shape.</summary>
    public static OrderNumber Parse(string value)
    {
        if (value is null || !FormatRegex().IsMatch(value))
        {
            throw new InvalidOrderNumberError(value ?? "<null>");
        }

        return new OrderNumber(value);
    }

    public override string ToString() => Value;

    private static string FormatFromSequence(long sequence)
    {
        if (sequence <= 0)
        {
            throw new InvalidOrderNumberError(sequence.ToString(CultureInfo.InvariantCulture));
        }

        return Prefix + sequence.ToString(new string('0', MinimumSequenceDigits), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^ORD-\d{6,}$")]
    private static partial Regex FormatRegex();
}
