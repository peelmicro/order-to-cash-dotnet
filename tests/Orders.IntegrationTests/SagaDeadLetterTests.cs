using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Infrastructure.Outbox;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// OR1, R16, design.md §3 — a poison fact retried then dead-lettered against
/// a REAL Kafka broker and a REAL MS-SQL database. Ledger L9 (the poison
/// shape), L12 (the committed offset read back from the broker, never
/// inferred), L15 (byte-for-byte).
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaDeadLetterTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private const string SourceTopic = OrdersFactTopic.Name;
    private const string DlqTopic = SourceTopic + ".dlq";
    private const string SagaGroupId = "orders.saga";
    private const int OrdersFactTopicPartitionCount = 6;

    [Fact]
    public async Task OR1_R16_APoisonFactIsRetriedThenDeadLetteredWithItsDiagnosticHeaders_AndTheCommittedOffsetAdvancesSoTheNextDistinctFactOnTheSamePartitionStillProcesses()
    {
        await EnsureDlqTopicExistsAsync();

        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(
            mssql, kafka, nats, "dlq",
            configureSaga: options =>
            {
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
            });

        try
        {
            var baseline = await SagaIntegrationTestSupport.ReadCommittedOffsetTotalAsync(
                kafka.BootstrapServers, SourceTopic, SagaGroupId, OrdersFactTopicPartitionCount, TimeSpan.FromSeconds(10));

            // (i) The poison fact is valid at the ENVELOPE level (every one
            // of the seven fields is present and well-typed) and fails ONLY
            // when the payload is deserialised against its catalogued type
            // — "stock.reserved.v1" declares an object payload, and this
            // one is a bare JSON string. A non-UUID correlationId (#7's own
            // shape) is UNREACHABLE in #8 (ledger L9) and would be caught by
            // the envelope guard, proving nothing about OR1.
            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var aggregateId = Guid.NewGuid();
            var occurredAt = DateTimeOffset.UtcNow;
            var poisonEnvelope = new Envelope<string>(eventId, "stock.reserved.v1", aggregateId, correlationId, Guid.NewGuid(), occurredAt, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
            {
                await producer.ProduceAsync(SourceTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = poisonBytes });
            }

            // (ii) The committed offset is READ BACK FROM THE BROKER — never
            // inferred from a non-redelivery, which is the exact claim a
            // previous feature ticked without arming. Only a genuine
            // StoreOffset()+commit (reached because DispatchAsync returns
            // NORMALLY after dead-lettering) can advance this.
            var advanced = await SagaIntegrationTestSupport.WaitForCommittedOffsetToExceedAsync(
                kafka.BootstrapServers, SourceTopic, SagaGroupId, OrdersFactTopicPartitionCount, baseline, TimeSpan.FromSeconds(90));
            Assert.True(advanced > baseline, $"Committed offset never advanced past baseline {baseline} (last observed {advanced}) — the poison fact must not block the partition.");

            var dlqRecord = await ConsumeOneAsync(DlqTopic, correlationId, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);

            // (iii) The .dlq message bytes compared to the produced bytes,
            // Assert.Equal over the arrays — byte-for-byte, never a
            // re-serialised envelope.
            Assert.Equal(poisonBytes, dlqRecord!.Message.Value);

            // (iv) Every header value asserted individually — including
            // x-error against an INDEPENDENTLY COMPUTED expected value (the
            // exact JsonException the same deserialisation call produces),
            // never merely "non-empty": a corruption probe only bites on a
            // field whose expected VALUE the test itself supplied.
            var headers = ReadHeaders(dlqRecord.Message.Headers);
            Assert.Equal("orders.saga", headers["x-failed-consumer"]);
            Assert.Equal("2", headers["x-attempts"]);
            Assert.Equal(SourceTopic, headers["x-original-topic"]);
            Assert.Equal("stock.reserved.v1", headers["x-event-type"]);
            Assert.Equal(ExpectedDeserialisationErrorMessage(), headers["x-error"]);
            Assert.True(DateTimeOffset.TryParse(headers["x-first-failed-at"], out _));
            Assert.True(DateTimeOffset.TryParse(headers["x-failed-at"], out _));

            // The next, DISTINCT fact on the SAME partition (same Kafka
            // key = same correlationId, so the default partitioner routes
            // it identically) still processes — an unknown order is a
            // deliberate SO8 ignore, observable without waiting on a full
            // saga command round trip.
            var secondEventId = Guid.NewGuid();
            var secondCorrelationId = correlationId; // SAME key → SAME partition.
            var healthyPayload = new StockReservedPayload("ORD-DOES-NOT-EXIST", "COMPANY1", []);
            var healthyEnvelope = new Envelope<StockReservedPayload>(secondEventId, "stock.reserved.v1", aggregateId, secondCorrelationId, Guid.NewGuid(), DateTimeOffset.UtcNow, healthyPayload);
            var healthyBytes = JsonSerializer.SerializeToUtf8Bytes(healthyEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
            {
                await producer.ProduceAsync(SourceTopic, new Message<string, byte[]> { Key = secondCorrelationId.ToString(), Value = healthyBytes });
            }

            var ignoredCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(
                connectionString, mssql, secondCorrelationId, eventType: "stock.reserved.v1", marker: "unknown_order", TimeSpan.FromSeconds(90));
            Assert.True(ignoredCount > 0, "The next distinct fact on the same partition never reached the handler — the partition stayed blocked.");
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// OR4/R57, design.md §5.3, ledger L22 — every retry attempt AND the
    /// eventual DLQ publish observe the SAME trace id as the inbound fact:
    /// the SAME poison-fact mechanism above, but this time the produced
    /// message itself carries a real <c>traceparent</c> header (as a
    /// genuine upstream writer would via <c>outbox.trace_parent</c>), and
    /// the assertion is that the DLQ copy's OWN <c>traceparent</c> header —
    /// already asserted to exist by <c>DeadLetterHeaders</c> — extracts to
    /// the SAME trace id, never merely "a header is present" (§5.5).
    /// </summary>
    [Fact]
    public async Task R57_OR4_EveryRetryAttemptAndTheEventualDlqPublishObserveTheSameTraceIdAsTheInboundFact()
    {
        await EnsureDlqTopicExistsAsync();

        var (host, _) = await SagaIntegrationTestSupport.StartHostAsync(
            mssql, kafka, nats, "dlq-trace",
            configureSaga: options =>
            {
                options.FactRetry.MaxAttempts = 2;
                options.FactRetry.BackoffMs = 100;
            });

        try
        {
            using var inboundActivity = OtcActivity.Source.StartActivity("test inbound fact writer");
            var inboundTraceParent = inboundActivity?.Id;
            Assert.NotNull(inboundTraceParent);

            var eventId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var aggregateId = Guid.NewGuid();
            var poisonEnvelope = new Envelope<string>(eventId, "stock.reserved.v1", aggregateId, correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
            var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
            {
                var headers = new Headers { { "traceparent", System.Text.Encoding.UTF8.GetBytes(inboundTraceParent!) } };
                await producer.ProduceAsync(SourceTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = poisonBytes, Headers = headers });
            }

            var dlqRecord = await ConsumeOneAsync(DlqTopic, correlationId, TimeSpan.FromSeconds(90));
            Assert.NotNull(dlqRecord);

            var headersRead = ReadHeaders(dlqRecord!.Message.Headers);
            Assert.True(headersRead.TryGetValue("traceparent", out var dlqTraceParent), "The .dlq message carries no traceparent header at all.");

            var dlqContext = TraceContext.ContextFromTraceParent(dlqTraceParent);
            Assert.NotNull(dlqContext);
            Assert.Equal(inboundActivity!.TraceId, dlqContext!.Value.TraceId);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Reproduces the EXACT deserialisation call
    /// <c>SagaFactsConsumer.ProcessFactAsync</c> makes on the poison
    /// payload — a bare JSON string bound to
    /// <see cref="StockReservedPayload"/> — and returns the real
    /// <see cref="JsonException.Message"/> it throws. Independently
    /// computed rather than hard-coded, so this stays correct across
    /// runtime/library versions while still supplying a genuine expected
    /// VALUE for the x-error header corruption probe.
    /// </summary>
    private static string ExpectedDeserialisationErrorMessage()
    {
        var poisonPayloadElement = JsonDocument.Parse("\"poison-payload-not-an-object\"").RootElement;
        try
        {
            JsonSerializer.Deserialize(poisonPayloadElement, typeof(StockReservedPayload), JsonWire.Options);
            throw new InvalidOperationException("Expected a JsonException — the poison payload no longer fails to deserialise.");
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
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
            // Already created by an earlier test in this collection.
        }
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
    /// <remarks>
    /// Filters by the poison fact's OWN <paramref name="correlationId"/> —
    /// found necessary by feature <c>observability_reliability</c>'s Group
    /// A2, which added a SECOND writer to this exact topic
    /// (<c>SagaCommandDeadLetterTests</c>, republishing a triggering fact to
    /// the same <c>otc.orders.facts.v1.dlq</c> topic, same
    /// <see cref="SagaCollection"/>). A bare "first message on the topic"
    /// read — correct while this test was the topic's only publisher within
    /// the collection — now races a sibling test's own DLQ copy and can
    /// return the WRONG one, exactly the shared-mutable-topic hazard
    /// <c>SagaCommandDeadLetterTests.ConsumeMatchingAsync</c> already
    /// documents and defends against with the identical filter shape.
    /// </remarks>
    private async Task<ConsumeResult<string, byte[]>?> ConsumeOneAsync(string topic, Guid correlationId, TimeSpan timeout)
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

    /// <summary>The <c>.dlq</c> payload is the UNMODIFIED original envelope (ledger L15), so its own <c>correlationId</c> field is read directly — never the DLQ header, which names the failed CONSUMER, not the fact.</summary>
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
