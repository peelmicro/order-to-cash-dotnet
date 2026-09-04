using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// The A11 debt owed to this feature by name (design.md §11.3,
/// <c>progress/review_orders_acceptance.md</c>) — <see cref="EfCoreOrderNumberAllocator"/>
/// had no direct test: neither its <c>WITH (UPDLOCK, ROWLOCK)</c>
/// concurrency claim nor its self-seeding branch over a NON-EMPTY
/// <c>orders</c> table. Test-only — the allocator itself is not touched.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class OrderNumberAllocatorTests(MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task AllocateNextAsync_ConcurrentAllocationsYieldAGapFreeDuplicateFreeSequence()
    {
        const int concurrency = 24;
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_allocator_concurrency_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        // Pre-seed the sequence row with ONE single-threaded allocation
        // first — steady state, and the state feature seed_job leaves a real
        // deployment in before any concurrent placement ever happens. This
        // isolates the ROW-LEVEL claim under test here from the SEPARATE,
        // narrower self-seeding race documented and reproduced by
        // <see cref="AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail"/>
        // below (an A11 finding for feature 15, not fixed here).
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            var seedAllocator = new EfCoreOrderNumberAllocator(seedDb);
            await using var seedTransaction = await seedDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            await seedAllocator.AllocateNextAsync(CancellationToken.None); // ORD-000001, discarded — only seeds the row.
            await seedTransaction.CommitAsync();
        }

        // AllocateNextAsync's UPDLOCK claim only serialises callers that
        // hold it across BOTH the claiming SELECT and the incrementing
        // UPDATE in the SAME transaction (design.md's own remark: "Called
        // from inside the SAME transaction IUnitOfWork opens") — the shape
        // PlaceOrderCommandHandler actually uses it under. An autocommit
        // call (no ambient transaction) releases the row lock the instant
        // the SELECT statement completes, which this fixture reproduces
        // deliberately to prove the concurrency claim under its real
        // calling convention, not a weaker one.
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await using var db = mssql.CreateDbContext(connectionString);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            var allocator = new EfCoreOrderNumberAllocator(db);
            var allocated = await allocator.AllocateNextAsync(CancellationToken.None);
            await transaction.CommitAsync();
            return allocated;
        });

        var results = await Task.WhenAll(tasks);
        var sequenceNumbers = results.Select(r => int.Parse(r.Value["ORD-".Length..])).OrderBy(n => n).ToList();

        Assert.Equal(concurrency, sequenceNumbers.Distinct().Count()); // no duplicates
        Assert.Equal(Enumerable.Range(2, concurrency), sequenceNumbers); // gap-free, continuing right after the seed allocation (ORD-000001).
    }

    /// <summary>
    /// An A11-adjacent FINDING, reported rather than fixed (tasks.md I6: "a
    /// failure here is a finding about feature 15 to report, not to fix in
    /// this feature"): <see cref="EfCoreOrderNumberAllocator"/>'s
    /// self-seeding branch (<c>IF NOT EXISTS ... INSERT</c>) takes no lock
    /// of its own. Under genuine concurrency against a completely FRESH,
    /// never-allocated sequence table, more than one caller can evaluate
    /// "not exists" as true before either commits, and every caller after
    /// the first fails with a primary-key violation on
    /// <c>order_number_sequences</c> rather than serialising behind it —
    /// reproduced deterministically below. <see cref="EfCoreOrderNumberAllocator"/>
    /// itself is NOT touched.
    /// </summary>
    [Fact]
    public async Task AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail()
    {
        // A genuine race is inherently timing-sensitive: an EARLIER, simpler
        // version of this test (no synchronisation at all — see
        // progress/impl_order_saga_orchestrator.md §4/§5) fired every task
        // via Task.WhenAll with no coordination and was OBSERVED FLAKY under
        // load: connection-open latency alone was sometimes enough to
        // serialise every caller behind the first one, producing zero
        // failures and a false-negative test run.
        //
        // A System.Threading.Barrier was tried next and DISCARDED: it
        // requires every one of the N participants to reach
        // SignalAndWait(), so a single connection that is slow to open (or
        // never opens) hangs the other N-1 forever with no timeout — a real
        // deadlock, reproduced live while verifying this fix. The design
        // below opens every connection FIRST, bounded by an explicit
        // timeout so a stuck connection fails the test loudly instead of
        // hanging it, and only THEN fires every allocation via a single
        // Task.WhenAll with no further gate. That still forces the
        // allocations to start within the same short scheduling window —
        // .NET starts each async lambda synchronously up to its first
        // incomplete await when Task.WhenAll enumerates them — without
        // introducing a primitive that can wait forever.
        const int concurrency = 16;
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_allocator_selfseed_race_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        using var openTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var dbs = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            var db = mssql.CreateDbContext(connectionString);
            await db.Database.OpenConnectionAsync(openTimeout.Token); // warm every connection up BEFORE any allocation races.
            return db;
        }));

        try
        {
            var tasks = dbs.Select(async db =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
                var allocator = new EfCoreOrderNumberAllocator(db);
                try
                {
                    await allocator.AllocateNextAsync(CancellationToken.None);
                    await transaction.CommitAsync();
                    return (Succeeded: true, Exception: (Exception?)null);
                }
                catch (Exception ex)
                {
                    return (Succeeded: false, Exception: ex);
                }
            });

            var results = await Task.WhenAll(tasks);

            // The finding: at least one concurrent FIRST-EVER caller fails
            // with a primary-key violation, rather than every caller
            // serialising behind the row lock as the "N concurrent
            // allocations" claim (proven separately, above, against a
            // PRE-EXISTING row) would suggest holds universally. If this
            // assertion ever fails because every caller succeeded, the
            // self-seed race has been fixed upstream (or, rarely, SQL
            // Server's own scheduler happened to serialise all sixteen) —
            // this test — and its citation in
            // progress/impl_order_saga_orchestrator.md — should be
            // revisited before trusting a green run as proof the race is
            // gone.
            Assert.Contains(results, r => !r.Succeeded);
        }
        finally
        {
            foreach (var db in dbs)
            {
                await db.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task AllocateNextAsync_AgainstATableAlreadyHoldingOrd000009_ContinuesAtOrd000010()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_allocator_seed_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(migrateDb);
            await SeedOrderRowWithReferenceAsync(migrateDb, "ORD-000009");
        }


        await using var db = mssql.CreateDbContext(connectionString);
        var allocator = new EfCoreOrderNumberAllocator(db);

        var allocated = await allocator.AllocateNextAsync(CancellationToken.None);

        Assert.Equal("ORD-000010", allocated.Value);
    }

    /// <summary>Inserts a bare <c>orders</c> row carrying only what <see cref="EfCoreOrderNumberAllocator"/>'s self-seeding query reads (<c>order_reference</c>) plus every required FK — <see cref="OrderPersistenceTestSupport.SeedReferenceDataAsync"/> must already have run against <paramref name="db"/>'s database.</summary>
    private static async Task SeedOrderRowWithReferenceAsync(OrdersDbContext db, string orderReference)
    {
        var now = DateTime.UtcNow;
        var currencyId = await db.Currencies.Where(c => c.Code == OrderPersistenceTestSupport.Currency).Select(c => c.Id).SingleAsync();
        var retailerId = await db.Retailers.Where(r => r.Code == OrderPersistenceTestSupport.RetailerCode).Select(r => r.Id).SingleAsync();
        var companyId = await db.Companies.Where(c => c.Code == OrderPersistenceTestSupport.CompanyCode).Select(c => c.Id).SingleAsync();

        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderReference = orderReference,
            OrderDate = now,
            RetailerId = retailerId,
            CompanyId = companyId,
            CurrencyId = currencyId,
            Status = "placed",
            InitialAmount = 0,
            InitialDiscount = 0,
            TotalAmount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
