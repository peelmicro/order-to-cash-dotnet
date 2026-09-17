using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>Backlog id 100 — SA-5's exponent table: EUR/GBP/USD default to 2, JPY is 0, BHD is 3, and an unrecognised code falls back to 2.</summary>
public sealed class CurrencyExponentTests
{
    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("GBP", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("BHD", 3)]
    [InlineData("KWD", 3)]
    [InlineData("CLF", 4)]
    public void Of_ReturnsTheIso4217MinorUnitExponent(string currency, int expected) =>
        Assert.Equal(expected, CurrencyExponent.Of(currency));

    [Theory]
    [InlineData("ZZZ")]
    [InlineData("")]
    [InlineData("EU")]
    public void Of_DefaultsToTwoForAnUnrecognisedCode(string currency) =>
        Assert.Equal(2, CurrencyExponent.Of(currency));

    [Fact]
    public void Of_IsZeroForUyi_TheFundCodeMissingFromTheOriginalTable() =>
        Assert.Equal(0, CurrencyExponent.Of("UYI"));

    /// <summary>
    /// Whole-table literal guard (backlog id 100, fix round 1 — D3): every
    /// non-default row plus <c>UYI</c>, plus one default-2 control (EUR).
    /// The table is a countable claim, so a per-row test is not enough —
    /// this asserts the whole set at once. Armed with a single-row
    /// corruption (JOD 3 -> 2); see the arming table in
    /// progress/impl_timeline_money_reads_as_minor_units.md, "Fix round 1".
    /// Each row's failure names the currency code (backlog id 103, fix
    /// round 1, id 100 leftover): a bare <c>Assert.Equal</c> inside the loop
    /// would print only "Expected: 3 / Actual: 2" with no way to tell which
    /// of the 26 rows failed, so this uses the <c>Assert.True</c>
    /// user-message overload instead, matching #7's
    /// <c>currency-exponent.spec.ts</c> ("currencyExponent(IQD): ...").
    /// </summary>
    [Fact]
    public void Of_MatchesTheWholeIso4217NonDefaultExponentTable()
    {
        var table = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Zero decimal digits.
            ["BIF"] = 0,
            ["CLP"] = 0,
            ["DJF"] = 0,
            ["GNF"] = 0,
            ["ISK"] = 0,
            ["JPY"] = 0,
            ["KMF"] = 0,
            ["KRW"] = 0,
            ["PYG"] = 0,
            ["RWF"] = 0,
            ["UGX"] = 0,
            ["VND"] = 0,
            ["VUV"] = 0,
            ["XAF"] = 0,
            ["XOF"] = 0,
            ["XPF"] = 0,

            // Three decimal digits.
            ["BHD"] = 3,
            ["IQD"] = 3,
            ["JOD"] = 3,
            ["KWD"] = 3,
            ["LYD"] = 3,
            ["OMR"] = 3,
            ["TND"] = 3,

            // Four decimal digits.
            ["CLF"] = 4,
            ["UYW"] = 4,

            // Fund code.
            ["UYI"] = 0,

            // Default-2 control.
            ["EUR"] = 2,
        };

        foreach (var (code, expected) in table)
        {
            var actual = CurrencyExponent.Of(code);
            Assert.True(actual == expected, $"CurrencyExponent.Of(\"{code}\"): expected {expected}, was {actual}");
        }
    }
}
