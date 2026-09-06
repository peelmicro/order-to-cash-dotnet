using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BC7`, `BC13`, `BC14` handler half, reply-after-commit, rollback ⇒ no
/// reply. `BC7`'s case reproduces #7's blocking defect `D1` deliberately
/// (design.md §6.4) — the port-refusal branch emitting no fact while the
/// reply stays correct.
/// </summary>
public sealed class CreditHoldServiceTests
{
    private static BuyerCredit ReconstituteLine(long limit, long committedExposure, IReadOnlyList<CreditLedgerEntrySnapshot>? entries = null, string currency = "EUR") =>
        BuyerCredit.Reconstitute(new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(limit, currency), committedExposure, entries ?? []));

    private static HoldCreditCommand Command(string orderReference, long amount, string currency = "EUR") =>
        new(orderReference, "CarrefourEs", "IBERFOODS", amount, currency, UniqueId.New(), UniqueId.New());

    [Fact]
    public async Task BC7_ShortCircuitsToAlreadyHeld_OnAHoldEntryWhoseExposureHasSinceBeenReleased_CallingNoPortAndWritingNothing()
    {
        // A hold entry exists for the order, but it has since been fully
        // released — net exposure for the order is zero, so a
        // net-exposure-filtering short-circuit would happily re-evaluate
        // and re-approve. The requested amount here IS affordable, which is
        // exactly what makes that wrong shape observable.
        var entries = new CreditLedgerEntrySnapshot[]
        {
            new(UniqueId.New(), "ORD-000001", new Money(1_000, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow),
            new(UniqueId.New(), "ORD-000001", new Money(1_000, "EUR"), CreditEntryType.Release, DateTimeOffset.UtcNow),
        };
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 0, entries: entries) };
        var port = new FakeCreditDecisionPort();
        var service = new CreditHoldService(new FakeUnitOfWork(), repository, port, new FakeClock());

        var reply = await service.HoldAsync(Command("ORD-000001", 500), CancellationToken.None);

        Assert.Equal("already_held", reply.Outcome);
        Assert.Equal(1_000, reply.HeldAmount);
        Assert.Equal(0, port.CallCount);
        Assert.Equal(0, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task BC13_ConsultsTheCreditDecisionPortOnlyAfterTheAggregateHasFoundTheAmountFits_AndNeverForAnOverLimitRequest()
    {
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 1_000, committedExposure: 0) };
        var port = new FakeCreditDecisionPort();
        var service = new CreditHoldService(new FakeUnitOfWork(), repository, port, new FakeClock());

        var reply = await service.HoldAsync(Command("ORD-000001", 1_500), CancellationToken.None);

        Assert.Equal("rejected", reply.Outcome);
        Assert.Equal("over_limit", reply.Reason);
        Assert.Equal(0, port.CallCount);
        Assert.Equal(1, repository.SaveChangesCallCount);
    }

    [Fact]
    public async Task BC14_ReturnsRejectedWithThePortsReasonAndRecordsExactlyOneCreditRejectedFactCarryingThatReasonTheRequestedAmountAndTheUnchangedAvailableCredit_WhenThePortRefusesAFittingHold()
    {
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 0) };
        var port = new FakeCreditDecisionPort { Result = new CreditDecision.Refuse(AdapterRejectionReason.SimulatedCentsRule) };
        var service = new CreditHoldService(new FakeUnitOfWork(), repository, port, new FakeClock());

        var command = Command("ORD-000001", 1_500);
        var reply = await service.HoldAsync(command, CancellationToken.None);

        Assert.Equal("rejected", reply.Outcome);
        Assert.Equal("simulated_cents_rule", reply.Reason);
        Assert.Equal(1, port.CallCount);
        Assert.Equal(10_000, reply.AvailableCredit);

        // Asserted on the domain events of the aggregate HANDED TO
        // SaveChangesAsync — not on the reply, not on a spy.
        Assert.NotNull(repository.Saved);
        var fact = Assert.IsType<CreditRejected>(Assert.Single(repository.Saved!.DomainEvents));
        Assert.Equal(CreditRejectionReason.SimulatedCentsRule, fact.Reason);
        Assert.Equal(1_500, fact.RequestedAmount.MinorUnits);
        Assert.Equal(10_000, fact.AvailableCredit.MinorUnits);
        Assert.Empty(repository.Saved.AppendedEntries);
    }

    [Fact]
    public async Task TheReplyIsBuiltFromTheOutcomeButOnlyReturnedAfterTheTransactionCommits_AndARollbackNeverProducesAReply()
    {
        var repository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 0) };
        var port = new FakeCreditDecisionPort();
        var okService = new CreditHoldService(new FakeUnitOfWork(), repository, port, new FakeClock());

        var okReply = await okService.HoldAsync(Command("ORD-000001", 1_000), CancellationToken.None);
        Assert.Equal("approved", okReply.Outcome);

        var rollingBackRepository = new FakeBuyerCreditRepository { LockResult = ReconstituteLine(limit: 10_000, committedExposure: 0) };
        var rollingBackService = new CreditHoldService(new ThrowingAfterWorkUnitOfWork(), rollingBackRepository, port, new FakeClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => rollingBackService.HoldAsync(Command("ORD-000002", 1_000), CancellationToken.None));
    }
}
