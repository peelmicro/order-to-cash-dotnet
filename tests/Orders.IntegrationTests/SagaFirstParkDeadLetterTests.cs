using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Saga;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// <c>observability_reliability</c>, design.md §4.3 (<c>OR3</c>, ledger L16)
/// — <see cref="EfCoreSagaCommandStore.TryClaimDeadLetterAsync"/>'s
/// at-most-once claim, against a REAL MS-SQL database and GENUINELY
/// concurrent callers (separate <c>OrdersDbContext</c>/connection instances,
/// driven with <see cref="Task.WhenAll"/>). A single-threaded proof is not
/// evidence for this claim — the whole point of the guarded
/// <c>UPDATE ... WHERE dead_lettered_at IS NULL</c> is that it survives a
/// GENUINE race, which only a real database's row-level locking can prove.
/// </summary>
/// <remarks>
/// Reworded on the box from tasks.md A2c's plain "<c>tests/Orders.UnitTests/</c>"
/// designation: proving SQL Server's own atomicity guarantee for a single
/// conditional <c>UPDATE</c> statement is an ENGINE claim (CLAUDE.md's
/// "does this test execute the code the row is about?"), and a fake-backed
/// unit test can only ever prove the shape of the fake's own logic, never
/// the real <see cref="EfCoreSagaCommandStore"/>'s behaviour under a real
/// concurrent load. This is the SAME reasoning B7 (feature
/// <c>observability_reliability</c>'s own Group B, <c>RI3</c>) already
/// applied to the identical shape of claim — its own box records the same
/// rewording. <c>Orders.UnitTests</c> carries no Testcontainers dependency
/// at all, by design, so this lives here instead.
/// </remarks>
[Collection(MsSqlCollection.Name)]
public sealed class SagaFirstParkDeadLetterTests(MsSqlContainerFixture mssql)
{
    private const int ConcurrentCallers = 16;

    [Fact]
    public async Task OR3_ClaimsTheFirstParkExactlyOnce_AndDoesNothingOnASecondPark()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_firstpark_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var orderId = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(new OrdersSagaOptions());

        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            var seedStore = new EfCoreSagaCommandStore(seedDb, clock, options);
            await seedStore.EnqueueAsync(orderId, "ORD-000001", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
        }

        Guid commandId;
        await using (var readDb = mssql.CreateDbContext(connectionString))
        {
            var row = await readDb.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId);
            commandId = row.Id;
        }

        // GENUINELY concurrent: each caller opens its OWN OrdersDbContext
        // (its own connection), and every call is issued before any of
        // them awaits its own reply — Task.WhenAll, not a sequential loop.
        var tasks = new Task<bool>[ConcurrentCallers];
        for (var i = 0; i < ConcurrentCallers; i++)
        {
            tasks[i] = ClaimAsync(connectionString, commandId, clock, options);
        }

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(ConcurrentCallers - 1, results.Count(r => !r));

        // A SECOND park of the SAME row (a later sweep cycle re-parking an
        // already-dead-lettered row) claims nothing further, sequentially —
        // the "and does nothing on a second park" half of this test's name.
        await using var secondDb = mssql.CreateDbContext(connectionString);
        var secondStore = new EfCoreSagaCommandStore(secondDb, clock, options);
        var secondClaim = await secondStore.TryClaimDeadLetterAsync(commandId, CancellationToken.None);
        Assert.False(secondClaim);
    }

    private async Task<bool> ClaimAsync(string connectionString, Guid commandId, IClock clock, IOptions<OrdersSagaOptions> options)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var store = new EfCoreSagaCommandStore(db, clock, options);
        return await store.TryClaimDeadLetterAsync(commandId, CancellationToken.None);
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
