using System.Diagnostics;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// R58/OR7, design.md §6, ledger L25 — captures REAL emitted JSON from a
/// FULLY configured real host (<c>OrdersHost.CreateBuilder</c>'s own
/// <c>AddJsonConsole</c> + <c>IncludeScopes</c> + <c>ActivityTrackingOptions</c>
/// wiring), never asserted against the logger abstraction — design.md
/// §10.4 names that exact gap as the one a mocked/faked logger cannot see.
/// <see cref="Console.Out"/> MUST be redirected BEFORE the host is built —
/// <c>AddJsonConsole</c>'s writer captures the <see cref="TextWriter"/>
/// reference once, at construction time, so redirecting afterwards is
/// silently a no-op (probed directly: see the implementation record).
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class LogCorrelationTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private const string SourceTopic = OrdersFactTopic.Name;

    /// <summary>
    /// A poison fact retried then dead-lettered (the SAME mechanism
    /// <c>SagaDeadLetterTests</c> uses) — guarantees MULTIPLE log lines
    /// (one warning per attempt, one final error) within ONE fact-consume
    /// flow, all sharing the SAME <c>correlationId</c> scope
    /// (<c>envelope.correlationId</c>, design.md §6's table) and the SAME
    /// trace id (ONE Activity wraps the whole retry-then-DLQ call, ledger
    /// L22). This is the "a fact" leg of R58's "a request, a command and a
    /// fact"; the request/command legs are proven by
    /// <c>ProblemJsonCorrelationTests</c> and by inspection of
    /// <c>SagaCommandDispatcher</c>'s own <c>order_id</c> scope (§6's
    /// table) — a single flow spanning independently-rooted operations is
    /// deliberately NOT claimed here, since end-to-end composed-stack trace
    /// continuity is R56's own, separately-deferred half (feature 28).
    /// </summary>
    [Fact]
    public async Task R58_OR7_EveryRecordProducedWhileHandlingAFactCarriesTheSameCorrelationIdAndTheSameTraceId()
    {
        using var capture = new CapturedConsole();
        Guid correlationId;

        using (capture.Redirect())
        {
            var (host, _) = await SagaIntegrationTestSupport.StartHostAsync(
                mssql, kafka, nats, "logcorrelation",
                configureSaga: options =>
                {
                    options.FactRetry.MaxAttempts = 2;
                    options.FactRetry.BackoffMs = 100;
                });

            try
            {
                await EnsureDlqTopicExistsAsync();

                correlationId = Guid.NewGuid();
                var poisonEnvelope = new Envelope<string>(Guid.NewGuid(), "stock.reserved.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
                var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

                using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
                {
                    await producer.ProduceAsync(SourceTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = poisonBytes });
                }

                await WaitForDlqMessageAsync(correlationId, TimeSpan.FromSeconds(60));

                // A short settle so the JSON console's own background
                // writer thread has drained its queue before this test
                // restores Console.Out.
                await Task.Delay(500);
            }
            finally
            {
                await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
            }
        }

        var records = capture.ParseJsonLines();
        var mine = records.Where(r => ScopeValue(r, "correlationId") == correlationId.ToString()).ToList();

        // The retry warnings (MaxAttempts = 2) plus the final dead-letter
        // error — MORE than one record, so "the same correlationId AND the
        // same traceId" is a genuine claim about multiple lines, not a
        // vacuous one about a single line.
        Assert.True(mine.Count > 1, $"Expected more than one log record for this poison fact's correlationId; found {mine.Count}.");

        var traceIds = mine.Select(r => ScopeValue(r, "TraceId")).ToList();
        Assert.All(traceIds, t => Assert.False(string.IsNullOrEmpty(t)));
        Assert.Single(traceIds.Distinct(StringComparer.Ordinal));

        foreach (var record in mine)
        {
            Assert.NotNull(ScopeValue(record, "SpanId"));
        }
    }

    /// <summary>
    /// Review round 2, D5 rows 48–49 (partial → closed) — #7's
    /// <c>saga-facts-log-trace-id.spec</c> asserts the failure log carries
    /// the REAL INBOUND trace id, never merely "some non-empty value". The
    /// sibling test above produces its poison fact with NO
    /// <c>traceparent</c> header at all, so nothing in this file was ever
    /// compared to an inbound id — only that every record shared ONE
    /// (unverified) value. This publishes the SAME poison-payload fact
    /// (a valid envelope whose payload fails to deserialise inside
    /// <c>ProcessFactAsync</c> — the branch WRAPPED by the "consume" span,
    /// design.md §5.3, unlike a structurally invalid envelope, which fails
    /// <c>ValidateEnvelope</c> BEFORE the span starts and carries no trace
    /// id in EITHER stack) carrying a REAL <c>traceparent</c> header, and
    /// asserts the resulting log lines' <c>TraceId</c> scope equals that
    /// header's own trace id — the omission half stays proven by the
    /// sibling test below.
    /// </summary>
    [Fact]
    public async Task R58_OR7_OR4_TheMalformedEnvelopeLogCarriesTheRealInboundTraceId_WhenATraceparentHeaderIsPresent()
    {
        using var capture = new CapturedConsole();
        Activity? inboundActivity = null;

        using (capture.Redirect())
        {
            var (host, _) = await SagaIntegrationTestSupport.StartHostAsync(
                mssql, kafka, nats, "logcorrelation-inbound-trace",
                configureSaga: options =>
                {
                    options.FactRetry.MaxAttempts = 2;
                    options.FactRetry.BackoffMs = 100;
                });

            try
            {
                await EnsureDlqTopicExistsAsync();

                inboundActivity = OtcActivity.Source.StartActivity("test inbound fact writer");
                var inboundTraceParent = inboundActivity?.Id;
                Assert.NotNull(inboundTraceParent);

                var correlationId = Guid.NewGuid();
                var poisonEnvelope = new Envelope<string>(Guid.NewGuid(), "stock.reserved.v1", Guid.NewGuid(), correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
                var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

                using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
                {
                    var headers = new Headers { { "traceparent", System.Text.Encoding.UTF8.GetBytes(inboundTraceParent!) } };
                    await producer.ProduceAsync(SourceTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = poisonBytes, Headers = headers });
                }

                await WaitForDlqMessageAsync(correlationId, TimeSpan.FromSeconds(60));
                await Task.Delay(500);
            }
            finally
            {
                await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
                inboundActivity?.Dispose();
            }
        }

        Assert.NotNull(inboundActivity);
        var records = capture.ParseJsonLines();
        var mine = records.Where(r => ScopeValue(r, "TraceId") == inboundActivity!.TraceId.ToString()).ToList();

        Assert.NotEmpty(mine);
    }

    [Fact]
    public async Task R58_OR7_OmitsTheTraceFieldsEntirelyRatherThanRenderingThemEmpty_WhenNoSpanIsActive()
    {
        using var capture = new CapturedConsole();

        using (capture.Redirect())
        {
            var (host, _) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "logcorrelation-nospan");
            try
            {
                var logger = host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("OrderToCash.Orders.IntegrationTests.NoSpanProbe");

                logger.LogInformation("no-span-probe {Marker}", "R58_OR7_no_span_marker");
                await Task.Delay(300);
            }
            finally
            {
                await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
            }
        }

        var records = capture.ParseJsonLines();
        var probe = Assert.Single(records, r => r.RootElement.TryGetProperty("Message", out var m) && m.GetString()!.Contains("R58_OR7_no_span_marker", StringComparison.Ordinal));

        // No Scopes entry carries a TraceId/SpanId key at all — omitted,
        // never rendered as "" or "undefined".
        Assert.True(probe.RootElement.TryGetProperty("Scopes", out var scopes));
        foreach (var scope in scopes.EnumerateArray())
        {
            Assert.False(scope.TryGetProperty("TraceId", out _));
            Assert.False(scope.TryGetProperty("SpanId", out _));
        }
    }

    private async Task EnsureDlqTopicExistsAsync()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = $"{SourceTopic}.dlq", NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
            // Already created by an earlier test in this collection.
        }
    }

    private async Task WaitForDlqMessageAsync(Guid correlationId, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"logcorrelation-dlq-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6).Select(p => new TopicPartitionOffset($"{SourceTopic}.dlq", new Partition(p), Offset.Beginning)).ToList());

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null || result.IsPartitionEOF)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(result.Message.Value);
                if (document.RootElement.TryGetProperty("correlationId", out var value) && value.GetGuid() == correlationId)
                {
                    return;
                }
            }
            catch (JsonException)
            {
            }
        }

        throw new TimeoutException($"The poison fact for correlationId {correlationId} was never dead-lettered within {timeout}.");
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
}
