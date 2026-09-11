using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using OrderToCash.Notifications.Infrastructure.Persistence;
using OrderToCash.Notifications.Infrastructure.Persistence.Entities;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>Shared harness for the real-Kafka/real-MS-SQL consumption suites.</summary>
internal static class NotificationConsumptionTestSupport
{
    /// <summary>
    /// Starts a REAL Notifications host (<c>NotificationsHost.CreateBuilder</c>
    /// itself — same call <c>Program.cs</c> makes) against a fresh migrated
    /// database and the real Kafka broker, with the sender port overridden to
    /// a fake so sends are observable without touching a real SMTP server —
    /// then WARMS UP the consumer (<see cref="WarmUpAsync"/>) before
    /// returning, so every caller's own publish is guaranteed to land after
    /// this consumer group's partitions are genuinely assigned.
    /// </summary>
    public static async Task<(IHost Host, string ConnectionString, FakeNotificationSender Sender)> StartHostAsync(
        MsSqlContainerFixture mssql,
        KafkaContainerFixture kafka,
        string databaseNameSuffix)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_notifications_{databaseNameSuffix}_{Guid.NewGuid():N}");
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
                // SenderKind left at its Console default deliberately — this
                // suite overrides INotificationSender directly below, which
                // is a STRONGER form of "the same port" than binding console
                // and merely not asserting on its output.
            });

        var sender = new FakeNotificationSender();
        builder.Services.Replace(ServiceDescriptor.Singleton<INotificationSender>(sender));

        var host = builder.Build();
        await host.StartAsync();

        // Warmed up on EVERY topic this service subscribes to, not just
        // OrdersFacts — a real gap this same helper closed on the FIRST
        // attempt only for OrdersFacts: with the fixed partition key
        // (PublishAsync's own remarks) a warm-up success on OrdersFacts'
        // key-hashed partition is a strong signal for the SAME partition
        // number on every other topic too, but topic-partition assignment
        // and each partition's own "latest" resolution are still tracked
        // per (topic, partition) pair inside librdkafka, so a caller
        // publishing straight to FulfillmentFacts or BillingFacts without
        // its OWN warm-up observed exactly this race once, reproducibly
        // (AStockReservedFact_IsAcknowledgedButNeverDispatched, which
        // publishes only to FulfillmentFacts).
        await WarmUpAsync(kafka, sender, NotificationFactTopics.OrdersFacts);
        await WarmUpAsync(kafka, sender, NotificationFactTopics.FulfillmentFacts);
        await WarmUpAsync(kafka, sender, NotificationFactTopics.BillingFacts);

        return (host, connectionString, sender);
    }

    /// <summary>
    /// Publishes a THROWAWAY <c>order.placed.v1</c> fact — with
    /// <paramref name="topic"/>'s own <c>eventType</c> substituted so the
    /// consumer's own routing still accepts it — repeatedly (its own
    /// distinct <c>eventId</c> every attempt) until the bound
    /// <see cref="FakeNotificationSender"/> observes it, then clears the
    /// sender's record — leaving the caller with a consumer PROVABLY
    /// consuming FROM THIS SPECIFIC TOPIC, and no trace of the warm-up in
    /// its own assertions.
    /// </summary>
    /// <remarks>
    /// Exists because this service's consumer, unlike Orders' saga consumer,
    /// starts every fresh group at <see cref="AutoOffsetReset.Latest"/> (a
    /// deliberate divergence — <c>KafkaFactStreamSubscriber</c>'s own
    /// remarks). <c>Latest</c> resolves to the topic's CURRENT tail at the
    /// moment each partition is actually assigned to this consumer, not at
    /// <c>Subscribe()</c>'s call site — a real, reproducible gap proven with
    /// a raw <c>Confluent.Kafka</c> consumer and a
    /// <c>SetPartitionsAssignedHandler</c> probe: assignment took ~6s in a
    /// cold broker, and a fact published even 700ms earlier was silently
    /// invisible to it, exactly as <c>Latest</c>'s own semantics say it
    /// should be. Orders' own tests never have to solve this because
    /// <c>Earliest</c> makes "publish, then start the consumer" always
    /// correct; this service's tests instead publish repeatedly UNTIL
    /// observed, which is robust to the exact assignment timing without
    /// hard-coding a duration.
    /// </remarks>
    public static async Task WarmUpAsync(KafkaContainerFixture kafka, FakeNotificationSender sender, string topic, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));

        while (DateTime.UtcNow < deadline)
        {
            var warmupEventId = Guid.NewGuid();
            var envelope = new Envelope<OrderPlacedPayload>(
                warmupEventId,
                "order.placed.v1",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                new OrderPlacedPayload("ORD-WARMUP", "CarrefourEs", "COMP01", "1", "2", "USD", DateTimeOffset.UtcNow, [], 1, 0, 1));

            await PublishAsync(kafka.BootstrapServers, topic, SerializeEnvelope(envelope));

            var expectedMessageId = $"{warmupEventId}@order-to-cash";
            var attemptDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < attemptDeadline)
            {
                if (sender.SentMessages.Any(m => m.MessageId == expectedMessageId))
                {
                    sender.Clear();
                    return;
                }

                await Task.Delay(150);
            }
        }

        throw new TimeoutException($"The Notifications consumer never warmed up on topic '{topic}' — no warm-up fact was observed within the budget.");
    }

    public static byte[] SerializeEnvelope<TPayload>(Envelope<TPayload> envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);

    /// <summary>
    /// EVERY publish in this whole test project — warm-up and real alike —
    /// uses the SAME fixed partition key, deliberately. Production keys by
    /// <c>correlationId</c> (asyncapi.yaml), scattering facts across all six
    /// partitions; this suite instead forces every message from every test
    /// onto ONE partition, so <see cref="WarmUpAsync"/>'s proof that ITS
    /// message was consumed is a proof about the SAME partition every
    /// caller's own real message will also land on — closing a real,
    /// reproduced race where a fresh <see cref="AutoOffsetReset.Latest"/>
    /// consumer's SIX partitions do not all finish resolving their "latest"
    /// position at the same instant, so a warm-up success on ONE partition
    /// did not previously guarantee a DIFFERENT partition (default
    /// round-robin placement) was equally ready — reproduced directly:
    /// warm-up observed and cleared successfully in every failing run, yet
    /// the test's own very next publish was still missed.
    /// </summary>
    private const string FixedPartitionKey = "notifications-integration-tests";

    public static async Task PublishAsync(string bootstrapServers, string topic, byte[] value)
    {
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = FixedPartitionKey, Value = value });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    public static async Task<int> WaitForSenderCallCountAsync(FakeNotificationSender sender, int atLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = 0;

        while (DateTime.UtcNow < deadline)
        {
            last = sender.CallCount;
            if (last >= atLeast)
            {
                return last;
            }

            await Task.Delay(150);
        }

        return last;
    }

    public static async Task<int> CountLedgerRowsAsync(MsSqlContainerFixture mssql, string connectionString, Guid eventId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.ProcessedEvents.CountAsync(p => p.EventId == eventId && p.Consumer == "notifications");
    }

    /// <summary>
    /// Stops <paramref name="host"/> AND CONFIRMS its own consumer has
    /// actually LEFT <paramref name="groupId"/> before returning — never
    /// merely that <c>StopAsync</c>/<c>Dispose</c> were called and trusted.
    /// Every Notifications integration test host joins the SAME LITERAL
    /// production group ("notifications",
    /// <see cref="OrderToCash.Notifications.Infrastructure.Messaging.Consumers.KafkaFactStreamSubscriber"/>'s
    /// own hardcoded <c>GroupId</c>) — under load, the generic host's own
    /// default shutdown budget can expire while
    /// <c>KafkaFactStreamSubscriber.ConsumeAsync</c>'s own
    /// <c>finally { consumer.Close(); }</c> is still blocked on a slow
    /// broker round trip, orphaning that task on its own thread-pool thread
    /// with the consumer object never disposed. A member in that state
    /// keeps sending heartbeats independently of the abandoned managed
    /// <see cref="Task"/>, so it stays "alive" from the broker's own point
    /// of view — reproduced directly and DETERMINISTICALLY by a standalone
    /// member that stops polling and is never <c>Close()</c>'d/disposed: a
    /// second, otherwise healthy member joining the SAME group was
    /// assigned ZERO of the topic's SIX partitions after the full 90s
    /// budget this suite's own DLQ tests use (`zombieprobe` scratch
    /// reproduction, review round 3 fix record). Left unconfirmed, that
    /// zombie silently blocks the NEXT test's own host — a DIFFERENT test
    /// method, so the failure surfaces on an unrelated assertion with no
    /// trace back to the test whose teardown actually caused it. This
    /// converts that silent, mislocated failure into either a clean pass
    /// (the group genuinely cleared) or a loud, correctly-attributed
    /// failure (the test whose own teardown never released its
    /// membership), and closes the class at its cause rather than at one
    /// symptom.
    /// </summary>
    public static async Task StopHostAndWaitForGroupToClearAsync(IHost host, KafkaContainerFixture kafka, string groupId = "notifications", TimeSpan? timeout = null)
    {
        await host.StopAsync();
        host.Dispose();

        var budget = timeout ?? TimeSpan.FromSeconds(150);
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + budget;
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await admin.DescribeConsumerGroupsAsync([groupId], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                var description = result.ConsumerGroupDescriptions.SingleOrDefault(g => g.GroupId == groupId);
                if (description is null || description.Members.Count == 0)
                {
                    return;
                }
            }
            catch (KafkaException)
            {
                // DescribeConsumerGroupsAsync itself can transiently fail
                // under the SAME contention that motivates this wait —
                // retry within the budget rather than surface a spurious
                // failure from the PROBE itself.
            }

            await Task.Delay(300);
        }

        throw new TimeoutException(
            $"Consumer group '{groupId}' still reported members {(DateTime.UtcNow - startedAt).TotalSeconds:F0}s after this test's own host was stopped — its teardown left a stale member that would otherwise block the NEXT test's rebalance (observed directly: a silent member can hold every partition of a topic for well over 90s, `zombieprobe` reproduction).");
    }

    public static async Task<int> WaitForLedgerRowCountAsync(MsSqlContainerFixture mssql, string connectionString, Guid eventId, int atLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = 0;

        while (DateTime.UtcNow < deadline)
        {
            last = await CountLedgerRowsAsync(mssql, connectionString, eventId);
            if (last >= atLeast)
            {
                return last;
            }

            await Task.Delay(150);
        }

        return last;
    }

    /// <summary>A genuine <see cref="INotificationSender"/> fake — thread-safe, since the BackgroundService's consume loop runs on its own thread, independent of the test's.</summary>
    public sealed class FakeNotificationSender : INotificationSender
    {
        private readonly List<NotificationMessage> _sent = [];
        private readonly Lock _gate = new();

        public int CallCount
        {
            get
            {
                lock (_gate)
                {
                    return _sent.Count;
                }
            }
        }

        public IReadOnlyList<NotificationMessage> SentMessages
        {
            get
            {
                lock (_gate)
                {
                    return _sent.ToList();
                }
            }
        }

        /// <summary>When set, the NEXT <see cref="SendAsync"/> call throws this once, then reverts to sending normally — the N6 recovery probe.</summary>
        public Exception? ThrowOnNextSend { get; set; }

        /// <summary>Discards every call recorded so far — used ONLY by <see cref="WarmUpAsync"/>, so a warm-up fact never appears in a test's own assertions.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                _sent.Clear();
            }
        }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (ThrowOnNextSend is { } exception)
                {
                    ThrowOnNextSend = null;
                    throw exception;
                }

                _sent.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
