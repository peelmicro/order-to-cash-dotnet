using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BC5`/`BC6` as identities over <see cref="CreditExposure.Summarise"/>,
/// including the cancelled-before-invoice case `domain-model.md` §5.1's
/// literal formula gets wrong; `BC28`'s grouping half; `BC30`'s overflow
/// guard, driven directly with no aggregate and no database (design.md
/// §3.3, §15 `L25`).
/// </summary>
public sealed class CreditExposureTests
{
    private static CreditLedgerEntrySnapshot Entry(string orderReference, long amount, CreditEntryType type, string currency = "EUR") =>
        new(UniqueId.New(), orderReference, new Money(amount, currency), type, DateTimeOffset.UtcNow);

    [Fact]
    public void NeverHeld_YieldsAllZeroesAndNoHoldEntry()
    {
        var summary = CreditExposure.Summarise([]);

        Assert.Empty(summary.ByOrder);
        Assert.Equal(0, summary.CommittedExposure);
        Assert.Equal(0, summary.ActiveHolds);
        Assert.Equal(0, summary.OpenExposure);
    }

    [Fact]
    public void Held_YieldsActiveHoldEqualToTheHoldAndNoOpenExposure()
    {
        var entries = new[] { Entry("ORD-000001", 1_000, CreditEntryType.Hold) };

        var summary = CreditExposure.Summarise(entries);
        var order = Assert.Single(summary.ByOrder);

        Assert.Equal("ORD-000001", order.OrderReference);
        Assert.Equal(1_000, order.Exposure);
        Assert.Equal(0, order.OpenExposure);
        Assert.Equal(1_000, order.ActiveHold);
        Assert.True(order.HasHoldEntry);

        Assert.Equal(1_000, summary.CommittedExposure);
        Assert.Equal(1_000, summary.ActiveHolds);
        Assert.Equal(0, summary.OpenExposure);
    }

    [Fact]
    public void HeldThenConsumed_MovesTheEntireAmountFromActiveHoldToOpenExposure_LeavingCommittedExposureUnchanged()
    {
        var entries = new[]
        {
            Entry("ORD-000002", 2_000, CreditEntryType.Hold),
            Entry("ORD-000002", 2_000, CreditEntryType.Consume),
        };

        var summary = CreditExposure.Summarise(entries);
        var order = Assert.Single(summary.ByOrder);

        Assert.Equal(2_000, order.Exposure);
        Assert.Equal(2_000, order.OpenExposure);
        Assert.Equal(0, order.ActiveHold);
        Assert.True(order.HasHoldEntry);

        // R40: consume is numerically neutral — committedExposure is Σhold − Σrelease, consume appears in neither term.
        Assert.Equal(2_000, summary.CommittedExposure);
        Assert.Equal(0, summary.ActiveHolds);
        Assert.Equal(2_000, summary.OpenExposure);
    }

    [Fact]
    public void HeldThenReleased_CancelledBeforeInvoice_YieldsZeroExposureZeroOpenExposureZeroActiveHold_NeverNegative()
    {
        // The case domain-model.md §5.1's literal "applied to holds" formula
        // gets wrong: a naive openExposure = Σconsume − Σrelease would read
        // 0 − 3_000 = −3_000, a negative exposure B5 forbids.
        var entries = new[]
        {
            Entry("ORD-000003", 3_000, CreditEntryType.Hold),
            Entry("ORD-000003", 3_000, CreditEntryType.Release),
        };

        var summary = CreditExposure.Summarise(entries);
        var order = Assert.Single(summary.ByOrder);

        Assert.Equal(0, order.Exposure);
        Assert.Equal(0, order.OpenExposure);
        Assert.Equal(0, order.ActiveHold);
        Assert.True(order.HasHoldEntry);

        Assert.Equal(0, summary.CommittedExposure);
        Assert.Equal(0, summary.ActiveHolds);
        Assert.Equal(0, summary.OpenExposure);
    }

    [Fact]
    public void BC6_SplitsOutstandingExposureIntoActiveHoldsAndOpenInvoiceExposureWhoseSumAlwaysEqualsTheLimitMinusAvailableCredit()
    {
        var entries = new[]
        {
            Entry("ORD-000004", 1_000, CreditEntryType.Hold),
            Entry("ORD-000005", 2_000, CreditEntryType.Hold),
            Entry("ORD-000005", 2_000, CreditEntryType.Consume),
            Entry("ORD-000006", 3_000, CreditEntryType.Hold),
            Entry("ORD-000006", 3_000, CreditEntryType.Release),
        };

        var creditLimit = new Money(500_000, "EUR");
        var summary = CreditExposure.Summarise(entries);
        var availableCredit = creditLimit.Subtract(new Money(summary.CommittedExposure, "EUR"));

        Assert.Equal(summary.ActiveHolds + summary.OpenExposure, creditLimit.MinorUnits - availableCredit.MinorUnits);
        Assert.Equal(1_000, summary.ActiveHolds);
        Assert.Equal(2_000, summary.OpenExposure);
    }

    [Fact]
    public void BC28_GroupsLedgerEntriesByOrderReferenceCaseInsensitively_MatchingTheDatabasesOwnComparison()
    {
        var entries = new[]
        {
            Entry("ORD-000007", 500, CreditEntryType.Hold),
            Entry("ord-000007", 500, CreditEntryType.Consume),
        };

        var summary = CreditExposure.Summarise(entries);

        var order = Assert.Single(summary.ByOrder);
        Assert.Equal(500, order.Exposure);
        Assert.Equal(500, order.OpenExposure);
        Assert.Equal(0, order.ActiveHold);
    }

    [Fact]
    public void BC30_RaisesCreditLedgerOverflowRatherThanWrapping_WhenTheHoldTotalExceedsALong()
    {
        var half = long.MaxValue / 2 + 1;
        var entries = new[]
        {
            Entry("ORD-000008", half, CreditEntryType.Hold),
            Entry("ORD-000008", half, CreditEntryType.Hold),
        };

        var error = Assert.Throws<CreditLedgerOverflowError>(() => CreditExposure.Summarise(entries));
        Assert.Equal("CREDIT_LEDGER_OVERFLOW", error.Code);
    }

    [Fact]
    public void BC30_RaisesCreditLedgerOverflowRatherThanWrapping_WhenTheCommittedExposureAcrossOrdersExceedsALong()
    {
        // The same total, split across TWO orderReferences, so the wrap
        // happens in the CROSS-ORDER term (committedExposure += exposure)
        // rather than the per-order accumulator — a checked region that
        // covered only the inner (per-order) loop would pass the previous
        // case and fail this one.
        var half = long.MaxValue / 2 + 1;
        var entries = new[]
        {
            Entry("ORD-000009", half, CreditEntryType.Hold),
            Entry("ORD-000010", half, CreditEntryType.Hold),
        };

        var error = Assert.Throws<CreditLedgerOverflowError>(() => CreditExposure.Summarise(entries));
        Assert.Equal("CREDIT_LEDGER_OVERFLOW", error.Code);
    }
}
