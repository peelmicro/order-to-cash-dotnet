using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using OrderToCash.Notifications.Infrastructure.Observability;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// OR1, R16, design.md §3 — the SAME proof as Orders'
/// <c>SagaDeadLetterTests</c>, against Notifications' own real Kafka
/// broker: a poison fact is retried then dead-lettered, the committed
/// offset (read back from the broker) advances, and the next distinct fact
/// on the same partition still processes.
/// </summary>
[Collection(NotificationsCollection.Name)]
public sealed class NotificationDeadLetterTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql)
{
    private const string SourceTopic = NotificationFactTopics.OrdersFacts;
    private const string DlqTopic = SourceTopic + ".dlq";
    private const string GroupId = "notifications";
    private const int PartitionCount = 6;

    /// <summary>
    /// Review round 3 fix — IDENTICAL to <c>NotificationConsumptionTestSupport.WarmUpAsync</c>'s
    /// own hardcoded partition key, and deliberately so: this file's own
    /// <see cref="WarmUpAsync"/> calls that key (a DIFFERENT literal than
    /// this class previously used, <c>"notifications-dead-letter-tests"</c>)
    /// to prove ONE partition is ready before every publish below, and a
    /// DIFFERENT key hashes (murmur2, confirmed directly against a real
    /// broker: <c>"notifications-integration-tests"</c> → partition 3,
    /// <c>"notifications-dead-letter-tests"</c> → partition 5, on 6
    /// partitions) to a DIFFERENT partition — exactly the race
    /// <c>NotificationConsumptionTestSupport.PublishAsync</c>'s own remarks
    /// document and fix ("EVERY publish in this whole test project — warm-up
    /// and real alike — uses the SAME fixed partition key, deliberately"):
    /// a fresh <see cref="Confluent.Kafka.AutoOffsetReset.Latest"/>
    /// consumer's SIX partitions do not all finish resolving their "latest"
    /// position at the same instant, so a warm-up success on ONE partition
    /// never proved a DIFFERENT partition was equally ready. This class's
    /// own key silently violated that already-established, already-fixed
    /// invariant. Confirmed present under a full, unmodified sequential run
    /// of this whole project (no artificial contention): OR4_R57 failed with
    /// the SAME null-DLQ symptom even after the round's OWN group-clearance
    /// fix (below) — proving the shared-group mechanism was not the (or not
    /// the only) cause here, and pointing back at this partition mismatch.
    /// </summary>
    private const string FixedPartitionKey = "notifications-integration-tests";

