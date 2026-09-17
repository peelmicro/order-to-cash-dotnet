using System.Globalization;
using System.Text;

namespace OrderToCash.SharedKernel;

/// <summary>
/// Renders a <see langword="long"/> minor-units money value with its
/// currency's ISO 4217 minor-unit exponent (<see cref="CurrencyExponent"/>)
/// — grouped integer part, a <c>.</c> decimal separator, exactly
/// <c>exponent</c> fraction digits (none at all when the exponent is 0),
/// a space, then the ISO code: <c>(9245, "EUR")</c> -&gt; <c>"92.45 EUR"</c>,
/// <c>(5000, "JPY")</c> -&gt; <c>"5 000 JPY"</c>, <c>(12345, "BHD")</c> -&gt;
/// <c>"12.345 BHD"</c>. Integer arithmetic only — split by string slicing
/// on the digit string, never by division or a floating-point/
/// <see langword="decimal"/> conversion — and no culture: digits are
/// rendered with <see cref="CultureInfo.InvariantCulture"/> and grouped
/// with a single hard-coded ASCII space, never <c>ToString("N")</c> under
/// the current culture (which would give <c>16,130.00</c> under
/// <c>en-US</c> and <c>16.130,00</c> under <c>de-DE</c>).
/// </summary>
/// <remarks>
/// This is the ONE place this rendering is implemented. Every server-written
/// human-readable rendering of a money amount in this repository —
/// <c>Projector.Domain.MoneyFormat.Of</c> (the timeline summaries), the
/// Seed's timeline fixtures (<c>SagaFixtures.cs</c>) and
/// <c>Notifications.Application.Templates.NotificationFormat.FormatMoney</c>
/// (the email bodies) — calls through here, so a seeded document, a
/// projected document and a notification email read alike for the same
/// amount by construction, not by three implementations kept in sync by
/// hand.
/// </remarks>
public static class MoneyText
{
    public static string Format(long minorUnits, string currency)
    {
        var exponent = CurrencyExponent.Of(currency);
        var negative = minorUnits < 0;

        // long.MinValue has no positive counterpart representable as long —
        // work in a string of digits throughout rather than negate first.
        var digits = negative
            ? (minorUnits == long.MinValue ? ((ulong)long.MaxValue + 1).ToString(CultureInfo.InvariantCulture) : (-minorUnits).ToString(CultureInfo.InvariantCulture))
            : minorUnits.ToString(CultureInfo.InvariantCulture);

        if (digits.Length <= exponent)
        {
            digits = digits.PadLeft(exponent + 1, '0');
        }

        var splitAt = digits.Length - exponent;
        var integerDigits = digits[..splitAt];
        var fractionDigits = digits[splitAt..];

        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append('-');
        }

        builder.Append(GroupInThrees(integerDigits));

        if (exponent > 0)
        {
            builder.Append('.');
            builder.Append(fractionDigits);
        }

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
