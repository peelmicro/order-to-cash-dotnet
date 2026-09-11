using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.Projector.Infrastructure.Observability;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// OR1, R16, design.md §3 — the SAME proof as Orders' <c>SagaDeadLetterTests</c>
/// and Notifications' <c>NotificationDeadLetterTests</c>, against Projector's
/// own real Kafka broker: a poison fact is retried then dead-lettered, the
/// committed offset (read back from the broker) advances, and the next
/// distinct fact on the same partition still processes.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class ProjectorDeadLetterTests(MongoContainerFixture mongoFixture, KafkaContainerFixture kafkaFixture, NatsContainerFixture natsFixture)
{
    private const string SourceTopic = ProjectorFactTopics.OrdersFacts;
    private const string DlqTopic = SourceTopic + ".dlq";
    private const string GroupId = "projector";
    private const int PartitionCount = 6;

    [Fact]
    public async Task OR1_R16_APoisonFactIsRetriedThenDeadLetteredWithItsDiagnosticHeaders_AndTheCommittedOffsetAdvancesSoTheNextDistinctFactOnTheSamePartitionStillProcesses()
    {
        await EnsureDlqTopicExistsAsync();

        var host = await ProjectorTestHost.StartAsync(
            kafkaFixture, natsFixture, mongoFixture.ConnectionString, "otc_rm_dlq_" + Guid.NewGuid().ToString("N"),
            configure: options =>
            {
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
                options.DeadLetter.BootstrapServers = kafkaFixture.BootstrapServers;
            });

        try
        {
            var baseline = await ProjectorOffsetSupport.ReadCommittedOffsetTotalAsync(kafkaFixture.BootstrapServers, SourceTopic, GroupId, PartitionCount, TimeSpan.FromSeconds(10));

            var orderId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var poisonEnvelope = new Envelope<string>(eventId, "order.placed.v1", orderId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            await PublishRawAsync(SourceTopic, orderId.ToString(), poisonBytes);

            var advanced = await ProjectorOffsetSupport.WaitForCommittedOffsetToExceedAsync(kafkaFixture.BootstrapServers, SourceTopic, GroupId, PartitionCount, baseline, TimeSpan.FromSeconds(90));
            Assert.True(advanced > baseline, $"Committed offset never advanced past baseline {baseline} (last observed {advanced}).");

            var dlqRecord = await ConsumeOneAsync(DlqTopic, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);
            Assert.Equal(poisonBytes, dlqRecord!.Message.Value);

            var headers = ReadHeaders(dlqRecord.Message.Headers);
            Assert.Equal("projector", headers["x-failed-consumer"]);
            Assert.Equal("2", headers["x-attempts"]);
            Assert.Equal(SourceTopic, headers["x-original-topic"]);
            Assert.Equal("order.placed.v1", headers["x-event-type"]);
            Assert.Equal(ExpectedDeserialisationErrorMessage(), headers["x-error"]);

            // D3 (review round 1) — all EIGHT DeadLetterHeaders
            // (asyncapi.yaml), not the five above: x-first-failed-at and
            // x-failed-at were previously unasserted here, and traceparent
            // is ALWAYS present (ProjectorFactsConsumer always wraps
            // dispatch in its own "consume {eventType}" span, design.md
            // §5.3, even with no inbound header to continue). Armed by the
            // review's own P7 probe — corrupting x-first-failed-at in
            // THIS service's KafkaDeadLetterPublisher copy left both
            // Projector suites green before this assertion existed.
            Assert.True(DateTimeOffset.TryParse(headers["x-first-failed-at"], out _));
            Assert.True(DateTimeOffset.TryParse(headers["x-failed-at"], out _));
            Assert.True(headers.ContainsKey("traceparent"), "The .dlq message carries no traceparent header at all.");

            // The next, distinct fact on the SAME partition (same key =
            // orderId) still processes: a healthy order.placed.v1 lands in
            // Mongo.
            var secondOrderId = orderId; // same key -> same partition.
            var healthyPayload = new OrderPlacedPayload("ORD-DLQ-NEXT", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0);
            var healthyEnvelope = new Envelope<OrderPlacedPayload>(Guid.NewGuid(), "order.placed.v1", secondOrderId, secondOrderId, Guid.NewGuid(), DateTimeOffset.UtcNow, healthyPayload);
            var healthyBytes = JsonSerializer.SerializeToUtf8Bytes(healthyEnvelope, JsonWire.Options);

            await PublishRawAsync(SourceTopic, secondOrderId.ToString(), healthyBytes);

            var doc = await ProjectorTestHost.PollUntilAsync(
                GetCollection(host),
                secondOrderId,
                d => d.Contains("status"),
                TimeSpan.FromSeconds(90));

            Assert.NotNull(doc);
        }
        finally
        {
            await ProjectorTestHost.StopHostAndWaitForGroupToClearAsync(host, kafkaFixture);
        }
    }

    /// <summary>
    /// Review round 2, D5 rows 36–37 — <c>ProjectorFactsConsumer</c>'s
    /// <c>"consume {eventType}"</c> span CONTINUES the inbound Kafka
    /// <c>traceparent</c> (design.md §5.3), never a fresh root: the SAME
    /// mechanism <c>SagaDeadLetterTests.R57_OR4_EveryRetryAttempt…</c>
    /// proves for Orders, applied here. The poison fact carries a REAL
    /// <c>traceparent</c> header (as a genuine upstream writer would via
    /// <c>outbox.trace_parent</c>); the DLQ copy's own <c>traceparent</c>
    /// header — already asserted present by the sibling test above —
    /// extracts to the SAME trace id, never merely "a header exists".
    /// Armed by dropping <c>parentContext: parent</c> in
    /// <c>ProjectorFactsConsumer.cs</c> — see the round-2 fix record.
    /// Deliberately on <see cref="ProjectorFactTopics.BillingFacts"/> — NOT
    /// this class's own sibling <see cref="SourceTopic"/> (OrdersFacts),
    /// and NOT <c>LogCorrelationTests.cs</c>'s own
    /// <see cref="ProjectorFactTopics.FulfillmentFacts"/> either (found
    /// live, under the full solution run: sharing <c>FulfillmentFacts</c>
    /// made THIS test fail with a byte-mismatch on the WRONG record — the
    /// exact shape advisory A8 (round 2 record) predicts) — AND matched by
    /// CONTENT (<c>correlationId</c>) via <see cref="ConsumeMatchingAsync"/>,
    /// never by position: the shared class-level
    /// <see cref="ConsumeOneAsync(string, TimeSpan)"/> matches by POSITION
    /// ("first non-EOF message"), which is exactly the hazard advisory A8
    /// names — the SAME fix Orders' own
    /// <c>SagaDeadLetterTests.ConsumeOneAsync(topic, correlationId,
    /// timeout)</c> already uses.
    /// </summary>
    [Fact]
    public async Task OR4_R57_TheConsumeSpanContinuesTheInboundKafkaTrace_TheDlqCopyCarriesTheSameTraceIdAsTheInboundFact()
    {
        const string altTopic = ProjectorFactTopics.BillingFacts;
        const string altDlqTopic = altTopic + ".dlq";
        await EnsureDlqTopicExistsAsync(altDlqTopic);

        var host = await ProjectorTestHost.StartAsync(
            kafkaFixture, natsFixture, mongoFixture.ConnectionString, "otc_rm_dlq_trace_" + Guid.NewGuid().ToString("N"),
            configure: options =>
            {
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
                options.DeadLetter.BootstrapServers = kafkaFixture.BootstrapServers;
            });

        try
        {
            using var inboundActivity = OtcActivity.Source.StartActivity("test inbound fact writer");
            var inboundTraceParent = inboundActivity?.Id;
            Assert.NotNull(inboundTraceParent);

            var orderId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var poisonEnvelope = new Envelope<string>(eventId, "stock.reserved.v1", orderId, orderId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafkaFixture.BootstrapServers }).Build())
            {
                var headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(inboundTraceParent!) } };
                await producer.ProduceAsync(altTopic, new Message<string, byte[]> { Key = orderId.ToString(), Value = poisonBytes, Headers = headers });
            }

            var dlqRecord = await ConsumeMatchingAsync(altDlqTopic, orderId, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);

            var headersRead = ReadHeaders(dlqRecord!.Message.Headers);
            Assert.True(headersRead.TryGetValue("traceparent", out var dlqTraceParent), "The .dlq message carries no traceparent header at all.");

            var dlqContext = TraceContext.ContextFromTraceParent(dlqTraceParent);
            Assert.NotNull(dlqContext);
            Assert.Equal(inboundActivity!.TraceId, dlqContext!.Value.TraceId);
        }
        finally
        {
            await ProjectorTestHost.StopHostAndWaitForGroupToClearAsync(host, kafkaFixture);
        }
    }

    private static IMongoCollection<BsonDocument> GetCollection(Microsoft.Extensions.Hosting.IHost host) =>
        (IMongoCollection<BsonDocument>)host.Services.GetService(typeof(IMongoCollection<BsonDocument>))!;

    private static string ExpectedDeserialisationErrorMessage()
    {
        var poisonPayloadElement = JsonDocument.Parse("\"poison-payload-not-an-object\"").RootElement;
        try
        {
            JsonSerializer.Deserialize<OrderPlacedPayload>(poisonPayloadElement, JsonWire.Options);
            throw new InvalidOperationException("Expected a JsonException — the poison payload no longer fails to deserialise.");
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }

    private Task EnsureDlqTopicExistsAsync() => EnsureDlqTopicExistsAsync(DlqTopic);

    private async Task EnsureDlqTopicExistsAsync(string dlqTopic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafkaFixture.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = dlqTopic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private async Task PublishRawAsync(string topic, string key, byte[] value)
    {
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafkaFixture.BootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = key, Value = value });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Reads the SAME <c>.dlq</c> topic with <see cref="IConsumer{TKey,TValue}.Assign"/>
    /// over its own known partition set, at <see cref="Offset.Beginning"/> —
    /// deliberately NOT <c>Subscribe()</c>. A consumer-GROUP subscription
    /// pays the full FindCoordinator/JoinGroup/SyncGroup round trip before
    /// its first poll can return anything; under the CPU contention of a
    /// full solution-wide <c>dotnet test</c> run (many concurrent
    /// Testcontainers-backed suites), that round trip was observed to
    /// exceed even a 90 s budget while the SAME probe passed reliably
    /// standalone. A direct partition assignment needs no coordinator at
    /// all — it is the same class of fix
    /// <c>SagaIntegrationTestSupport.ReadCommittedOffsetTotalAsync</c>
    /// already uses (<c>consumer.Committed</c>, not a full group join) for
    /// exactly this reason.
    /// </summary>
    private async Task<ConsumeResult<string, byte[]>?> ConsumeOneAsync(string topic, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafkaFixture.BootstrapServers,
            GroupId = $"dlq-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6)
            .Select(p => new TopicPartitionOffset(topic, new Partition(p), Offset.Beginning))
            .ToList());

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is not null && !result.IsPartitionEOF)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Review round 2 — the CONTENT-matching counterpart to
    /// <see cref="ConsumeOneAsync(string, TimeSpan)"/> above, the SAME
    /// shape Orders' own <c>SagaDeadLetterTests.ConsumeOneAsync(topic,
    /// correlationId, timeout)</c> already uses: matches the <c>.dlq</c>
    /// payload's OWN <c>correlationId</c> field (the payload is the
    /// UNMODIFIED original envelope, ledger L15), never "whatever arrived
    /// first" — advisory A8 (round 2 record) names the positional overload
    /// above as a hazard for any second poison publisher on the SAME
    /// <c>.dlq</c> topic in the same collection.
    /// </summary>
    private async Task<ConsumeResult<string, byte[]>?> ConsumeMatchingAsync(string topic, Guid correlationId, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafkaFixture.BootstrapServers,
            GroupId = $"dlq-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6)
            .Select(p => new TopicPartitionOffset(topic, new Partition(p), Offset.Beginning))
            .ToList());

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is not null && !result.IsPartitionEOF && MatchesCorrelationId(result.Message.Value, correlationId))
            {
                return result;
            }
        }

        return null;
    }

    private static bool MatchesCorrelationId(byte[] messageValue, Guid correlationId)
    {
        try
        {
            using var document = JsonDocument.Parse(messageValue);
            return document.RootElement.TryGetProperty("correlationId", out var actual) && actual.GetGuid() == correlationId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> ReadHeaders(Headers headers) =>
        headers.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()), StringComparer.Ordinal);
}

