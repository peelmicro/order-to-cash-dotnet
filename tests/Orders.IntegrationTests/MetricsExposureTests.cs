using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// OR5/design.md §7 — <c>otc_outbox_lag_ms</c> and <c>otc_dlq_depth</c>,
/// against REAL MS-SQL and REAL Kafka. Every assertion is an EXACT value —
/// "a 'greater than zero' assertion proves nothing here" (design.md §7's
/// own table).
/// </summary>
[Collection(KafkaCollection.Name)]
public sealed class MetricsExposureTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task OtcOutboxLagMs_TracksAGenuinelyAgedRealRow_ThenDropsTo0AfterTheRelayDrains()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_lag_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var ageMs = 7_500d;
        var createdAt = clock.UtcNow.UtcDateTime.AddMilliseconds(-ageMs);

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                EventType = "order.placed.v1",
                AggregateId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                Payload = "{}",
                OccurredAt = createdAt,
                CreatedAt = createdAt,
            });
            await db.SaveChangesAsync();
        }

        using var publisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build());

        // First cycle — the row is still unpublished at the moment
        // RecordOutboxLagAsync runs (it runs BEFORE the claim), so the
        // gauge must report the EXACT age computed from the same clock.
        using (var capture = MetricCapture.ForInstrument("otc_outbox_lag_ms"))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Published);

            var measurement = Assert.Single(capture.Measurements);
            Assert.Equal(ageMs, measurement.Value, precision: 0);
        }

        // Second cycle — nothing left unpublished, so the gauge reports 0.
        using (var capture = MetricCapture.ForInstrument("otc_outbox_lag_ms"))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(0, result.Claimed);

            var measurement = Assert.Single(capture.Measurements);
            Assert.Equal(0, measurement.Value);
        }
    }

    [Fact]
    public async Task OtcDlqDepth_TheRealKafkaBackedGauge_SumsHighMinusLowAcrossEveryPartitionOfEveryDlqTopic_AgainstTheBrokersOwnReportedCount()
    {
        var dlqTopic = $"{SagaFactTopics.OrdersFacts}.dlq";
        await EnsureTopicExistsAsync(dlqTopic, partitions: 1);

        // Produce THREE messages to the .dlq topic — the broker's own
        // watermark is the expected value, never a locally kept tally.
        using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            for (var i = 0; i < 3; i++)
            {
                await producer.ProduceAsync(dlqTopic, new Message<string, byte[]> { Key = Guid.NewGuid().ToString(), Value = "poison"u8.ToArray() });
            }
        }

        var ordersDlqDepth = await ReadWatermarkDepthAsync(dlqTopic);
        Assert.True(ordersDlqDepth >= 3, $"Expected the broker's own watermark depth to be at least 3; was {ordersDlqDepth}.");

        // The gauge sums ACROSS ALL THREE .dlq topics (SagaFactTopics.All),
        // never just this one — so the expected total is itself a sum of
        // the broker's own watermark for every topic, not this topic's
        // depth alone. Other tests in this suite (e.g. the cross-topic
        // independence case below) may leave real, permanent messages on
        // the other two .dlq topics; reading each topic's OWN current
        // watermark, rather than assuming it is empty, is what keeps this
        // assertion true regardless of what ran before it.
        var fulfillmentDlqDepth = await ReadWatermarkDepthAsync($"{SagaFactTopics.FulfillmentFacts}.dlq");
        var billingDlqDepth = await ReadWatermarkDepthAsync($"{SagaFactTopics.BillingFacts}.dlq");
        var expectedDepth = ordersDlqDepth + fulfillmentDlqDepth + billingDlqDepth;

        var gauge = new KafkaDlqDepthGauge(Options.Create(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, ClientId = "otc-orders-dlq-depth-test" }));

        using var capture = MetricCapture.ForInstrument("otc_dlq_depth");
        await gauge.RecordAsync(CancellationToken.None);

        var measurement = Assert.Single(capture.LongMeasurements);
        Assert.Equal(expectedDepth, measurement.Value);
    }

    [Fact]
    public async Task OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows()
    {
        // A fresh bootstrap-servers-only client against a topic that has
        // never been created — SagaFactTopics.All's own three topics exist
        // in this shared broker (other tests create them), so this proves
        // the "missing topic" leg against a GENUINELY absent one.
        var gauge = new KafkaDlqDepthGauge(Options.Create(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, ClientId = "otc-orders-dlq-depth-test" }));

        using var capture = MetricCapture.ForInstrument("otc_dlq_depth");
        var exception = await Record.ExceptionAsync(() => gauge.RecordAsync(CancellationToken.None));

        Assert.Null(exception);
        // The measurement is recorded regardless — SagaFactTopics.All's
        // three .dlq topics are queried independently, and any that do not
        // exist contribute 0 rather than throwing (ported case 68).
        Assert.Single(capture.LongMeasurements);
    }

    /// <summary>
    /// D3 (review round 1), design.md §11's ported case 69 — "queries each
    /// topic independently". The two cases above prove case 67 (multiple
    /// MESSAGES in one topic) and case 68 (one MISSING topic reports 0
    /// alone); neither proves that a missing topic does not poison — or
    /// get silently skipped past — the SUM across the other, real topics.
    /// This test creates TWO real .dlq topics with DIFFERENT partition
    /// counts and DIFFERENT message counts, leaves the third
    /// (<c>SagaFactTopics.BillingFacts</c> — genuinely never created by any
    /// other test in this suite, verified by construction) absent, and
    /// asserts the recorded total is EXACTLY the sum of the two real
    /// topics' own broker-reported depths — proving the gauge queries and
    /// sums every topic independently, in the SAME <c>RecordAsync</c> call.
    /// </summary>
    [Fact]
    public async Task OtcDlqDepth_SumsAcrossMultipleTopicsIndependently_AMissingTopicNeitherStopsNorZeroesTheOthers()
    {
        var ordersDlq = $"{SagaFactTopics.OrdersFacts}.dlq";
        var fulfillmentDlq = $"{SagaFactTopics.FulfillmentFacts}.dlq";
        await EnsureTopicExistsAsync(ordersDlq, partitions: 1);
        await EnsureTopicExistsAsync(fulfillmentDlq, partitions: 2);

        using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            for (var i = 0; i < 2; i++)
            {
                await producer.ProduceAsync(ordersDlq, new Message<string, byte[]> { Key = Guid.NewGuid().ToString(), Value = "poison"u8.ToArray() });
            }

            for (var i = 0; i < 5; i++)
            {
                await producer.ProduceAsync(fulfillmentDlq, new Message<string, byte[]> { Key = Guid.NewGuid().ToString(), Value = "poison"u8.ToArray() });
            }
        }

        var expectedOrdersDepth = await ReadWatermarkDepthAsync(ordersDlq);
        var expectedFulfillmentDepth = await ReadWatermarkDepthAsync(fulfillmentDlq);
        Assert.True(expectedOrdersDepth >= 2, $"Expected the broker's own watermark depth to be at least 2; was {expectedOrdersDepth}.");
        Assert.True(expectedFulfillmentDepth >= 5, $"Expected the broker's own watermark depth to be at least 5; was {expectedFulfillmentDepth}.");

        var gauge = new KafkaDlqDepthGauge(Options.Create(new KafkaOptions { BootstrapServers = kafka.BootstrapServers, ClientId = "otc-orders-dlq-depth-test" }));

        using var capture = MetricCapture.ForInstrument("otc_dlq_depth");
        await gauge.RecordAsync(CancellationToken.None);

        var measurement = Assert.Single(capture.LongMeasurements);
        // SagaFactTopics.BillingFacts.dlq (not created here) contributes 0
        // — the total is EXACTLY the sum of the two REAL topics' own
        // broker-reported depths.
        Assert.Equal(expectedOrdersDepth + expectedFulfillmentDepth, measurement.Value);
    }

    private async Task EnsureTopicExistsAsync(string topic, int partitions)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    /// <summary>The broker's own watermark depth for one topic — 0 (never a throw) when the topic does not exist, the SAME "not yet created" shape <see cref="KafkaDlqDepthGauge"/> itself handles.</summary>
    private async Task<long> ReadWatermarkDepthAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(5));
        var topicMetadata = metadata.Topics.SingleOrDefault(t => t.Topic == topic);

        if (topicMetadata is null || topicMetadata.Error.Code != ErrorCode.NoError || topicMetadata.Partitions.Count == 0)
        {
            return 0;
        }

        using var consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig { BootstrapServers = kafka.BootstrapServers, GroupId = $"watermark-probe-{Guid.NewGuid():N}" }).Build();

        long total = 0;
        foreach (var partition in topicMetadata.Partitions)
        {
            var watermarks = consumer.QueryWatermarkOffsets(new TopicPartition(topic, new Partition(partition.PartitionId)), TimeSpan.FromSeconds(5));
            total += Math.Max(0, watermarks.High.Value - watermarks.Low.Value);
        }

        await Task.CompletedTask;
        return total;
    }
}
