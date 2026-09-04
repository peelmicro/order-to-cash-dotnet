using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Saga;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// design.md §6.3 — SO11 (the lease), enqueue enlisting in the ambient
/// transaction, and a duplicate enqueue. Real MS-SQL, no Kafka/NATS needed.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class SagaCommandStoreTests(MsSqlContainerFixture mssql)
{
    private static readonly Guid _orderId = Guid.NewGuid();

    [Fact]
    public async Task SO11_AClaimedRowIsInvisibleToAConcurrentClaimUntilItsLeaseElapses()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(BuildOptions(leaseMs: 60_000));

        await using var db = mssql.CreateDbContext(connectionString);
        var store = new EfCoreSagaCommandStore(db, clock, options);

        await store.EnqueueAsync(_orderId, "ORD-000001", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), CancellationToken.None);

        var firstClaim = await store.TryClaimAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);
        Assert.NotNull(firstClaim);

        // A concurrent claim, from a SEPARATE store/context instance, sees
        // the row as unclaimable while the lease holds.
        await using var db2 = mssql.CreateDbContext(connectionString);
        var store2 = new EfCoreSagaCommandStore(db2, clock, options);
        var secondClaimWhileLeased = await store2.TryClaimAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);
        Assert.Null(secondClaimWhileLeased);

        // Once the lease has elapsed, the SAME row is claimable again.
        clock.Advance(TimeSpan.FromMilliseconds(60_001));
        var claimAfterLeaseElapsed = await store2.TryClaimAsync(_orderId, SagaCommandKind.StockReserve, CancellationToken.None);
        Assert.NotNull(claimAfterLeaseElapsed);
        Assert.Equal(firstClaim!.Id, claimAfterLeaseElapsed!.Id);
    }

    [Fact]
    public async Task EnqueueAsync_RollsBackWithTheCallersTransaction()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var orderId = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(BuildOptions(leaseMs: 60_000));

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var store = new EfCoreSagaCommandStore(db, clock, options);

            await store.EnqueueAsync(orderId, "ORD-000002", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), CancellationToken.None);

            await transaction.RollbackAsync();
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        Assert.Equal(0, await assertDb.SagaCommands.CountAsync(c => c.OrderId == orderId));
    }

    [Fact]
    public async Task EnqueueAsync_ADuplicateEnqueue_ReturnsAlreadyEnqueuedAndLeavesTheExistingRowUntouched()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var orderId = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(BuildOptions(leaseMs: 60_000));

        await using var db = mssql.CreateDbContext(connectionString);
        var store = new EfCoreSagaCommandStore(db, clock, options);

        var first = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"first\":true}", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(EnqueueOutcome.Enqueued, first);

        var second = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"second\":true}", Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(EnqueueOutcome.AlreadyEnqueued, second);

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var row = await assertDb.SagaCommands.SingleAsync(c => c.OrderId == orderId);
        Assert.Contains("first", row.Payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Feature 42 (acceptance bullet 3) — a saga_commands row that receives
    /// a terminal RpcError reaches a resolved end state ("rejected") rather
    /// than retrying indefinitely: RejectAsync marks the row rejected,
    /// accumulates attempts onto whatever the row already carried, clears
    /// the lease/backoff (next_attempt_at), AND — the durable half of "never
    /// retried again" — ClaimDueAsync's own pending/parked predicate
    /// structurally never reclaims it, proven here against the SAME row
    /// rather than by reading the predicate.
    /// </summary>
    [Fact]
    public async Task RejectAsync_MarksTheRowRejectedAccumulatesAttemptsClearsTheLease_AndClaimDueAsyncNeverReclaimsIt()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var orderId = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(BuildOptions(leaseMs: 60_000));

        await using var db = mssql.CreateDbContext(connectionString);
        var store = new EfCoreSagaCommandStore(db, clock, options);

        await store.EnqueueAsync(orderId, "ORD-000004", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(), CancellationToken.None);
        var claimed = await store.TryClaimAsync(orderId, SagaCommandKind.StockRelease, CancellationToken.None);
        Assert.NotNull(claimed);

        await store.RejectAsync(claimed!.Id, attemptsMade: 1, "PRECONDITION_FAILED: reservation already consumed", CancellationToken.None);

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var row = await assertDb.SagaCommands.AsNoTracking().SingleAsync(c => c.Id == claimed.Id);
        Assert.Equal("rejected", row.Status);
        Assert.Equal(1, row.Attempts); // 0 (fresh row) + 1 attemptsMade.
        Assert.Equal("PRECONDITION_FAILED: reservation already consumed", row.LastError);
        Assert.Null(row.NextAttemptAt); // no retry is ever scheduled for a rejected row.

        // The durable proof of "never retried again": ClaimDueAsync's own
        // pending/parked predicate never reclaims a rejected row, even once
        // it is unambiguously "due" by every other criterion (old enough,
        // no lease).
        var overdue = row.CreatedAt.AddSeconds(-10);
        await assertDb.SagaCommands
            .Where(c => c.Id == claimed.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.CreatedAt, overdue)
                .SetProperty(c => c.NextAttemptAt, (DateTime?)null));

        var due = await store.ClaimDueAsync(batchSize: 10, CancellationToken.None);
        Assert.DoesNotContain(due, r => r.Id == claimed.Id);
    }

    private static OrdersSagaOptions BuildOptions(int leaseMs)
    {
        var options = new OrdersSagaOptions();
        options.Command.LeaseMs = leaseMs;
        return options;
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;

        public void Advance(TimeSpan by) => UtcNow += by;
    }
}
