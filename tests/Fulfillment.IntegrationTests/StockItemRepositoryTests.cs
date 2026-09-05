using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS12` (stored equality), `FS19`'s "the idempotency read blocks rather than reading a snapshot" half, and the OI9/`R13` rollback hazard — over the REAL repository and REAL MS-SQL, no NATS.</summary>
[Collection(MsSqlCollection.Name)]
public sealed class StockItemRepositoryTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task FS12_ReservedUnitsEqualsTheSumOfReservedReservationUnits_AfterEveryCommittedOperation()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_repo_fs12_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var stockId = Guid.NewGuid();
        await SeedStockAsync(connectionString, stockId, "ACME", "P1", units: 10);

        var orderReference = new OrderNumber(1);

        // Reserve.
        await RunAsync(connectionString, async (repo, uow) =>
        {
            var locked = await repo.LockForOrderAsync("ACME", ["P1"], orderReference, CancellationToken.None);
            var item = locked.ItemsByProductCode["P1"];
            var input = new ReserveOrderInput(orderReference, "ACME", "RETAILER1", [new ReserveOrderLine("P1", new Quantity(4))], UniqueId.New());
            OrderStockReservation.Reserve(locked.ItemsByProductCode, input, new StockContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
            await repo.SaveChangesAsync(CancellationToken.None);
        });

        await AssertReservedUnitsEqualsSumAsync(connectionString, "ACME", "P1");

        // Release.
        await RunAsync(connectionString, async (repo, uow) =>
        {
            var locked = await repo.LockForOrderAsync("ACME", ["P1"], orderReference, CancellationToken.None);
            var item = locked.ItemsByProductCode["P1"];
            OrderStockReservation.Release([item], new ReleaseOrderInput(orderReference, "order_cancelled", UniqueId.New()), new StockContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
            await repo.SaveChangesAsync(CancellationToken.None);
        });

        await AssertReservedUnitsEqualsSumAsync(connectionString, "ACME", "P1");
    }

    [Fact]
    public async Task ForcedRollback_LeavesNeitherTheStockOrReservationChangesNorTheOutboxRows()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_repo_rollback_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var stockId = Guid.NewGuid();
        await SeedStockAsync(connectionString, stockId, "ACME", "P1", units: 10);

        var orderReference = new OrderNumber(2);

        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock()), new FixedClock());
        var uow = new EfCoreUnitOfWork(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync(
            async ct =>
            {
                var locked = await repo.LockForOrderAsync("ACME", ["P1"], orderReference, ct);
                var input = new ReserveOrderInput(orderReference, "ACME", "RETAILER1", [new ReserveOrderLine("P1", new Quantity(4))], UniqueId.New());
                OrderStockReservation.Reserve(locked.ItemsByProductCode, input, new StockContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                await repo.SaveChangesAsync(ct);
                throw new InvalidOperationException("simulated post-save failure — must roll back everything.");
            },
            CancellationToken.None));

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var row = await assertDb.Stocks.AsNoTracking().SingleAsync(s => s.CompanyCode == "ACME" && s.ProductCode == "P1");
        Assert.Equal(0, row.ReservedUnits);
        Assert.Equal(0, await assertDb.Reservations.CountAsync());
        Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
    }

    /// <summary>
    /// `FS19`, ledger L1 — a DETERMINISTIC proof, not a probabilistic one:
    /// session A holds an uncommitted reservation INSERT for the order;
    /// session B's <c>LockForOrderAsync</c> call is polled via
    /// <c>sys.dm_exec_requests</c> until it is OBSERVED blocked by A, then A
    /// resolves and B's read proceeds against A's committed row. Under
    /// RCSI, an un-hinted read would instead return immediately with the
    /// PRE-insert snapshot (D9's arming target).
    /// </summary>
    [Fact]
    public async Task FS19_TheIdempotencyReadBlocksOnAConcurrentUncommittedReservationInsert_RatherThanReadingAStaleSnapshotUnderRcsi()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_repo_fs19_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var stockId = Guid.NewGuid();
        await SeedStockAsync(connectionString, stockId, "ACME", "P1", units: 10);
        var orderReference = new OrderNumber(3);

        // Session A: raw ADO, holds an UNCOMMITTED reservation insert.
        await using var connectionA = new SqlConnection(connectionString);
        await connectionA.OpenAsync();
        var transactionA = connectionA.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
        await using (var insert = connectionA.CreateCommand())
        {
            insert.Transaction = transactionA;
            insert.CommandText = "INSERT INTO dbo.reservations (id, stock_id, company_code, retailer_code, product_code, order_reference, units, status, created_at, updated_at) " +
                "VALUES (@id, @stockId, 'ACME', 'RETAILER1', 'P1', @orderRef, 4, 'reserved', SYSUTCDATETIME(), SYSUTCDATETIME());";
            insert.Parameters.AddWithValue("@id", Guid.NewGuid());
            insert.Parameters.AddWithValue("@stockId", stockId);
            insert.Parameters.AddWithValue("@orderRef", orderReference.Value);
            await insert.ExecuteNonQueryAsync();
        }

        // Session B: the real repository, unmodified.
        await using var dbB = mssql.CreateDbContext(connectionString);
        await dbB.Database.OpenConnectionAsync();
        var connectionB = (SqlConnection)dbB.Database.GetDbConnection();
        var transactionB = await dbB.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        var repoB = new EfCoreStockItemRepository(dbB, new OutboxWriter(new FixedClock()), new FixedClock());
        var lockTaskB = repoB.LockForOrderAsync("ACME", ["P1"], orderReference, CancellationToken.None);

        await using var monitorConnection = new SqlConnection(connectionString);
        await monitorConnection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var observedBBlockedByA = false;
        while (DateTime.UtcNow < deadline)
        {
            await using var check = monitorConnection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @b AND blocking_session_id = @a;";
            check.Parameters.AddWithValue("@b", connectionB.ServerProcessId);
            check.Parameters.AddWithValue("@a", connectionA.ServerProcessId);
            var blockedCount = (int)(await check.ExecuteScalarAsync())!;
            if (blockedCount > 0)
            {
                observedBBlockedByA = true;
                break;
            }

            await Task.Delay(20);
        }

        Assert.True(observedBBlockedByA, "Caller B was never observed blocked by caller A within 10s — the interleaving this test depends on did not occur, so it proves nothing this run.");

        await transactionA.CommitAsync();

        var resultB = await lockTaskB;
        await transactionB.CommitAsync();

        // B's read is CURRENT, not the pre-insert snapshot: it sees A's committed reservation.
        Assert.Single(resultB.ExistingReservationsOfOrder);
    }

    private async Task AssertReservedUnitsEqualsSumAsync(string connectionString, string companyCode, string productCode)
    {
        await using var db = mssql.CreateDbContext(connectionString);

        var row = await db.Stocks.AsNoTracking().SingleAsync(s => s.CompanyCode == companyCode && s.ProductCode == productCode);
        var sum = await db.Reservations.AsNoTracking()
            .Where(r => r.CompanyCode == companyCode && r.ProductCode == productCode && r.Status == "reserved")
            .SumAsync(r => (int?)r.Units) ?? 0;

        Assert.Equal(sum, row.ReservedUnits);
    }

    private async Task RunAsync(string connectionString, Func<EfCoreStockItemRepository, EfCoreUnitOfWork, Task> work)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock()), new FixedClock());
        var uow = new EfCoreUnitOfWork(db);

        await uow.ExecuteAsync(async ct => await work(repo, uow), CancellationToken.None);
    }

    private async Task SeedStockAsync(string connectionString, Guid id, string companyCode, string productCode, int units)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        db.Stocks.Add(new Infrastructure.Persistence.Entities.Stock
        {
            Id = id,
            CompanyCode = companyCode,
            ProductCode = productCode,
            Units = units,
            ReservedUnits = 0,
            LowStockThreshold = 5,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock : Application.Ports.IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
