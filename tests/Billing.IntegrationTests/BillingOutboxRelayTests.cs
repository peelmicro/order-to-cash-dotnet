using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC16`, and ledger `L16`'s seq-order guarantee — over REAL MS-SQL and REAL Kafka.</summary>
[Collection(KafkaCollection.Name)]
public sealed class BillingOutboxRelayTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task BC16_PublishesTheFactsOfACreditHoldTransactionToTheBillingTopicKeyedByCorrelationId_AndStampsPublishedAtOnlyAfterAcknowledgement()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_relay_bc16_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        var correlationId = UniqueId.New();
        var orderReference = OrderNumber.Parse("ORD-000001");

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            await uow.ExecuteAsync(async ct =>
            {
                var credit = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", orderReference, ct);
                var request = new HoldRequest(orderReference, new Money(1_000, "EUR"), correlationId);
                credit!.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                await repo.SaveChangesAsync(credit, ct);
            }, CancellationToken.None);
        }

        // Confirm the row exists, unpublished, before the relay runs.
        await using (var beforeDb = mssql.CreateDbContext(connectionString))
        {
            var row = await beforeDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId.Value);
            Assert.Null(row.PublishedAt);
        }

        using var producer = new ProducerBuilder<string, byte[]>(KafkaFactPublisher.BuildProducerConfig(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, ClientId = "otc-billing-test" })).Build();
        using var publisher = new KafkaFactPublisher(producer);

        // Backlog id 74 bullets 2-3 — the DETERMINISM lives in this test, not
        // in the mutation. A decoy envelope with a DIFFERENT eventId is placed
        // on the SAME topic BEFORE the relay runs, under the SAME Kafka key
        // (the correlationId) the relay will use — so it is on the SAME
        // partition at a strictly EARLIER offset, and a read that takes
        // "whatever the first Consume() returns" returns the decoy on EVERY
        // run. The old read here did exactly that and then asserted only on
        // the message KEY, which the decoy also satisfies.
        var decoyEventId = await PublishDecoyAsync(BillingFactTopic.Name, correlationId.Value.ToString());

        await using var relayDb = mssql.CreateDbContext(connectionString);
        var relay = new OutboxRelay(relayDb, publisher, new FixedClock(), Microsoft.Extensions.Options.Options.Create(new OutboxRelayOptions()), new NoOpDlqDepthGauge(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);

        // published_at stamped only AFTER acknowledgement.
        await using var afterDb = mssql.CreateDbContext(connectionString);
        var publishedRow = await afterDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId.Value);
        Assert.NotNull(publishedRow.PublishedAt);
        Assert.Equal("credit.approved.v1", publishedRow.EventType);

        // Read it back through a REAL consumer — the record this test's OWN
        // relay cycle published, selected by the eventId the outbox row
        // carries, never "whatever arrived first on a topic the whole Kafka
        // collection shares" (backlog id 74 bullets 1-2).
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(BillingFactTopic.Name);

        var consumed = ConsumeMatchingEventId(consumer, publishedRow.EventId, TimeSpan.FromSeconds(30), decoyEventId, BillingFactTopic.Name);

        // Deliberately kept after the content-matching read, and deliberately
        // redundant on the happy path: this is the assertion a REVERSION to a
        // positional `consumer.Consume(...)` kills. The KEY assertion below
        // cannot do that job — the decoy carries the SAME key, by design, so
        // the pre-fix shape passed it while holding another record entirely.
        AssertIsThisTestsOwnRecord(consumed, publishedRow.EventId, decoyEventId, BillingFactTopic.Name);
        Assert.Equal(correlationId.Value.ToString(), consumed.Message.Key);
    }

    /// <summary>Backlog id 74 bullet 3 — names the record actually read, so a positional regression fails with the DECOY's identity rather than silently passing a key assertion the decoy also satisfies.</summary>
    private static void AssertIsThisTestsOwnRecord(ConsumeResult<string, byte[]> record, Guid expectedEventId, Guid decoyEventId, string topic)
    {
        var actualEventId = ReadEventId(record.Message.Value);

        Assert.True(
            actualEventId == expectedEventId,
            $"the read from '{topic}' returned the envelope with eventId {actualEventId}, not the fact this test's own relay cycle "
            + $"published ({expectedEventId}). A decoy with eventId {decoyEventId} was deliberately published to that topic first, "
            + "under the SAME Kafka key, so a read that selects by POSITION returns the decoy — and the message-key assertion that "
            + "follows cannot detect it, because the decoy shares the key.");
    }

    /// <summary>
    /// Backlog id 74 bullet 3 — publishes a DECOY envelope with a DIFFERENT
    /// <c>eventId</c> under <paramref name="key"/>, so it shares the real
    /// record's partition at a strictly earlier offset. Returns the decoy's
    /// own <c>eventId</c> so a failure can name what was read instead.
    /// </summary>
    private async Task<Guid> PublishDecoyAsync(string topic, string key)
    {
        var decoyEventId = Guid.NewGuid();
        var decoy = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"eventId\":\"{decoyEventId}\",\"eventType\":\"credit.approved.v1\",\"aggregateId\":\"{Guid.NewGuid()}\",\"correlationId\":\"{Guid.NewGuid()}\",\"causationId\":\"{Guid.NewGuid()}\",\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{{}}}}");

        using var decoyProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await decoyProducer.ProduceAsync(topic, new Message<string, byte[]> { Key = key, Value = decoy });
        decoyProducer.Flush(TimeSpan.FromSeconds(10));

        return decoyEventId;
    }

    /// <summary>Returns the record whose envelope <c>eventId</c> is <paramref name="expectedEventId"/>, and fails NAMING the record it did see otherwise.</summary>
    private static ConsumeResult<string, byte[]> ConsumeMatchingEventId(IConsumer<string, byte[]> consumer, Guid expectedEventId, TimeSpan timeout, Guid decoyEventId, string topic)
    {
        var deadline = DateTime.UtcNow + timeout;
        var seen = new List<Guid>();

        while (DateTime.UtcNow < deadline)
        {
            var candidate = consumer.Consume(TimeSpan.FromSeconds(5));
            if (candidate is null || candidate.IsPartitionEOF)
            {
                continue;
            }

            var eventId = ReadEventId(candidate.Message.Value);
            seen.Add(eventId);

            if (eventId == expectedEventId)
            {
                return candidate;
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"the read from '{topic}' never returned this test's own published fact (eventId {expectedEventId}) within {timeout}. "
            + $"The envelopes it did see, in arrival order, were [{string.Join(", ", seen)}]. "
            + $"A decoy with eventId {decoyEventId} was deliberately published to that topic first, under the SAME key, so a read that "
            + "selects by POSITION returns the decoy rather than the record this test produced.");
    }

    private static Guid ReadEventId(byte[] value)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(value);
            return document.RootElement.TryGetProperty("eventId", out var element) ? element.GetGuid() : Guid.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Ledger `L16` — publication order of facts written in one transaction
    /// depends ENTIRELY on the per-row awaited <c>INSERT</c>. This drains
    /// TWO credit lines' facts through TWO separate transactions (a credit
    /// line is a single-aggregate transaction, unlike Fulfillment's
    /// multi-item drain) and asserts the resulting <c>seq</c> order matches
    /// emission order (`F4`'s arming target).
    /// </summary>
    [Fact]
    public async Task OutboxRowsPreserveEmissionOrderAsSeq_AcrossTwoSequentiallyCommittedTransactions()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_relay_seqorder_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);
        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000002", "AldiEs", "IBERFOODS", 500_000);

        Guid firstEventId;
        Guid secondEventId;

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            firstEventId = await uow.ExecuteAsync(async ct =>
            {
                var c = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", OrderNumber.Parse("ORD-000010"), ct);
                var request = new HoldRequest(OrderNumber.Parse("ORD-000010"), new Money(1_000, "EUR"), UniqueId.New());
                c!.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                // SaveChangesAsync clears DomainEvents once everything is
                // durable (repository doc comment, line ~85) — capture the
                // emitted event's id BEFORE that call, not after.
                var eventId = ((Domain.Events.CreditApproved)c.DomainEvents[0]).EventId.Value;
                await repo.SaveChangesAsync(c, ct);
                return eventId;
            }, CancellationToken.None);
        }

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            secondEventId = await uow.ExecuteAsync(async ct =>
            {
                var c = await repo.LockForOrderAsync("AldiEs", "IBERFOODS", OrderNumber.Parse("ORD-000011"), ct);
                var request = new HoldRequest(OrderNumber.Parse("ORD-000011"), new Money(2_000, "EUR"), UniqueId.New());
                c!.Approve(request, new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                var eventId = ((Domain.Events.CreditApproved)c.DomainEvents[0]).EventId.Value;
                await repo.SaveChangesAsync(c, ct);
                return eventId;
            }, CancellationToken.None);
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var rowsInSeqOrder = await assertDb.OutboxMessages.AsNoTracking().OrderBy(m => m.Seq).ToListAsync();

        Assert.Equal(2, rowsInSeqOrder.Count);
        Assert.Equal(firstEventId, rowsInSeqOrder[0].EventId);
        Assert.Equal(secondEventId, rowsInSeqOrder[1].EventId);
    }

    /// <summary>
    /// `F4`'s own arming target. The cross-transaction test above cannot
    /// distinguish the per-row awaited <c>INSERT</c> from a batched
    /// <c>AddRange</c>, because every one of <see cref="BuyerCredit"/>'s
    /// commands raises exactly one domain event — two SEPARATE transactions
    /// each with a single row commit in wall-clock order regardless of how
    /// the single row inside each is written. This drives ONE aggregate
    /// through TWO holds for two DIFFERENT orders before a SINGLE
    /// <c>SaveChangesAsync</c> — <c>credit.DomainEvents</c> then carries TWO
    /// facts drained into the SAME transaction, which is exactly the shape
    /// a batched, order-non-deterministic <c>AddRange</c> can scramble.
    /// </summary>
    [Fact]
    public async Task OutboxRowsPreserveEmissionOrderAsSeq_ForTwoFactsRaisedWithinOneTransaction()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_relay_seqorder2_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-000001", "CarrefourEs", "IBERFOODS", 500_000);

        Guid firstEventId;
        Guid secondEventId;

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreBuyerCreditRepository(db, new OutboxWriter(new FixedClock(), new BillingFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            (firstEventId, secondEventId) = await uow.ExecuteAsync(async ct =>
            {
                var c = await repo.LockForOrderAsync("CarrefourEs", "IBERFOODS", OrderNumber.Parse("ORD-000020"), ct);
                c!.Approve(
                    new HoldRequest(OrderNumber.Parse("ORD-000020"), new Money(1_000, "EUR"), UniqueId.New()),
                    new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()),
                    UniqueId.New);
                c.Approve(
                    new HoldRequest(OrderNumber.Parse("ORD-000021"), new Money(2_000, "EUR"), UniqueId.New()),
                    new CreditContext(DateTimeOffset.UtcNow, UniqueId.New()),
                    UniqueId.New);

                var first = ((Domain.Events.CreditApproved)c.DomainEvents[0]).EventId.Value;
                var second = ((Domain.Events.CreditApproved)c.DomainEvents[1]).EventId.Value;
                await repo.SaveChangesAsync(c, ct);
                return (first, second);
            }, CancellationToken.None);
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var rowsInSeqOrder = await assertDb.OutboxMessages.AsNoTracking().OrderBy(m => m.Seq).ToListAsync();

        Assert.Equal(2, rowsInSeqOrder.Count);
        Assert.Equal(firstEventId, rowsInSeqOrder[0].EventId);
        Assert.Equal(secondEventId, rowsInSeqOrder[1].EventId);
    }

    /// <summary>
    /// Design.md §9 — Billing registers NO Kafka consumer and copies NO
    /// idempotent-consumer pair. <c>OutboxRelayParityTests</c> case 3
    /// (`tests/Orders.UnitTests`) already proves the CENSUS obligation this
    /// entry's absence would trigger; this confirms directly, from the
    /// source tree, that no <c>Infrastructure/Messaging/IdempotentConsumer.cs</c>
    /// exists for Billing.
    /// </summary>
    [Fact]
    public void BillingRegistersNoKafkaConsumer_AndCopiesNoIdempotentConsumerPair()
    {
        var repoRoot = FindRepositoryRoot();
        var path = Path.Combine(repoRoot, "src", "Billing", "Infrastructure", "Messaging", "IdempotentConsumer.cs");
        Assert.False(File.Exists(path));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderToCash.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate OrderToCash.sln.");
    }

    private sealed class FixedClock : Application.Ports.IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }
}
