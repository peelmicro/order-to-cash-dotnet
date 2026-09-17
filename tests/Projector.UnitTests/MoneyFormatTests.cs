using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>MoneyFormat.Of</c> is a thin delegate to
/// <c>OrderToCash.SharedKernel.MoneyText.Format</c> — the algorithm itself
/// is guarded exhaustively by <c>SharedKernel.UnitTests.MoneyTextTests</c>.
/// This file proves the delegation is real (backlog id 100): that calling
/// through <c>MoneyFormat.Of</c> gives the SAME exponent-scaled, grouped
/// answer for a 2/0/3-exponent currency, a negative value and zero.
/// </summary>
public sealed class MoneyFormatTests
{
    [Theory]
    [InlineData(9245, "EUR", "92.45 EUR")]
    [InlineData(1613000, "EUR", "16 130.00 EUR")]
    [InlineData(5000, "JPY", "5 000 JPY")]
    [InlineData(12345, "BHD", "12.345 BHD")]
    [InlineData(-150, "EUR", "-1.50 EUR")]
    [InlineData(0, "EUR", "0.00 EUR")]
    public void B100_DelegatesToMoneyText_ScalingByTheCurrencysExponent(long minorUnits, string currency, string expected) =>
        Assert.Equal(expected, MoneyFormat.Of(minorUnits, currency));

    [Fact]
    public void B5_RendersLongMaxValueWithoutTruncation() =>
        Assert.Equal("92 233 720 368 547 758.07 EUR", MoneyFormat.Of(long.MaxValue, "EUR"));

    [Fact]
    public void B5_RendersLongMinValueWithoutTruncation() =>
        Assert.Equal("-92 233 720 368 547 758.08 EUR", MoneyFormat.Of(long.MinValue, "EUR"));

    [Fact]
    public void B5_NeverGroupsWithACommaOrADot_RegardlessOfCurrentCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("161.30 EUR", MoneyFormat.Of(16130, "EUR"));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("161.30 EUR", MoneyFormat.Of(16130, "EUR"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
