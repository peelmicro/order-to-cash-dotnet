using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Notifications.IntegrationTests;

/// <summary>
/// Backlog id 73, end to end, against the REAL bound
/// <c>DegradingNotificationSender</c> (never a <c>FakeNotificationSender</c>
/// override — the SenderKind.Smtp binding is exactly what
/// <c>NotificationsServiceCollectionExtensions</c> wires for the real host):
/// a fact whose SMTP send hits a genuinely permanent Mailpit rejection is
/// rendered through the console fallback, its structured degraded-send log
/// line carries the SAME <c>correlationId</c>/<c>traceId</c> mechanism
/// <c>LogCorrelationTests</c> already proves for every other line this
/// service emits, the idempotency ledger row is NEVER compensated/deleted,
/// and — the third arm family's real-world counterpart —
/// NO <c>.dlq</c> publication ever happens for this fact.
/// </summary>
[Collection(NotificationsWithMailpitCollection.Name)]
public sealed class NotificationDegradesOnPermanentFailureTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql, MailpitContainerFixture mailpit)
{
    private const string SourceTopic = NotificationFactTopics.OrdersFacts;
    private const string DlqTopic = SourceTopic + ".dlq";
    private const string GroupId = "notifications";

    [Fact]
    public async Task APermanentSmtpFailure_DegradesToConsole_KeepsTheLedgerRow_AndNeverDeadLetters_WithCorrelationIdAndTraceIdOnTheLogLine()
    {
        await EnsureDlqTopicExistsAsync();
        await mailpit.SetRecipientErrorAsync(550);

        using var capture = new CapturedConsole();
        Guid correlationId;
        Guid eventId;
        string connectionString;

        using (capture.Redirect())
        {
            connectionString = await mssql.CreateFreshDatabaseAsync($"otc_notifications_degrade_{Guid.NewGuid():N}");
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
                    // The real Smtp binding — DegradingNotificationSender
                    // wraps the real MailKitNotificationSender, exactly
                    // Program.cs's own composition.
                    options.SenderKind = NotificationSenderKind.Smtp;
                    options.Smtp.Host = mailpit.SmtpHost;
                    options.Smtp.Port = mailpit.SmtpPort;
                });

            var host = builder.Build();
            await host.StartAsync();

