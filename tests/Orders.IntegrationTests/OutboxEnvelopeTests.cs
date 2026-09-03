using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>R12, OI1 and (D7) the membership half of R11 — design.md §4.4, §4.7.</summary>
[Collection(MsSqlCollection.Name)]
public sealed class OutboxEnvelopeTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task R12_Outbox_StampsEveryFactOfOneOrderWithTheOrderIdAsCorrelationIdAndTheCausingEventIdAsCausationId()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_r12_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var placeCausationId = UniqueId.New();
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(3), clock.UtcNow, placeCausationId);

        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        // Two fact-bearing transitions of the SAME order: Place (already
        // saved above) and Confirm, driven through a freshly reloaded
        // aggregate so the second transaction proves the chain end to end.
        var confirmCausationId = UniqueId.New();
        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    var reloaded = await repository.GetByIdAsync(order.Id, ct);
                    Assert.NotNull(reloaded);
                    // T-1: placed -> stock_reserved -> credit_approved ->
                    // confirmed. The first two edges are silent (design.md
                    // §7.4) — walked here only to make Confirm legal.
                    reloaded!.MarkStockReserved(clock.UtcNow.AddSeconds(30));
                    reloaded.ApproveCredit(clock.UtcNow.AddSeconds(45));
                    reloaded.Confirm(clock.UtcNow.AddMinutes(1), confirmCausationId);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        await using var assertDb = fixture.CreateDbContext(connectionString);
        var rows = await assertDb.OutboxMessages.OrderBy(o => o.Seq).ToListAsync();
        Assert.Equal(2, rows.Count);

        Assert.All(rows, row => Assert.Equal(order.Id.Value, row.CorrelationId));

        var placedRow = Assert.Single(rows, r => r.EventType == "order.placed.v1");
        Assert.Equal(placeCausationId.Value, placedRow.CausationId);

        var confirmedRow = Assert.Single(rows, r => r.EventType == "order.confirmed.v1");
        Assert.Equal(confirmCausationId.Value, confirmedRow.CausationId);
    }

    [Fact]
    public async Task OI1_Relay_ReconstructsTheCompleteEnvelopeFromTheStoredRecordAloneInferringNoFieldAtPublicationTime()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi1_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var causationId = UniqueId.New();
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(4), clock.UtcNow, causationId);
        var placedEvent = (OrderPlaced)order.DomainEvents[0];

        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(db);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        await using var assertDb = fixture.CreateDbContext(connectionString);
        var row = await assertDb.OutboxMessages.SingleAsync();

        // All seven envelope fields survive the round trip — read from the
        // stored row alone, no field re-derived at read time.
        Assert.Equal(placedEvent.EventId.Value, row.EventId);
        Assert.Equal(placedEvent.EventType, row.EventType);
        Assert.Equal(placedEvent.AggregateId.Value, row.AggregateId);
        Assert.Equal(placedEvent.CorrelationId.Value, row.CorrelationId);
        Assert.Equal(placedEvent.CausationId.Value, row.CausationId);
        Assert.Equal(placedEvent.OccurredAt.UtcDateTime, row.OccurredAt);
        Assert.False(string.IsNullOrEmpty(row.Payload));

        // The relay's own row -> wire mapping reconstructs the envelope from
        // the stored row alone — no clock, no Guid.NewGuid(), no default.
        var wireBytes = OutboxEnvelopeMapper.ToWireBytes(row);
        using var document = JsonDocument.Parse(wireBytes);
        var envelope = document.RootElement;

        Assert.Equal(placedEvent.EventId.Value, envelope.GetProperty("eventId").GetGuid());
        Assert.Equal(placedEvent.EventType, envelope.GetProperty("eventType").GetString());
        Assert.Equal(placedEvent.AggregateId.Value, envelope.GetProperty("aggregateId").GetGuid());
        Assert.Equal(placedEvent.CorrelationId.Value, envelope.GetProperty("correlationId").GetGuid());
        Assert.Equal(placedEvent.CausationId.Value, envelope.GetProperty("causationId").GetGuid());
        Assert.True(envelope.TryGetProperty("payload", out _));
    }

    /// <summary>
    /// An event failing <c>Validate</c> — an empty <c>causationId</c> — is
    /// refused AT THE WRITER, so no incomplete row can be committed (OI1's
    /// second half).
    /// </summary>
    [Fact]
    public void OI1_Writer_RefusesAnEventWithAnEmptyCausationIdBeforeAnyRowIsBuilt()
    {
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(5), clock.UtcNow, causationId: default);

        var writer = new OutboxWriter(clock);

        Assert.Throws<IncompleteDomainEventEnvelopeError>(() => writer.BuildRows(order.DomainEvents));
    }

    /// <summary>The membership half of D2 (§4.4): an eventType absent from FactCatalog is refused, which the pure SharedKernel guard cannot check on its own.</summary>
    [Fact]
    public void R11_Outbox_RefusesToStoreAFactWhoseEventTypeIsNotInTheDeclaredFactCatalogue()
    {
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var uncataloguedEvent = new UncataloguedDomainEvent(
            UniqueId.New(), UniqueId.New(), UniqueId.New(), UniqueId.New(), clock.UtcNow);

        var writer = new OutboxWriter(clock);

        var exception = Assert.Throws<InvalidOperationException>(() => writer.BuildRows([uncataloguedEvent]));
        Assert.Contains("order.not_a_real_fact.v1", exception.Message, StringComparison.Ordinal);
    }

    private sealed record UncataloguedDomainEvent(
        UniqueId EventId,
        UniqueId AggregateId,
        UniqueId CorrelationId,
        UniqueId CausationId,
        DateTimeOffset OccurredAt) : OrderDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
    {
        public override string EventType => "order.not_a_real_fact.v1";
    }
}
