using System.Globalization;
using System.Text;

namespace OrderToCash.Projector.Domain;

/// <summary>
/// Renders a <see langword="long"/> minor-units money value with its
/// currency code — <c>PR16</c>: <c>(16130, "EUR")</c> → <c>"16 130 EUR"</c>,
/// digits grouped in threes by a single ASCII space, then the code. Integer
/// arithmetic only: no <c>/ 100</c>, no <see langword="decimal"/>, no
/// <c>ToString("N0")</c> with a culture (which would give <c>16,130</c>
/// under <c>en-US</c> and <c>16.130</c> under <c>de-DE</c> — a
/// culture-dependent separator this repository's wire and this repository's
/// summaries must never carry).
/// </summary>
public static class MoneyFormat
{
    public static string Of(long minorUnits, string currency)
    {
        var negative = minorUnits < 0;

        // long.MinValue has no positive counterpart representable as long —
        // work in a string of digits throughout rather than negate first.
        var digits = negative
            ? (minorUnits == long.MinValue ? ((ulong)long.MaxValue + 1).ToString(CultureInfo.InvariantCulture) : (-minorUnits).ToString(CultureInfo.InvariantCulture))
            : minorUnits.ToString(CultureInfo.InvariantCulture);

        var grouped = GroupInThrees(digits);

        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append('-');
        }

        builder.Append(grouped);
        builder.Append(' ');
        builder.Append(currency);

        return builder.ToString();
    }

    private static string GroupInThrees(string digits)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var builder = new StringBuilder();
        var firstGroupLength = digits.Length % 3;
        if (firstGroupLength == 0)
        {
            firstGroupLength = 3;
        }

        builder.Append(digits, 0, firstGroupLength);

        for (var i = firstGroupLength; i < digits.Length; i += 3)
        {
            builder.Append(' ');
            builder.Append(digits, i, 3);
        }

        return builder.ToString();
    }
}
