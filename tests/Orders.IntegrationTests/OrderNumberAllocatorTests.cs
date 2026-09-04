using Microsoft.Data.SqlClient;
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
    /// Feature 45's INVERSION of the original
    /// <c>AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail</c>
    /// (renamed to name the now-correct behaviour it proves), which
    /// asserted the finding reported by feature 16's review (A1) and fixed
    /// here: <see cref="EfCoreOrderNumberAllocator"/>'s self-seeding branch
    /// took no lock of its own, so two genuinely concurrent first-ever
    /// callers could both evaluate "not exists" as true before either
    /// committed, and the loser failed with a primary-key violation.
    ///
    /// <b>Why this is a two-connection, explicitly-driven interleaving and
    /// not "fire sixteen tasks and hope"</b> (see
    /// <see cref="AllocateNextAsync_SixteenConcurrentFirstEverAllocations_AllSucceedWithDistinctReferences"/>
    /// below for that positive-but-probabilistic form, which this test
    /// exists alongside rather than replaces). A pass/fail count over many
    /// concurrent tasks is deterministic once the code is FIXED but only
    /// probabilistic as a detector of the code being BROKEN — reverting the
    /// fix only changes how LIKELY a failure is, not whether one occurs,
    /// and a guard whose armed run can still go green on a lucky
    /// scheduling is not a guard (the exact trap a previous feature's
    /// review rejected a probabilistic reproduction over). So this test
    /// manufactures the race as a checked FACT instead of a hoped-for
    /// timing:
    ///
    /// <list type="number">
    /// <item>Session A inserts the counter row directly, in its own open,
    /// UNCOMMITTED transaction — occupying exactly the state a genuinely
    /// concurrent first caller is in mid-seed. SQL Server always takes a
    /// real exclusive lock on a newly INSERTed key regardless of RCSI (row
    /// versioning governs READS, never WRITES), so from the moment the
    /// INSERT returns, session A is DEMONSTRABLY holding the row, not
    /// probably holding it.</item>
    /// <item>Session B calls the real, unmodified
    /// <see cref="EfCoreOrderNumberAllocator.AllocateNextAsync"/> — exactly
    /// as <c>PlaceOrderCommandHandler</c> calls it — inside its own ambient
    /// transaction, started but not yet awaited.</item>
    /// <item>Before doing anything else, this test POLLS
    /// <c>sys.dm_exec_requests</c> until it OBSERVES session B reported as
    /// blocked by session A, bounded by a timeout that fails the test
    /// loudly rather than silently passing on an interleaving that never
    /// happened. This is the fact that replaces the hope: both callers are
    /// now provably, simultaneously inside the seeding window, not
    /// plausibly so.</item>
    /// <item>Only THEN does the test commit session A and await session
    /// B's call.</item>
    /// </list>
    ///
    /// With the bug present, B is blocked either way (on its own unlocked
    /// read finding "not exists" — true, since RCSI's row-versioned read
    /// cannot see A's still-uncommitted insert — and then blocking on B's
    /// own INSERT attempt against the same still-locked key), and once A
    /// commits, B's blocked INSERT resolves against a now-committed
    /// duplicate key and throws — EVERY time this sequence is run, because
    /// step 3 above forces B to already be at that exact point before A is
    /// ever allowed to commit. With the fix, B blocks on its OWN
    /// <c>WITH (UPDLOCK, HOLDLOCK)</c> existence check instead, which
    /// re-evaluates once A's lock releases, finds the row A committed, and
    /// skips B's insert — no exception, ever, under the same forced
    /// sequence. That is a change in KIND (throws vs. does not throw)
    /// under an interleaving this test verifies rather than assumes, not a
    /// change in how often a race happens to be won.
    /// </summary>
    [Fact]
    public async Task AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_allocator_selfseed_lock_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        // Session A: raw ADO, deliberately NOT the allocator — it only
        // needs to occupy the counter row's key, uncommitted, to put a real
        // caller into "genuinely mid-seed". Using the allocator itself here
        // would need a controllable pause point AllocateNextAsync does not
        // expose (and should not, for production code's sake).
        await using var connectionA = new SqlConnection(connectionString);
        await connectionA.OpenAsync();
        var transactionA = connectionA.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
        await using (var seed = connectionA.CreateCommand())
        {
            seed.Transaction = transactionA;
            seed.CommandText = "INSERT INTO dbo.order_number_sequences (id, next_value) VALUES (1, 1);";
            await seed.ExecuteNonQueryAsync();
        }

        // Session B: the real allocator, unmodified.
        await using var dbB = mssql.CreateDbContext(connectionString);
        await dbB.Database.OpenConnectionAsync();
        var connectionB = (SqlConnection)dbB.Database.GetDbConnection();
        var transactionB = await dbB.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        var allocatorB = new EfCoreOrderNumberAllocator(dbB);
        var allocateTaskB = allocatorB.AllocateNextAsync(CancellationToken.None);

        // The checked fact, not the hope: poll until B is OBSERVED blocked
        // by A, bounded so a stuck poll fails loudly instead of hanging.
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

        // Both callers are now provably inside the seeding window. Only
        // now does A resolve — the sequencing step 4 above depends on.
        await transactionA.CommitAsync();

        // Fixed: no exception. B's own WITH (UPDLOCK, HOLDLOCK) check
        // re-evaluates against A's now-committed row and skips its insert,
        // then claims the row A seeded.
        var allocatedB = await allocateTaskB;
        await transactionB.CommitAsync();

        Assert.Equal("ORD-000001", allocatedB.Value);
    }

    /// <summary>
    /// The acceptance criterion's own literal form ("sixteen genuinely
    /// concurrent first-ever allocations against a never-seeded sequence
    /// table all succeed and yield sixteen distinct references"): a
    /// positive proof, deterministic once the fix is in place. NOT the
    /// vehicle this feature arms against a reverted fix — see
    /// <see cref="AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt"/>'s
    /// own remarks for why a many-tasks pass/fail count is only
    /// probabilistic as a broken-detector: reverting the fix makes a
    /// failure here likely, not certain, so this test is kept for the
    /// literal acceptance wording and as a broader (if softer) concurrency
    /// smoke test, while the arming proof lives in the other test.
    /// </summary>
    [Fact]
    public async Task AllocateNextAsync_SixteenConcurrentFirstEverAllocations_AllSucceedWithDistinctReferences()
    {
        // Same coordination shape as the discarded earlier attempts
        // documented on the sibling test above (unsynchronised
        // Task.WhenAll was flaky; a Barrier could deadlock on a slow
        // connection): open every connection first, bounded by an explicit
        // timeout, then fire every allocation via one Task.WhenAll with no
        // further gate.
        const int concurrency = 16;
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_allocator_selfseed_success_{Guid.NewGuid():N}");
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
                var allocated = await allocator.AllocateNextAsync(CancellationToken.None);
                await transaction.CommitAsync();
                return allocated;
            });

            var results = await Task.WhenAll(tasks);
            var sequenceNumbers = results.Select(r => int.Parse(r.Value["ORD-".Length..])).OrderBy(n => n).ToList();

            Assert.Equal(concurrency, sequenceNumbers.Distinct().Count()); // no duplicates
            Assert.Equal(Enumerable.Range(1, concurrency), sequenceNumbers); // gap-free from the very first ever allocation.
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
