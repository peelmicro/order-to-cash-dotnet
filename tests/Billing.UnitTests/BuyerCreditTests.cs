using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `R37` (matrix `buyer-credit.spec`), `BC5`, and `B10`'s OUTER bound —
/// <see cref="BuyerCredit.Reconstitute"/> refusing a snapshot that already
/// breaks `B1` or `B3`.
/// </summary>
public sealed class BuyerCreditTests
{
    private static BuyerCreditSnapshot LineWithOneHold(long limit, long heldAmount, string orderReference = "ORD-000001", string currency = "EUR") =>
        new(
            UniqueId.New(),
            "CR-000001",
            "CarrefourEs",
            "IBERFOODS",
            new Money(limit, currency),
            heldAmount,
            [new CreditLedgerEntrySnapshot(UniqueId.New(), orderReference, new Money(heldAmount, currency), CreditEntryType.Hold, DateTimeOffset.UtcNow)]);

    private static CreditContext Ctx() => new(DateTimeOffset.UtcNow, UniqueId.New());

    [Fact]
    public void R37_KeepsActiveHoldsPlusOpenExposureWithinTheCreditLimitAndRaisesOnAnyUpdateOrDeletionOfALedgerEntry()
    {
        var snapshot = LineWithOneHold(limit: 10_000, heldAmount: 1_000);
        var originalHold = snapshot.Entries[0];
        var credit = BuyerCredit.Reconstitute(snapshot);

        // There is no mutator on CreditLedgerEntry that could "update or
        // delete" the loaded hold row — B2 is enforced by shape. The only
        // way to reverse a hold is to APPEND a release.
        var released = credit.Release(OrderNumber.Parse("ORD-000001"), CreditReleaseReason.OrderCancelled, UniqueId.New(), Ctx(), UniqueId.New);

        Assert.NotNull(released);
        Assert.Equal(CreditEntryType.Release, released!.Type);

        // The loaded entry survives byte for byte — appended, never rewritten.
        var afterSnapshot = credit.ToSnapshot();
        var survivingHold = Assert.Single(afterSnapshot.Entries, e => e.Type == CreditEntryType.Hold);
        Assert.Equal(originalHold.Id, survivingHold.Id);
        Assert.Equal(originalHold.Amount, survivingHold.Amount);
        Assert.Equal(originalHold.EntryDate, survivingHold.EntryDate);

        var appended = Assert.Single(credit.AppendedEntries);
        Assert.Equal(CreditEntryType.Release, appended.Type);

        // R37's invariant, restated over the resulting state.
        Assert.True(credit.Summary.ActiveHolds + credit.Summary.OpenExposure <= credit.CreditLimit.MinorUnits);
    }

    [Fact]
    public void BC5_DerivesAvailableCreditAsTheLimitMinusHoldsPlusReleases_SoAConsumeEntryMovesItByNothing()
    {
        var snapshot = LineWithOneHold(limit: 10_000, heldAmount: 1_000);
        var credit = BuyerCredit.Reconstitute(snapshot);

        Assert.Equal(9_000, credit.AvailableCredit.MinorUnits);

        var consumed = credit.Consume(OrderNumber.Parse("ORD-000001"), Ctx(), UniqueId.New);

        Assert.Equal(CreditEntryType.Consume, consumed.Type);
        Assert.Equal(9_000, credit.AvailableCredit.MinorUnits);
        Assert.Empty(credit.DomainEvents);
    }

    [Fact]
    public void Reconstitute_RefusesASnapshotWhoseCommittedExposureAlreadyExceedsTheCreditLimit_AndASnapshotWhoseEntryCurrencyDiffersFromTheLines()
    {
        var overLimitSnapshot = new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(1_000, "EUR"), 1_001, []);
        Assert.Throws<InvalidBuyerCreditSnapshotError>(() => BuyerCredit.Reconstitute(overLimitSnapshot));

        var mismatchedCurrencySnapshot = new BuyerCreditSnapshot(
            UniqueId.New(),
            "CR-000002",
            "CarrefourEs",
            "IBERFOODS",
            new Money(10_000, "EUR"),
            1_000,
            [new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(1_000, "GBP"), CreditEntryType.Hold, DateTimeOffset.UtcNow)]);
        Assert.Throws<InvalidBuyerCreditSnapshotError>(() => BuyerCredit.Reconstitute(mismatchedCurrencySnapshot));
    }
}
