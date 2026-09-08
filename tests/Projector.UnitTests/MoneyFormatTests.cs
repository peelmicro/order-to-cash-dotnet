using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

public sealed class MoneyFormatTests
{
    [Theory]
    [InlineData(16130, "EUR", "16 130 EUR")]
    [InlineData(0, "EUR", "0 EUR")]
    [InlineData(999, "EUR", "999 EUR")]
    [InlineData(1000, "EUR", "1 000 EUR")]
    [InlineData(-16130, "EUR", "-16 130 EUR")]
    [InlineData(1000000, "USD", "1 000 000 USD")]
    public void PR16_RendersGroupedMinorUnitsWithTheCurrencyCode(long minorUnits, string currency, string expected) =>
        Assert.Equal(expected, MoneyFormat.Of(minorUnits, currency));

    [Fact]
    public void B5_RendersAValueAboveIntMaxValueWithoutTruncation()
    {
        long aboveIntMax = (long)int.MaxValue + 1; // 2 147 483 648
        Assert.Equal("2 147 483 648 EUR", MoneyFormat.Of(aboveIntMax, "EUR"));
    }

    [Fact]
    public void B5_RendersLongMaxValueWithoutTruncation() =>
        Assert.Equal("9 223 372 036 854 775 807 EUR", MoneyFormat.Of(long.MaxValue, "EUR"));

    [Fact]
    public void B5_RendersLongMinValueWithoutTruncation() =>
        Assert.Equal("-9 223 372 036 854 775 808 EUR", MoneyFormat.Of(long.MinValue, "EUR"));

    [Fact]
    public void B5_NeverGroupsWithACommaOrADot_RegardlessOfCurrentCulture()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("16 130 EUR", MoneyFormat.Of(16130, "EUR"));

            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("16 130 EUR", MoneyFormat.Of(16130, "EUR"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
