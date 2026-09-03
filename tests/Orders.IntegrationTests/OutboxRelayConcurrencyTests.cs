using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// OI4, OI5, OI13 — the claim's locking semantics, against real MS-SQL with
/// a fake publisher (design.md §5.2, §9.1: "the point is the claim, not the
/// broker"). <see cref="MsSqlContainerFixture"/>'s
/// <c>CreateFreshDatabaseAsync</c> now sets <c>READ_COMMITTED_SNAPSHOT ON</c>
/// (task group B), which is a precondition for OI13's measurement meaning
/// anything.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class OutboxRelayConcurrencyTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task OI4_TwoConcurrentRelays_GrantDisjointBatchesAndPublishEveryRecordExactlyOnce()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi4_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            for (var i = 0; i < 20; i++)
            {
                seedDb.OutboxMessages.Add(NewRow());
                await seedDb.SaveChangesAsync();
            }
        }

        var publisherA = new FakeFactPublisher();
        var publisherB = new FakeFactPublisher();

        await using var dbA = fixture.CreateDbContext(connectionString);
        await using var dbB = fixture.CreateDbContext(connectionString);

        var relayA = BuildRelay(dbA, publisherA, batchSize: 20);
        var relayB = BuildRelay(dbB, publisherB, batchSize: 20);

        // Genuinely concurrent — both instances race for the SAME unpublished
        // rows.
        var resultA = relayA.RunOnceAsync(CancellationToken.None);
        var resultB = relayB.RunOnceAsync(CancellationToken.None);
        await Task.WhenAll(resultA, resultB);

        var idsA = publisherA.Calls.SelectMany(batch => batch.Select(f => f.Key)).ToHashSet();
        var idsB = publisherB.Calls.SelectMany(batch => batch.Select(f => f.Key)).ToHashSet();

        // Disjoint batches...
        Assert.Empty(idsA.Intersect(idsB));
        // ...and the union is every record.
        Assert.Equal(20, idsA.Count + idsB.Count);

        await using var assertDb = fixture.CreateDbContext(connectionString);
        Assert.Equal(20, await assertDb.OutboxMessages.CountAsync(r => r.PublishedAt != null));
    }

    [Fact]
    public async Task OI5_Relay_ReturnsRecordsClaimedByARelayThatDiedBeforeStampingToTheNextPollWithoutALeaseWait()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi5_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            seedDb.OutboxMessages.Add(NewRow());
            await seedDb.SaveChangesAsync();
        }

        // Simulate a relay that claimed the row (took the UPDLOCK) and then
        // died BEFORE stamping — never committing, never rolling back
        // explicitly. Disposing an IDbContextTransaction that was never
        // committed performs an implicit ROLLBACK, which is exactly what a
        // dropped connection does at the server: the row's locks release
        // immediately.
        var dyingDb = fixture.CreateDbContext(connectionString);
        await using (dyingDb)
        {
            await using var transaction = await dyingDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var claimed = await dyingDb.OutboxMessages
                .FromSqlInterpolated($@"
                    SELECT TOP (10) id, event_id, event_type, aggregate_id, correlation_id, causation_id,
                           payload, occurred_at, published_at, created_at, seq, trace_parent
                    FROM   dbo.outbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE  published_at IS NULL
                    ORDER  BY seq ASC")
                .AsNoTracking()
                .ToListAsync();
            Assert.Single(claimed);
            // No commit, no explicit rollback — `await using` disposes the
            // transaction (and the connection) here, simulating the crash.
        }

        var secondPublisher = new FakeFactPublisher();
        await using var secondDb = fixture.CreateDbContext(connectionString);
        var secondRelay = BuildRelay(secondDb, secondPublisher, batchSize: 10);

        var stopwatch = Stopwatch.StartNew();
        var result = await secondRelay.RunOnceAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);
        // No lease-expiry wait: this returns fast, not after some
        // configured lease duration.
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"took {stopwatch.ElapsedMilliseconds}ms — a dropped connection's locks should already be gone");
    }

    /// <summary>
    /// OI13 — the item the stack-comparison document has carried open since
    /// Phase 1. MEASURES skip-versus-block rather than asserting the
    /// expected answer (per the brief): holds a claim open in one
    /// transaction and times how long a second transaction's claim takes to
    /// return. A skip returns near-instantly; a block returns only after the
    /// first transaction ends (or after a lock-wait timeout) — the two are
    /// different OUTCOMES and only a timing assertion tells them apart.
    /// </summary>
    /// <remarks>
    /// BOTH transactions run through the REAL <see cref="OutboxRelay"/>
    /// class, deliberately — a first draft of this test reimplemented the
    /// claim as inline SQL for both sides, which meant deleting
    /// <c>READPAST</c> from <c>OutboxRelay.cs</c> itself would NOT have
    /// failed this test at all, since the test exercised a second, hand-
    /// written copy of the query rather than the production code path. That
    /// is exactly the "guard that does not guard" class CLAUDE.md's arming
    /// protocol exists to catch, and it was caught arming row 6, not by
    /// inspection. Transaction 1 is held open by giving its relay a
    /// publisher whose <c>PublishAsync</c> never completes (so its
    /// transaction — and the UPDLOCK its claim took — stays open for as
    /// long as the test needs), and released at the end via cancellation
    /// (design.md §5.4's "genuine outer cancellation propagates" path,
    /// which disposes its transaction without a commit).
    /// </remarks>
    [Fact]
    public async Task OI13_Claim_SkipsRowsHeldByAnotherRelayAndBehavesIdenticallyWithRowVersioningEnabledOnTheDatabase()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_oi13_{Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            // Two rows, so a "skip the held one, take the other" outcome is
            // distinguishable from "claimed nothing".
            seedDb.OutboxMessages.Add(NewRow());
            await seedDb.SaveChangesAsync();
            seedDb.OutboxMessages.Add(NewRow());
            await seedDb.SaveChangesAsync();
        }

        var holderPublisher = new FakeFactPublisher
        {
            OnPublish = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct),
        };
        await using var holderDb = fixture.CreateDbContext(connectionString);
        var holderRelay = BuildRelay(holderDb, holderPublisher, batchSize: 1, publishTimeoutMs: 60_000);
        using var holderCts = new CancellationTokenSource();

        // Transaction 1 (the REAL relay): claims and holds ONE row, then
        // blocks forever inside PublishAsync — its transaction, and the
        // UPDLOCK its claim took, stay open until holderCts is cancelled.
        var holderTask = holderRelay.RunOnceAsync(holderCts.Token);

        // Wait until it has genuinely claimed and started publishing —
        // bounded polling, not an arbitrary sleep.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (holderPublisher.Calls.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.NotEmpty(holderPublisher.Calls);
        var heldId = holderPublisher.Calls[0][0].Key;

        // Transaction 2 (also the REAL relay), on a separate connection:
        // claim with a batch size covering BOTH rows. Timed.
        var secondPublisher = new FakeFactPublisher();
        await using var secondDb = fixture.CreateDbContext(connectionString);
        var secondRelay = BuildRelay(secondDb, secondPublisher, batchSize: 10);

        var stopwatch = Stopwatch.StartNew();
        var secondResult = await secondRelay.RunOnceAsync(CancellationToken.None);
        stopwatch.Stop();

        // The measurement: returned FAST (a skip), not after however long a
        // lock wait would have taken. Measured on this stack: 117ms
        // (progress/impl_outbox_and_idempotency.md has the full run). A
        // blocking claim would instead wait for the holder's transaction to
        // end, which never happens during this measurement — so the 3s
        // bound below is generous headroom for a slow CI box, not a number
        // close to the real skip/block boundary.
        Assert.True(stopwatch.ElapsedMilliseconds < 3000, $"claim took {stopwatch.ElapsedMilliseconds}ms — READPAST should skip the held row rather than block on it");

        // And it skipped the held row specifically — it published the
        // OTHER (different) row, never the one transaction 1 holds.
        Assert.Equal(1, secondResult.Claimed);
        var secondKey = Assert.Single(Assert.Single(secondPublisher.Calls));
        Assert.NotEqual(heldId, secondKey.Key);

        // Release the holder and let its (never-committed) transaction roll
        // back.
        await holderCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => holderTask);
    }

    private static OutboxRelay BuildRelay(OrdersDbContext db, FakeFactPublisher publisher, int batchSize, int publishTimeoutMs = 5000) =>
        new(db, publisher, new FakeClock(FakeClock.UtcNowToTheMillisecond()), Options.Create(new OutboxRelayOptions { BatchSize = batchSize, PublishTimeoutMs = publishTimeoutMs }), NullLogger<OutboxRelay>.Instance);

    private static OutboxMessage NewRow()
    {
        var now = DateTime.UtcNow;
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            EventType = "order.placed.v1",
            AggregateId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            Payload = "{}",
            OccurredAt = now,
            CreatedAt = now,
        };
    }
}
