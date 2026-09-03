using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>R17, R18, OI10 — the idempotent-consumer primitive, over the real MS-SQL write model (design.md §6).</summary>
[Collection(MsSqlCollection.Name)]
public sealed class IdempotentConsumerTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task R17_IdempotentConsumer_RecordsTheEventIdAndConsumerNameInTheSameTransactionAsTheStateChangeAndTheOutboxRecords()
    {
        var connectionString = await SeedOrderReadyToConfirmAsync();
        var orderId = await GetOnlyOrderIdAsync(connectionString);
        var eventId = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var consumer = BuildConsumer(db);

            var outcome = await consumer.RunOnceAsync(
                eventId,
                ConsumerName.OrdersSaga,
                async ct =>
                {
                    var repository = new EfCoreOrderRepository(db, new OutboxWriter(new FakeClock(FakeClock.UtcNowToTheMillisecond())));
                    var order = await repository.GetByIdAsync(orderId, ct);
                    order!.Confirm(FakeClock.UtcNowToTheMillisecond(), UniqueId.New());
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);

            Assert.Equal(ConsumptionOutcome.Processed, outcome);
        }

        await using (var assertDb = fixture.CreateDbContext(connectionString))
        {
            Assert.Equal(1, await assertDb.ProcessedEvents.CountAsync(p => p.EventId == eventId));
            Assert.Equal("confirmed", (await assertDb.Orders.SingleAsync()).Status);
            Assert.Equal(1, await assertDb.OutboxMessages.CountAsync(o => o.EventType == "order.confirmed.v1"));
        }
    }

    /// <summary>
    /// The second half R17 also demands, and #7's reviewer found missing
    /// from an identically-named case (defect D10): a failure INSIDE
    /// <c>work</c> — after the dedup record was inserted — leaves NO dedup
    /// row, because the whole transaction rolls back together.
    /// </summary>
    [Fact]
    public async Task R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction()
    {
        var connectionString = await SeedOrderReadyToConfirmAsync();
        var eventId = Guid.NewGuid();

        await using var db = fixture.CreateDbContext(connectionString);
        var consumer = BuildConsumer(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.RunOnceAsync(
            eventId,
            ConsumerName.OrdersSaga,
            _ => throw new InvalidOperationException("the handler's effects failed"),
            CancellationToken.None));

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(0, await assertDb.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task R18_IdempotentConsumer_AcknowledgesARedeliveredFactWithoutMutatingStateEmittingAFactOrIssuingACommand()
    {
        var connectionString = await SeedOrderReadyToConfirmAsync();
        var orderId = await GetOnlyOrderIdAsync(connectionString);
        var eventId = Guid.NewGuid();
        var workCallCount = 0;

        Func<OrdersDbContext, CancellationToken, Task> work = async (db, ct) =>
        {
            workCallCount++;
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(new FakeClock(FakeClock.UtcNowToTheMillisecond())));
            var order = await repository.GetByIdAsync(orderId, ct);
            order!.Confirm(FakeClock.UtcNowToTheMillisecond(), UniqueId.New());
            await repository.SaveChangesAsync(ct);
        };

        await using (var db1 = fixture.CreateDbContext(connectionString))
        {
            var consumer1 = BuildConsumer(db1);
            var first = await consumer1.RunOnceAsync(eventId, ConsumerName.OrdersSaga, ct => work(db1, ct), CancellationToken.None);
            Assert.Equal(ConsumptionOutcome.Processed, first);
        }

        Assert.Equal(1, workCallCount);

        await using (var db2 = fixture.CreateDbContext(connectionString))
        {
            var consumer2 = BuildConsumer(db2);
            var second = await consumer2.RunOnceAsync(eventId, ConsumerName.OrdersSaga, ct => work(db2, ct), CancellationToken.None);
            Assert.Equal(ConsumptionOutcome.Duplicate, second);
        }

        // work was NEVER invoked on the redelivery.
        Assert.Equal(1, workCallCount);

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(1, await assertDb.ProcessedEvents.CountAsync());
        // Exactly one confirmation — the redelivery emitted no second fact.
        Assert.Equal(1, await assertDb.OutboxMessages.CountAsync(o => o.EventType == "order.confirmed.v1"));
    }

    [Fact]
    public async Task OI10_IdempotentConsumer_AppliesTheHandlersEffectsOnceWhenTheSameEventIsDeliveredConcurrentlyToTwoConsumers()
    {
        var connectionString = await SeedOrderReadyToConfirmAsync();
        var eventId = Guid.NewGuid();
        var orderId = await GetOnlyOrderIdAsync(connectionString);

        var effectsRun = 0;

        async Task<ConsumptionOutcome> DeliverAsync()
        {
            await using var db = fixture.CreateDbContext(connectionString);
            var consumer = BuildConsumer(db);
            return await consumer.RunOnceAsync(
                eventId,
                ConsumerName.OrdersSaga,
                async ct =>
                {
                    Interlocked.Increment(ref effectsRun);
                    var repository = new EfCoreOrderRepository(db, new OutboxWriter(new FakeClock(FakeClock.UtcNowToTheMillisecond())));
                    var order = await repository.GetByIdAsync(orderId, ct);
                    order!.Confirm(FakeClock.UtcNowToTheMillisecond(), UniqueId.New());
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        var taskA = DeliverAsync();
        var taskB = DeliverAsync();
        var outcomes = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, outcomes.Count(o => o == ConsumptionOutcome.Processed));
        Assert.Equal(1, outcomes.Count(o => o == ConsumptionOutcome.Duplicate));
        Assert.Equal(1, effectsRun);

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(1, await assertDb.ProcessedEvents.CountAsync(p => p.EventId == eventId));
        Assert.Equal(1, await assertDb.OutboxMessages.CountAsync(o => o.EventType == "order.confirmed.v1"));
    }

    /// <summary>saga.md §6 layer 1: dedup is per (eventId, consumer) PAIR, not per eventId alone — the same event delivered to two different consumers must run BOTH.</summary>
    [Fact]
    public async Task IdempotentConsumer_DedupsPerEventIdAndConsumerPairNotPerEventId()
    {
        var connectionString = await SeedOrderReadyToConfirmAsync();
        var eventId = Guid.NewGuid();

        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var consumer = BuildConsumer(db);
            var outcome = await consumer.RunOnceAsync(eventId, ConsumerName.OrdersSaga, _ => Task.CompletedTask, CancellationToken.None);
            Assert.Equal(ConsumptionOutcome.Processed, outcome);
        }

        await using (var db = fixture.CreateDbContext(connectionString))
        {
            var consumer = BuildConsumer(db);
            var outcome = await consumer.RunOnceAsync(eventId, ConsumerName.Projector, _ => Task.CompletedTask, CancellationToken.None);
            Assert.Equal(ConsumptionOutcome.Processed, outcome);
        }

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(2, await assertDb.ProcessedEvents.CountAsync(p => p.EventId == eventId));
    }

    /// <summary>Seeds reference data and a placed order walked to <c>credit_approved</c>, ready for a legal <c>Confirm</c> — the fact-bearing transition <c>work</c> exercises in these tests.</summary>
    private async Task<string> SeedOrderReadyToConfirmAsync()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_idem_{Guid.NewGuid():N}");
        await using var db = fixture.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
        await OrderPersistenceTestSupport.SeedReferenceDataAsync(db);

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());
        order.MarkStockReserved(clock.UtcNow.AddSeconds(1));
        order.ApproveCredit(clock.UtcNow.AddSeconds(2));

        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
        var unitOfWork = new EfCoreUnitOfWork(db);
        await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);

        return connectionString;
    }

    private async Task<UniqueId> GetOnlyOrderIdAsync(string connectionString)
    {
        await using var db = fixture.CreateDbContext(connectionString);
        var row = await db.Orders.SingleAsync();
        return UniqueId.From(row.Id);
    }

    private static IdempotentConsumer BuildConsumer(OrdersDbContext db)
    {
        var unitOfWork = new EfCoreUnitOfWork(db);
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var ledger = new ProcessedEventLedger();
        return new IdempotentConsumer(unitOfWork, clock, ledger, db);
    }
}