            try
            {
                await WarmUpAsync(mssql, connectionString);

                eventId = Guid.NewGuid();
                correlationId = Guid.NewGuid();
                var payload = new OrderPlacedPayload("ORD-DEGRADE-01", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0);
                var envelope = new Envelope<OrderPlacedPayload>(eventId, "order.placed.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, payload);

                await PublishAsync(SourceTopic, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options));

                // Positive proof the fact was processed at all: the
                // idempotency ledger row is written BEFORE the send is
                // attempted (NotificationDispatchService.DispatchAsync's
                // own insert-first ordering), so its appearance is
                // independent of whether the send succeeds, degrades or
                // fails transiently.
                var ledgerRows = await WaitForLedgerRowAsync(mssql, connectionString, eventId, TimeSpan.FromSeconds(60));
                Assert.Equal(1, ledgerRows);

                // The degraded-send log line: a stable Event field, the
                // SAME correlationId this fact carries, and the MessageId
                // review round-1 A3 found unguarded — never merely "a line
                // was logged".
                var expectedMessageId = $"{eventId}@order-to-cash";
                var degraded = await WaitForDegradedLogLineAsync(capture, correlationId, TimeSpan.FromSeconds(30));
                Assert.NotNull(degraded);
                Assert.Equal("notification.send.degraded", degraded!.Value.Event);
                Assert.Equal(expectedMessageId, degraded.Value.MessageId);

                // Review round 1, D2 — the FALLBACK's own console-adapter
                // line (ConsoleNotificationSender.cs:19-23) is the ONLY
                // record a notification went out on this path (#7's own
                // console-notification-sender-log-trace-id.spec.ts:16
                // header: "the console line is the ONLY record"). It must
                // be emitted and carry the SAME correlationId/MessageId.
                var consoleLine = await WaitForConsoleAdapterLogLineAsync(capture, correlationId, TimeSpan.FromSeconds(30));
                Assert.NotNull(consoleLine);
                Assert.Equal(expectedMessageId, consoleLine!.Value.MessageId);

                // Review round 1, A1 — traceId strengthened from
                // presence-only to IDENTITY: the degraded (error) line and
                // the console-adapter (fallback) line are two log
                // statements for the SAME fact, so they must carry the
                // SAME real active trace id, each matching the 32-hex
                // shape (#7's own degrading-notification-sender-log-trace-id.spec.ts:56-57
                // assertion, never merely non-empty).
                Assert.False(string.IsNullOrEmpty(degraded.Value.TraceId));
                Assert.Matches("^[0-9a-f]{32}$", degraded.Value.TraceId!);
                Assert.False(string.IsNullOrEmpty(consoleLine.Value.TraceId));
                Assert.Matches("^[0-9a-f]{32}$", consoleLine.Value.TraceId!);
                Assert.Equal(degraded.Value.TraceId, consoleLine.Value.TraceId);

                // The ledger row is NEVER compensated/deleted for a
                // permanent failure — settle a little longer and confirm it
                // still holds exactly one row.
                await Task.Delay(TimeSpan.FromSeconds(2));
                var settledRows = await CountLedgerRowsAsync(mssql, connectionString, eventId);
                Assert.Equal(1, settledRows);

                // The third arm family's real-world counterpart: NO .dlq
                // publication ever happens for a permanent failure.
                var dlqRecord = await TryConsumeMatchingAsync(DlqTopic, correlationId, TimeSpan.FromSeconds(15));
                Assert.Null(dlqRecord);
            }
            finally
            {
                await NotificationConsumptionTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
            }
        }

        await mailpit.ResetChaosAsync();
    }

    /// <summary>Publishes a throwaway fact and waits for its OWN ledger row — no <c>FakeNotificationSender</c> is available on the real Smtp binding, so warm-up cannot use <c>NotificationConsumptionTestSupport.WarmUpAsync</c>.</summary>
    private async Task WarmUpAsync(MsSqlContainerFixture mssqlFixture, string connectionString)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            var warmupEventId = Guid.NewGuid();
            var payload = new OrderPlacedPayload("ORD-WARMUP", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0);
            var envelope = new Envelope<OrderPlacedPayload>(warmupEventId, "order.placed.v1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, payload);

            await PublishAsync(SourceTopic, JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options));

            var attemptDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < attemptDeadline)
            {
                if (await CountLedgerRowsAsync(mssqlFixture, connectionString, warmupEventId) > 0)
                {
                    return;
                }

                await Task.Delay(150);
            }
        }

        throw new TimeoutException("The Notifications consumer never warmed up on OrdersFacts.");
    }

    private static async Task<int> CountLedgerRowsAsync(MsSqlContainerFixture mssqlFixture, string connectionString, Guid eventId) =>
        await NotificationConsumptionTestSupport.CountLedgerRowsAsync(mssqlFixture, connectionString, eventId);

    private static async Task<int> WaitForLedgerRowAsync(MsSqlContainerFixture mssqlFixture, string connectionString, Guid eventId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = 0;

        while (DateTime.UtcNow < deadline)
        {
            last = await CountLedgerRowsAsync(mssqlFixture, connectionString, eventId);
            if (last > 0)
            {
                return last;
            }

            await Task.Delay(200);
        }

        return last;
    }

    private static async Task<(string Event, string? MessageId, string? TraceId)?> WaitForDegradedLogLineAsync(CapturedConsole capture, Guid correlationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var doc in capture.ParseJsonLines())
            {
                if (ScopeValue(doc, "correlationId") != correlationId.ToString())
                {
                    continue;
                }

                if (!doc.RootElement.TryGetProperty("State", out var state) ||
                    !state.TryGetProperty("Event", out var eventProperty) ||
                    eventProperty.GetString() != "notification.send.degraded")
                {
                    continue;
                }

                var messageId = state.TryGetProperty("MessageId", out var messageIdProperty) ? messageIdProperty.GetString() : null;
                return (eventProperty.GetString()!, messageId, ScopeValue(doc, "TraceId"));
            }

            await Task.Delay(200);
        }

        return null;
    }

    /// <summary>
    /// Review round 1, D2 — the fallback's OWN console-adapter line
    /// (<c>ConsoleNotificationSender.cs:19-23</c>'s
    /// <c>"notification (console adapter): to={To} subject={Subject} messageId={MessageId}"</c>),
    /// matched by its message PREFIX (it carries no stable <c>Event</c>
    /// field of its own) and by the SAME <c>correlationId</c> scope this
    /// fact's degraded-send line also carries.
    /// </summary>
    private static async Task<(string? MessageId, string? TraceId)?> WaitForConsoleAdapterLogLineAsync(CapturedConsole capture, Guid correlationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            foreach (var doc in capture.ParseJsonLines())
            {
                if (ScopeValue(doc, "correlationId") != correlationId.ToString())
                {
                    continue;
                }

                if (!doc.RootElement.TryGetProperty("Message", out var messageProperty) ||
                    messageProperty.GetString() is not { } message ||
                    !message.StartsWith("notification (console adapter):", StringComparison.Ordinal))
                {
                    continue;
                }

                var messageId = doc.RootElement.TryGetProperty("State", out var state) && state.TryGetProperty("MessageId", out var messageIdProperty)
                    ? messageIdProperty.GetString()
                    : null;
                return (messageId, ScopeValue(doc, "TraceId"));
            }

            await Task.Delay(200);
        }

        return null;
    }

    private static string? ScopeValue(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private async Task EnsureDlqTopicExistsAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = DlqTopic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private async Task PublishAsync(string topic, byte[] value)
    {
        using var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = "notifications-degrade-tests", Value = value });
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    /// <summary>Never blocks past <paramref name="timeout"/> looking for a NEGATIVE result — a genuine absence, not merely "not found yet".</summary>
    private async Task<ConsumeResult<string, byte[]>?> TryConsumeMatchingAsync(string topic, Guid correlationId, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"degrade-probe-{Guid.NewGuid():N}",
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
}
