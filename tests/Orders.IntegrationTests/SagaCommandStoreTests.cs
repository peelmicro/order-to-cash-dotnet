using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Outbox;
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

        await store.EnqueueAsync(_orderId, "ORD-000001", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);

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

            await store.EnqueueAsync(orderId, "ORD-000002", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);

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

        var first = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"first\":true}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
        Assert.Equal(EnqueueOutcome.Enqueued, first);

        var second = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"second\":true}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
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

        await store.EnqueueAsync(orderId, "ORD-000004", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
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

    /// <summary>
    /// <c>observability_reliability</c>, design.md §4.2 (ledger L15) — the
    /// triggering envelope's raw bytes and topic round-trip through the
    /// REAL <c>nvarchar(max)</c>/<c>nvarchar(64)</c> columns byte-for-byte.
    /// Bytes with a scrambled key order (never producible by re-serialising
    /// through <c>Envelope&lt;JsonElement&gt;</c>, whose declared field
    /// order is fixed) so a "store a re-serialised copy" mutation would be
    /// caught even if it happened to preserve every field's VALUE.
    /// </summary>
    [Fact]
    public async Task OR3_EnqueueAsync_PersistsTheTriggeringEnvelopeBytesAndTopicByteForByte()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var orderId = Guid.NewGuid();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var options = Options.Create(BuildOptions(leaseMs: 60_000));

        var scrambledOrderBytes = System.Text.Encoding.UTF8.GetBytes(
            """{"payload":{},"occurredAt":"2026-01-02T03:04:05.678Z","causationId":"11111111-1111-1111-1111-111111111111","correlationId":"22222222-2222-2222-2222-222222222222","aggregateId":"33333333-3333-3333-3333-333333333333","eventType":"order.placed.v1","eventId":"44444444-4444-4444-4444-444444444444"}""");
        const string sourceTopic = "otc.orders.facts.v1";

        await using var db = mssql.CreateDbContext(connectionString);
        var store = new EfCoreSagaCommandStore(db, clock, options);

        await store.EnqueueAsync(orderId, "ORD-000005", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), scrambledOrderBytes, sourceTopic, CancellationToken.None);

        var claimed = await store.TryClaimAsync(orderId, SagaCommandKind.StockReserve, CancellationToken.None);
        Assert.NotNull(claimed);
        Assert.Equal(scrambledOrderBytes, claimed!.TriggeringEventEnvelope);
        Assert.Equal(sourceTopic, claimed.TriggeringEventTopic);
    }

    /// <summary>
    /// Id 71 — <c>credit.release</c>'s own synthetic envelope carries the
    /// note; <c>stock.release</c> has none for this order at all (the
    /// <c>stock_reserved</c> branch's own row, never enqueued here). Reads
    /// through the REAL <c>nvarchar(max)</c> column and the REAL JSON parse,
    /// bracketed to the exact text supplied (CLAUDE.md's provenance rule).
    /// </summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_CreditReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote()
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

        const string note = "Reverse-order compensation — credit released first, per saga.md §4.3.";
        await store.EnqueueAsync(
            orderId, "ORD-000006", SagaCommandKind.CreditRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, note), OrdersFactTopic.Name, CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Equal(note, found);
    }

    /// <summary>The <c>stock_reserved</c> branch's own (and only) row.</summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_StockReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote()
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

        const string note = "Cancel while stock_reserved — buyer changed the delivery address twice.";
        await store.EnqueueAsync(
            orderId, "ORD-000007", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, note), OrdersFactTopic.Name, CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Equal(note, found);
    }

    /// <summary>
    /// The precedence claim itself, made falsifiable. Review round 1's D2:
    /// no code ever rewrites <c>triggering_event_envelope</c> — the column
    /// is written exactly once, by <see cref="EfCoreSagaCommandStore.EnqueueAsync"/>'s
    /// own <c>INSERT</c> — so the ordinary credit-held chain has
    /// <c>stock.release</c> carrying a REAL fact's bytes (no note), which
    /// <see cref="FindOperatorCancelNoteAsync_ARealFactEnvelope_ReturnsNull"/>
    /// already covers. THIS test makes BOTH rows carry a synthetic
    /// <c>orders.cancel.requested</c> envelope, each with its OWN, distinct
    /// note, so <c>credit.release</c> winning is observable and can fail —
    /// unlike the retired version, which put a note-less REAL envelope on
    /// the <c>stock.release</c> row and so returned the same answer
    /// whichever row was checked first (review round 1, probe M7: reversing
    /// the precedence left it green).
    /// </summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder()
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

        const string creditReleaseNote = "Credit released first — the canonical carrier for this order.";
        const string stockReleaseNote = "A DIFFERENT note — must lose to the credit.release row's own.";

        await store.EnqueueAsync(
            orderId, "ORD-000008", SagaCommandKind.CreditRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, creditReleaseNote), OrdersFactTopic.Name, CancellationToken.None);
        await store.EnqueueAsync(
            orderId, "ORD-000008", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, stockReleaseNote), OrdersFactTopic.Name, CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Equal(creditReleaseNote, found);
    }

    /// <summary>
    /// Id 62 bullet 4 — "under the race": id 71's review advisory A3, closed
    /// here. Without an <c>ORDER BY</c>, this query happens to come back
    /// ordered by the covering unique index's own key <c>(order_id, command)</c>
    /// — <c>"credit.release"</c> sorts before <c>"stock.release"</c>
    /// lexicographically REGARDLESS of insertion order (checked directly:
    /// inserting <c>stock.release</c> first and <c>credit.release</c> second
    /// still returns <c>credit.release</c> first). So a position-based
    /// mutation cannot be armed by reordering INSERTS — it has to reorder
    /// which COMMAND carries the note. Here <c>credit.release</c> is the
    /// row that is NOT the operator's (a real fact's own envelope — the
    /// shape a saga-decided compensation leaves, never reachable from
    /// <c>credit.release</c> in production today since only
    /// <c>CancelOrderCommandHandler</c> ever enqueues it, but guarded
    /// defensively exactly as <see cref="FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder"/>
    /// already does for the symmetric case), and <c>stock.release</c> —
    /// physically SECOND in the index — carries the operator's own note. A
    /// lookup that selected by ROW POSITION (the physically-first row)
    /// rather than by the envelope's own <c>eventType</c> would return
    /// <see langword="null"/> here; the content-based lookup must not.
    /// </summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_StillSelectsByEnvelopeContentNotPosition()
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

        const string operatorNote = "Operator cancelled — under the race, the note must still win even off the physically-first row.";

        // credit.release — NOT the operator's, a real fact's own envelope.
        // Physically first: the covering index orders by command name, and
        // "credit.release" < "stock.release".
        await store.EnqueueAsync(
            orderId, "ORD-000012", SagaCommandKind.CreditRelease, "{}", Guid.NewGuid(),
            BuildRealFactEnvelopeBytes(orderId, "credit.rejected.v1"), SagaFactTopics.BillingFacts, CancellationToken.None);

        // stock.release — the operator's own row, carrying the note.
        // Physically SECOND.
        await store.EnqueueAsync(
            orderId, "ORD-000012", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, operatorNote), OrdersFactTopic.Name, CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Equal(operatorNote, found);
    }

    /// <summary>
    /// D2's rewritten row 3 names its OWN invariant a guard — this is it: a
    /// duplicate <c>(order_id, command)</c> enqueue is a no-op on the
    /// STORED envelope, never a second write. The first envelope's exact
    /// bytes must survive, byte-for-byte, not merely "some note" — review
    /// round 2's A6: a rewrite that kept the note but changed some other
    /// byte (a different <c>eventId</c>, say) would pass a note-only
    /// assertion, so the stored column is read back and compared to
    /// <c>firstEnvelope</c> byte for byte, in addition to the note.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace()
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

        var firstEnvelope = BuildSyntheticEnvelopeBytes(orderId, "the first envelope's own note");
        var secondEnvelope = BuildSyntheticEnvelopeBytes(orderId, "a SECOND, different note — must never land");

        var first = await store.EnqueueAsync(orderId, "ORD-000011", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(), firstEnvelope, OrdersFactTopic.Name, CancellationToken.None);
        Assert.Equal(EnqueueOutcome.Enqueued, first);

        var second = await store.EnqueueAsync(orderId, "ORD-000011", SagaCommandKind.StockRelease, "{\"different\":true}", Guid.NewGuid(), secondEnvelope, OrdersFactTopic.Name, CancellationToken.None);
        Assert.Equal(EnqueueOutcome.AlreadyEnqueued, second);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);
        Assert.Equal("the first envelope's own note", found);

        var stockReleaseToken = SagaCommandKinds.ToToken(SagaCommandKind.StockRelease);
        await using var assertDb = mssql.CreateDbContext(connectionString);
        var storedRow = await assertDb.SagaCommands.AsNoTracking().SingleAsync(c => c.OrderId == orderId && c.Command == stockReleaseToken);
        Assert.Equal(firstEnvelope, System.Text.Encoding.UTF8.GetBytes(storedRow.TriggeringEventEnvelope!));
    }

    /// <summary>A saga-decided cancellation — the row's envelope is a REAL fact (<c>credit.rejected.v1</c>), not the synthetic one, so no note is ever fabricated.</summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_ARealFactEnvelope_ReturnsNull()
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

        var realCreditRejectedFactBytes = BuildRealFactEnvelopeBytes(orderId, "credit.rejected.v1");
        await store.EnqueueAsync(
            orderId, "ORD-000009", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(),
            realCreditRejectedFactBytes, "otc.billing.facts.v1", CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindOperatorCancelNoteAsync_NoRowsForTheOrder_ReturnsNull()
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

        var found = await store.FindOperatorCancelNoteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(found);
    }

    /// <summary>The synthetic envelope exists (R29 needs it regardless), but the operator supplied no note — JsonWire's own null-omission, no <c>note</c> key at all.</summary>
    [Fact]
    public async Task FindOperatorCancelNoteAsync_TheOperatorSuppliedNoNote_ReturnsNull()
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

        await store.EnqueueAsync(
            orderId, "ORD-000010", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(),
            BuildSyntheticEnvelopeBytes(orderId, note: null), OrdersFactTopic.Name, CancellationToken.None);

        var found = await store.FindOperatorCancelNoteAsync(orderId, CancellationToken.None);

        Assert.Null(found);
    }

    /// <summary>Builds the exact bytes <see cref="OperatorCancelRequestedEnvelope.Build"/> would (which this project cannot call — <c>internal</c> to <c>OrderToCash.Orders</c>), via the SAME public <see cref="Envelope{TPayload}"/>/<see cref="OperatorCancelRequestedPayload"/> types and the SAME <see cref="JsonWire.Options"/> the production code uses.</summary>
    private static byte[] BuildSyntheticEnvelopeBytes(Guid orderId, string? note)
    {
        var requestId = Guid.NewGuid();
        var envelope = new Envelope<OperatorCancelRequestedPayload>(
            requestId, "orders.cancel.requested", orderId, orderId, requestId, DateTimeOffset.UtcNow,
            new OperatorCancelRequestedPayload(orderId, "operator_cancelled", note));

        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
    }

    /// <summary>A REAL fact's envelope bytes (never the synthetic one) — same shape any of the fourteen real facts has, an empty <c>payload</c> object since <see cref="EfCoreSagaCommandStore.FindOperatorCancelNoteAsync"/> only ever reads <c>eventType</c>/<c>payload.note</c>, never anything payload-type-specific.</summary>
    private static byte[] BuildRealFactEnvelopeBytes(Guid orderId, string eventType)
    {
        var envelope = new Envelope<object>(Guid.NewGuid(), eventType, orderId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, new { });
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
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
