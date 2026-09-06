using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC11` no-op, `BC25` handler half.</summary>
public sealed class CreditReleaseServiceTests
{
    private static BuyerCredit ReconstituteLine(long limit, long committedExposure, IReadOnlyList<CreditLedgerEntrySnapshot>? entries = null) =>
        BuyerCredit.Reconstitute(new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(limit, "EUR"), committedExposure, entries ?? []));

    private static ReleaseCreditCommand Command(string orderReference) =>
        new(orderReference, "CarrefourEs", "IBERFOODS", UniqueId.New(), UniqueId.New());

    [Fact]
    public async Task ReleasingAnOrderWithOutstandingExposure_RecordsExactlyOneReleaseEntryAndExactlyOneCreditReleasedV1WithReasonOrderCancelled()
    {
        var entries = new[] { new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(2_000, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow) };
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 2_000, entries: entries) };
        var service = new CreditReleaseService(new FakeUnitOfWork(), repository, new FakeClock());

        var reply = await service.ReleaseAsync(Command("ORD-000001"), CancellationToken.None);

        Assert.True(reply.Released);
        Assert.Equal(2_000, reply.ReleasedAmount);
        Assert.Equal(1, repository.SaveChangesCallCount);

        Assert.NotNull(repository.Saved);
        var appended = Assert.Single(repository.Saved!.AppendedEntries);
        Assert.Equal(CreditEntryType.Release, appended.Type);

        var fact = Assert.IsType<CreditReleased>(Assert.Single(repository.Saved.DomainEvents));
        Assert.Equal(CreditReleaseReason.OrderCancelled, fact.Reason);
        Assert.Equal(2_000, fact.ReleasedAmount.MinorUnits);
        Assert.Equal(10_000, fact.AvailableCreditAfter.MinorUnits);
    }

    [Fact]
    public async Task ReleasingAnOrderWithNoOutstandingExposure_WritesNothingRecordsNoFactAndRepliesReleasedFalse()
    {
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 0, entries: []) };
        var service = new CreditReleaseService(new FakeUnitOfWork(), repository, new FakeClock());

        var reply = await service.ReleaseAsync(Command("ORD-000099"), CancellationToken.None);

        Assert.False(reply.Released);
        Assert.Equal(0, repository.SaveChangesCallCount);
        Assert.Null(repository.Saved);
    }
}
