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
            var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock()), new FixedClock());
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

        await using var relayDb = mssql.CreateDbContext(connectionString);
        var relay = new OutboxRelay(relayDb, publisher, new FixedClock(), Microsoft.Extensions.Options.Options.Create(new OutboxRelayOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, result.Claimed);
        Assert.Equal(1, result.Published);

        // published_at stamped only AFTER acknowledgement.
        await using var afterDb = mssql.CreateDbContext(connectionString);
        var publishedRow = await afterDb.OutboxMessages.AsNoTracking().SingleAsync(m => m.CorrelationId == correlationId.Value);
        Assert.NotNull(publishedRow.PublishedAt);
        Assert.Equal("stock.reserved.v1", publishedRow.EventType);

        // Read it back through a real consumer, keyed by correlationId.
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(FulfillmentFactTopic.Name);

        var consumed = consumer.Consume(TimeSpan.FromSeconds(20));
        Assert.NotNull(consumed);
        Assert.Equal(correlationId.Value.ToString(), consumed!.Message.Key);
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
            var repo = new EfCoreStockItemRepository(db, new OutboxWriter(new FixedClock()), new FixedClock());
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
