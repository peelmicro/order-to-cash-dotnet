using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// <c>observability_reliability</c>, design.md §4 (<c>OR3</c>, <c>R29</c>'s
/// dead-letter clause) — the first-park hook, end to end against a REAL
/// Kafka broker, a REAL NATS transport (deliberately with no
/// <c>stock.reserve</c> responder) and a REAL MS-SQL database. Ledger L15
/// (byte-for-byte republish), L16 (the at-most-once claim), L17 (what is
/// atomic with what).
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaCommandDeadLetterTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private const string SourceTopic = OrdersFactTopic.Name;
    private const string DlqTopic = SourceTopic + ".dlq";
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task R29_OR3_OnFirstParkAppendsExactlyOneOrderSagaFailedFactCarryingTheCommandAttemptsAndLastError_AndPublishesTheTriggeringFactToTheSourceTopicsDlqExactlyOnce_AndRepeatsNeitherOnASecondForcedParkOfTheSameRow_WhileSO5sRetryScheduleIsUnchanged()
    {
        await EnsureDlqTopicExistsAsync();

        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "firstpark");
        try
        {
            // Deliberately NO stand-in for fulfillment.stock.reserve — the
            // command exhausts SO4's in-line retries and parks (SO5).
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            // The exact bytes the relay published for THIS order's
            // order.placed.v1 — the "produced message" the .dlq copy must
            // match byte-for-byte (ledger L15). Read from the topic itself,
            // never from the stored saga_commands column, so the assertion
            // is independent of the storage round trip it is proving.
            var producedOrderPlaced = await ConsumeMatchingAsync(SourceTopic, orderId, TimeSpan.FromSeconds(30), eventType: "order.placed.v1");
            Assert.NotNull(producedOrderPlaced);
            var producedBytes = producedOrderPlaced!.Message.Value;

            // Poll the condition the assertion below actually depends on —
            // NEVER "Status == parked" alone. ParkAsync's own status/attempts
            // UPDATE commits strictly BEFORE SagaFirstParkDeadLetterHandler's
            // own claim UPDATE, both awaited sequentially inside the SAME
            // SagaCommandDispatcher.DispatchClaimedAsync call; a poll that
            // stops at "parked" can land in that gap and read DeadLetteredAt
            // as still null on a correct system. Proven by controlled delay,
            // not by re-running: see progress/impl_observability_reliability.md's
            // "Correction" section.
            SagaCommand? parkedRow = null;
            var deadline = DateTime.UtcNow + _wait;
            while (DateTime.UtcNow < deadline)
            {
                await using var db = mssql.CreateDbContext(connectionString);
                parkedRow = await db.SagaCommands.AsNoTracking().SingleOrDefaultAsync(c => c.OrderId == orderId && c.Status == "parked" && c.DeadLetteredAt != null);
                if (parkedRow is not null)
                {
                    break;
                }

                await Task.Delay(150);
            }

            Assert.NotNull(parkedRow);
            Assert.Equal("stock.reserve", parkedRow!.Command);

            // The claim and the fact commit TOGETHER (ledger L17) — both
            // observed on the SAME row read, which the poll predicate above
            // now guarantees rather than merely hopes for.
            var firstParkedAt = parkedRow.DeadLetteredAt;
            Assert.NotNull(firstParkedAt);

            var outboxCountAfterFirstPark = await SagaIntegrationTestSupport.WaitForOutboxEventCountAsync(
                connectionString, mssql, orderId, "order.saga_failed.v1", atLeast: 1, TimeSpan.FromSeconds(15));
            Assert.Equal(1, outboxCountAfterFirstPark);

            await using (var db = mssql.CreateDbContext(connectionString))
            {
                var factRow = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == orderId && m.EventType == "order.saga_failed.v1");
                var payload = JsonSerializer.Deserialize<JsonElement>(factRow.Payload);
                Assert.Equal("stock.reserve", payload.GetProperty("command").GetString());
                Assert.Equal(parkedRow.Attempts, payload.GetProperty("attempts").GetInt32());
                Assert.False(string.IsNullOrEmpty(payload.GetProperty("lastError").GetString()));
                Assert.Equal(placed.OrderReference.Value, payload.GetProperty("orderReference").GetString());
            }

            var firstDlqMessage = await ConsumeMatchingAsync(DlqTopic, orderId, TimeSpan.FromSeconds(30));
            Assert.NotNull(firstDlqMessage);
            Assert.Equal(producedBytes, firstDlqMessage!.Message.Value);

            // SO5's own schedule is UNCHANGED by this feature — the row's
            // next_attempt_at still advances past "now", exactly as it did
            // before OR3 existed.
            Assert.NotNull(parkedRow.NextAttemptAt);
            Assert.True(parkedRow.NextAttemptAt > DateTime.UtcNow.AddSeconds(-5));

            // Force a SECOND park of the SAME row: the sweeper's own capped
            // backoff (configured to 5 s by SagaIntegrationTestSupport) will
            // re-issue it, fail again (still no responder), and park it
            // again — SO5's indefinite retry, deliberately unaffected.
            SagaCommand? secondParkRow = null;
            var secondAttemptsFloor = parkedRow.Attempts;
            var secondDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < secondDeadline)
            {
                await using var db = mssql.CreateDbContext(connectionString);
                var candidate = await db.SagaCommands.AsNoTracking().SingleOrDefaultAsync(c => c.OrderId == orderId && c.Command == "stock.reserve");
                if (candidate is not null && candidate.Status == "parked" && candidate.Attempts > secondAttemptsFloor)
                {
                    secondParkRow = candidate;
                    break;
                }

                await Task.Delay(200);
            }

            Assert.NotNull(secondParkRow);

            // The SAME race in the opposite direction: the second park's own
            // status/attempts UPDATE (just detected above) commits strictly
            // BEFORE SagaFirstParkDeadLetterHandler's own SECOND claim
            // attempt resolves — win or lose — inside the SAME
            // DispatchClaimedAsync call. A losing claim (the correct
            // outcome) leaves no independent observable, so this settles on
            // two consecutive reads agreeing across a window comfortably
            // larger than that hop's own processing time, the same pattern
            // NotificationOffsetSupport.WaitForCommittedOffsetToSettleAsync
            // already uses for an identical "no further change" claim.
            // Reading DeadLetteredAt off the SAME snapshot that detected
            // "parked" would only prove the park happened, never that the
            // SECOND claim attempt has also finished.
            var settledDeadLetteredAt = await WaitForDeadLetteredAtToSettleAsync(connectionString, secondParkRow!.Id, TimeSpan.FromSeconds(30));

            // R29's OR3 clause: "at most once per row" — dead_lettered_at
            // is set ONCE and never moves on a later park.
            Assert.Equal(firstParkedAt, settledDeadLetteredAt);
            // SO5 untouched: the schedule STILL advances on the second park.
            Assert.NotNull(secondParkRow.NextAttemptAt);
            Assert.True(secondParkRow.NextAttemptAt > DateTime.UtcNow.AddSeconds(-5));

            // Neither the fact nor the .dlq copy repeats.
            var outboxCountAfterSecondPark = await SagaIntegrationTestSupport.CountOutboxEventsAsync(connectionString, mssql, orderId, "order.saga_failed.v1");
            Assert.Equal(1, outboxCountAfterSecondPark);

            var secondDlqMessage = ConsumeAllRemaining(DlqTopic, orderId, TimeSpan.FromSeconds(5));
            Assert.Empty(secondDlqMessage);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Settles on <c>DeadLetteredAt</c> only once two consecutive reads,
    /// <c>SettleWindow</c> apart, agree — the same "wait for eventual
    /// quiescence" shape
    /// <c>NotificationOffsetSupport.WaitForCommittedOffsetToSettleAsync</c>
    /// already uses. Required because a LOSING second claim attempt (the
    /// correct outcome under <c>OR3</c>) leaves no independent observable to
    /// poll a positive condition on — only the ABSENCE of further change,
    /// which can only be proven by watching for a while, not by reading
    /// once. <c>SettleWindow</c> must stay comfortably larger than that
    /// hop's own real processing time (a handful of DB round trips) or this
    /// degenerates into exactly the race it exists to close.
    /// </summary>
    private async Task<DateTime?> WaitForDeadLetteredAtToSettleAsync(string connectionString, Guid commandId, TimeSpan timeout)
    {
        var settleWindow = TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow + timeout;
        var previous = await ReadDeadLetteredAtAsync(connectionString, commandId);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(settleWindow);
            var current = await ReadDeadLetteredAtAsync(connectionString, commandId);
            if (current == previous)
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }

    private async Task<DateTime?> ReadDeadLetteredAtAsync(string connectionString, Guid commandId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var row = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.Id == commandId);
        return row.DeadLetteredAt;
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
    /// Reads <paramref name="topic"/> from <see cref="Offset.Beginning"/>
    /// over its own known partition set (never <c>Subscribe()</c> — the
    /// same coordinator-round-trip-avoidance <see cref="SagaDeadLetterTests"/>
    /// already documents) and returns the FIRST message whose envelope
    /// <c>correlationId</c> equals <paramref name="orderId"/> — this
    /// collection's tests share one long-lived topic across many test
    /// cases, so a bare "first message found" would pick up a PRIOR test's
    /// residue.
    /// </summary>
    private async Task<ConsumeResult<string, byte[]>?> ConsumeMatchingAsync(string topic, Guid orderId, TimeSpan timeout, string? eventType = null)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"first-park-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6)
            .Select(p => new TopicPartitionOffset(topic, new Partition(p), Offset.Beginning))
            .ToList());

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null || result.IsPartitionEOF)
            {
                continue;
            }

            if (MatchesOrder(result.Message.Value, orderId, eventType))
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>Drains every remaining message on <paramref name="topic"/> matching <paramref name="orderId"/> within a SHORT window — used to prove ABSENCE (a second .dlq copy never arrives), not presence, so the window is deliberately short and the method returns whatever it found rather than blocking to a timeout on every call.</summary>
    private List<ConsumeResult<string, byte[]>> ConsumeAllRemaining(string topic, Guid orderId, TimeSpan window)
    {
        var found = new List<ConsumeResult<string, byte[]>>();
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"first-park-absence-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6)
            .Select(p => new TopicPartitionOffset(topic, new Partition(p), Offset.Beginning))
            .ToList());

        var deadline = DateTime.UtcNow + window;
        var seen = 0;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(300));
            if (result is null || result.IsPartitionEOF)
            {
                continue;
            }

            if (MatchesOrder(result.Message.Value, orderId))
            {
                seen++;
                if (seen > 1) // the FIRST park's own copy is expected; only a SECOND is the violation.
                {
                    found.Add(result);
                }
            }
        }

        return found;
    }

    private static bool MatchesOrder(byte[] messageValue, Guid orderId, string? eventType = null)
    {
        try
        {
            using var document = JsonDocument.Parse(messageValue);
            var correlationMatches = document.RootElement.TryGetProperty("correlationId", out var correlationId)
                && correlationId.GetGuid() == orderId;

            if (!correlationMatches)
            {
                return false;
            }

            return eventType is null
                || (document.RootElement.TryGetProperty("eventType", out var actualEventType) && actualEventType.GetString() == eventType);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