    [Fact]
    public async Task OR1_R16_APoisonFactIsRetriedThenDeadLetteredWithItsDiagnosticHeaders_AndTheCommittedOffsetAdvancesSoTheNextDistinctFactOnTheSamePartitionStillProcesses()
    {
        await EnsureDlqTopicExistsAsync();

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_notifications_dlq_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var builder = NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
                options.DeadLetter.BootstrapServers = kafka.BootstrapServers;
            });

        var sender = new NotificationConsumptionTestSupport.FakeNotificationSender();
        builder.Services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<INotificationSender>(sender));

        var host = builder.Build();
        await host.StartAsync();

        try
        {
            // AutoOffsetReset.Latest (this service's own deliberate
            // divergence) — warm up on THIS topic before reading a
            // baseline, or the baseline itself races the consumer group's
            // own partition assignment.
            await NotificationConsumptionTestSupport.WarmUpAsync(kafka, sender, SourceTopic, TimeSpan.FromSeconds(90));

            // WarmUpAsync only guarantees the warm-up fact was PROCESSED
            // (StoreOffset called, observed via the sender) — the broker
            // COMMIT of that offset is separate: KafkaFactStreamSubscriber
            // uses EnableAutoCommit=true with the client's own periodic
            // interval, so `consumer.Committed()` can lag the true stored
            // offset by up to that interval. Reading `baseline` before that
            // commit lands makes it stale-by-exactly-one-record, and the
            // FIRST commit to arrive afterward — the warm-up's own delayed
            // one, not the poison fact's — then satisfies "advanced >
            // baseline" immediately, before the poison fact has been
            // retried or dead-lettered at all. Settling on two identical
            // consecutive reads closes that race without hard-coding the
            // interval as a magic number.
            var baseline = await NotificationOffsetSupport.WaitForCommittedOffsetToSettleAsync(kafka.BootstrapServers, SourceTopic, GroupId, PartitionCount, TimeSpan.FromSeconds(30));

            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var poisonEnvelope = new Envelope<string>(eventId, "order.placed.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            await PublishAsync(SourceTopic, poisonBytes);

            var advanced = await NotificationOffsetSupport.WaitForCommittedOffsetToExceedAsync(kafka.BootstrapServers, SourceTopic, GroupId, PartitionCount, baseline, TimeSpan.FromSeconds(90));
            Assert.True(advanced > baseline, $"Committed offset never advanced past baseline {baseline} (last observed {advanced}).");

            var dlqRecord = await ConsumeOneAsync(DlqTopic, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);
            Assert.Equal(poisonBytes, dlqRecord!.Message.Value);

            var headers = ReadHeaders(dlqRecord.Message.Headers);
            Assert.Equal("notifications", headers["x-failed-consumer"]);
            Assert.Equal("2", headers["x-attempts"]);
            Assert.Equal(SourceTopic, headers["x-original-topic"]);
            Assert.Equal("order.placed.v1", headers["x-event-type"]);
            Assert.Equal(ExpectedDeserialisationErrorMessage(), headers["x-error"]);

            // D3 (review round 1) — all EIGHT DeadLetterHeaders
            // (asyncapi.yaml), not the five above: x-first-failed-at and
            // x-failed-at were previously unasserted here, and traceparent
            // is ALWAYS present (NotificationFactsConsumer always wraps
            // dispatch in its own "consume {eventType}" span, design.md
            // §5.3, even with no inbound header to continue).
            Assert.True(DateTimeOffset.TryParse(headers["x-first-failed-at"], out _));
            Assert.True(DateTimeOffset.TryParse(headers["x-failed-at"], out _));
            Assert.True(headers.ContainsKey("traceparent"), "The .dlq message carries no traceparent header at all.");

            // The next, distinct fact on the SAME partition (SAME fixed
            // key) still processes.
            var secondEventId = Guid.NewGuid();
            var healthyPayload = new OrderPlacedPayload("ORD-DLQ-NEXT", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0);
            var healthyEnvelope = new Envelope<OrderPlacedPayload>(secondEventId, "order.placed.v1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, healthyPayload);
            var healthyBytes = JsonSerializer.SerializeToUtf8Bytes(healthyEnvelope, JsonWire.Options);

            await PublishAsync(SourceTopic, healthyBytes);

            var expectedMessageId = $"{secondEventId}@order-to-cash";
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            var seen = false;
            while (DateTime.UtcNow < deadline)
            {
                if (sender.SentMessages.Any(m => m.MessageId == expectedMessageId))
                {
                    seen = true;
                    break;
                }

                await Task.Delay(200);
            }

            Assert.True(seen, "The next distinct fact on the same partition never reached the sender — the partition stayed blocked.");
        }
        finally
        {
            await NotificationConsumptionTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Review round 2, D5 rows 36–37 — <c>NotificationFactsConsumer</c>'s
    /// <c>"consume {eventType}"</c> span CONTINUES the inbound Kafka
    /// <c>traceparent</c> (design.md §5.3), never a fresh root: the SAME
    /// mechanism <c>SagaDeadLetterTests.R57_OR4_EveryRetryAttempt…</c>
    /// proves for Orders, applied here. The poison fact carries a REAL
    /// <c>traceparent</c> header; the DLQ copy's own <c>traceparent</c>
    /// header — already asserted present by the sibling test above —
    /// extracts to the SAME trace id, never merely "a header exists".
    /// Armed by dropping <c>parentContext: parent</c> in
    /// <c>NotificationFactsConsumer.cs</c> — see the round-2 fix record.
    /// </summary>
    [Fact]
    public async Task OR4_R57_TheConsumeSpanContinuesTheInboundKafkaTrace_TheDlqCopyCarriesTheSameTraceIdAsTheInboundFact()
    {
        // Deliberately on NotificationFactTopics.FulfillmentFacts — NOT
        // this class's own sibling SourceTopic (OrdersFacts), and NOT
        // LogCorrelationTests.cs's own SourceTopic (BillingFacts) either —
        // AND matched by CONTENT (correlationId), never by position: the
        // shared class-level ConsumeOneAsync(topic, timeout) matches by
        // POSITION ("first non-EOF message"), which advisory A8 (round 2
        // record) already names as a hazard for any second poison
        // publisher on the same .dlq topic in the same collection.
        // Confirmed live, under the full solution run: sharing BillingFacts
        // with LogCorrelationTests.cs failed with a byte-mismatch on the
        // WRONG record — the positional read returned LogCorrelationTests'
        // own poison fact. The fix is the SAME one Orders' own
        // SagaDeadLetterTests.ConsumeOneAsync(topic, correlationId, timeout)
        // already uses — match the envelope's OWN correlationId field,
        // never "whatever arrived first," which makes the topic choice
        // itself no longer safety-critical (kept on FulfillmentFacts for
        // readability, not because it is now required).
        const string altTopic = NotificationFactTopics.FulfillmentFacts;
        const string altDlqTopic = altTopic + ".dlq";
        await EnsureDlqTopicExistsAsync(altDlqTopic);

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_notifications_dlq_trace_{Guid.NewGuid():N}");
        await using (var migrateDb = mssql.CreateDbContext(connectionString))
        {
            await migrateDb.Database.MigrateAsync();
        }

        var builder = NotificationsHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
                options.DeadLetter.BootstrapServers = kafka.BootstrapServers;
            });

        var sender = new NotificationConsumptionTestSupport.FakeNotificationSender();
        builder.Services.Replace(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<INotificationSender>(sender));

        var host = builder.Build();
        await host.StartAsync();

        try
        {
            await NotificationConsumptionTestSupport.WarmUpAsync(kafka, sender, altTopic, TimeSpan.FromSeconds(90));

            using var inboundActivity = OtcActivity.Source.StartActivity("test inbound fact writer");
            var inboundTraceParent = inboundActivity?.Id;
            Assert.NotNull(inboundTraceParent);

            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            // "order.placed.v1" — NOT "credit.approved.v1" — deliberately:
            // Notifications' own `_routes` table only dispatches (and
            // therefore only retries/dead-letters) the SEVEN eventTypes it
            // notifies on; `credit.*` is acknowledged with NO retry and NO
            // DLQ (domain-model.md §7.3, `NotificationFactsConsumer.cs`'s
            // own `_routes` table) regardless of which TOPIC it arrives on
            // — routing is by eventType, never by topic, exactly like the
            // warm-up helper above, which publishes "order.placed.v1" onto
            // whichever topic it is given.
            var poisonEnvelope = new Envelope<string>(eventId, "order.placed.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
            {
                var headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(inboundTraceParent!) } };
                await producer.ProduceAsync(altTopic, new Message<string, byte[]> { Key = FixedPartitionKey, Value = poisonBytes, Headers = headers });
            }

            var dlqRecord = await ConsumeMatchingAsync(altDlqTopic, correlationId, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);

            var headersRead = ReadHeaders(dlqRecord!.Message.Headers);
            Assert.True(headersRead.TryGetValue("traceparent", out var dlqTraceParent), "The .dlq message carries no traceparent header at all.");

            var dlqContext = TraceContext.ContextFromTraceParent(dlqTraceParent);
            Assert.NotNull(dlqContext);
            Assert.Equal(inboundActivity!.TraceId, dlqContext!.Value.TraceId);
        }
        finally
        {
            await NotificationConsumptionTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

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
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = dlqTopic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private async Task PublishAsync(string topic, byte[] value)
    {
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = FixedPartitionKey, Value = value });
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
            BootstrapServers = kafka.BootstrapServers,
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
            BootstrapServers = kafka.BootstrapServers,
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

/// <summary>SO9's broker-side proof, the Orders' <c>SagaIntegrationTestSupport</c> shape, duplicated here rather than cross-project-referenced.</summary>
internal static class NotificationOffsetSupport
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

    /// <summary>
    /// Polls until two consecutive reads, 1.5 s apart, report the SAME
    /// total — proof the periodic auto-committer has caught up to the
    /// last <c>StoreOffset</c> call, not merely that a read happened to
    /// return a number. Used to establish a baseline that will not be
    /// satisfied by a stale, already-in-flight commit.
    /// </summary>
    public static async Task<long> WaitForCommittedOffsetToSettleAsync(string bootstrapServers, string topic, string groupId, int partitionCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var previous = await ReadCommittedOffsetTotalAsync(bootstrapServers, topic, groupId, partitionCount, TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1500);
            var current = await ReadCommittedOffsetTotalAsync(bootstrapServers, topic, groupId, partitionCount, TimeSpan.FromSeconds(5));
            if (current == previous)
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }
}