/// <summary>SO9's broker-side proof, duplicated here rather than cross-project-referenced.</summary>
internal static class ProjectorOffsetSupport
{
    public static async Task<long> ReadCommittedOffsetTotalAsync(string bootstrapServers, string topic, string groupId, int partitionCount, TimeSpan requestTimeout)
    {
        var config = new ConsumerConfig { BootstrapServers = bootstrapServers, GroupId = groupId, EnableAutoCommit = false };
        using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();
        var partitions = Enumerable.Range(0, partitionCount).Select(p => new TopicPartition(topic, new Partition(p))).ToList();

        List<TopicPartitionOffset> committed = [];
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                committed = await Task.Run(() => consumer.Committed(partitions, requestTimeout));
                break;
            }
            catch (KafkaException) when (attempt < 5)
            {
                await Task.Delay(300);
            }
        }

        return committed.Sum(tpo => tpo.Offset.IsSpecial ? 0L : tpo.Offset.Value);
    }

    public static async Task<long> WaitForCommittedOffsetToExceedAsync(string bootstrapServers, string topic, string groupId, int partitionCount, long baseline, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = baseline;

        while (DateTime.UtcNow < deadline)
        {
            last = await ReadCommittedOffsetTotalAsync(bootstrapServers, topic, groupId, partitionCount, TimeSpan.FromSeconds(5));
            if (last > baseline)
            {
                return last;
            }

            await Task.Delay(300);
        }

        return last;
    }
}
