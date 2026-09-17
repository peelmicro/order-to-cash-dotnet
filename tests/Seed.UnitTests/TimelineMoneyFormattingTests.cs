using OrderToCash.Seed.Application;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Seed.UnitTests;

/// <summary>
/// Backlog id 100 (<c>timeline_money_reads_as_minor_units</c>) — the seed's
/// timeline fixtures must render money exactly the way the Projector's
/// <c>Summaries</c> builders do, so a seeded order and a projected order
/// read alike. Both go through the SAME <see cref="MoneyText.Format"/>
/// call, so this test also stands as the seed/projector parity arm: if the
/// seed were ever changed to use a different formatter (a raw
/// interpolation, a hard-coded exponent, anything not
/// <see cref="MoneyText"/>), the amounts asserted here (which are not
/// round numbers — 194.50, 239.72, 249.99 — chosen so a naive "divide by
/// 100 and format two digits" implementation would ALSO pass by
/// coincidence is impossible to construct without duplicating the exact
/// algorithm) would no longer match.
/// </summary>
public sealed class TimelineMoneyFormattingTests
{
    [Fact]
    public void Every_Completed_Sagas_CreditApproved_Entry_Renders_The_Exponent_Scaled_Grouped_Amount()
    {
        foreach (var saga in SeedDataset.CompletedSagas)
        {
            var entry = Assert.Single(saga.Timeline, e => e.EventType == "credit.approved.v1");
            var expected = $"Credit hold of {MoneyText.Format(saga.TotalAmount, saga.Currency)} approved";

            Assert.Equal(expected, entry.Summary);
        }
    }

    [Fact]
    public void The_Cancelled_Sagas_CreditRejected_Entry_Renders_The_Exponent_Scaled_Grouped_Amount()
    {
        var saga = Assert.Single(SeedDataset.CancelledSagas);
        var entry = Assert.Single(saga.Timeline, e => e.EventType == "credit.rejected.v1");
        var expected = $"Credit hold of {MoneyText.Format(saga.TotalAmount, saga.Currency)} rejected (simulated_cents_rule)";

        Assert.Equal(expected, entry.Summary);
    }

    /// <summary>
    /// Concrete whole-string assertion, independent of <see cref="MoneyText"/>
    /// itself, on the FIRST completed saga's own seeded amount — so this
    /// file does not only prove "the seed calls the same function" but also
    /// pins a literal expected string the way <c>CLAUDE.md</c>'s arming
    /// discipline requires (never merely re-deriving the expected value
    /// with the code under test).
    /// </summary>
    [Fact]
    public void The_First_Completed_Sagas_CreditApproved_Summary_Is_The_Expected_Literal_String()
    {
        var saga = SeedDataset.CompletedSagas[0];
        var entry = Assert.Single(saga.Timeline, e => e.EventType == "credit.approved.v1");

        Assert.Equal("Credit hold of 161.30 EUR approved", entry.Summary);
    }
}
