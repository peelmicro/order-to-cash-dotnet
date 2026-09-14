using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS16`, and ledger L8's seq-order guarantee — over REAL MS-SQL and REAL Kafka.</summary>
[Collection(KafkaCollection.Name)]
public sealed class FulfillmentOutboxRelayTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task FS16_PublishesTheFactsOfAReserveTransactionToTheFulfillmentTopicKeyedByCorrelationId_AndStampsPublishedAtOnlyAfterAcknowledgement()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_relay_fs16_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var stockId = Guid.NewGuid();
        await SeedStockAsync(connectionString, stockId, "ACME", "P1", 10);

        var correlationId = UniqueId.New();
        var orderReference = new OrderNumber(1);

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock(), new StockFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            await uow.ExecuteAsync(async ct =>
            {
                var locked = await repo.LockForOrderAsync("ACME", ["P1"], orderReference, ct);
                var input = new ReserveOrderInput(orderReference, "ACME", "RETAILER1", [new ReserveOrderLine("P1", new Quantity(3))], correlationId);
                OrderStockReservation.Reserve(locked.ItemsByProductCode, input, new StockContext(DateTimeOffset.UtcNow, UniqueId.New()), UniqueId.New);
                await repo.SaveChangesAsync(ct);
            }, CancellationToken.None);
        }

        // Confirm the row exists, unpublished, before the relay runs.
        await using (var beforeDb = mssql.CreateDbContext(connectionString))
        {
            var row = await beforeDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId.Value);
            Assert.Null(row.PublishedAt);
        }

        using var producer = new ProducerBuilder<string, byte[]>(KafkaFactPublisher.BuildProducerConfig(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, ClientId = "otc-fulfillment-test" })).Build();
        using var publisher = new KafkaFactPublisher(producer);

        // Backlog id 74 bullets 2-3 — the DETERMINISM lives in this test, not
        // in the mutation. A decoy envelope with a DIFFERENT eventId is placed
        // on the SAME topic BEFORE the relay runs, under the SAME Kafka key
        // (the correlationId) the relay will use — so it is on the SAME
        // partition at a strictly EARLIER offset, and a read that takes
        // "whatever the first Consume() returns" returns the decoy on EVERY
        // run. The old read here did exactly that and then asserted only on
        // the message KEY, which the decoy also satisfies.
        var decoyEventId = await PublishDecoyAsync(FulfillmentFactTopic.Name, correlationId.Value.ToString());

        await using var relayDb = mssql.CreateDbContext(connectionString);
        var relay = new OutboxRelay(relayDb, publisher, new FixedClock(), Microsoft.Extensions.Options.Options.Create(new OutboxRelayOptions()), new NoOpDlqDepthGauge(), Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);

        // published_at stamped only AFTER acknowledgement.
        await using var afterDb = mssql.CreateDbContext(connectionString);
        var publishedRow = await afterDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId.Value);
        Assert.NotNull(publishedRow.PublishedAt);
        Assert.Equal("stock.reserved.v1", publishedRow.EventType);

        // Read it back through a real consumer — the record this test's OWN
        // relay cycle published, selected by the eventId the outbox row
        // carries, never "whatever arrived first on a topic the whole Kafka
        // collection shares" (backlog id 74 bullets 1-2).
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(FulfillmentFactTopic.Name);

        var consumed = ConsumeMatchingEventId(consumer, publishedRow.EventId, TimeSpan.FromSeconds(30), decoyEventId, FulfillmentFactTopic.Name);

        // Deliberately kept after the content-matching read, and deliberately
        // redundant on the happy path: this is the assertion a REVERSION to a
        // positional `consumer.Consume(...)` kills. The KEY assertion below
        // cannot do that job — the decoy carries the SAME key, by design, so
        // the pre-fix shape passed it while holding another record entirely.
        AssertIsThisTestsOwnRecord(consumed, publishedRow.EventId, decoyEventId, FulfillmentFactTopic.Name);
        Assert.Equal(correlationId.Value.ToString(), consumed.Message.Key);
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
            $"{{\"eventId\":\"{decoyEventId}\",\"eventType\":\"stock.reserved.v1\",\"aggregateId\":\"{Guid.NewGuid()}\",\"correlationId\":\"{Guid.NewGuid()}\",\"causationId\":\"{Guid.NewGuid()}\",\"occurredAt\":\"{DateTimeOffset.UtcNow:O}\",\"payload\":{{}}}}");

        using var decoyProducer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await decoyProducer.ProduceAsync(topic, new Message<string, byte[]> { Key = key, Value = decoy });
        decoyProducer.Flush(TimeSpan.FromSeconds(10));

        return decoyEventId;
    }

    /// <summary>Returns the record whose envelope <c>eventId</c> is <paramref name="expectedEventId"/>, and fails NAMING the records it did see otherwise.</summary>
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
    /// Ledger L8 — publication order of facts written in one transaction
    /// depends ENTIRELY on the per-row awaited <c>INSERT</c>
    /// (<c>EfCoreOrderRepository.InsertOutboxRowAsync</c>'s own measured
    /// finding): EF Core's SQL Server provider does not preserve <c>Add</c>
    /// order when assigning IDENTITY values. This drains TWO stock items'
    /// facts through ONE <c>SaveChangesAsync</c> call and asserts the
    /// resulting <c>seq</c> order matches emission order (F3 arming target,
    /// `tasks.md` F3).
    /// </summary>
    [Fact]
    public async Task OutboxRowsPreserveEmissionOrderAsSeq_WhenOneTransactionDrainsMultipleAggregates()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_relay_seqorder_{Guid.NewGuid():N}");
        await using (var migrate = mssql.CreateDbContext(connectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        var stockId1 = Guid.NewGuid();
        var stockId2 = Guid.NewGuid();
        await SeedStockAsync(connectionString, stockId1, "ACME", "P1", 10);
        await SeedStockAsync(connectionString, stockId2, "ACME", "P2", 10);

        var firstEventId = UniqueId.New();
        var secondEventId = UniqueId.New();

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock(), new StockFactPayloadMapper()), new FixedClock());
            var uow = new EfCoreUnitOfWork(db);

            await uow.ExecuteAsync(async ct =>
            {
                var locked1 = await repo.LockForOrderAsync("ACME", ["P1"], new OrderNumber(10), ct);
                var item1 = locked1.ItemsByProductCode["P1"];
                item1.Reserve(UniqueId.New(), new OrderNumber(10), "RETAILER1", new Quantity(1));
                item1.RecordOrderFact(new StockReserved(firstEventId, item1.Id, UniqueId.New(), UniqueId.New(), DateTimeOffset.UtcNow, new OrderNumber(10), "ACME", null, []));

                var locked2 = await repo.LockForOrderAsync("ACME", ["P2"], new OrderNumber(11), ct);
                var item2 = locked2.ItemsByProductCode["P2"];
                item2.Reserve(UniqueId.New(), new OrderNumber(11), "RETAILER1", new Quantity(1));
                item2.RecordOrderFact(new StockReserved(secondEventId, item2.Id, UniqueId.New(), UniqueId.New(), DateTimeOffset.UtcNow, new OrderNumber(11), "ACME", null, []));

                await repo.SaveChangesAsync(ct);
            }, CancellationToken.None);
        }

        await using var assertDb = mssql.CreateDbContext(connectionString);
        var rowsInSeqOrder = await assertDb.OutboxMessages.AsNoTracking().OrderBy(m => m.Seq).ToListAsync();

        Assert.Equal(2, rowsInSeqOrder.Count);
        Assert.Equal(firstEventId.Value, rowsInSeqOrder[0].EventId);
        Assert.Equal(secondEventId.Value, rowsInSeqOrder[1].EventId);
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
