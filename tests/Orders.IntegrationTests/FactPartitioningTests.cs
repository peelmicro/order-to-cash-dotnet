using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>R15 — real Kafka, a topic with 6 partitions, at least two orders interleaved, a real consumer (design.md §5.3, §9.3).</summary>
[Collection(KafkaCollection.Name)]
public sealed class FactPartitioningTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task R15_FactStream_DeliversAllFactsProducedByOneContextAboutOneOrderToConsumersInEmissionOrder()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_r15_{Guid.NewGuid():N}");
        await using var db = mssql.CreateDbContext(connectionString);
        await db.Database.MigrateAsync();

        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Interleaved: A, B, A, B, A — three facts for A, two for B. Added
        // ONE AT A TIME, each with its own SaveChangesAsync — see
        // EfCoreOrderRepository.InsertOutboxRowAsync's remarks: a GUID-keyed
        // entity added via AddRange (or several Add calls) inside ONE
        // SaveChangesAsync does not get its `seq` IDENTITY assigned in
        // Add-call order on this provider, so a test asserting append order
        // must not fall into the same trap the production writer was fixed
        // to avoid.
        var rows = new[]
        {
            NewRow(orderA, "order.placed.v1", now),
            NewRow(orderB, "order.placed.v1", now),
            NewRow(orderA, "order.confirmed.v1", now),
            NewRow(orderB, "order.confirmed.v1", now),
            NewRow(orderA, "order.completed.v1", now),
        };
        foreach (var row in rows)
        {
            db.OutboxMessages.Add(row);
            await db.SaveChangesAsync();
        }

        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        using var publisher = new KafkaFactPublisher(producer);
        var relayOptions = Options.Create(new OutboxRelayOptions { BatchSize = 10 });
        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var relay = new OutboxRelay(db, publisher, clock, relayOptions, NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);
        Assert.Equal(5, result.Published);

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"r15-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(OrdersFactTopic.Name);

        var consumed = new List<ConsumeResult<string, byte[]>>();
        while (consumed.Count < 5)
        {
            var next = consumer.Consume(TimeSpan.FromSeconds(15));
            Assert.NotNull(next);
            // Only count records this test's own two order ids produced —
            // the topic is shared by the whole Kafka collection.
            if (next!.Message.Key == orderA.ToString() || next.Message.Key == orderB.ToString())
            {
                consumed.Add(next);
            }
        }

        var orderAMessages = consumed.Where(m => m.Message.Key == orderA.ToString()).ToList();
        var orderBMessages = consumed.Where(m => m.Message.Key == orderB.ToString()).ToList();

        Assert.Equal(3, orderAMessages.Count);
        Assert.Equal(2, orderBMessages.Count);

        // Every fact about one order lands on ONE partition.
        Assert.Single(orderAMessages.Select(m => m.Partition.Value).Distinct());
        Assert.Single(orderBMessages.Select(m => m.Partition.Value).Distinct());

        // Emission order, per order: placed -> confirmed [-> completed].
        Assert.Equal(
            ["order.placed.v1", "order.confirmed.v1", "order.completed.v1"],
            orderAMessages.Select(EventTypeOf));
        Assert.Equal(
            ["order.placed.v1", "order.confirmed.v1"],
            orderBMessages.Select(EventTypeOf));
    }

    private static string EventTypeOf(ConsumeResult<string, byte[]> message)
    {
        var header = message.Message.Headers.First(h => h.Key == "x-event-type");
        return System.Text.Encoding.UTF8.GetString(header.GetValueBytes());
    }

    private static OutboxMessage NewRow(Guid orderId, string eventType, DateTime occurredAt) => new()
    {
        Id = Guid.NewGuid(),
        EventId = Guid.NewGuid(),
        EventType = eventType,
        AggregateId = orderId,
        CorrelationId = orderId,
        CausationId = Guid.NewGuid(),
        Payload = "{}",
        OccurredAt = occurredAt,
        CreatedAt = occurredAt,
    };
}
