using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// R13 and OI9 — the outbox writer's atomicity claim, against a real
/// MS-SQL transaction (design.md §4).
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class OutboxAtomicityTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task R13_UnitOfWork_PersistsNeitherTheAggregateNorTheOutboxRecordAndPublishesNothingWhenTheTransactionFails()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_r13_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(1), clock.UtcNow, UniqueId.New());
        var eventId = ((OrderDomainEvent)order.DomainEvents[0]).EventId.Value;

        // A row that will collide with the one the writer is about to
        // build, so the outbox INSERT (which runs before the aggregate's
        // own SaveChangesAsync — EfCoreOrderRepository.SaveChangesAsync)
        // fails while still inside the ambient transaction the unit of
        // work opened.
        await using (var conflictDb = fixture.CreateDbContext(connectionString))
        {
            conflictDb.OutboxMessages.Add(ConflictingOutboxMessage(eventId));
            await conflictDb.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext(connectionString);
        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
        var unitOfWork = new EfCoreUnitOfWork(db);

        await Assert.ThrowsAsync<SqlException>(() => unitOfWork.ExecuteAsync(
            async ct =>
            {
                await repository.AddAsync(order, ct);
                await repository.SaveChangesAsync(ct);
            },
            CancellationToken.None));

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(0, await assertDb.Orders.CountAsync());
        Assert.Equal(0, await assertDb.OrderItems.CountAsync());
        // The only row present is the pre-existing conflicting one — the
        // writer's own row never landed.
        Assert.Equal(1, await assertDb.OutboxMessages.CountAsync());
        Assert.Equal(eventId, (await assertDb.OutboxMessages.SingleAsync()).EventId);
    }

    /// <summary>
    /// The OTHER direction from the case above, and the one that actually
    /// discriminates arming row 2 (design.md §9.4): the outbox row's own
    /// INSERT succeeds first (no conflict on it at all), and the
    /// aggregate's own <c>db.SaveChangesAsync()</c> — which runs AFTER it,
    /// per <c>EfCoreOrderRepository.SaveChangesAsync</c> — is what fails
    /// (a duplicate <c>order_reference</c>). If the outbox INSERT ever
    /// escaped the ambient transaction (committed on its own, outside it),
    /// this is the case that would catch it: the outbox row would survive
    /// even though the order row that was supposed to accompany it never
    /// landed. The first R13 case above cannot catch that defect, because
    /// its failure happens on the FIRST write attempted either way, so
    /// "escaped the transaction" and "rolled back correctly" look
    /// identical (zero rows, either way) — found and recorded during this
    /// feature's own arming pass (progress/impl_outbox_and_idempotency.md).
    /// </summary>
    [Fact]
    public async Task R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_r13b_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var reference = new OrderNumber(50);

        // First order legitimately takes the reference, committed for real.
        await using (var seedOrderDb = fixture.CreateDbContext(connectionString))
        {
            var seedOrder = OrderPersistenceTestSupport.Place(reference, clock.UtcNow, UniqueId.New());
            var seedRepository = new EfCoreOrderRepository(seedOrderDb, new OutboxWriter(clock));
            var seedUnitOfWork = new EfCoreUnitOfWork(seedOrderDb);
            await seedUnitOfWork.ExecuteAsync(async ct => { await seedRepository.AddAsync(seedOrder, ct); await seedRepository.SaveChangesAsync(ct); }, CancellationToken.None);
        }

        // Second order collides on order_reference (unique index) — its own
        // outbox row has no conflict and inserts cleanly; the FAILURE is on
        // the order row's own SaveChangesAsync, which runs afterward.
        var colliding = OrderPersistenceTestSupport.Place(reference, clock.UtcNow, UniqueId.New());

        await using var db = fixture.CreateDbContext(connectionString);
        var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock));
        var unitOfWork = new EfCoreUnitOfWork(db);

        await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteAsync(
            async ct =>
            {
                await repository.AddAsync(colliding, ct);
                await repository.SaveChangesAsync(ct);
            },
            CancellationToken.None));

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(1, await assertDb.Orders.CountAsync());
        // Exactly one outbox row — the seed order's. The colliding order's
        // own outbox row, though it inserted cleanly before the failure,
        // did not survive: it rolled back with everything else.
        Assert.Equal(1, await assertDb.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task OI9_Repository_ProducesExactlyOneOutboxRecordPerFactWhenTheOperationIsRetriedAfterARolledBackUnitOfWork()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi9_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(2), clock.UtcNow, UniqueId.New());
        var eventId = ((OrderDomainEvent)order.DomainEvents[0]).EventId.Value;
        Assert.Single(order.DomainEvents);

        // Force the outbox row's own INSERT (EfCoreOrderRepository.
        // InsertOutboxRowAsync, called BEFORE the aggregate's
        // db.SaveChangesAsync()) to throw, by pre-committing a row that
        // collides with the writer's own — never a manual `throw` placed
        // AFTER SaveChangesAsync returns, which would already have cleared
        // the aggregate's events (this is the case design.md §4.5 and
        // arming row 3 name).
        await using (var conflictDb = fixture.CreateDbContext(connectionString))
        {
            conflictDb.OutboxMessages.Add(ConflictingOutboxMessage(eventId));
            await conflictDb.SaveChangesAsync();
        }

        await using (var failingDb = fixture.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(failingDb, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(failingDb);

            await Assert.ThrowsAsync<SqlException>(() => unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None));
        }

        // The failed unit of work did not clear the aggregate's events —
        // design.md §4.5's contract, proven rather than assumed.
        Assert.Single(order.DomainEvents);

        // Clear the path, then retry with THE SAME aggregate instance in a
        // fresh scope (fresh DbContext, fresh repository) — the delegate
        // contract of IUnitOfWork.ExecuteAsync ("safe to execute more than
        // once") realised the way a retried command handler would realise
        // it: re-create the scope, not the aggregate, when the aggregate
        // itself was never persisted.
        await using (var cleanupDb = fixture.CreateDbContext(connectionString))
        {
            await cleanupDb.OutboxMessages.Where(o => o.EventId == eventId).ExecuteDeleteAsync();
        }

        await using (var retryDb = fixture.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(retryDb, new OutboxWriter(clock));
            var unitOfWork = new EfCoreUnitOfWork(retryDb);

            await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    await repository.AddAsync(order, ct);
                    await repository.SaveChangesAsync(ct);
                },
                CancellationToken.None);
        }

        // The retry cleared the events (SaveChangesAsync completed this time).
        Assert.Empty(order.DomainEvents);

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(1, await assertDb.Orders.CountAsync());
        var outboxRows = await assertDb.OutboxMessages.ToListAsync();
        Assert.Single(outboxRows);
        Assert.Equal(eventId, outboxRows[0].EventId);
        Assert.Equal("order.placed.v1", outboxRows[0].EventType);
    }

    private static OutboxMessage ConflictingOutboxMessage(Guid eventId) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        EventType = "order.placed.v1",
        AggregateId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid(),
        CausationId = Guid.NewGuid(),
        Payload = "{}",
        OccurredAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };
}
