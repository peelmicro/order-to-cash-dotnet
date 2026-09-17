using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// Backlog id 100 (<c>timeline_money_reads_as_minor_units</c>) — the ONE
/// implementation every server-written human-readable money rendering in
/// this repository shares. Whole-string assertions only (never
/// <c>Contains</c>): grouped integer part, a <c>.</c> decimal separator,
/// exactly the currency's ISO 4217 exponent worth of fraction digits (none
/// at all for a 0-exponent currency), a space, the code. Integer
/// arithmetic only — no floating point, no <c>decimal</c>, no culture.
/// </summary>
public sealed class MoneyTextTests
{
    [Theory]
    // 2-exponent (EUR) — the maintainer's own reported defect.
    [InlineData(9245, "EUR", "92.45 EUR")]
    [InlineData(1613000, "EUR", "16 130.00 EUR")]
    [InlineData(100, "EUR", "1.00 EUR")]
    [InlineData(99, "EUR", "0.99 EUR")]
    [InlineData(1000000, "USD", "10 000.00 USD")]
    // 0-exponent (JPY) — no decimal separator at all.
    [InlineData(5000, "JPY", "5 000 JPY")]
    [InlineData(0, "JPY", "0 JPY")]
    [InlineData(7, "JPY", "7 JPY")]
    // 3-exponent (BHD).
    [InlineData(12345, "BHD", "12.345 BHD")]
    [InlineData(5, "BHD", "0.005 BHD")]
    // Negative and zero.
    [InlineData(-150, "EUR", "-1.50 EUR")]
    [InlineData(0, "EUR", "0.00 EUR")]
    [InlineData(-1, "EUR", "-0.01 EUR")]
    public void Format_RendersTheGroupedExponentScaledAmount(long minorUnits, string currency, string expected) =>
        Assert.Equal(expected, MoneyText.Format(minorUnits, currency));

    [Fact]
    public void Format_RendersAValueAboveIntMaxValueWithoutTruncation()
    {
        long aboveIntMax = (long)int.MaxValue + 1; // 2 147 483 648
        Assert.Equal("21 474 836.48 EUR", MoneyText.Format(aboveIntMax, "EUR"));
    }

    [Fact]
    public void Format_RendersLongMaxValueWithoutTruncation_TwoExponentCurrency() =>
        Assert.Equal("92 233 720 368 547 758.07 EUR", MoneyText.Format(long.MaxValue, "EUR"));

    [Fact]
    public void Format_RendersLongMinValueWithoutTruncation_TwoExponentCurrency() =>
        Assert.Equal("-92 233 720 368 547 758.08 EUR", MoneyText.Format(long.MinValue, "EUR"));

    [Fact]
    public void Format_RendersLongMaxValueWithoutTruncation_ZeroExponentCurrency() =>
        Assert.Equal("9 223 372 036 854 775 807 JPY", MoneyText.Format(long.MaxValue, "JPY"));

    [Fact]
    public void Format_NeverGroupsWithACommaOrADot_RegardlessOfCurrentCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("16 130.00 EUR", MoneyText.Format(1613000, "EUR"));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("16 130.00 EUR", MoneyText.Format(1613000, "EUR"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
