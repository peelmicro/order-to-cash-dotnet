using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`R40`, `R41` (matrix `credit-ledger.spec`), `BC11`, `BC12`.</summary>
public sealed class CreditLedgerTests
{
    private static BuyerCreditSnapshot LineWithHold(long limit, long heldAmount, string orderReference = "ORD-000001") =>
        new(
            UniqueId.New(),
            "CR-000001",
            "CarrefourEs",
            "IBERFOODS",
            new Money(limit, "EUR"),
            heldAmount,
            [new CreditLedgerEntrySnapshot(UniqueId.New(), orderReference, new Money(heldAmount, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow)]);

    private static CreditContext Ctx() => new(DateTimeOffset.UtcNow, UniqueId.New());

    [Fact]
    public void R40_AppendsAConsumeEntryAtInvoiceIssueThatLeavesAvailableCreditNumericallyUnchangedAndEmitsNoFact()
    {
        var credit = BuyerCredit.Reconstitute(LineWithHold(limit: 10_000, heldAmount: 3_000));
        var before = credit.AvailableCredit.MinorUnits;

        var entry = credit.Consume(OrderNumber.Parse("ORD-000001"), Ctx(), UniqueId.New);

        Assert.Equal(CreditEntryType.Consume, entry.Type);
        Assert.Equal(3_000, entry.Amount.MinorUnits);
        Assert.Equal(before, credit.AvailableCredit.MinorUnits);
        Assert.Empty(credit.DomainEvents);
    }

    [Fact]
    public void R41_ReleasesWithReasonInvoicePaidOnPaymentAndWithReasonOrderCancelledOnCancellation_RestoringAvailableCreditWithoutGoingBelowZero()
    {
        var cancelled = BuyerCredit.Reconstitute(LineWithHold(limit: 10_000, heldAmount: 2_000, orderReference: "ORD-000001"));
        var cancelledBefore = cancelled.AvailableCredit.MinorUnits;

        var cancelledEntry = cancelled.Release(OrderNumber.Parse("ORD-000001"), CreditReleaseReason.OrderCancelled, UniqueId.New(), Ctx(), UniqueId.New);
        var cancelledFact = Assert.IsType<CreditReleased>(Assert.Single(cancelled.DomainEvents));

        Assert.NotNull(cancelledEntry);
        Assert.Equal(CreditReleaseReason.OrderCancelled, cancelledFact.Reason);
        Assert.Equal(2_000, cancelledFact.ReleasedAmount.MinorUnits);
        Assert.Equal(cancelledBefore + 2_000, cancelled.AvailableCredit.MinorUnits);
        Assert.True(cancelled.AvailableCredit.MinorUnits <= cancelled.CreditLimit.MinorUnits);

        var paid = BuyerCredit.Reconstitute(LineWithHold(limit: 10_000, heldAmount: 4_000, orderReference: "ORD-000002"));
        var paidBefore = paid.AvailableCredit.MinorUnits;

        var paidEntry = paid.Release(OrderNumber.Parse("ORD-000002"), CreditReleaseReason.InvoicePaid, UniqueId.New(), Ctx(), UniqueId.New);
        var paidFact = Assert.IsType<CreditReleased>(Assert.Single(paid.DomainEvents));

        Assert.NotNull(paidEntry);
        Assert.Equal(CreditReleaseReason.InvoicePaid, paidFact.Reason);
        Assert.Equal(4_000, paidFact.ReleasedAmount.MinorUnits);
        Assert.Equal(paidBefore + 4_000, paid.AvailableCredit.MinorUnits);
    }

    [Fact]
    public void BC11_ReleasesTheOrdersOutstandingExposureOnce_ReportsANoOpOnASecondRelease_AndRefusesAReleaseThatWouldDriveExposureBelowZero()
    {
        var credit = BuyerCredit.Reconstitute(LineWithHold(limit: 10_000, heldAmount: 1_500));

        var first = credit.Release(OrderNumber.Parse("ORD-000001"), CreditReleaseReason.OrderCancelled, UniqueId.New(), Ctx(), UniqueId.New);

        Assert.NotNull(first);
        Assert.Equal(1_500, first!.Amount.MinorUnits);
        var appended = Assert.Single(credit.AppendedEntries);
        Assert.Equal(CreditEntryType.Release, appended.Type);
        var fact = Assert.IsType<CreditReleased>(Assert.Single(credit.DomainEvents));
        Assert.Equal(1_500, fact.ReleasedAmount.MinorUnits);

        // A second release for the same order: exposure is now zero — B5
        // forbids releasing further, and the aggregate refuses to append a
        // release that would drive it below zero by reporting a no-op
        // rather than fabricating a negative entry.
        var second = credit.Release(OrderNumber.Parse("ORD-000001"), CreditReleaseReason.OrderCancelled, UniqueId.New(), Ctx(), UniqueId.New);

        Assert.Null(second);
        Assert.Single(credit.AppendedEntries); // unchanged — still just the one release
        Assert.Single(credit.DomainEvents); // no second fact
    }

    [Fact]
    public void BC12_AppendsAConsumeEntryThatLeavesAvailableCreditUnchangedAndEmitsNoFact_AndRefusesToConsumeAnOrderWithNoActiveHold()
    {
        var credit = BuyerCredit.Reconstitute(LineWithHold(limit: 10_000, heldAmount: 2_500));
        var before = credit.AvailableCredit.MinorUnits;

        var entry = credit.Consume(OrderNumber.Parse("ORD-000001"), Ctx(), UniqueId.New);

        Assert.Equal(CreditEntryType.Consume, entry.Type);
        Assert.Equal(before, credit.AvailableCredit.MinorUnits);
        Assert.Empty(credit.DomainEvents);

        var noHoldCredit = BuyerCredit.Reconstitute(new BuyerCreditSnapshot(UniqueId.New(), "CR-000002", "CarrefourEs", "IBERFOODS", new Money(10_000, "EUR"), 0, []));

        Assert.Throws<NoActiveHoldError>(() => noHoldCredit.Consume(OrderNumber.Parse("ORD-000099"), Ctx(), UniqueId.New));
    }
}
